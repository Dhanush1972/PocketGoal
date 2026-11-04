using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PocketGoal.Services;

namespace PocketGoal.Controllers
{
    [Authorize]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
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
                return RedirectToAction("Login", "Account");
            }

            var dashboardData = await _dashboardService.GetDashboardDataAsync(userId.Value);
            if (dashboardData == null)
            {
                await _profileContext.SignOutAsync();
                return RedirectToAction("Login", "Account");
            }

            return View(dashboardData);
        }
    }
}
