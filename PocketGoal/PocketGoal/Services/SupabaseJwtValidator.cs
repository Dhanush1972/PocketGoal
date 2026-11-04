using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;

namespace PocketGoal.Services
{
    public class SupabaseTokenValidationResult
    {
        public bool IsValid { get; set; }
        public Guid? UserId { get; set; }
        public string? Email { get; set; }
        public string? FullName { get; set; }
        public string? PhoneNumber { get; set; }
        public DateTime? ExpiresUtc { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public interface ISupabaseJwtValidator
    {
        Task<SupabaseTokenValidationResult> ValidateTokenAsync(string token);
    }

    public class SupabaseJwtValidator : ISupabaseJwtValidator
    {
        private readonly IConfiguration _configuration;
        private readonly HttpClient _httpClient;
        private readonly ILogger<SupabaseJwtValidator> _logger;

        private static readonly SemaphoreSlim _jwksLock = new SemaphoreSlim(1, 1);
        private static JsonWebKeySet? _cachedJwks;
        private static DateTime _jwksCachedAt = DateTime.MinValue;

        public SupabaseJwtValidator(
            IConfiguration configuration,
            ILogger<SupabaseJwtValidator> logger,
            HttpClient? httpClient = null)
        {
            _configuration = configuration;
            _logger = logger;
            _httpClient = httpClient ?? new HttpClient();
        }

        public async Task<SupabaseTokenValidationResult> ValidateTokenAsync(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return new SupabaseTokenValidationResult { IsValid = false, ErrorMessage = "Token is empty." };
            }

            var rawUrl = _configuration["Supabase:Url"] ?? Environment.GetEnvironmentVariable("SUPABASE_URL");
            if (string.IsNullOrWhiteSpace(rawUrl) || !Uri.TryCreate(rawUrl, UriKind.Absolute, out var parsedUri))
            {
                return new SupabaseTokenValidationResult { IsValid = false, ErrorMessage = "Supabase URL is not configured or invalid." };
            }

            var baseUrl = $"{parsedUri.Scheme}://{parsedUri.Authority}";
            var validIssuer = $"{baseUrl}/auth/v1";
            var jwksUri = $"{baseUrl}/auth/v1/.well-known/jwks.json";

            var tokenHandler = new JwtSecurityTokenHandler();
            if (!tokenHandler.CanReadToken(token))
            {
                return new SupabaseTokenValidationResult { IsValid = false, ErrorMessage = "Invalid JWT token structure." };
            }

            JwtSecurityToken jwtToken;
            try
            {
                jwtToken = tokenHandler.ReadJwtToken(token);
            }
            catch (Exception ex)
            {
                return new SupabaseTokenValidationResult { IsValid = false, ErrorMessage = $"Malformed token: {ex.Message}" };
            }

            // Enforce ES256 algorithm only
            if (!string.Equals(jwtToken.Header.Alg, "ES256", StringComparison.OrdinalIgnoreCase))
            {
                return new SupabaseTokenValidationResult
                {
                    IsValid = false,
                    ErrorMessage = $"Invalid token algorithm '{jwtToken.Header.Alg}'. Only ES256 is supported."
                };
            }

            var kid = jwtToken.Header.Kid;

            // Retrieve and resolve keys from JWKS (refresh if unknown kid or expired cache)
            var signingKeys = await ResolveSigningKeysAsync(jwksUri, kid);
            if (signingKeys == null || !signingKeys.Any())
            {
                return new SupabaseTokenValidationResult
                {
                    IsValid = false,
                    ErrorMessage = $"No matching ES256 key found in Supabase JWKS for kid '{kid}'."
                };
            }

            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKeys = signingKeys,
                ValidAlgorithms = new[] { "ES256" },
                ValidateIssuer = true,
                ValidIssuer = validIssuer,
                ValidateAudience = true,
                ValidAudience = "authenticated",
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(2),
                RequireSignedTokens = true,
                RequireExpirationTime = true
            };

            try
            {
                var principal = tokenHandler.ValidateToken(token, validationParameters, out var validatedToken);
                if (validatedToken is not JwtSecurityToken validJwt)
                {
                    return new SupabaseTokenValidationResult { IsValid = false, ErrorMessage = "Invalid token type." };
                }

                var subClaim = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value ??
                               principal.FindFirst("sub")?.Value;

                if (string.IsNullOrEmpty(subClaim) || !Guid.TryParse(subClaim, out var userId))
                {
                    return new SupabaseTokenValidationResult { IsValid = false, ErrorMessage = "Token does not contain a valid user UUID (sub)." };
                }

                var emailClaim = principal.FindFirst(ClaimTypes.Email)?.Value ??
                                 principal.FindFirst("email")?.Value ??
                                 string.Empty;

                string? fullName = null;
                string? phone = principal.FindFirst("phone")?.Value;

                var userMetadataClaim = validJwt.Claims.FirstOrDefault(c => c.Type == "user_metadata")?.Value;
                if (!string.IsNullOrEmpty(userMetadataClaim))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(userMetadataClaim);
                        if (doc.RootElement.TryGetProperty("full_name", out var fn))
                        {
                            fullName = fn.GetString();
                        }
                        else if (doc.RootElement.TryGetProperty("name", out var n))
                        {
                            fullName = n.GetString();
                        }

                        if (string.IsNullOrEmpty(phone) && doc.RootElement.TryGetProperty("phone", out var p))
                        {
                            phone = p.GetString();
                        }
                    }
                    catch
                    {
                        // Ignore metadata parse exceptions
                    }
                }

