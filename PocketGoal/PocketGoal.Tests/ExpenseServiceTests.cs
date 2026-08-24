using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PocketGoal.Data;
using PocketGoal.Models;
using PocketGoal.Services;
using PocketGoal.ViewModels;
using Xunit;

namespace PocketGoal.Tests
{
    public class ExpenseServiceTests
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
        public async Task ExpenseTracking_CalculatesMonthlyAndCategoryTotalsCorrectly()
        {
            // Arrange
            var db = CreateInMemoryDbContext();
            var service = new ExpenseService(db, NullLogger<ExpenseService>.Instance);

            var userId = Guid.NewGuid();
            var foodCat = await db.ExpenseCategories.FirstAsync(c => c.Name.Contains("Food"));
            var fuelCat = await db.ExpenseCategories.FirstAsync(c => c.Name.Contains("Fuel"));

            // Add expenses for current month
            var now = DateTime.UtcNow;
            db.Expenses.Add(new Expense
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                CategoryId = foodCat.Id,
                Amount = 3000m,
                ExpenseDate = new DateTime(now.Year, now.Month, 10, 0, 0, 0, DateTimeKind.Utc),
                Description = "Groceries"
            });
            db.Expenses.Add(new Expense
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                CategoryId = foodCat.Id,
                Amount = 1000m,
                ExpenseDate = new DateTime(now.Year, now.Month, 15, 0, 0, 0, DateTimeKind.Utc),
                Description = "Dinner"
            });
            db.Expenses.Add(new Expense
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                CategoryId = fuelCat.Id,
                Amount = 2000m,
                ExpenseDate = new DateTime(now.Year, now.Month, 12, 0, 0, 0, DateTimeKind.Utc),
                Description = "Petrol"
            });

            await db.SaveChangesAsync();

            // Act
            var indexVm = await service.GetExpensesIndexAsync(userId, now.Month, now.Year, null);

            // Assert
            Assert.Equal(6000m, indexVm.CurrentMonthTotal);
            Assert.Equal(3, indexVm.Expenses.Count);
            Assert.Equal(2, indexVm.CategorySummaries.Count);

            var foodSummary = indexVm.CategorySummaries.First(c => c.CategoryId == foodCat.Id);
            Assert.Equal(4000m, foodSummary.TotalAmount);
            Assert.Equal(66.7m, foodSummary.PercentageOfTotal);

            var fuelSummary = indexVm.CategorySummaries.First(c => c.CategoryId == fuelCat.Id);
            Assert.Equal(2000m, fuelSummary.TotalAmount);
            Assert.Equal(33.3m, fuelSummary.PercentageOfTotal);
        }

        [Fact]
        public async Task Security_UserCannotDeleteAnotherUsersExpense()
        {
            // Arrange
            var db = CreateInMemoryDbContext();
            var service = new ExpenseService(db, NullLogger<ExpenseService>.Instance);

            var user1 = Guid.NewGuid();
            var user2 = Guid.NewGuid();
            var foodCat = await db.ExpenseCategories.FirstAsync();

            var expense = new Expense
            {
                Id = Guid.NewGuid(),
                UserId = user1,
                CategoryId = foodCat.Id,
                Amount = 500m,
                ExpenseDate = DateTime.UtcNow
            };
            db.Expenses.Add(expense);
            await db.SaveChangesAsync();

            // Act: user2 tries to delete user1's expense
            var result = await service.DeleteExpenseAsync(expense.Id, user2);

            // Assert
            Assert.False(result);
            var exists = await db.Expenses.AnyAsync(e => e.Id == expense.Id);
            Assert.True(exists);
        }
    }
}
