using Microsoft.EntityFrameworkCore;
using PocketGoal.Data;
using PocketGoal.Models;
using PocketGoal.ViewModels;

namespace PocketGoal.Services
{
    public interface IDashboardService
    {
        Task<DashboardViewModel?> GetDashboardDataAsync(Guid userId);
    }

    public class DashboardService : IDashboardService
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly ISavingGoalService _savingGoalService;
        private readonly IExpenseService _expenseService;
        private readonly ISavingsService _savingsService;
        private readonly INotificationsService _notificationsService;

        public DashboardService(
            ApplicationDbContext dbContext,
            ISavingGoalService savingGoalService,
            IExpenseService expenseService,
            ISavingsService savingsService,
            INotificationsService notificationsService)
        {
            _dbContext = dbContext;
            _savingGoalService = savingGoalService;
            _expenseService = expenseService;
            _savingsService = savingsService;
            _notificationsService = notificationsService;
        }

        public async Task<DashboardViewModel?> GetDashboardDataAsync(Guid userId)
        {
            var user = await _dbContext.UserProfiles
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == userId);

            if (user == null) return null;

            var allGoals = await _savingGoalService.GetGoalsForUserAsync(userId);
            var primaryGoal = allGoals.FirstOrDefault(g => g.Status == GoalStatus.Active) ?? allGoals.FirstOrDefault();

            var totalSaved = allGoals.Sum(g => g.CurrentSavedAmount);
            var totalTarget = allGoals.Sum(g => g.TargetAmount);
            var totalRemaining = Math.Max(0m, totalTarget - totalSaved);

            var activeCount = allGoals.Count(g => g.Status == GoalStatus.Active);
            var completedCount = allGoals.Count(g => g.Status == GoalStatus.Completed || g.CurrentSavedAmount >= g.TargetAmount);

            var now = DateTime.UtcNow;
            var currentMonth = now.Month;
            var currentYear = now.Year;

            var expenseIndex = await _expenseService.GetExpensesIndexAsync(userId, currentMonth, currentYear, null);
            var recentExpenses = await _expenseService.GetRecentExpensesForUserAsync(userId, 5);
            var recentSavings = await _savingsService.GetRecentSavingsForUserAsync(userId, 5);
            var activeReminders = await _notificationsService.GenerateActiveRemindersForUserAsync(userId);

            var reminderViewModels = activeReminders.Select(r => new NotificationItemViewModel
            {
                Title = r.Title,
                Message = r.MessageBody,
                BadgeClass = r.UrgencyBadge,
                Timestamp = r.GeneratedAt,
                Type = r.Type,
                Icon = r.Type == NotificationType.GoalCompleted ? "bi-trophy" :
                       r.Type == NotificationType.BehindPaceAlert ? "bi-exclamation-triangle" :
                       r.Type == NotificationType.UpcomingDeadline ? "bi-hourglass-split" : "bi-calendar-check"
            }).Take(3).ToList();

            return new DashboardViewModel
            {
                User = user,
                PrimaryGoal = primaryGoal,
                AllGoals = allGoals,
                TotalSavedAllGoals = totalSaved,
                TotalTargetAllGoals = totalTarget,
                TotalRemainingAllGoals = totalRemaining,
                ActiveGoalsCount = activeCount,
                CompletedGoalsCount = completedCount,
                CurrentMonthExpenses = expenseIndex.CurrentMonthTotal,
                PreviousMonthExpenses = expenseIndex.PreviousMonthTotal,
                TodayExpenses = expenseIndex.TodayTotal,
                TotalExpensesAllTime = expenseIndex.AllTimeTotal,
                TopExpenseCategories = expenseIndex.CategorySummaries.Take(5).ToList(),
                RecentExpenses = recentExpenses,
                RecentSavings = recentSavings,
                ActiveReminders = reminderViewModels
            };
        }
    }
}
