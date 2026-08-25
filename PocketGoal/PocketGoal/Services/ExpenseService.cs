using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using PocketGoal.Data;
using PocketGoal.Helpers;
using PocketGoal.Models;
using PocketGoal.ViewModels;

namespace PocketGoal.Services
{
    public interface IExpenseService
    {
        Task<ExpenseIndexViewModel> GetExpensesIndexAsync(Guid userId, int? month, int? year, Guid? categoryId);
        Task<ExpenseCreateEditViewModel> GetCreateViewModelAsync(Guid userId);
        Task<ExpenseCreateEditViewModel?> GetEditViewModelAsync(Guid expenseId, Guid userId);
        Task<(bool Success, string? ErrorMessage, Expense? Expense)> CreateExpenseAsync(Guid userId, ExpenseCreateEditViewModel model);
        Task<(bool Success, string? ErrorMessage)> UpdateExpenseAsync(Guid userId, ExpenseCreateEditViewModel model);
        Task<bool> DeleteExpenseAsync(Guid expenseId, Guid userId);
        Task<List<ExpenseCategory>> GetCategoriesForUserAsync(Guid userId);
        Task<ExpenseCategory> CreateCustomCategoryAsync(Guid userId, CreateCategoryViewModel model);
        Task<List<ExpenseItemViewModel>> GetRecentExpensesForUserAsync(Guid userId, int count = 10);
        Task<List<ExpenseCategorySummaryViewModel>> GetCategorySummariesAsync(Guid userId, int month, int year);
    }

    public class ExpenseService : IExpenseService
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly ILogger<ExpenseService> _logger;

        private static DateTime EnsureUtc(DateTime dt) =>
            dt.Kind == DateTimeKind.Utc ? dt : DateTime.SpecifyKind(dt, DateTimeKind.Utc);

        public ExpenseService(ApplicationDbContext dbContext, ILogger<ExpenseService> logger)
        {
            _dbContext = dbContext;
            _logger = logger;
        }

        public async Task<ExpenseIndexViewModel> GetExpensesIndexAsync(Guid userId, int? month, int? year, Guid? categoryId)
        {
            var now = DateTime.UtcNow;
            var targetMonth = month ?? now.Month;
            var targetYear = year ?? now.Year;

            var startOfMonth = new DateTime(targetYear, targetMonth, 1, 0, 0, 0, DateTimeKind.Utc);
            var endOfMonth = startOfMonth.AddMonths(1);

            var prevMonthStart = startOfMonth.AddMonths(-1);
            var prevMonthEnd = startOfMonth;

            var todayStart = now.Date;
            var todayEnd = todayStart.AddDays(1);

            // Query expenses for user
            var baseQuery = _dbContext.Expenses
                .Include(e => e.Category)
                .Where(e => e.UserId == userId);

            // Fetch summary stats
            var currentMonthTotal = await baseQuery
                .Where(e => e.ExpenseDate >= startOfMonth && e.ExpenseDate < endOfMonth)
                .SumAsync(e => (decimal?)e.Amount) ?? 0m;

            var prevMonthTotal = await baseQuery
                .Where(e => e.ExpenseDate >= prevMonthStart && e.ExpenseDate < prevMonthEnd)
                .SumAsync(e => (decimal?)e.Amount) ?? 0m;

            var todayTotal = await baseQuery
                .Where(e => e.ExpenseDate >= todayStart && e.ExpenseDate < todayEnd)
                .SumAsync(e => (decimal?)e.Amount) ?? 0m;

            var allTimeTotal = await baseQuery
                .SumAsync(e => (decimal?)e.Amount) ?? 0m;

            // Filter for current view table
            var filteredQuery = baseQuery.Where(e => e.ExpenseDate >= startOfMonth && e.ExpenseDate < endOfMonth);
            if (categoryId.HasValue && categoryId.Value != Guid.Empty)
            {
                filteredQuery = filteredQuery.Where(e => e.CategoryId == categoryId.Value);
            }

            var expenseList = await filteredQuery
                .OrderByDescending(e => e.ExpenseDate)
                .ThenByDescending(e => e.CreatedAt)
                .Select(e => new ExpenseItemViewModel
                {
                    Id = e.Id,
                    CategoryId = e.CategoryId,
                    CategoryName = e.Category != null ? e.Category.Name : "Uncategorized",
                    CategoryIcon = e.Category != null && !string.IsNullOrEmpty(e.Category.Icon) ? e.Category.Icon : "bi-tag",
                    CategoryColor = e.Category != null && !string.IsNullOrEmpty(e.Category.ColorHex) ? e.Category.ColorHex : "#4f46e5",
                    Amount = e.Amount,
                    ExpenseDate = e.ExpenseDate,
                    Description = e.Description,
                    CreatedAt = e.CreatedAt
                })
                .ToListAsync();

            // Category Summaries for current month
            var categorySummaries = await GetCategorySummariesAsync(userId, targetMonth, targetYear);

            // Categories options
            var categories = await GetCategoriesForUserAsync(userId);
            var categoryOptions = categories.Select(c => new SelectListItem
            {
                Value = c.Id.ToString(),
                Text = c.Name,
                Selected = categoryId == c.Id
            }).ToList();

            return new ExpenseIndexViewModel
            {
                Expenses = expenseList,
                CategorySummaries = categorySummaries,
                CurrentMonthTotal = currentMonthTotal,
                PreviousMonthTotal = prevMonthTotal,
                TodayTotal = todayTotal,
                AllTimeTotal = allTimeTotal,
                SelectedMonth = targetMonth,
                SelectedYear = targetYear,
                SelectedCategoryId = categoryId,
                CategoryOptions = categoryOptions
            };
        }

