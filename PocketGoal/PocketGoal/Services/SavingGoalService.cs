using Microsoft.EntityFrameworkCore;
using PocketGoal.Data;
using PocketGoal.Helpers;
using PocketGoal.Models;
using PocketGoal.ViewModels;

namespace PocketGoal.Services
{
    public interface ISavingGoalService
    {
        Task<List<SavingGoalViewModel>> GetGoalsForUserAsync(Guid userId);
        Task<SavingGoalViewModel?> GetGoalDetailsAsync(Guid goalId, Guid userId);
        Task<SavingGoal> CreateGoalAsync(Guid userId, SavingGoalCreateEditViewModel model);
        Task<bool> UpdateGoalAsync(Guid userId, SavingGoalCreateEditViewModel model);
        Task<bool> DeleteGoalAsync(Guid goalId, Guid userId);
        SavingGoalViewModel MapToViewModel(SavingGoal goal);
    }

    public class SavingGoalService : ISavingGoalService
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly ILogger<SavingGoalService> _logger;

        private static DateTime EnsureUtc(DateTime dt) =>
            dt.Kind == DateTimeKind.Utc ? dt : DateTime.SpecifyKind(dt, DateTimeKind.Utc);

        public SavingGoalService(ApplicationDbContext dbContext, ILogger<SavingGoalService> logger)
        {
            _dbContext = dbContext;
            _logger = logger;
        }

        public async Task<List<SavingGoalViewModel>> GetGoalsForUserAsync(Guid userId)
        {
            var goals = await _dbContext.SavingGoals
                .Where(g => g.UserId == userId)
                .Include(g => g.Savings)
                .OrderByDescending(g => g.CreatedAt)
                .ToListAsync();

            return goals.Select(MapToViewModel).ToList();
        }

        public async Task<SavingGoalViewModel?> GetGoalDetailsAsync(Guid goalId, Guid userId)
        {
            var goal = await _dbContext.SavingGoals
                .Where(g => g.Id == goalId && g.UserId == userId)
                .Include(g => g.Savings.OrderByDescending(s => s.SavedDate))
                .FirstOrDefaultAsync();

            return goal == null ? null : MapToViewModel(goal);
        }

        public async Task<SavingGoal> CreateGoalAsync(Guid userId, SavingGoalCreateEditViewModel model)
        {
            var goal = new SavingGoal
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                GoalName = model.GoalName.Trim(),
                Description = model.Description?.Trim(),
                TargetAmount = model.TargetAmount,
                TargetDate = EnsureUtc(model.TargetDate),
                CurrentSavedAmount = 0m,
                Status = GoalStatus.Active,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _dbContext.SavingGoals.Add(goal);
            await _dbContext.SaveChangesAsync();
            return goal;
        }

        public async Task<bool> UpdateGoalAsync(Guid userId, SavingGoalCreateEditViewModel model)
        {
            if (!model.Id.HasValue) return false;

            var goal = await _dbContext.SavingGoals
                .FirstOrDefaultAsync(g => g.Id == model.Id.Value && g.UserId == userId);

            if (goal == null) return false;

            goal.GoalName = model.GoalName.Trim();
            goal.Description = model.Description?.Trim();
            goal.TargetAmount = model.TargetAmount;
            goal.TargetDate = EnsureUtc(model.TargetDate);
            goal.Status = model.Status;
            goal.UpdatedAt = DateTime.UtcNow;

            // Check if status should be completed
            if (goal.CurrentSavedAmount >= goal.TargetAmount)
            {
                goal.Status = GoalStatus.Completed;
            }

            await _dbContext.SaveChangesAsync();
            return true;
        }

        public async Task<bool> DeleteGoalAsync(Guid goalId, Guid userId)
        {
            var goal = await _dbContext.SavingGoals
                .FirstOrDefaultAsync(g => g.Id == goalId && g.UserId == userId);

            if (goal == null) return false;

            _dbContext.SavingGoals.Remove(goal);
            await _dbContext.SaveChangesAsync();
            return true;
        }

