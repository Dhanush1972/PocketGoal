using Microsoft.AspNetCore.Mvc;
using PocketGoal.Models;
using PocketGoal.Services;
using PocketGoal.ViewModels;

namespace PocketGoal.Controllers
{
    public class SavingGoalsController : Controller
    {
        private readonly ISavingGoalService _savingGoalService;
        private readonly IProfileContextService _profileContext;

        public SavingGoalsController(
            ISavingGoalService savingGoalService,
            IProfileContextService profileContext)
        {
            _savingGoalService = savingGoalService;
            _profileContext = profileContext;
        }

        private Guid? EnsureAuthenticated() => _profileContext.GetCurrentUserId();

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var userId = EnsureAuthenticated();
            if (!userId.HasValue) return RedirectToAction("Onboarding", "Profile");

            var goals = await _savingGoalService.GetGoalsForUserAsync(userId.Value);
            return View(goals);
        }

        [HttpGet]
        public async Task<IActionResult> Details(Guid id)
        {
            var userId = EnsureAuthenticated();
            if (!userId.HasValue) return RedirectToAction("Onboarding", "Profile");

            var goal = await _savingGoalService.GetGoalDetailsAsync(id, userId.Value);
            if (goal == null)
            {
                TempData["ErrorMessage"] = "Goal kidaikkala illa access panna mudiyala da.";
                return RedirectToAction("Index");
            }

            return View(goal);
        }

        [HttpGet]
        public IActionResult Create()
        {
            var userId = EnsureAuthenticated();
            if (!userId.HasValue) return RedirectToAction("Onboarding", "Profile");

            var model = new SavingGoalCreateEditViewModel
            {
                TargetDate = DateTime.UtcNow.AddMonths(6)
            };
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(SavingGoalCreateEditViewModel model)
        {
            var userId = EnsureAuthenticated();
            if (!userId.HasValue) return RedirectToAction("Onboarding", "Profile");

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var goal = await _savingGoalService.CreateGoalAsync(userId.Value, model);
            TempData["SuccessMessage"] = $"Savings goal '{goal.GoalName}' successfully create aayiduchu! 🚀";
            return RedirectToAction("Details", new { id = goal.Id });
        }

        [HttpGet]
        public async Task<IActionResult> Edit(Guid id)
        {
            var userId = EnsureAuthenticated();
            if (!userId.HasValue) return RedirectToAction("Onboarding", "Profile");

            var goal = await _savingGoalService.GetGoalDetailsAsync(id, userId.Value);
            if (goal == null)
            {
                TempData["ErrorMessage"] = "Goal kidaikkala illa access panna mudiyala da.";
                return RedirectToAction("Index");
            }

            var model = new SavingGoalCreateEditViewModel
            {
                Id = goal.Id,
                GoalName = goal.GoalName,
                Description = goal.Description,
                TargetAmount = goal.TargetAmount,
                TargetDate = goal.TargetDate,
                Status = goal.Status
            };

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(SavingGoalCreateEditViewModel model)
        {
            var userId = EnsureAuthenticated();
            if (!userId.HasValue) return RedirectToAction("Onboarding", "Profile");

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var success = await _savingGoalService.UpdateGoalAsync(userId.Value, model);
            if (!success)
            {
                TempData["ErrorMessage"] = "Goal update panna mudiyala da.";
                return RedirectToAction("Index");
            }

            TempData["SuccessMessage"] = $"Goal '{model.GoalName}' successfully update aayiduchu ✅";
            return RedirectToAction("Details", new { id = model.Id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(Guid id)
        {
            var userId = EnsureAuthenticated();
            if (!userId.HasValue) return RedirectToAction("Onboarding", "Profile");

            var success = await _savingGoalService.DeleteGoalAsync(id, userId.Value);
            if (success)
            {
                TempData["SuccessMessage"] = "Goal and adhoda savings history delete panniyaachu.";
            }
            else
            {
                TempData["ErrorMessage"] = "Goal kidaikkala illa delete panna mudiyala da.";
            }

            return RedirectToAction("Index");
        }
    }
}
