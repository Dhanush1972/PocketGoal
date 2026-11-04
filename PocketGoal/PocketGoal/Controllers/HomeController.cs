using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using PocketGoal.Models;
using PocketGoal.Services;

namespace PocketGoal.Controllers
{
    public class HomeController : Controller
    {
        private readonly IProfileContextService _profileContext;

        public HomeController(IProfileContextService profileContext)
        {
            _profileContext = profileContext;
        }

        public IActionResult Index()
        {
            var userId = _profileContext.GetCurrentUserId();
            if (userId.HasValue)
            {
                return RedirectToAction("Index", "Dashboard");
            }

            return RedirectToAction("Login", "Account");
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
