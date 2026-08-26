using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PocketGoal.Data;
using PocketGoal.Models;
using PocketGoal.Services;
using PocketGoal.ViewModels;
using Xunit;

namespace PocketGoal.Tests
{
    public class SavingGoalServiceTests
    {
        private ApplicationDbContext CreateInMemoryDbContext()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;
            var context = new ApplicationDbContext(options);
            context.Database.EnsureCreated();
            return context;
        }

        [Fact]
        public void MapToViewModel_CalculatesRemainingAndPaceCorrectly()
        {
            // Arrange
            var db = CreateInMemoryDbContext();
            var service = new SavingGoalService(db, NullLogger<SavingGoalService>.Instance);

            var userId = Guid.NewGuid();
            var goal = new SavingGoal
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                GoalName = "Gaming Laptop",
                TargetAmount = 120000m,
                CurrentSavedAmount = 20000m,
                CreatedAt = DateTime.UtcNow.AddMonths(-1),
                TargetDate = DateTime.UtcNow.AddMonths(4),
                Status = GoalStatus.Active,
                Savings = new List<Saving>
                {
                    new Saving { Id = Guid.NewGuid(), Amount = 20000m, SavedDate = DateTime.UtcNow.AddDays(-10) }
                }
            };

            // Act
            var vm = service.MapToViewModel(goal);

