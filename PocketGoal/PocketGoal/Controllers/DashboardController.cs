using Microsoft.AspNetCore.Mvc;
using PocketGoal.Services;

namespace PocketGoal.Controllers
{
    public class DashboardController : Controller
    {
        private readonly IDashboardService _dashboardService;
        private readonly IProfileContextService _profileContext;

        public DashboardController(
            IDashboardService dashboardService,
            IProfileContextService profileContext)
        {
            _dashboardService = dashboardService;
            _profileContext = profileContext;
        }

        public async Task<IActionResult> Index()
        {
            var userId = _profileContext.GetCurrentUserId();
            if (!userId.HasValue)
            {
                return RedirectToAction("Onboarding", "Profile");
            }

            var dashboardData = await _dashboardService.GetDashboardDataAsync(userId.Value);
            if (dashboardData == null)
            {
                _profileContext.ClearCurrentUserId();
                return RedirectToAction("Onboarding", "Profile");
            }

            return View(dashboardData);
        }
    }
}
