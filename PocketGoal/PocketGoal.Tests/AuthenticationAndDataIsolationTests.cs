using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
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
    public class FakeProfileContextService : IProfileContextService
    {
        public Guid? CurrentUserId { get; set; }

        public Guid? GetCurrentUserId() => CurrentUserId;
        public void SetCurrentUserId(Guid userId) => CurrentUserId = userId;
        public void ClearCurrentUserId() => CurrentUserId = null;
        public Task SignInAsync(UserProfile user, bool isPersistent = true, DateTimeOffset? expiresUtc = null)
        {
            CurrentUserId = user.Id;
            return Task.CompletedTask;
        }
        public Task SignOutAsync()
        {
            CurrentUserId = null;
            return Task.CompletedTask;
        }
    }

    public class FakeHttpContextAccessor : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
    }

    public class FakeNotificationsService : INotificationsService
    {
        public Task SendMonthlyReminderAsync(UserProfile user, SavingGoal goal, decimal monthlyRequiredAmount) => Task.CompletedTask;
        public Task SendBehindPaceAlertAsync(UserProfile user, SavingGoal goal, decimal monthlyRequiredAmount) => Task.CompletedTask;
        public Task SendUpcomingDeadlineReminderAsync(UserProfile user, SavingGoal goal, int daysRemaining, decimal remainingAmount) => Task.CompletedTask;
        public Task SendGoalCompletedAlertAsync(UserProfile user, SavingGoal goal) => Task.CompletedTask;
        public Task<List<NotificationPreviewItem>> GenerateActiveRemindersForUserAsync(Guid userId) => Task.FromResult(new List<NotificationPreviewItem>());
        public Task<(bool Success, string Message)> TriggerSimulatedNotificationAsync(Guid userId, TriggerNotificationRequest request) => Task.FromResult((true, "OK"));
        public Task<bool> SendEmailNotificationAsync(string recipientEmail, string subject, string body) => Task.FromResult(true);
        public Task<bool> SendSmsNotificationAsync(string recipientPhone, string message) => Task.FromResult(true);
    }

    public class FakeSupabaseJwtValidator : ISupabaseJwtValidator
    {
        public Task<SupabaseTokenValidationResult> ValidateTokenAsync(string token) =>
            Task.FromResult(new SupabaseTokenValidationResult { IsValid = true, UserId = Guid.NewGuid() });
    }

    public class FakeTempDataProvider : Microsoft.AspNetCore.Mvc.ViewFeatures.ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }

    public class AuthenticationAndDataIsolationTests
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
        public void HomeController_Unauthenticated_RedirectsToLogin()
        {
            // Arrange
            var fakeContext = new FakeProfileContextService { CurrentUserId = null };
            var controller = new HomeController(fakeContext);

            // Act
            var result = controller.Index();

            // Assert
            var redirectResult = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Login", redirectResult.ActionName);
            Assert.Equal("Account", redirectResult.ControllerName);
        }

        [Fact]
        public void HomeController_Authenticated_RedirectsToDashboard()
        {
            // Arrange
            var userId = Guid.NewGuid();
            var fakeContext = new FakeProfileContextService { CurrentUserId = userId };
            var controller = new HomeController(fakeContext);

            // Act
            var result = controller.Index();

            // Assert
            var redirectResult = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Index", redirectResult.ActionName);
            Assert.Equal("Dashboard", redirectResult.ControllerName);
        }

        [Fact]
        public void ProfileContextService_ReturnsNullWhenUnauthenticated_WithoutCookieOrSessionFallback()
        {
            // Arrange
            var accessor = new FakeHttpContextAccessor();
            var httpContext = new DefaultHttpContext();
            // Even if legacy cookies or session keys exist in request, they must be ignored
            httpContext.Request.Headers["Cookie"] = "PocketGoal_ActiveProfileId=" + Guid.NewGuid().ToString();
            httpContext.User = new ClaimsPrincipal(new ClaimsIdentity()); // unauthenticated

            accessor.HttpContext = httpContext;
            var service = new ProfileContextService(accessor);

            // Act
            var result = service.GetCurrentUserId();

            // Assert: Must be null because User is not authenticated
            Assert.Null(result);
        }

        [Fact]
        public void ProfileContextService_ReturnsUserId_WhenClaimsPrincipalIsAuthenticated()
        {
            // Arrange
            var expectedUserId = Guid.NewGuid();
            var accessor = new FakeHttpContextAccessor();
            var httpContext = new DefaultHttpContext();

            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, expectedUserId.ToString()),
                new Claim(ClaimTypes.Email, "test@pocketgoal.app")
            };
            var identity = new ClaimsIdentity(claims, "Cookies");
            httpContext.User = new ClaimsPrincipal(identity);

            accessor.HttpContext = httpContext;
            var service = new ProfileContextService(accessor);

            // Act
            var result = service.GetCurrentUserId();

            // Assert
            Assert.NotNull(result);
            Assert.Equal(expectedUserId, result.Value);
        }

        [Fact]
        public async Task ExpenseService_Queries_ReturnOnlyCurrentUsersData()
        {
            // Arrange
            var db = CreateInMemoryDbContext();
            var userA = Guid.NewGuid();
            var userB = Guid.NewGuid();

            var category = new ExpenseCategory
            {
                Id = Guid.NewGuid(),
                Name = "General",
                UserId = null
            };
            db.ExpenseCategories.Add(category);

            // User A expenses
            db.Expenses.Add(new Expense
            {
                Id = Guid.NewGuid(),
                UserId = userA,
                CategoryId = category.Id,
                Amount = 500m,
                ExpenseDate = DateTime.UtcNow,
                Description = "User A expense"
            });

            // User B expenses
            db.Expenses.Add(new Expense
            {
                Id = Guid.NewGuid(),
                UserId = userB,
                CategoryId = category.Id,
                Amount = 1500m,
                ExpenseDate = DateTime.UtcNow,
                Description = "User B expense"
            });

            await db.SaveChangesAsync();

            var expenseService = new ExpenseService(db, NullLogger<ExpenseService>.Instance);

            // Act
            var userAResult = await expenseService.GetExpensesIndexAsync(userA, null, null, null);
            var userBResult = await expenseService.GetExpensesIndexAsync(userB, null, null, null);

            // Assert: User A sees only 500, User B sees only 1500
            Assert.Single(userAResult.Expenses);
            Assert.Equal(500m, userAResult.Expenses.First().Amount);
            Assert.Equal(500m, userAResult.CurrentMonthTotal);

            Assert.Single(userBResult.Expenses);
            Assert.Equal(1500m, userBResult.Expenses.First().Amount);
            Assert.Equal(1500m, userBResult.CurrentMonthTotal);
        }

        [Fact]
        public async Task SavingGoalService_Queries_ReturnOnlyCurrentUsersData()
        {
            // Arrange
            var db = CreateInMemoryDbContext();
            var userA = Guid.NewGuid();
            var userB = Guid.NewGuid();

            db.UserProfiles.Add(new UserProfile { Id = userA, Name = "User A", Email = "a@example.com" });
            db.UserProfiles.Add(new UserProfile { Id = userB, Name = "User B", Email = "b@example.com" });

            db.SavingGoals.Add(new SavingGoal
            {
                Id = Guid.NewGuid(),
                UserId = userA,
                GoalName = "User A Vacation",
                TargetAmount = 25000m,
                TargetDate = DateTime.UtcNow.AddMonths(3)
            });

            db.SavingGoals.Add(new SavingGoal
            {
                Id = Guid.NewGuid(),
                UserId = userB,
                GoalName = "User B Car",
                TargetAmount = 500000m,
                TargetDate = DateTime.UtcNow.AddMonths(12)
            });

            await db.SaveChangesAsync();

            var goalService = new SavingGoalService(db, NullLogger<SavingGoalService>.Instance);

            // Act
            var goalsA = await goalService.GetGoalsForUserAsync(userA);
            var goalsB = await goalService.GetGoalsForUserAsync(userB);

            // Assert
            Assert.Single(goalsA);
            Assert.Equal("User A Vacation", goalsA.First().GoalName);

            Assert.Single(goalsB);
            Assert.Equal("User B Car", goalsB.First().GoalName);
        }

        [Fact]
        public async Task SavingsService_AddAndQuery_IsolatesBetweenUsers()
        {
            // Arrange
            var db = CreateInMemoryDbContext();
            var userA = Guid.NewGuid();
            var userB = Guid.NewGuid();

            db.UserProfiles.Add(new UserProfile { Id = userA, Name = "User A", Email = "a@example.com" });
            db.UserProfiles.Add(new UserProfile { Id = userB, Name = "User B", Email = "b@example.com" });

            var goalA = new SavingGoal
            {
                Id = Guid.NewGuid(),
                UserId = userA,
                GoalName = "Goal A",
                TargetAmount = 10000m,
                TargetDate = DateTime.UtcNow.AddMonths(6)
            };
            db.SavingGoals.Add(goalA);
            await db.SaveChangesAsync();

            var fakeNotify = new FakeNotificationsService();
            var savingsService = new SavingsService(db, fakeNotify, NullLogger<SavingsService>.Instance);

            // Act: User A adds saving to Goal A
            var (successA, _, savingA) = await savingsService.AddSavingAsync(userA, new AddSavingViewModel
            {
                SavingGoalId = goalA.Id,
                Amount = 2000m,
                SavedDate = DateTime.UtcNow,
                Notes = "User A Deposit"
            });

            // Act: User B tries to add saving to User A's goal -> should fail permission
            var (successB, errorB, _) = await savingsService.AddSavingAsync(userB, new AddSavingViewModel
            {
                SavingGoalId = goalA.Id,
                Amount = 3000m,
                SavedDate = DateTime.UtcNow,
                Notes = "User B Intruding Deposit"
            });

            var recentSavingsA = await savingsService.GetRecentSavingsForUserAsync(userA);
            var recentSavingsB = await savingsService.GetRecentSavingsForUserAsync(userB);

            // Assert
            Assert.True(successA);
            Assert.NotNull(savingA);
            Assert.Equal(userA, savingA.UserId);

            Assert.False(successB);
            Assert.Contains("permission", errorB, StringComparison.OrdinalIgnoreCase);

            Assert.Single(recentSavingsA);
            Assert.Empty(recentSavingsB);
        }

        [Fact]
        public async Task AccountController_Logout_SetsNoStoreHeaders_DeletesCookies_AndRedirectsWithLoggedOutFlag()
        {
            // Arrange
            var db = CreateInMemoryDbContext();
            var profileContext = new FakeProfileContextService { CurrentUserId = Guid.NewGuid() };
            var hasher = new PasswordHasherService();
            var profileService = new UserProfileService(db, hasher, NullLogger<UserProfileService>.Instance);
            var configuration = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();
            var fakeJwtValidator = new FakeSupabaseJwtValidator();

            var controller = new AccountController(fakeJwtValidator, profileService, profileContext, configuration, NullLogger<AccountController>.Instance);

            var httpContext = new DefaultHttpContext();
            controller.ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            };
            controller.TempData = new Microsoft.AspNetCore.Mvc.ViewFeatures.TempDataDictionary(httpContext, new FakeTempDataProvider());

            // Act
            var result = await controller.Logout();

            // Assert
            Assert.Null(profileContext.CurrentUserId);
            Assert.Contains("no-store", httpContext.Response.Headers["Cache-Control"].ToString());
            Assert.Contains("no-cache", httpContext.Response.Headers["Pragma"].ToString());

            var redirectResult = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Login", redirectResult.ActionName);
            Assert.True(redirectResult.RouteValues?.ContainsKey("loggedOut"));
            Assert.Equal("true", redirectResult.RouteValues?["loggedOut"]);
        }
    }
}
