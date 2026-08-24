using Microsoft.AspNetCore.Mvc;
using PocketGoal.Services;
using PocketGoal.ViewModels;

namespace PocketGoal.Controllers
{
    public class SavingsController : Controller
    {
        private readonly ISavingsService _savingsService;
        private readonly ISavingGoalService _savingGoalService;
        private readonly IProfileContextService _profileContext;

        public SavingsController(
            ISavingsService savingsService,
            ISavingGoalService savingGoalService,
            IProfileContextService profileContext)
        {
            _savingsService = savingsService;
            _savingGoalService = savingGoalService;
            _profileContext = profileContext;
        }

        private Guid? EnsureAuthenticated() => _profileContext.GetCurrentUserId();

        [HttpGet]
        public async Task<IActionResult> Add(Guid goalId)
        {
            var userId = EnsureAuthenticated();
            if (!userId.HasValue) return RedirectToAction("Onboarding", "Profile");

            var goal = await _savingGoalService.GetGoalDetailsAsync(goalId, userId.Value);
            if (goal == null)
            {
                TempData["ErrorMessage"] = "Goal not found.";
                return RedirectToAction("Index", "SavingGoals");
            }

            var model = new AddSavingViewModel
            {
                SavingGoalId = goal.Id,
                GoalName = goal.GoalName,
                TargetAmount = goal.TargetAmount,
                CurrentSavedAmount = goal.CurrentSavedAmount,
                SavedDate = DateTime.UtcNow,
                Amount = goal.RequiredMonthlySaving > 0 ? Math.Min(goal.RequiredMonthlySaving, goal.RemainingAmount) : Math.Min(1000m, goal.RemainingAmount)
            };

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Add(AddSavingViewModel model)
        {
            var userId = EnsureAuthenticated();
            if (!userId.HasValue) return RedirectToAction("Onboarding", "Profile");

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var (success, errorMessage, saving) = await _savingsService.AddSavingAsync(userId.Value, model);
            if (!success)
            {
                ModelState.AddModelError(string.Empty, errorMessage ?? "Failed to record savings.");
                return View(model);
            }

            TempData["SuccessMessage"] = $"₹{model.Amount:N2} added to your savings for '{model.GoalName}'!";
            return RedirectToAction("Details", "SavingGoals", new { id = model.SavingGoalId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(Guid id, Guid goalId)
        {
            var userId = EnsureAuthenticated();
            if (!userId.HasValue) return RedirectToAction("Onboarding", "Profile");

            var success = await _savingsService.DeleteSavingAsync(id, userId.Value);
            if (success)
            {
                TempData["SuccessMessage"] = "Saving deposit transaction deleted.";
            }
            else
            {
                TempData["ErrorMessage"] = "Transaction not found or could not be deleted.";
            }

            return RedirectToAction("Details", "SavingGoals", new { id = goalId });
        }
    }
}
