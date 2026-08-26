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
        Task<(bool Success, UserProfile? User, string? ErrorMessage)> AuthenticateAsync(string emailOrPhone, string password);
        Task<(bool Success, UserProfile? User, string? ErrorMessage)> ValidateProfileSwitchAsync(Guid profileId, string password);
        Task<bool> SetPasswordAsync(Guid userId, string newPassword);
    }

    public class UserProfileService : IUserProfileService
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly IPasswordHasherService _passwordHasher;
        private readonly ILogger<UserProfileService> _logger;

        public UserProfileService(
            ApplicationDbContext dbContext,
            IPasswordHasherService passwordHasher,
            ILogger<UserProfileService> logger)
        {
            _dbContext = dbContext;
            _passwordHasher = passwordHasher;
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

        public async Task<(bool Success, UserProfile? User, string? ErrorMessage)> AuthenticateAsync(string emailOrPhone, string password)
        {
            if (string.IsNullOrWhiteSpace(emailOrPhone) || string.IsNullOrWhiteSpace(password))
            {
                return (false, null, "Email/phone and password both required.");
            }

            var user = await FindByEmailOrPhoneAsync(emailOrPhone);
            if (user == null)
            {
                return (false, null, "Indha email/phone-la profile onnum illa da.");
            }

            // Legacy profiles without password hash
            if (string.IsNullOrEmpty(user.PasswordHash))
            {
                // Auto-set password on first login if user didn't have one
                user.PasswordHash = _passwordHasher.HashPassword(password);
                user.UpdatedAt = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync();
                return (true, user, null);
            }

            if (!_passwordHasher.VerifyPassword(password, user.PasswordHash))
            {
                return (false, null, "Password thappu da! Sariyaana password enter pannunga.");
            }

            return (true, user, null);
        }

        public async Task<(bool Success, UserProfile? User, string? ErrorMessage)> ValidateProfileSwitchAsync(Guid profileId, string password)
        {
            var user = await _dbContext.UserProfiles.FirstOrDefaultAsync(u => u.Id == profileId);
            if (user == null)
            {
                return (false, null, "Profile kidaikkala da.");
            }

            if (string.IsNullOrWhiteSpace(password))
            {
                return (false, null, "Password required to switch into this profile.");
            }

            // Legacy profiles without password hash
            if (string.IsNullOrEmpty(user.PasswordHash))
            {
                user.PasswordHash = _passwordHasher.HashPassword(password);
                user.UpdatedAt = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync();
                return (true, user, null);
            }

            if (!_passwordHasher.VerifyPassword(password, user.PasswordHash))
            {
                return (false, null, "Password thappu da! Sariyaana password enter pannunga.");
            }

            return (true, user, null);
        }

        public async Task<bool> SetPasswordAsync(Guid userId, string newPassword)
        {
            var user = await _dbContext.UserProfiles.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null || string.IsNullOrWhiteSpace(newPassword)) return false;

            user.PasswordHash = _passwordHasher.HashPassword(newPassword);
            user.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();
            return true;
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
                if (!string.IsNullOrWhiteSpace(model.Password))
                {
                    user.PasswordHash = _passwordHasher.HashPassword(model.Password);
                }
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
                    PasswordHash = !string.IsNullOrWhiteSpace(model.Password) ? _passwordHasher.HashPassword(model.Password) : null,
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
