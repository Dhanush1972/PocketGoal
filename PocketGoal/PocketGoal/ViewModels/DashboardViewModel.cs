using PocketGoal.Models;

namespace PocketGoal.ViewModels
{
    public class DashboardViewModel
    {
        public UserProfile User { get; set; } = null!;

        // Primary Active Saving Goal for the Circular Progress Widget
        public SavingGoalViewModel? PrimaryGoal { get; set; }

        // All User Goals
        public List<SavingGoalViewModel> AllGoals { get; set; } = new();

        // Financial KPIs
        public decimal TotalSavedAllGoals { get; set; }
        public decimal TotalTargetAllGoals { get; set; }
        public decimal TotalRemainingAllGoals { get; set; }
        public decimal OverallSavingsProgressPercentage => TotalTargetAllGoals > 0 ? Math.Min(100m, Math.Round((TotalSavedAllGoals / TotalTargetAllGoals) * 100m, 2)) : 0m;

        public int ActiveGoalsCount { get; set; }
        public int CompletedGoalsCount { get; set; }

        // Expense Summary
        public decimal CurrentMonthExpenses { get; set; }
        public decimal PreviousMonthExpenses { get; set; }
        public decimal TodayExpenses { get; set; }
        public decimal TotalExpensesAllTime { get; set; }

        // Category breakdown for current month
        public List<ExpenseCategorySummaryViewModel> TopExpenseCategories { get; set; } = new();

        // Recent Activity
        public List<ExpenseItemViewModel> RecentExpenses { get; set; } = new();
        public List<SavingTransactionViewModel> RecentSavings { get; set; } = new();

        // Reminders & Notifications
        public List<NotificationItemViewModel> ActiveReminders { get; set; } = new();
    }

    public class NotificationItemViewModel
    {
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string Icon { get; set; } = "bi-bell";
        public string BadgeClass { get; set; } = "bg-primary";
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public NotificationType Type { get; set; }
    }
}
