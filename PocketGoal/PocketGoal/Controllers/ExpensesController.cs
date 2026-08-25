using Microsoft.AspNetCore.Mvc;
using PocketGoal.Services;
using PocketGoal.ViewModels;

namespace PocketGoal.Controllers
{
    public class ExpensesController : Controller
    {
        private readonly IExpenseService _expenseService;
        private readonly IProfileContextService _profileContext;

        public ExpensesController(
            IExpenseService expenseService,
            IProfileContextService profileContext)
        {
            _expenseService = expenseService;
            _profileContext = profileContext;
        }

        private Guid? EnsureAuthenticated() => _profileContext.GetCurrentUserId();

        [HttpGet]
        public async Task<IActionResult> Index(int? month, int? year, Guid? categoryId)
        {
            var userId = EnsureAuthenticated();
            if (!userId.HasValue) return RedirectToAction("Onboarding", "Profile");

            var model = await _expenseService.GetExpensesIndexAsync(userId.Value, month, year, categoryId);
            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> Create()
        {
            var userId = EnsureAuthenticated();
            if (!userId.HasValue) return RedirectToAction("Onboarding", "Profile");

            var model = await _expenseService.GetCreateViewModelAsync(userId.Value);
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(ExpenseCreateEditViewModel model)
        {
            var userId = EnsureAuthenticated();
            if (!userId.HasValue) return RedirectToAction("Onboarding", "Profile");

            if (!ModelState.IsValid)
            {
                var refreshed = await _expenseService.GetCreateViewModelAsync(userId.Value);
                model.CategoryOptions = refreshed.CategoryOptions;
                return View(model);
            }

            var (success, error, expense) = await _expenseService.CreateExpenseAsync(userId.Value, model);
            if (!success)
            {
                ModelState.AddModelError(string.Empty, error ?? "Expense save panna mudiyala da.");
                var refreshed = await _expenseService.GetCreateViewModelAsync(userId.Value);
                model.CategoryOptions = refreshed.CategoryOptions;
                return View(model);
            }

            TempData["SuccessMessage"] = $"₹{model.Amount:N2} expense add panniyaachu! ✅";
            return RedirectToAction("Index");
        }

        [HttpGet]
        public async Task<IActionResult> Edit(Guid id)
        {
            var userId = EnsureAuthenticated();
            if (!userId.HasValue) return RedirectToAction("Onboarding", "Profile");

            var model = await _expenseService.GetEditViewModelAsync(id, userId.Value);
            if (model == null)
            {
                TempData["ErrorMessage"] = "Expense kidaikkala illa access panna mudiyala da.";
                return RedirectToAction("Index");
            }

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(ExpenseCreateEditViewModel model)
        {
            var userId = EnsureAuthenticated();
            if (!userId.HasValue) return RedirectToAction("Onboarding", "Profile");

            if (!ModelState.IsValid)
            {
                var categories = await _expenseService.GetCategoriesForUserAsync(userId.Value);
                model.CategoryOptions = categories.Select(c => new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem
                {
                    Value = c.Id.ToString(),
                    Text = c.Name,
                    Selected = c.Id == model.CategoryId
                }).ToList();
                return View(model);
            }

            var (success, error) = await _expenseService.UpdateExpenseAsync(userId.Value, model);
            if (!success)
            {
                ModelState.AddModelError(string.Empty, error ?? "Expense update panna mudiyala da.");
                var categories = await _expenseService.GetCategoriesForUserAsync(userId.Value);
                model.CategoryOptions = categories.Select(c => new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem
                {
                    Value = c.Id.ToString(),
                    Text = c.Name,
                    Selected = c.Id == model.CategoryId
                }).ToList();
                return View(model);
            }

            TempData["SuccessMessage"] = "Expense update panniyaachu! ✅";
            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(Guid id)
        {
            var userId = EnsureAuthenticated();
            if (!userId.HasValue) return RedirectToAction("Onboarding", "Profile");

            var success = await _expenseService.DeleteExpenseAsync(id, userId.Value);
            if (success)
            {
                TempData["SuccessMessage"] = "Expense delete panniyaachu.";
            }
            else
            {
                TempData["ErrorMessage"] = "Expense kidaikkala illa delete panna mudiyala da.";
            }

            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateCategory(CreateCategoryViewModel model)
        {
            var userId = EnsureAuthenticated();
            if (!userId.HasValue) return RedirectToAction("Onboarding", "Profile");

            if (!ModelState.IsValid)
            {
                TempData["ErrorMessage"] = "Category name compulsory da.";
                return RedirectToAction("Index");
            }

            await _expenseService.CreateCustomCategoryAsync(userId.Value, model);
            TempData["SuccessMessage"] = $"Custom category '{model.Name}' create panniyaachu! 🚀";
            return RedirectToAction("Index");
        }
    }
}
