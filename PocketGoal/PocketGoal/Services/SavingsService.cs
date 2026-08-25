using Microsoft.EntityFrameworkCore;
using PocketGoal.Data;
using PocketGoal.Helpers;
using PocketGoal.Models;
using PocketGoal.ViewModels;

namespace PocketGoal.Services
{
    public interface ISavingsService
    {
        Task<(bool Success, string? ErrorMessage, Saving? Saving)> AddSavingAsync(Guid userId, AddSavingViewModel model);
        Task<bool> DeleteSavingAsync(Guid savingId, Guid userId);
        Task<List<SavingTransactionViewModel>> GetRecentSavingsForUserAsync(Guid userId, int count = 10);
    }

    public class SavingsService : ISavingsService
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly INotificationsService _notificationsService;
        private readonly ILogger<SavingsService> _logger;

        private static DateTime EnsureUtc(DateTime dt) =>
            dt.Kind == DateTimeKind.Utc ? dt : DateTime.SpecifyKind(dt, DateTimeKind.Utc);

        public SavingsService(
            ApplicationDbContext dbContext,
            INotificationsService notificationsService,
            ILogger<SavingsService> logger)
        {
            _dbContext = dbContext;
            _notificationsService = notificationsService;
            _logger = logger;
        }

        public async Task<(bool Success, string? ErrorMessage, Saving? Saving)> AddSavingAsync(Guid userId, AddSavingViewModel model)
        {
            if (model.Amount <= 0)
            {
                return (false, "Saving amount must be greater than zero.", null);
            }

            var goal = await _dbContext.SavingGoals
                .Include(g => g.User)
                .Include(g => g.Savings)
                .FirstOrDefaultAsync(g => g.Id == model.SavingGoalId && g.UserId == userId);

            if (goal == null)
            {
                return (false, "Saving goal not found or you do not have permission.", null);
            }

            var saving = new Saving
            {
                Id = Guid.NewGuid(),
                SavingGoalId = goal.Id,
                Amount = model.Amount,
                SavedDate = EnsureUtc(model.SavedDate),
                Notes = model.Notes?.Trim(),
                CreatedAt = DateTime.UtcNow
            };

            _dbContext.Savings.Add(saving);

            // Update goal current saved amount
            var wasCompletedBefore = goal.CurrentSavedAmount >= goal.TargetAmount;
            var newTotal = goal.CurrentSavedAmount + model.Amount;
            goal.CurrentSavedAmount = newTotal;
            goal.UpdatedAt = DateTime.UtcNow;

            if (newTotal >= goal.TargetAmount && !wasCompletedBefore)
            {
                goal.Status = GoalStatus.Completed;
            }

            await _dbContext.SaveChangesAsync();

            // If newly completed, trigger milestone notification
            if (newTotal >= goal.TargetAmount && !wasCompletedBefore && goal.User != null)
            {
                await _notificationsService.SendGoalCompletedAlertAsync(goal.User, goal);
            }

            _logger.LogInformation("Added saving of ₹{Amount} to goal {GoalId} for user {UserId}", model.Amount, goal.Id, userId);
            return (true, null, saving);
        }

        public async Task<bool> DeleteSavingAsync(Guid savingId, Guid userId)
        {
            var saving = await _dbContext.Savings
                .Include(s => s.SavingGoal)
                .FirstOrDefaultAsync(s => s.Id == savingId && s.SavingGoal!.UserId == userId);

            if (saving == null || saving.SavingGoal == null) return false;

            var goal = saving.SavingGoal;
            _dbContext.Savings.Remove(saving);
            await _dbContext.SaveChangesAsync();

            // Recalculate goal saved total
            var remainingTotal = await _dbContext.Savings
                .Where(s => s.SavingGoalId == goal.Id)
                .SumAsync(s => (decimal?)s.Amount) ?? 0m;

            goal.CurrentSavedAmount = remainingTotal;
            if (goal.CurrentSavedAmount < goal.TargetAmount && goal.Status == GoalStatus.Completed)
            {
                goal.Status = GoalStatus.Active;
            }
            goal.UpdatedAt = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync();
            return true;
        }

        public async Task<List<SavingTransactionViewModel>> GetRecentSavingsForUserAsync(Guid userId, int count = 10)
        {
            var savings = await _dbContext.Savings
                .Include(s => s.SavingGoal)
                .Where(s => s.SavingGoal!.UserId == userId)
                .OrderByDescending(s => s.SavedDate)
                .Take(count)
                .Select(s => new SavingTransactionViewModel
                {
                    Id = s.Id,
                    SavingGoalId = s.SavingGoalId,
                    GoalName = s.SavingGoal!.GoalName,
                    Amount = s.Amount,
                    SavedDate = s.SavedDate,
                    Notes = s.Notes,
                    CreatedAt = s.CreatedAt
                })
                .ToListAsync();

            return savings;
        }
    }
}