            // Assert
            Assert.Equal(120000m, vm.TargetAmount);
            Assert.Equal(20000m, vm.CurrentSavedAmount);
            Assert.Equal(100000m, vm.RemainingAmount);
            Assert.Equal(16.67m, vm.ProgressPercentage);
            Assert.True(vm.RequiredMonthlySaving > 0);
            Assert.Equal(25000m, Math.Round(vm.RequiredMonthlySaving / 1000m) * 1000m); // ~25,000 / month
            Assert.False(vm.IsCompleted);
        }

        [Fact]
        public void MapToViewModel_FullyFundedGoal_MarksCompleted()
        {
            // Arrange
            var db = CreateInMemoryDbContext();
            var service = new SavingGoalService(db, NullLogger<SavingGoalService>.Instance);

            var goal = new SavingGoal
            {
                Id = Guid.NewGuid(),
                UserId = Guid.NewGuid(),
                GoalName = "Bike Down Payment",
                TargetAmount = 50000m,
                CurrentSavedAmount = 50000m,
                CreatedAt = DateTime.UtcNow.AddMonths(-2),
                TargetDate = DateTime.UtcNow.AddMonths(2),
                Status = GoalStatus.Active,
                Savings = new List<Saving>
                {
                    new Saving { Id = Guid.NewGuid(), Amount = 50000m, SavedDate = DateTime.UtcNow }
                }
            };

            // Act
            var vm = service.MapToViewModel(goal);

            // Assert
            Assert.Equal(0m, vm.RemainingAmount);
            Assert.Equal(100m, vm.ProgressPercentage);
            Assert.Equal(0m, vm.RequiredMonthlySaving);
            Assert.Equal(GoalPaceStatus.Completed, vm.PaceStatus);
            Assert.True(vm.IsCompleted);
        }

        [Fact]
        public async Task AddSavingAsync_UpdatesGoalTotalAndCompletesWhenTargetReached()
        {
            // Arrange
            var db = CreateInMemoryDbContext();
            var notifService = new NotificationsService(null!, db, NullLogger<NotificationsService>.Instance);
            var savingsService = new SavingsService(db, notifService, NullLogger<SavingsService>.Instance);

            var user = new UserProfile
            {
                Id = Guid.NewGuid(),
                Name = "Ananya",
                Email = "ananya@example.com",
                PhoneNumber = "+919876543210"
            };
            db.UserProfiles.Add(user);

            var goal = new SavingGoal
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                GoalName = "Air Conditioner",
                TargetAmount = 40000m,
                CurrentSavedAmount = 30000m,
                TargetDate = DateTime.UtcNow.AddMonths(2)
            };
            db.SavingGoals.Add(goal);
            db.Savings.Add(new Saving { Id = Guid.NewGuid(), SavingGoalId = goal.Id, Amount = 30000m });
            await db.SaveChangesAsync();

            // Act: Add remaining ₹10,000
            var (success, error, saving) = await savingsService.AddSavingAsync(user.Id, new AddSavingViewModel
            {
                SavingGoalId = goal.Id,
                Amount = 10000m,
                SavedDate = DateTime.UtcNow,
                Notes = "Final payment"
            });

            // Assert
            Assert.True(success);
            var updatedGoal = await db.SavingGoals.FindAsync(goal.Id);
            Assert.NotNull(updatedGoal);
            Assert.Equal(40000m, updatedGoal.CurrentSavedAmount);
            Assert.Equal(GoalStatus.Completed, updatedGoal.Status);
        }

        [Fact]
        public async Task Security_UserCannotAccessAnotherUsersGoal()
        {
            // Arrange
            var db = CreateInMemoryDbContext();
            var service = new SavingGoalService(db, NullLogger<SavingGoalService>.Instance);

            var user1Id = Guid.NewGuid();
            var user2Id = Guid.NewGuid();

            var goal = new SavingGoal
            {
                Id = Guid.NewGuid(),
                UserId = user1Id,
                GoalName = "Secret Goal",
                TargetAmount = 10000m,
                TargetDate = DateTime.UtcNow.AddMonths(1)
            };
            db.SavingGoals.Add(goal);
            await db.SaveChangesAsync();

            // Act: User 2 attempts to fetch user 1's goal
            var retrieved = await service.GetGoalDetailsAsync(goal.Id, user2Id);
            var deleteResult = await service.DeleteGoalAsync(goal.Id, user2Id);

            // Assert
            Assert.Null(retrieved);
            Assert.False(deleteResult);
        }

        [Fact]
        public async Task OnboardingProfileService_CreatesUserAndInitialGoalWithSavings()
        {
            // Arrange
            var db = CreateInMemoryDbContext();
            var service = new UserProfileService(db, new PasswordHasherService(), NullLogger<UserProfileService>.Instance);

            var onboarding = new OnboardingViewModel
            {
                Name = "Rohit Verma",
                Email = "rohit@test.com",
                PhoneNumber = "+919123456789",
                Password = "Password123!",
                ConfirmPassword = "Password123!",
                GoalName = "Electric Scooter",
                TargetPrice = 90000m,
                SavingPeriodMonths = 6,
                InitialSavings = 15000m,
                GoalDescription = "Eco friendly commute"
            };

            // Act
            var (user, goal) = await service.CreateOnboardingProfileAsync(onboarding);

            // Assert
            Assert.NotNull(user);
            Assert.NotNull(goal);
            Assert.Equal("Rohit Verma", user.Name);
            Assert.Equal(90000m, goal.TargetAmount);
            Assert.Equal(15000m, goal.CurrentSavedAmount);
            Assert.Equal(GoalStatus.Active, goal.Status);

            var savedCount = await db.Savings.CountAsync(s => s.SavingGoalId == goal.Id);
            Assert.Equal(1, savedCount);
        }

        [Fact]
        public async Task DeleteSavingAsync_RecalculatesGoalTotal()
        {
            // Arrange
            var db = CreateInMemoryDbContext();
            var notifService = new NotificationsService(null!, db, NullLogger<NotificationsService>.Instance);
            var savingsService = new SavingsService(db, notifService, NullLogger<SavingsService>.Instance);

            var user = new UserProfile { Id = Guid.NewGuid(), Name = "Maya", Email = "maya@test.com", PhoneNumber = "123" };
            db.UserProfiles.Add(user);

            var goal = new SavingGoal
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                GoalName = "Watch",
                TargetAmount = 20000m,
                CurrentSavedAmount = 15000m,
                TargetDate = DateTime.UtcNow.AddMonths(1)
            };
            db.SavingGoals.Add(goal);

            var saving1 = new Saving { Id = Guid.NewGuid(), SavingGoalId = goal.Id, Amount = 10000m, SavedDate = DateTime.UtcNow };
            var saving2 = new Saving { Id = Guid.NewGuid(), SavingGoalId = goal.Id, Amount = 5000m, SavedDate = DateTime.UtcNow };
            db.Savings.AddRange(saving1, saving2);
            await db.SaveChangesAsync();

            // Act: Delete saving2 (₹5,000)
            var deleted = await savingsService.DeleteSavingAsync(saving2.Id, user.Id);

            // Assert
            Assert.True(deleted);
            var updatedGoal = await db.SavingGoals.FindAsync(goal.Id);
            Assert.NotNull(updatedGoal);
            Assert.Equal(10000m, updatedGoal.CurrentSavedAmount);
        }

        [Fact]
        public async Task CreateGoalAsync_And_UpdateGoalAsync_SetsUtcKindOnUnspecifiedDateTime()
        {
            // Arrange
            var db = CreateInMemoryDbContext();
            var service = new SavingGoalService(db, NullLogger<SavingGoalService>.Instance);
            var userId = Guid.NewGuid();

            var unspecifiedDate = new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Unspecified);

            // Act: Create
            var createdGoal = await service.CreateGoalAsync(userId, new SavingGoalCreateEditViewModel
            {
                GoalName = "Emergency Fund",
                TargetAmount = 100000m,
                TargetDate = unspecifiedDate
            });

            // Assert Create
            Assert.Equal(DateTimeKind.Utc, createdGoal.TargetDate.Kind);
            Assert.Equal(2026, createdGoal.TargetDate.Year);
            Assert.Equal(12, createdGoal.TargetDate.Month);
            Assert.Equal(31, createdGoal.TargetDate.Day);

            // Act: Update with another Unspecified DateTime
            var updatedDate = new DateTime(2027, 6, 15, 0, 0, 0, DateTimeKind.Unspecified);
            var updateResult = await service.UpdateGoalAsync(userId, new SavingGoalCreateEditViewModel
            {
                Id = createdGoal.Id,
                GoalName = "Emergency Fund Updated",
                TargetAmount = 120000m,
                TargetDate = updatedDate,
                Status = GoalStatus.Active
            });

            // Assert Update
            Assert.True(updateResult);
            var refreshedGoal = await db.SavingGoals.FindAsync(createdGoal.Id);
            Assert.NotNull(refreshedGoal);
            Assert.Equal(DateTimeKind.Utc, refreshedGoal.TargetDate.Kind);
            Assert.Equal(2027, refreshedGoal.TargetDate.Year);
            Assert.Equal(6, refreshedGoal.TargetDate.Month);
            Assert.Equal(15, refreshedGoal.TargetDate.Day);
        }
    }
}
