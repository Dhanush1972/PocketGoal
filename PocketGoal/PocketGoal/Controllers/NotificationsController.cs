using Microsoft.AspNetCore.Mvc;
using PocketGoal.Services;
using PocketGoal.ViewModels;

namespace PocketGoal.Controllers
{
    public class NotificationsController : Controller
    {
        private readonly INotificationsService _notificationsService;
        private readonly IUserProfileService _userProfileService;
        private readonly IProfileContextService _profileContext;

        public NotificationsController(
            INotificationsService notificationsService,
            IUserProfileService userProfileService,
            IProfileContextService profileContext)
        {
            _notificationsService = notificationsService;
            _userProfileService = userProfileService;
            _profileContext = profileContext;
        }

        private Guid? EnsureAuthenticated() => _profileContext.GetCurrentUserId();

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var userId = EnsureAuthenticated();
            if (!userId.HasValue) return RedirectToAction("Onboarding", "Profile");

            var user = await _userProfileService.GetUserProfileAsync(userId.Value);
            if (user == null) return RedirectToAction("Onboarding", "Profile");

            var reminders = await _notificationsService.GenerateActiveRemindersForUserAsync(userId.Value);

            var model = new NotificationCenterViewModel
            {
                User = user,
                GeneratedReminders = reminders,
                EmailNotificationsEnabled = true,
                SmsNotificationsEnabled = true,
                SuccessMessage = TempData["SuccessMessage"]?.ToString()
            };

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Trigger(TriggerNotificationRequest request)
        {
            var userId = EnsureAuthenticated();
            if (!userId.HasValue) return RedirectToAction("Onboarding", "Profile");

            var (success, message) = await _notificationsService.TriggerSimulatedNotificationAsync(userId.Value, request);
            if (success)
            {
                TempData["SuccessMessage"] = message;
            }
            else
            {
                TempData["ErrorMessage"] = message;
            }

            return RedirectToAction("Index");
        }
    }
}
