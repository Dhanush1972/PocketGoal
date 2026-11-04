using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using PocketGoal.Models;

namespace PocketGoal.Services
{
    public interface IProfileContextService
    {
        Guid? GetCurrentUserId();
        void SetCurrentUserId(Guid userId);
        void ClearCurrentUserId();
        Task SignInAsync(UserProfile user, bool isPersistent = false, DateTimeOffset? expiresUtc = null);
        Task SignOutAsync();
    }

    public class ProfileContextService : IProfileContextService
    {
        private readonly IHttpContextAccessor _httpContextAccessor;

        public ProfileContextService(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        public Guid? GetCurrentUserId()
        {
            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext == null) return null;

            // Identity comes ONLY from the authenticated ClaimsPrincipal
            var claimVal = httpContext.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!string.IsNullOrEmpty(claimVal) && Guid.TryParse(claimVal, out var claimGuid))
            {
                return claimGuid;
            }

            return null;
        }

        public void SetCurrentUserId(Guid userId)
        {
            // Identity is strictly managed via ClaimsPrincipal authentication
        }

        public async Task SignInAsync(UserProfile user, bool isPersistent = false, DateTimeOffset? expiresUtc = null)
        {
            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext == null) return;

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.Name),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim(ClaimTypes.MobilePhone, user.PhoneNumber ?? string.Empty)
            };

            var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var claimsPrincipal = new ClaimsPrincipal(claimsIdentity);

            var authProperties = new AuthenticationProperties
            {
                IsPersistent = isPersistent
            };

            if (isPersistent)
            {
                // Persist for at most 14 days, non-sliding (capped total lifetime)
                authProperties.ExpiresUtc = expiresUtc ?? DateTimeOffset.UtcNow.AddDays(14);
                authProperties.AllowRefresh = false;
            }
            else
            {
                // Session cookie: disappears when browser closes, with 30-min idle timeout
                authProperties.ExpiresUtc = null;
                authProperties.AllowRefresh = true;
            }

            await httpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, claimsPrincipal, authProperties);
        }

        public async Task SignOutAsync()
        {
            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext != null)
            {
                await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            }
        }

        public void ClearCurrentUserId()
        {
            // Handled via SignOutAsync
        }
    }
}
