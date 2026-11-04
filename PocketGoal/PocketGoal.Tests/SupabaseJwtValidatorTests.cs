using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;
using PocketGoal.Services;
using Xunit;

namespace PocketGoal.Tests
{
    public class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_handler(request));
        }
    }

    public class SupabaseJwtValidatorTests : IDisposable
    {
        private const string TestBaseUrl = "https://testproject.supabase.co";
        private readonly ECDsa _ecdsaKey;
        private readonly ECDsaSecurityKey _ecdsaSecurityKey;
        private readonly string _jwkJson;
        private readonly string _jwksJson;

        public SupabaseJwtValidatorTests()
        {
            _ecdsaKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            _ecdsaSecurityKey = new ECDsaSecurityKey(_ecdsaKey) { KeyId = "primary-key-1" };

            var jwk = JsonWebKeyConverter.ConvertFromECDsaSecurityKey(_ecdsaSecurityKey);
            var jwksObj = new { keys = new[] { JsonSerializer.Deserialize<object>(JsonSerializer.Serialize(jwk)) } };
            _jwksJson = JsonSerializer.Serialize(jwksObj);
            _jwkJson = JsonSerializer.Serialize(jwk);

            SupabaseJwtValidator.ResetCacheForTesting(null);
        }

        public void Dispose()
        {
            _ecdsaKey.Dispose();
            SupabaseJwtValidator.ResetCacheForTesting(null);
        }

        private IConfiguration CreateConfiguration(string url = TestBaseUrl)
        {
            var inMemorySettings = new Dictionary<string, string?>
            {
                { "Supabase:Url", url },
                { "Supabase:AnonKey", "test-anon-key" }
            };

            return new ConfigurationBuilder()
                .AddInMemoryCollection(inMemorySettings)
                .Build();
        }

        private HttpClient CreateMockHttpClient(string responseBody, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            var handler = new MockHttpMessageHandler(req => new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            });
            return new HttpClient(handler);
        }

        private string GenerateEs256Token(
            Guid userId,
            string email,
            string? name,
            DateTime expiresUtc,
            string issuer = $"{TestBaseUrl}/auth/v1",
            string audience = "authenticated",
            string kid = "primary-key-1")
        {
            var creds = new SigningCredentials(_ecdsaSecurityKey, SecurityAlgorithms.EcdsaSha256);

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim("sub", userId.ToString()),
                new Claim("email", email),
                new Claim("aud", audience)
            };

            if (!string.IsNullOrEmpty(name))
            {
                claims.Add(new Claim("user_metadata", $"{{\"full_name\":\"{name}\"}}"));
            }

            var token = new JwtSecurityToken(
                issuer: issuer,
                audience: audience,
                claims: claims,
                expires: expiresUtc,
                signingCredentials: creds);

            token.Header["kid"] = kid;

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        private string GenerateHs256Token(Guid userId, string email)
        {
            var symmetricKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("a-dummy-symmetric-key-for-testing-only-1234567890!"));
            var creds = new SigningCredentials(symmetricKey, SecurityAlgorithms.HmacSha256);

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim("sub", userId.ToString()),
                new Claim("email", email),
                new Claim("aud", "authenticated")
            };

            var token = new JwtSecurityToken(
                issuer: $"{TestBaseUrl}/auth/v1",
                audience: "authenticated",
                claims: claims,
                expires: DateTime.UtcNow.AddHours(1),
                signingCredentials: creds);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        [Fact]
        public async Task ValidateTokenAsync_ValidEs256Token_SucceedsWithClaims()
        {
            var config = CreateConfiguration();
            var httpClient = CreateMockHttpClient(_jwksJson);
            var validator = new SupabaseJwtValidator(config, NullLogger<SupabaseJwtValidator>.Instance, httpClient);

            var userId = Guid.NewGuid();
            var token = GenerateEs256Token(userId, "alex@example.com", "Alex Morgan", DateTime.UtcNow.AddHours(1));

            var result = await validator.ValidateTokenAsync(token);

            Assert.True(result.IsValid);
            Assert.Equal(userId, result.UserId);
            Assert.Equal("alex@example.com", result.Email);
            Assert.Equal("Alex Morgan", result.FullName);
        }

        [Fact]
        public async Task ValidateTokenAsync_Hs256Token_IsRejectedDueToDisallowedAlgorithm()
        {
            var config = CreateConfiguration();
            var httpClient = CreateMockHttpClient(_jwksJson);
            var validator = new SupabaseJwtValidator(config, NullLogger<SupabaseJwtValidator>.Instance, httpClient);

            var userId = Guid.NewGuid();
            var hsToken = GenerateHs256Token(userId, "attacker@example.com");

            var result = await validator.ValidateTokenAsync(hsToken);

            Assert.False(result.IsValid);
            Assert.Contains("Only ES256 is supported", result.ErrorMessage);
        }

        [Fact]
        public async Task ValidateTokenAsync_WrongIssuer_IsRejected()
        {
            var config = CreateConfiguration();
            var httpClient = CreateMockHttpClient(_jwksJson);
            var validator = new SupabaseJwtValidator(config, NullLogger<SupabaseJwtValidator>.Instance, httpClient);

            var userId = Guid.NewGuid();
            var tokenWithWrongIssuer = GenerateEs256Token(
                userId,
                "user@example.com",
                "User",
                DateTime.UtcNow.AddHours(1),
                issuer: "https://evil-supabase-clone.co/auth/v1");

            var result = await validator.ValidateTokenAsync(tokenWithWrongIssuer);

            Assert.False(result.IsValid);
            Assert.Contains("invalid issuer", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task ValidateTokenAsync_ExpiredToken_IsRejected()
        {
            var config = CreateConfiguration();
            var httpClient = CreateMockHttpClient(_jwksJson);
            var validator = new SupabaseJwtValidator(config, NullLogger<SupabaseJwtValidator>.Instance, httpClient);

            var userId = Guid.NewGuid();
            var expiredToken = GenerateEs256Token(
                userId,
                "user@example.com",
                "User",
                DateTime.UtcNow.AddMinutes(-10)); // expired outside ClockSkew

            var result = await validator.ValidateTokenAsync(expiredToken);

            Assert.False(result.IsValid);
            Assert.Contains("expired", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task ValidateTokenAsync_RotatedKey_RefreshesJwksAndValidates()
        {
            var config = CreateConfiguration();

            // Create a second rotated key
            using var rotatedEcdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var rotatedKey = new ECDsaSecurityKey(rotatedEcdsa) { KeyId = "rotated-key-2" };
            var rotatedJwk = JsonWebKeyConverter.ConvertFromECDsaSecurityKey(rotatedKey);

            var updatedJwks = new { keys = new[] { JsonSerializer.Deserialize<object>(_jwkJson), JsonSerializer.Deserialize<object>(JsonSerializer.Serialize(rotatedJwk)) } };
            var updatedJwksJson = JsonSerializer.Serialize(updatedJwks);

            var fetchCount = 0;
            var handler = new MockHttpMessageHandler(req =>
            {
                fetchCount++;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(updatedJwksJson, Encoding.UTF8, "application/json")
                };
            });
            var httpClient = new HttpClient(handler);
            var validator = new SupabaseJwtValidator(config, NullLogger<SupabaseJwtValidator>.Instance, httpClient);

            // Generate token signed with the newly rotated key
            var userId = Guid.NewGuid();
            var creds = new SigningCredentials(rotatedKey, SecurityAlgorithms.EcdsaSha256);
            var claims = new[]
            {
                new Claim("sub", userId.ToString()),
                new Claim("email", "rotated@example.com"),
                new Claim("aud", "authenticated")
            };
            var token = new JwtSecurityToken(
                issuer: $"{TestBaseUrl}/auth/v1",
                audience: "authenticated",
                claims: claims,
                expires: DateTime.UtcNow.AddHours(1),
                signingCredentials: creds);
            token.Header["kid"] = "rotated-key-2";
            var tokenStr = new JwtSecurityTokenHandler().WriteToken(token);

            var result = await validator.ValidateTokenAsync(tokenStr);

            Assert.True(result.IsValid);
            Assert.Equal(userId, result.UserId);
            Assert.True(fetchCount >= 1);
        }
    }
}
