using Microsoft.EntityFrameworkCore;
using PocketGoal.Data;
using PocketGoal.Models;
using PocketGoal.ViewModels;

namespace PocketGoal.Services
{
    public interface IUserProfileService
    {
        Task<UserProfile?> GetUserProfileAsync(Guid userId);
        Task<UserProfile?> FindByEmailOrPhoneAsync(string emailOrPhone);
        Task<(UserProfile User, SavingGoal Goal)> CreateOnboardingProfileAsync(OnboardingViewModel model);
        Task<List<UserProfile>> GetAllProfilesAsync();
    }

    public class UserProfileService : IUserProfileService
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly ILogger<UserProfileService> _logger;

        public UserProfileService(ApplicationDbContext dbContext, ILogger<UserProfileService> logger)
        {
            _dbContext = dbContext;
            _logger = logger;
        }

        public async Task<UserProfile?> GetUserProfileAsync(Guid userId)
        {
            return await _dbContext.UserProfiles
                .AsNoTracking()
                .Include(u => u.SavingGoals)
                .Include(u => u.Expenses)
                .FirstOrDefaultAsync(u => u.Id == userId);
        }

        public async Task<UserProfile?> FindByEmailOrPhoneAsync(string emailOrPhone)
        {
            if (string.IsNullOrWhiteSpace(emailOrPhone)) return null;

            var cleaned = emailOrPhone.Trim().ToLower();
            return await _dbContext.UserProfiles
                .FirstOrDefaultAsync(u => u.Email.ToLower() == cleaned || u.PhoneNumber == cleaned);
        }

        public async Task<List<UserProfile>> GetAllProfilesAsync()
        {
            return await _dbContext.UserProfiles
                .OrderByDescending(u => u.CreatedAt)
                .Take(20)
                .ToListAsync();
        }

        public async Task<(UserProfile User, SavingGoal Goal)> CreateOnboardingProfileAsync(OnboardingViewModel model)
        {
            // Check if existing profile with this email or phone exists
            var existingUser = await _dbContext.UserProfiles
                .FirstOrDefaultAsync(u => u.Email.ToLower() == model.Email.Trim().ToLower() || u.PhoneNumber == model.PhoneNumber.Trim());

            UserProfile user;
            if (existingUser != null)
            {
                user = existingUser;
                user.Name = model.Name.Trim();
                user.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                user = new UserProfile
                {
                    Id = Guid.NewGuid(),
                    Name = model.Name.Trim(),
                    Email = model.Email.Trim(),
                    PhoneNumber = model.PhoneNumber.Trim(),
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                _dbContext.UserProfiles.Add(user);
            }

            var targetDate = DateTime.UtcNow.AddMonths(Math.Max(1, model.SavingPeriodMonths));
            var goal = new SavingGoal
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                GoalName = model.GoalName.Trim(),
                Description = model.GoalDescription?.Trim(),
                TargetAmount = model.TargetPrice,
                TargetDate = targetDate,
                CurrentSavedAmount = 0m,
                Status = GoalStatus.Active,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _dbContext.SavingGoals.Add(goal);

            // If initial savings were provided, record the initial transaction
            if (model.InitialSavings > 0)
            {
                var initialDeposit = new Saving
                {
                    Id = Guid.NewGuid(),
                    SavingGoalId = goal.Id,
                    Amount = model.InitialSavings,
                    SavedDate = DateTime.UtcNow,
                    Notes = "Initial deposit at goal setup",
                    CreatedAt = DateTime.UtcNow
                };
                goal.CurrentSavedAmount = model.InitialSavings;
                if (goal.CurrentSavedAmount >= goal.TargetAmount)
                {
                    goal.Status = GoalStatus.Completed;
                }
                _dbContext.Savings.Add(initialDeposit);
            }

            await _dbContext.SaveChangesAsync();
            _logger.LogInformation("Profile created/linked for {Email} with initial goal {GoalName}", user.Email, goal.GoalName);

            return (user, goal);
        }
    }
}
