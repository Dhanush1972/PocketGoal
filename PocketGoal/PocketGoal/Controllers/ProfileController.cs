using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PocketGoal.Services;

namespace PocketGoal.Controllers
{
    [Authorize]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public class ProfileController : Controller
    {
        private readonly IUserProfileService _profileService;
        private readonly IProfileContextService _profileContext;
        private readonly ILogger<ProfileController> _logger;

        public ProfileController(
            IUserProfileService profileService,
            IProfileContextService profileContext,
            ILogger<ProfileController> logger)
        {
            _profileService = profileService;
            _profileContext = profileContext;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var userId = _profileContext.GetCurrentUserId();
            if (!userId.HasValue) return RedirectToAction("Login", "Account");

            var user = await _profileService.GetUserProfileAsync(userId.Value);
            if (user == null) return RedirectToAction("Login", "Account");

            return View(user);
        }

        [HttpGet]
        [AllowAnonymous]
        public IActionResult Onboarding()
        {
            var activeId = _profileContext.GetCurrentUserId();
            if (activeId.HasValue)
            {
                return RedirectToAction("Index", "Dashboard");
            }

            return RedirectToAction("Signup", "Account");
        }

        [HttpGet]
        [AllowAnonymous]
        public IActionResult Switch()
        {
            return RedirectToAction("Login", "Account");
        }

        [HttpPost]
        [AllowAnonymous]
        public IActionResult Clear()
        {
            return RedirectToAction("Login", "Account");
        }
    }
}