        public async Task<List<ExpenseCategorySummaryViewModel>> GetCategorySummariesAsync(Guid userId, int month, int year)
        {
            var startOfMonth = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
            var endOfMonth = startOfMonth.AddMonths(1);

            var expenses = await _dbContext.Expenses
                .Include(e => e.Category)
                .Where(e => e.UserId == userId && e.ExpenseDate >= startOfMonth && e.ExpenseDate < endOfMonth)
                .ToListAsync();

            var totalMonthExpense = expenses.Sum(e => e.Amount);

            var grouped = expenses
                .GroupBy(e => new
                {
                    CategoryId = e.CategoryId,
                    Name = e.Category?.Name ?? "Other",
                    Icon = e.Category?.Icon ?? "bi-tag",
                    ColorHex = e.Category?.ColorHex ?? "#6366f1"
                })
                .Select(g => new ExpenseCategorySummaryViewModel
                {
                    CategoryId = g.Key.CategoryId,
                    CategoryName = g.Key.Name,
                    CategoryIcon = g.Key.Icon,
                    CategoryColor = g.Key.ColorHex,
                    TotalAmount = g.Sum(x => x.Amount),
                    ExpenseCount = g.Count(),
                    PercentageOfTotal = totalMonthExpense > 0 ? Math.Round((g.Sum(x => x.Amount) / totalMonthExpense) * 100m, 1) : 0m
                })
                .OrderByDescending(s => s.TotalAmount)
                .ToList();

            return grouped;
        }

        public async Task<List<ExpenseCategory>> GetCategoriesForUserAsync(Guid userId)
        {
            return await _dbContext.ExpenseCategories
                .Where(c => c.UserId == null || c.UserId == userId)
                .OrderBy(c => c.UserId.HasValue ? 1 : 0) // default categories first
                .ThenBy(c => c.Name)
                .ToListAsync();
        }

        public async Task<ExpenseCreateEditViewModel> GetCreateViewModelAsync(Guid userId)
        {
            var categories = await GetCategoriesForUserAsync(userId);
            return new ExpenseCreateEditViewModel
            {
                ExpenseDate = DateTime.UtcNow,
                CategoryOptions = categories.Select(c => new SelectListItem
                {
                    Value = c.Id.ToString(),
                    Text = c.Name
                }).ToList()
            };
        }

        public async Task<ExpenseCreateEditViewModel?> GetEditViewModelAsync(Guid expenseId, Guid userId)
        {
            var expense = await _dbContext.Expenses
                .FirstOrDefaultAsync(e => e.Id == expenseId && e.UserId == userId);

            if (expense == null) return null;

            var categories = await GetCategoriesForUserAsync(userId);
            return new ExpenseCreateEditViewModel
            {
                Id = expense.Id,
                CategoryId = expense.CategoryId,
                Amount = expense.Amount,
                ExpenseDate = expense.ExpenseDate,
                Description = expense.Description,
                CategoryOptions = categories.Select(c => new SelectListItem
                {
                    Value = c.Id.ToString(),
                    Text = c.Name,
                    Selected = c.Id == expense.CategoryId
                }).ToList()
            };
        }

