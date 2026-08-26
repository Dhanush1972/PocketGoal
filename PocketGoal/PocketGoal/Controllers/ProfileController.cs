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
            // If user already has an active authenticated profile, redirect to dashboard
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
                await _profileContext.SignInAsync(user);

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
            var activeId = _profileContext.GetCurrentUserId();

            var viewModel = new ProfileSwitchViewModel
            {
                AvailableProfiles = profiles,
                ActiveProfileId = activeId
            };

            return View(viewModel);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SelectProfile(Guid profileId, string switchPassword)
        {
            if (profileId == Guid.Empty)
            {
                TempData["ErrorMessage"] = "Profile select pannunga.";
                return RedirectToAction("Switch");
            }

            var (success, user, errorMessage) = await _profileService.ValidateProfileSwitchAsync(profileId, switchPassword);
            if (success && user != null)
            {
                await _profileContext.SignInAsync(user);
                TempData["SuccessMessage"] = $"Profile {user.Name}-ku switch panniyaachu!";
                return RedirectToAction("Index", "Dashboard");
            }

            TempData["ErrorMessage"] = errorMessage ?? "Profile switch panna mudila. Sariyaana password enter pannunga.";
            return RedirectToAction("Switch");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> LookupProfile(string emailOrPhone, string lookupPassword)
        {
            if (string.IsNullOrWhiteSpace(emailOrPhone))
            {
                TempData["ErrorMessage"] = "Email illa phone number enter pannunga.";
                return RedirectToAction("Switch");
            }

            if (string.IsNullOrWhiteSpace(lookupPassword))
            {
                TempData["ErrorMessage"] = "Password enter pannunga.";
                return RedirectToAction("Switch");
            }

            var (success, user, errorMessage) = await _profileService.AuthenticateAsync(emailOrPhone, lookupPassword);
            if (success && user != null)
            {
                await _profileContext.SignInAsync(user);
                TempData["SuccessMessage"] = $"Welcome back, {user.Name}! 👋";
                return RedirectToAction("Index", "Dashboard");
            }

            TempData["ErrorMessage"] = errorMessage ?? "Indha credentials-la profile kidaikkala da.";
            return RedirectToAction("Switch");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Clear()
        {
            await _profileContext.SignOutAsync();
            TempData["SuccessMessage"] = "Successfully signed out / session clear panniyaachu.";
            return RedirectToAction("Switch");
        }
    }
}
