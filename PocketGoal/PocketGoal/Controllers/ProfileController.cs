using Microsoft.AspNetCore.Mvc;
using PocketGoal.Services;
using PocketGoal.ViewModels;

namespace PocketGoal.Controllers
{
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
        public IActionResult Onboarding()
        {
            // If user already has an active profile, redirect to dashboard
            var activeId = _profileContext.GetCurrentUserId();
            if (activeId.HasValue)
            {
                return RedirectToAction("Index", "Dashboard");
            }

            var model = new OnboardingViewModel
            {
                SavingPeriodMonths = 4,
                TargetPrice = 50000m
            };

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Onboarding(OnboardingViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            try
            {
                var (user, goal) = await _profileService.CreateOnboardingProfileAsync(model);
                _profileContext.SetCurrentUserId(user.Id);

                TempData["SuccessMessage"] = $"Welcome to PocketGoal, {user.Name}! Un goal '{goal.GoalName}' ready aayiduchu 🚀";
                return RedirectToAction("Index", "Dashboard");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating onboarding profile");
                ModelState.AddModelError(string.Empty, "Profile setup pannumbodhu error vandhuduchu da. Marubadiyum try pannunga.");
                return View(model);
            }
        }

        [HttpGet]
        public async Task<IActionResult> Switch()
        {
            var profiles = await _profileService.GetAllProfilesAsync();
            ViewBag.ActiveProfileId = _profileContext.GetCurrentUserId();
            return View(profiles);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SelectProfile(Guid profileId)
        {
            var user = await _profileService.GetUserProfileAsync(profileId);
            if (user != null)
            {
                _profileContext.SetCurrentUserId(user.Id);
                TempData["SuccessMessage"] = $"Profile {user.Name}-ku switch panniyaachu!";
                return RedirectToAction("Index", "Dashboard");
            }

            TempData["ErrorMessage"] = "Profile kidaikkala da.";
            return RedirectToAction("Switch");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> LookupProfile(string emailOrPhone)
        {
            if (string.IsNullOrWhiteSpace(emailOrPhone))
            {
                TempData["ErrorMessage"] = "Email illa phone number enter pannunga.";
                return RedirectToAction("Switch");
            }

            var user = await _profileService.FindByEmailOrPhoneAsync(emailOrPhone);
            if (user != null)
            {
                _profileContext.SetCurrentUserId(user.Id);
                TempData["SuccessMessage"] = $"Welcome back, {user.Name}! 👋";
                return RedirectToAction("Index", "Dashboard");
            }

            TempData["ErrorMessage"] = "Indha email/phone-la profile onnum illa da.";
            return RedirectToAction("Switch");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Clear()
        {
            _profileContext.ClearCurrentUserId();
            TempData["SuccessMessage"] = "Session clear panniyaachu.";
            return RedirectToAction("Onboarding");
        }
    }
}
