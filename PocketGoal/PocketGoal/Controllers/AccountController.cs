using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PocketGoal.Models;
using PocketGoal.Services;

namespace PocketGoal.Controllers
{
    public class SyncSessionRequest
    {
        public string AccessToken { get; set; } = string.Empty;
        public string? RefreshToken { get; set; }
    }

    [AllowAnonymous]
    public class AccountController : Controller
    {
        private readonly ISupabaseJwtValidator _jwtValidator;
        private readonly IUserProfileService _profileService;
        private readonly IProfileContextService _profileContext;
        private readonly IConfiguration _configuration;
        private readonly ILogger<AccountController> _logger;

        public AccountController(
            ISupabaseJwtValidator jwtValidator,
            IUserProfileService profileService,
            IProfileContextService profileContext,
            IConfiguration configuration,
            ILogger<AccountController> logger)
        {
            _jwtValidator = jwtValidator;
            _profileService = profileService;
            _profileContext = profileContext;
            _configuration = configuration;
            _logger = logger;
        }

        [HttpGet]
        public IActionResult Login(string? returnUrl = null, string? mode = null)
        {
            var userId = _profileContext.GetCurrentUserId();
            if (userId.HasValue)
            {
                return RedirectToAction("Index", "Dashboard");
            }

            ViewData["ReturnUrl"] = returnUrl;
            ViewData["InitialMode"] = mode == "signup" ? "signup" : "login";
            ViewData["SupabaseUrl"] = (_configuration["Supabase:Url"] ?? Environment.GetEnvironmentVariable("SUPABASE_URL") ?? string.Empty).TrimEnd('/');
            ViewData["SupabaseAnonKey"] = _configuration["Supabase:AnonKey"] ?? Environment.GetEnvironmentVariable("SUPABASE_ANON_KEY") ?? string.Empty;

            return View();
        }

        [HttpGet]
        public IActionResult Signup(string? returnUrl = null)
        {
            return RedirectToAction("Login", new { returnUrl, mode = "signup" });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SyncSession([FromBody] SyncSessionRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.AccessToken))
            {
                return BadRequest(new { success = false, message = "Access token illa da." });
            }

            var validation = await _jwtValidator.ValidateTokenAsync(request.AccessToken);
            if (!validation.IsValid || !validation.UserId.HasValue)
            {
                _logger.LogWarning("Supabase JWT validation failed: {ErrorMessage}", validation.ErrorMessage);
                return Unauthorized(new { success = false, message = validation.ErrorMessage ?? "Session verify panna mudila da." });
            }

            try
            {
                var user = await _profileService.EnsureUserProfileAsync(
                    validation.UserId.Value,
                    validation.Email ?? string.Empty,
                    validation.FullName,
                    validation.PhoneNumber);

                DateTimeOffset? expiresUtc = validation.ExpiresUtc.HasValue 
                    ? new DateTimeOffset(validation.ExpiresUtc.Value) 
                    : null;

                await _profileContext.SignInAsync(user, isPersistent: true, expiresUtc: expiresUtc);

                _logger.LogInformation("Supabase session synced successfully for {Email} ({UserId})", user.Email, user.Id);
                return Ok(new
                {
                    success = true,
                    userName = user.Name,
                    redirectUrl = Url.Action("Index", "Dashboard")
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error syncing profile for user {UserId}", validation.UserId);
                return StatusCode(500, new { success = false, message = "Profile sync pannumbodhu error vandhuduchu." });
            }
        }

        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public async Task<IActionResult> Logout()
        {
            Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate, max-age=0";
            Response.Headers["Pragma"] = "no-cache";

            await _profileContext.SignOutAsync();

            // Explicitly delete authentication cookies
            Response.Cookies.Delete(CookieAuthenticationDefaults.AuthenticationScheme);
            Response.Cookies.Delete(".AspNetCore.Cookies");

            TempData["SuccessMessage"] = "Successfully signed out panniyaachu! Marubadiyum vaanga 👋";
            return RedirectToAction("Login", new { loggedOut = "true" });
        }
    }
}