                return new SupabaseTokenValidationResult
                {
                    IsValid = true,
                    UserId = userId,
                    Email = emailClaim,
                    FullName = fullName,
                    PhoneNumber = phone,
                    ExpiresUtc = validJwt.ValidTo
                };
            }
            catch (SecurityTokenExpiredException)
            {
                return new SupabaseTokenValidationResult { IsValid = false, ErrorMessage = "Token has expired." };
            }
            catch (SecurityTokenInvalidIssuerException)
            {
                return new SupabaseTokenValidationResult { IsValid = false, ErrorMessage = "Token has an invalid issuer." };
            }
            catch (SecurityTokenInvalidAudienceException)
            {
                return new SupabaseTokenValidationResult { IsValid = false, ErrorMessage = "Token has an invalid audience." };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Supabase JWT validation failed");
                return new SupabaseTokenValidationResult { IsValid = false, ErrorMessage = $"Token validation failed: {ex.Message}" };
            }
        }

        private async Task<List<SecurityKey>> ResolveSigningKeysAsync(string jwksUri, string? kid)
        {
            // 1. Check if key is available in cache
            if (_cachedJwks != null && (DateTime.UtcNow - _jwksCachedAt < TimeSpan.FromHours(1)))
            {
                var keys = GetMatchingKeys(_cachedJwks, kid);
                if (keys.Any())
                {
                    return keys;
                }
            }

            // 2. Fetch or refresh JWKS with thread safety
            await _jwksLock.WaitAsync();
            try
            {
                // Double check cache under lock
                if (_cachedJwks != null && (DateTime.UtcNow - _jwksCachedAt < TimeSpan.FromSeconds(30)))
                {
                    var keys = GetMatchingKeys(_cachedJwks, kid);
                    if (keys.Any())
                    {
                        return keys;
                    }
                }

                _logger.LogInformation("Fetching fresh Supabase JWKS from {JwksUri} for kid '{Kid}'", jwksUri, kid);
                var jwksJson = await _httpClient.GetStringAsync(jwksUri);
                _cachedJwks = new JsonWebKeySet(jwksJson);
                _jwksCachedAt = DateTime.UtcNow;

                return GetMatchingKeys(_cachedJwks, kid);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load Supabase JWKS from {JwksUri}", jwksUri);
                return new List<SecurityKey>();
            }
            finally
            {
                _jwksLock.Release();
            }
        }

        private static List<SecurityKey> GetMatchingKeys(JsonWebKeySet jwks, string? kid)
        {
            var result = new List<SecurityKey>();
            var allKeys = jwks.GetSigningKeys();

            foreach (var key in allKeys)
            {
                // If kid is specified, match by kid
                if (!string.IsNullOrEmpty(kid))
                {
                    if (string.Equals(key.KeyId, kid, StringComparison.Ordinal))
                    {
                        result.Add(key);
                    }
                }
                else
                {
                    result.Add(key);
                }
            }

            return result;
        }

        // Test helper to allow setting or clearing cache during unit tests
        public static void ResetCacheForTesting(JsonWebKeySet? jwks = null)
        {
            _cachedJwks = jwks;
            _jwksCachedAt = jwks != null ? DateTime.UtcNow : DateTime.MinValue;
        }
    }
}
