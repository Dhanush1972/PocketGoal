using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PocketGoal.Controllers;
using PocketGoal.Data;
using PocketGoal.Models;
using PocketGoal.Services;
using PocketGoal.ViewModels;
using Xunit;

namespace PocketGoal.Tests
{
    public class UserProfileSecurityTests
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
        public void PasswordHasher_HashesAndVerifiesPasswordCorrectly()
        {
            var hasher = new PasswordHasherService();
            var password = "SecurePassword123!";

            var hash = hasher.HashPassword(password);

            Assert.False(string.IsNullOrWhiteSpace(hash));
            Assert.True(hasher.VerifyPassword(password, hash));
            Assert.False(hasher.VerifyPassword("WrongPassword", hash));
            Assert.False(hasher.VerifyPassword("", hash));
            Assert.False(hasher.VerifyPassword(password, null));
        }

        [Fact]
        public async Task CreateOnboardingProfile_HashesPasswordAndStoresInDatabase()
        {
            var db = CreateInMemoryDbContext();
            var hasher = new PasswordHasherService();
            var service = new UserProfileService(db, hasher, NullLogger<UserProfileService>.Instance);

            var model = new OnboardingViewModel
            {
                Name = "Kavitha",
                Email = "kavitha@example.com",
                PhoneNumber = "9876543210",
                Password = "MySecretPassword456!",
                ConfirmPassword = "MySecretPassword456!",
                GoalName = "Emergency Fund",
                TargetPrice = 100000m,
                SavingPeriodMonths = 6,
                InitialSavings = 10000m
            };

            var (user, goal) = await service.CreateOnboardingProfileAsync(model);

            Assert.NotNull(user);
            Assert.NotNull(user.PasswordHash);
            Assert.NotEqual("MySecretPassword456!", user.PasswordHash);
            Assert.True(hasher.VerifyPassword("MySecretPassword456!", user.PasswordHash));
            Assert.Equal(10000m, goal.CurrentSavedAmount);
        }

        [Fact]
        public async Task AuthenticateAsync_ValidCredentials_ReturnsSuccess()
        {
            var db = CreateInMemoryDbContext();
            var hasher = new PasswordHasherService();
            var service = new UserProfileService(db, hasher, NullLogger<UserProfileService>.Instance);

            var model = new OnboardingViewModel
            {
                Name = "Ravi",
                Email = "ravi@example.com",
                PhoneNumber = "9988776655",
                Password = "RaviPassword2026",
                ConfirmPassword = "RaviPassword2026",
                GoalName = "Royal Enfield",
                TargetPrice = 250000m,
                SavingPeriodMonths = 12
            };

            await service.CreateOnboardingProfileAsync(model);

            // Authenticate by Email
            var (successByEmail, userByEmail, _) = await service.AuthenticateAsync("ravi@example.com", "RaviPassword2026");
            Assert.True(successByEmail);
            Assert.NotNull(userByEmail);
            Assert.Equal("Ravi", userByEmail.Name);

            // Authenticate by Phone
            var (successByPhone, userByPhone, _) = await service.AuthenticateAsync("9988776655", "RaviPassword2026");
            Assert.True(successByPhone);
            Assert.NotNull(userByPhone);
        }

        [Fact]
        public async Task AuthenticateAsync_InvalidPassword_ReturnsFailure()
        {
            var db = CreateInMemoryDbContext();
            var hasher = new PasswordHasherService();
            var service = new UserProfileService(db, hasher, NullLogger<UserProfileService>.Instance);

            var model = new OnboardingViewModel
            {
                Name = "Suresh",
                Email = "suresh@example.com",
                PhoneNumber = "9123456789",
                Password = "CorrectPassword123",
                ConfirmPassword = "CorrectPassword123",
                GoalName = "Trip to Japan",
                TargetPrice = 200000m,
                SavingPeriodMonths = 8
            };

            await service.CreateOnboardingProfileAsync(model);

            var (success, user, error) = await service.AuthenticateAsync("suresh@example.com", "WrongPassword!");
            Assert.False(success);
            Assert.Null(user);
            Assert.NotNull(error);
        }

        [Fact]
        public async Task ValidateProfileSwitchAsync_RequiresCorrectPassword()
        {
            var db = CreateInMemoryDbContext();
            var hasher = new PasswordHasherService();
            var service = new UserProfileService(db, hasher, NullLogger<UserProfileService>.Instance);

            var user = new UserProfile
            {
                Id = Guid.NewGuid(),
                Name = "Ananya",
                Email = "ananya@example.com",
                PhoneNumber = "9871234560",
                PasswordHash = hasher.HashPassword("AnanyaSecret99"),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.UserProfiles.Add(user);
            await db.SaveChangesAsync();

            // Correct password
            var (validSuccess, validUser, _) = await service.ValidateProfileSwitchAsync(user.Id, "AnanyaSecret99");
            Assert.True(validSuccess);
            Assert.NotNull(validUser);

            // Incorrect password
            var (invalidSuccess, invalidUser, invalidError) = await service.ValidateProfileSwitchAsync(user.Id, "WrongPass");
            Assert.False(invalidSuccess);
            Assert.Null(invalidUser);
            Assert.NotNull(invalidError);

            // Empty password
            var (emptySuccess, _, emptyError) = await service.ValidateProfileSwitchAsync(user.Id, "");
            Assert.False(emptySuccess);
            Assert.NotNull(emptyError);
        }

        [Fact]
        public async Task ValidateProfileSwitchAsync_LegacyProfileWithoutPassword_SetsPasswordOnSwitch()
        {
            var db = CreateInMemoryDbContext();
            var hasher = new PasswordHasherService();
            var service = new UserProfileService(db, hasher, NullLogger<UserProfileService>.Instance);

            var legacyUser = new UserProfile
            {
                Id = Guid.NewGuid(),
                Name = "Grandpa",
                Email = "grandpa@example.com",
                PhoneNumber = "9876500000",
                PasswordHash = null, // legacy profile without password
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.UserProfiles.Add(legacyUser);
            await db.SaveChangesAsync();

            // Switch by setting a password
            var (success, user, _) = await service.ValidateProfileSwitchAsync(legacyUser.Id, "GrandpaNewPass123");
            Assert.True(success);
            Assert.NotNull(user);
            Assert.NotNull(user.PasswordHash);
            Assert.True(hasher.VerifyPassword("GrandpaNewPass123", user.PasswordHash));
        }
    }
}
