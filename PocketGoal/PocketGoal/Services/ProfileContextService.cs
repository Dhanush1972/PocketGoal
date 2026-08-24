using System.Security.Claims;

namespace PocketGoal.Services
{
    public interface IProfileContextService
    {
        Guid? GetCurrentUserId();
        void SetCurrentUserId(Guid userId);
        void ClearCurrentUserId();
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

            // 1. Check Session
            var sessionVal = httpContext.Session.GetString(SessionKey);
            if (Guid.TryParse(sessionVal, out var sessionGuid))
            {
                return sessionGuid;
            }

            // 2. Check Cookie
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

        public void ClearCurrentUserId()
        {
            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext == null) return;

            httpContext.Session.Remove(SessionKey);
            httpContext.Response.Cookies.Delete(CookieName);
        }
    }
}