        public async Task<(bool Success, string? ErrorMessage, Expense? Expense)> CreateExpenseAsync(Guid userId, ExpenseCreateEditViewModel model)
        {
            if (model.Amount <= 0)
            {
                return (false, "Expense amount must be greater than zero.", null);
            }

            // Validate category
            var categoryExists = await _dbContext.ExpenseCategories
                .AnyAsync(c => c.Id == model.CategoryId && (c.UserId == null || c.UserId == userId));

            if (!categoryExists)
            {
                return (false, "Invalid expense category selected.", null);
            }

            var expense = new Expense
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                CategoryId = model.CategoryId,
                Amount = model.Amount,
                ExpenseDate = EnsureUtc(model.ExpenseDate),
                Description = model.Description?.Trim(),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _dbContext.Expenses.Add(expense);
            await _dbContext.SaveChangesAsync();
            _logger.LogInformation("Recorded expense of ₹{Amount} for user {UserId}", model.Amount, userId);

            return (true, null, expense);
        }

        public async Task<(bool Success, string? ErrorMessage)> UpdateExpenseAsync(Guid userId, ExpenseCreateEditViewModel model)
        {
            if (!model.Id.HasValue) return (false, "Expense ID is required.");

            var expense = await _dbContext.Expenses
                .FirstOrDefaultAsync(e => e.Id == model.Id.Value && e.UserId == userId);

            if (expense == null) return (false, "Expense not found or unauthorized.");

            var categoryExists = await _dbContext.ExpenseCategories
                .AnyAsync(c => c.Id == model.CategoryId && (c.UserId == null || c.UserId == userId));

            if (!categoryExists) return (false, "Invalid expense category.");

            expense.CategoryId = model.CategoryId;
            expense.Amount = model.Amount;
            expense.ExpenseDate = EnsureUtc(model.ExpenseDate);
            expense.Description = model.Description?.Trim();
            expense.UpdatedAt = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync();
            return (true, null);
        }

        public async Task<bool> DeleteExpenseAsync(Guid expenseId, Guid userId)
        {
            var expense = await _dbContext.Expenses
                .FirstOrDefaultAsync(e => e.Id == expenseId && e.UserId == userId);

            if (expense == null) return false;

            _dbContext.Expenses.Remove(expense);
            await _dbContext.SaveChangesAsync();
            return true;
        }

        public async Task<ExpenseCategory> CreateCustomCategoryAsync(Guid userId, CreateCategoryViewModel model)
        {
            var category = new ExpenseCategory
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Name = model.Name.Trim(),
                Icon = string.IsNullOrWhiteSpace(model.Icon) ? "bi-tag" : model.Icon.Trim(),
                ColorHex = string.IsNullOrWhiteSpace(model.ColorHex) ? "#4f46e5" : model.ColorHex.Trim(),
                CreatedAt = DateTime.UtcNow
            };

            _dbContext.ExpenseCategories.Add(category);
            await _dbContext.SaveChangesAsync();
            return category;
        }

        public async Task<List<ExpenseItemViewModel>> GetRecentExpensesForUserAsync(Guid userId, int count = 10)
        {
            return await _dbContext.Expenses
                .Include(e => e.Category)
                .Where(e => e.UserId == userId)
                .OrderByDescending(e => e.ExpenseDate)
                .ThenByDescending(e => e.CreatedAt)
                .Take(count)
                .Select(e => new ExpenseItemViewModel
                {
                    Id = e.Id,
                    CategoryId = e.CategoryId,
                    CategoryName = e.Category != null ? e.Category.Name : "Other",
                    CategoryIcon = e.Category != null && !string.IsNullOrEmpty(e.Category.Icon) ? e.Category.Icon : "bi-tag",
                    CategoryColor = e.Category != null && !string.IsNullOrEmpty(e.Category.ColorHex) ? e.Category.ColorHex : "#4f46e5",
                    Amount = e.Amount,
                    ExpenseDate = e.ExpenseDate,
                    Description = e.Description,
                    CreatedAt = e.CreatedAt
                })
                .ToListAsync();
        }
    }
}