        public SavingGoalViewModel MapToViewModel(SavingGoal goal)
        {
            var now = DateTime.UtcNow;
            var target = goal.TargetDate.ToUniversalTime();

            // Total saved from database or savings relationship
            var totalSaved = goal.Savings?.Any() == true ? goal.Savings.Sum(s => s.Amount) : goal.CurrentSavedAmount;
            var remainingAmount = Math.Max(0m, goal.TargetAmount - totalSaved);

            var totalDaysSpan = Math.Max(1, (target.Date - goal.CreatedAt.ToUniversalTime().Date).TotalDays);
            var elapsedDays = Math.Max(0, (now.Date - goal.CreatedAt.ToUniversalTime().Date).TotalDays);
            var remainingDays = Math.Max(0, (target.Date - now.Date).TotalDays);

            // Compute months and weeks remaining accurately
            var monthsRemaining = remainingDays / 30.4375;
            var weeksRemaining = remainingDays / 7.0;

            decimal monthlyReq = 0m;
            decimal weeklyReq = 0m;
            decimal dailyReq = 0m;

            if (remainingAmount > 0)
            {
                if (remainingDays <= 0)
                {
                    // Goal target date is reached or passed but not fully funded
                    dailyReq = remainingAmount;
                    weeklyReq = remainingAmount;
                    monthlyReq = remainingAmount;
                }
                else
                {
                    dailyReq = Math.Round(remainingAmount / (decimal)Math.Max(1.0, remainingDays), 2);
                    weeklyReq = Math.Round(remainingAmount / (decimal)Math.Max(1.0, weeksRemaining), 2);
                    monthlyReq = Math.Round(remainingAmount / (decimal)Math.Max(0.5, monthsRemaining), 2);
                }
            }

            // Determine Pace Status
            GoalPaceStatus pace;
            if (totalSaved >= goal.TargetAmount || goal.Status == GoalStatus.Completed)
            {
                pace = GoalPaceStatus.Completed;
            }
            else if (now > target)
            {
                pace = GoalPaceStatus.Overdue;
            }
            else
            {
                // Calculate expected progress ratio
                var timeRatio = totalDaysSpan > 0 ? (decimal)(elapsedDays / totalDaysSpan) : 1m;
                var expectedSaved = goal.TargetAmount * timeRatio;

                if (totalSaved >= expectedSaved * 0.9m)
                {
                    pace = GoalPaceStatus.OnTrack;
                }
                else
                {
                    pace = GoalPaceStatus.Behind;
                }
            }

            return new SavingGoalViewModel
            {
                Id = goal.Id,
                UserId = goal.UserId,
                GoalName = goal.GoalName,
                Description = goal.Description,
                TargetAmount = goal.TargetAmount,
                TargetDate = goal.TargetDate,
                CurrentSavedAmount = totalSaved,
                Status = totalSaved >= goal.TargetAmount ? GoalStatus.Completed : goal.Status,
                PaceStatus = pace,
                RequiredMonthlySaving = monthlyReq,
                RequiredWeeklySaving = weeklyReq,
                RequiredDailySaving = dailyReq,
                DaysRemaining = (int)remainingDays,
                MonthsRemaining = Math.Round(monthsRemaining, 1),
                CreatedAt = goal.CreatedAt,
                UpdatedAt = goal.UpdatedAt,
                SavingsHistory = goal.Savings?.Select(s => new SavingTransactionViewModel
                {
                    Id = s.Id,
                    SavingGoalId = s.SavingGoalId,
                    GoalName = goal.GoalName,
                    Amount = s.Amount,
                    SavedDate = s.SavedDate,
                    Notes = s.Notes,
                    CreatedAt = s.CreatedAt
                }).OrderByDescending(s => s.SavedDate).ToList() ?? new List<SavingTransactionViewModel>()
            };
        }
    }
}
