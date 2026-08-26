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
        Task SignInAsync(UserProfile user, bool isPersistent = true);
        Task SignOutAsync();
    }

    public class ProfileContextService : IProfileContextService
    {
        private readonly IHttpContextAccessor _httpContextAccessor;
        public const string CookieName = "PocketGoal_ActiveProfileId";
        public const string SessionKey = "PocketGoal_ActiveProfileId";

        public ProfileContextService(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        public Guid? GetCurrentUserId()
        {
            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext == null) return null;

            // 1. Check ClaimsPrincipal (ASP.NET Core Cookie Auth)
            var claimVal = httpContext.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!string.IsNullOrEmpty(claimVal) && Guid.TryParse(claimVal, out var claimGuid))
            {
                return claimGuid;
            }

            // 2. Check Session
            var sessionVal = httpContext.Session.GetString(SessionKey);
            if (Guid.TryParse(sessionVal, out var sessionGuid))
            {
                return sessionGuid;
            }

            // 3. Check Cookie
            if (httpContext.Request.Cookies.TryGetValue(CookieName, out var cookieVal) && Guid.TryParse(cookieVal, out var cookieGuid))
            {
                httpContext.Session.SetString(SessionKey, cookieGuid.ToString());
                return cookieGuid;
            }

            return null;
        }

        public void SetCurrentUserId(Guid userId)
        {
            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext == null) return;

            httpContext.Session.SetString(SessionKey, userId.ToString());

            httpContext.Response.Cookies.Append(CookieName, userId.ToString(), new CookieOptions
            {
                HttpOnly = true,
                Secure = httpContext.Request.IsHttps,
                SameSite = SameSiteMode.Lax,
                Expires = DateTimeOffset.UtcNow.AddYears(1)
            });
        }

        public async Task SignInAsync(UserProfile user, bool isPersistent = true)
        {
            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext == null) return;

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.Name),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim(ClaimTypes.MobilePhone, user.PhoneNumber)
            };

            var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var claimsPrincipal = new ClaimsPrincipal(claimsIdentity);

            var authProperties = new AuthenticationProperties
            {
                IsPersistent = isPersistent,
                ExpiresUtc = DateTimeOffset.UtcNow.AddDays(30)
            };

            await httpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, claimsPrincipal, authProperties);

            // Also keep Session & Cookie synced
            SetCurrentUserId(user.Id);
        }

        public async Task SignOutAsync()
        {
            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext != null)
            {
                await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            }

            ClearCurrentUserId();
        }

        public void ClearCurrentUserId()
        {
            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext == null) return;

            httpContext.Session.Remove(SessionKey);
            httpContext.Response.Cookies.Delete(CookieName);
        }
    }
}
