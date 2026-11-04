using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using PocketGoal.Controllers;
using PocketGoal.Data;
using PocketGoal.Models;
using PocketGoal.Models.Chat;
using PocketGoal.Services;
using PocketGoal.ViewModels;
using Xunit;

namespace PocketGoal.Tests
{
    public class ChatBotServiceTests
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

        private static IMemoryCache CreateMemoryCache() => new MemoryCache(new MemoryCacheOptions());

        private class MockGeminiService : IGeminiChatService
        {
            public Func<string, List<ChatMessage>?, DateTime, Task<GeminiExtractionResult>> Handler { get; set; } = null!;
            public Func<string, decimal, Task<ProductSuggestionResult>>? SuggestProductsHandler { get; set; }

            public Task<GeminiExtractionResult> ProcessWithGeminiAsync(string userMessage, List<ChatMessage>? history, DateTime currentDate)
            {
                return Handler(userMessage, history, currentDate);
            }

            public Task<ProductSuggestionResult> SuggestProductsAsync(string item, decimal budget)
            {
                if (SuggestProductsHandler != null)
                {
                    return SuggestProductsHandler(item, budget);
                }
                return Task.FromResult(new ProductSuggestionResult
                {
                    Status = ProductSuggestionStatus.Success,
                    Suggestions = new List<ProductSuggestion>()
                });
            }
        }

        private class MockProfileContextService : IProfileContextService
        {
            public Guid? CurrentUserId { get; set; }
            public Guid? GetCurrentUserId() => CurrentUserId;
            public void SetCurrentUserId(Guid userId) => CurrentUserId = userId;
            public void ClearCurrentUserId() => CurrentUserId = null;
            public Task SignInAsync(UserProfile user, bool isPersistent = false, DateTimeOffset? expiresUtc = null) => Task.CompletedTask;
            public Task SignOutAsync() => Task.CompletedTask;
        }

        private class MockHttpMessageHandler : HttpMessageHandler
        {
            public Func<HttpRequestMessage, HttpResponseMessage> ResponseFactory { get; set; } = null!;
            public int RequestCount { get; private set; }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                RequestCount++;
                return Task.FromResult(ResponseFactory(request));
            }
        }

        private async Task<UserProfile> CreateTestUserAsync(ApplicationDbContext db)
        {
            var user = new UserProfile
            {
                Id = Guid.NewGuid(),
                Name = "Test User",
                Email = "test@example.com",
                PhoneNumber = "+919876543210"
            };
            db.UserProfiles.Add(user);
            await db.SaveChangesAsync();
            return user;
        }

        [Fact]
        public async Task AddExpense_ValidMessage_CreatesExpenseRecordAndReturnsSuccess()
        {
            // Arrange
            var db = CreateInMemoryDbContext();
            var user = await CreateTestUserAsync(db);
            var userId = user.Id;
            var expenseService = new ExpenseService(db, NullLogger<ExpenseService>.Instance);
            var goalService = new SavingGoalService(db, NullLogger<SavingGoalService>.Instance);
            var notifService = new NotificationsService(new ConfigurationBuilder().Build(), db, NullLogger<NotificationsService>.Instance);
            var savingsService = new SavingsService(db, notifService, NullLogger<SavingsService>.Instance);

            var mockGemini = new MockGeminiService
            {
                Handler = (msg, hist, dt) => Task.FromResult(new GeminiExtractionResult
                {
                    Intent = "add_expense",
                    Item = "lunch",
                    Amount = 250m,
                    Category = "Food & Dining",
                    Date = dt.ToString("yyyy-MM-dd"),
                    MissingFields = new List<string>(),
                    Reply = "Added ₹250 for lunch"
                })
            };

            var chatBot = new ChatBotService(mockGemini, expenseService, goalService, savingsService, db, CreateMemoryCache(), NullLogger<ChatBotService>.Instance);

            // Act
            var response = await chatBot.ProcessChatAsync(userId, new ChatRequest { Message = "Add expense: lunch, 250" });

            // Assert
            Assert.True(response.Saved);
            Assert.Contains("₹250", response.Reply);
            Assert.Contains("lunch", response.Reply, StringComparison.OrdinalIgnoreCase);

            var expenseInDb = await db.Expenses.FirstOrDefaultAsync(e => e.UserId == userId);
            Assert.NotNull(expenseInDb);
            Assert.Equal(250m, expenseInDb.Amount);
            Assert.Equal("lunch", expenseInDb.Description);
        }

        [Fact]
        public async Task AddExpense_WithPastDate_ParsesDateCorrectly()
        {
            // Arrange
            var db = CreateInMemoryDbContext();
            var user = await CreateTestUserAsync(db);
            var userId = user.Id;
            var expenseService = new ExpenseService(db, NullLogger<ExpenseService>.Instance);
            var goalService = new SavingGoalService(db, NullLogger<SavingGoalService>.Instance);
            var notifService = new NotificationsService(new ConfigurationBuilder().Build(), db, NullLogger<NotificationsService>.Instance);
            var savingsService = new SavingsService(db, notifService, NullLogger<SavingsService>.Instance);

            var yesterday = DateTime.UtcNow.Date.AddDays(-1);

            var mockGemini = new MockGeminiService
            {
                Handler = (msg, hist, dt) => Task.FromResult(new GeminiExtractionResult
                {
                    Intent = "add_expense",
                    Item = "groceries",
                    Amount = 1200m,
                    Category = "Food & Dining",
                    Date = yesterday.ToString("yyyy-MM-dd"),
                    MissingFields = new List<string>(),
                    Reply = "Added ₹1200 for groceries yesterday"
                })
            };

            var chatBot = new ChatBotService(mockGemini, expenseService, goalService, savingsService, db, CreateMemoryCache(), NullLogger<ChatBotService>.Instance);

            // Act
            var response = await chatBot.ProcessChatAsync(userId, new ChatRequest { Message = "I spent 1200 on groceries yesterday" });

            // Assert
            Assert.True(response.Saved);
            var expenseInDb = await db.Expenses.FirstOrDefaultAsync(e => e.UserId == userId);
            Assert.NotNull(expenseInDb);
            Assert.Equal(1200m, expenseInDb.Amount);
            Assert.Equal(yesterday.Date, expenseInDb.ExpenseDate.Date);
        }

        [Fact]
        public async Task SetGoal_WithInitialSavings_CreatesGoalAndInitialSavingRecord()
        {
            // Arrange
            var db = CreateInMemoryDbContext();
            var user = await CreateTestUserAsync(db);
            var userId = user.Id;
            var expenseService = new ExpenseService(db, NullLogger<ExpenseService>.Instance);
            var goalService = new SavingGoalService(db, NullLogger<SavingGoalService>.Instance);
            var notifService = new NotificationsService(new ConfigurationBuilder().Build(), db, NullLogger<NotificationsService>.Instance);
            var savingsService = new SavingsService(db, notifService, NullLogger<SavingsService>.Instance);

            var mockGemini = new MockGeminiService
            {
                Handler = (msg, hist, dt) => Task.FromResult(new GeminiExtractionResult
                {
                    Intent = "set_goal",
                    Item = "Laptop",
                    Amount = 60000m,
                    CurrentSavings = 15000m,
                    Date = "2026-12-31",
                    MissingFields = new List<string>(),
                    Reply = "Goal set for Laptop at ₹60,000 with ₹15,000 saved!"
                })
            };

            var chatBot = new ChatBotService(mockGemini, expenseService, goalService, savingsService, db, CreateMemoryCache(), NullLogger<ChatBotService>.Instance);

            // Act
            var response = await chatBot.ProcessChatAsync(userId, new ChatRequest
            {
                Message = "Set a goal to buy a laptop for 60000 by December, I have 15000 saved"
            });

            // Assert
            Assert.True(response.Saved, $"Response reply was: {response.Reply}");
            Assert.Contains("Laptop", response.Reply);

            var goalInDb = await db.SavingGoals.FirstOrDefaultAsync(g => g.UserId == userId);
            Assert.NotNull(goalInDb);
            Assert.Equal("Laptop", goalInDb.GoalName);
            Assert.Equal(60000m, goalInDb.TargetAmount);
            Assert.Equal(15000m, goalInDb.CurrentSavedAmount);

            var savingInDb = await db.Savings.FirstOrDefaultAsync(s => s.SavingGoalId == goalInDb.Id);
            Assert.NotNull(savingInDb);
            Assert.Equal(15000m, savingInDb.Amount);
        }

        [Fact]
        public async Task AddSavings_SingleActiveGoal_DepositsToActiveGoal()
        {
            // Arrange
            var db = CreateInMemoryDbContext();
            var user = await CreateTestUserAsync(db);
            var userId = user.Id;
            var expenseService = new ExpenseService(db, NullLogger<ExpenseService>.Instance);
            var goalService = new SavingGoalService(db, NullLogger<SavingGoalService>.Instance);
            var notifService = new NotificationsService(new ConfigurationBuilder().Build(), db, NullLogger<NotificationsService>.Instance);
            var savingsService = new SavingsService(db, notifService, NullLogger<SavingsService>.Instance);

            var goal = new SavingGoal
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                GoalName = "Emergency Fund",
                TargetAmount = 100000m,
                CurrentSavedAmount = 20000m,
                TargetDate = DateTime.UtcNow.AddMonths(12),
                Status = GoalStatus.Active
            };
            db.SavingGoals.Add(goal);
            await db.SaveChangesAsync();

            var mockGemini = new MockGeminiService
            {
                Handler = (msg, hist, dt) => Task.FromResult(new GeminiExtractionResult
                {
                    Intent = "add_savings",
                    Amount = 5000m,
                    MissingFields = new List<string>(),
                    Reply = "Adding ₹5000 to your savings"
                })
            };

            var chatBot = new ChatBotService(mockGemini, expenseService, goalService, savingsService, db, CreateMemoryCache(), NullLogger<ChatBotService>.Instance);

            // Act
            var response = await chatBot.ProcessChatAsync(userId, new ChatRequest { Message = "Add 5000 to my savings" });

            // Assert
            Assert.True(response.Saved, $"Response reply was: {response.Reply}");
            var updatedGoal = await db.SavingGoals.FindAsync(goal.Id);
            Assert.NotNull(updatedGoal);
            Assert.Equal(25000m, updatedGoal.CurrentSavedAmount);
        }

        [Fact]
        public async Task AddSavings_MultipleActiveGoals_NoneSpecified_PromptsDisambiguation()
        {
            // Arrange
            var db = CreateInMemoryDbContext();
            var user = await CreateTestUserAsync(db);
            var userId = user.Id;
            var expenseService = new ExpenseService(db, NullLogger<ExpenseService>.Instance);
            var goalService = new SavingGoalService(db, NullLogger<SavingGoalService>.Instance);
            var notifService = new NotificationsService(new ConfigurationBuilder().Build(), db, NullLogger<NotificationsService>.Instance);
            var savingsService = new SavingsService(db, notifService, NullLogger<SavingsService>.Instance);

            db.SavingGoals.Add(new SavingGoal
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                GoalName = "Laptop",
                TargetAmount = 50000m,
                TargetDate = DateTime.UtcNow.AddMonths(6),
                Status = GoalStatus.Active
            });
            db.SavingGoals.Add(new SavingGoal
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                GoalName = "Vacation",
                TargetAmount = 30000m,
                TargetDate = DateTime.UtcNow.AddMonths(6),
                Status = GoalStatus.Active
            });
            await db.SaveChangesAsync();

            var mockGemini = new MockGeminiService
            {
                Handler = (msg, hist, dt) => Task.FromResult(new GeminiExtractionResult
                {
                    Intent = "add_savings",
                    Amount = 5000m,
                    Item = null,
                    MissingFields = new List<string>(),
                    Reply = "Add savings"
                })
            };

            var chatBot = new ChatBotService(mockGemini, expenseService, goalService, savingsService, db, CreateMemoryCache(), NullLogger<ChatBotService>.Instance);

            // Act
            var response = await chatBot.ProcessChatAsync(userId, new ChatRequest { Message = "Add 5000 to my savings" });

            // Assert
            Assert.False(response.Saved);
            Assert.Contains("Which goal would you like to add", response.Reply);
            Assert.Contains("Laptop", response.Reply);
            Assert.Contains("Vacation", response.Reply);
        }

        [Fact]
        public async Task MissingRequiredFields_DoesNotSave_ReturnsClarifyingQuestion()
        {
            // Arrange
            var db = CreateInMemoryDbContext();
            var user = await CreateTestUserAsync(db);
            var userId = user.Id;
            var expenseService = new ExpenseService(db, NullLogger<ExpenseService>.Instance);
            var goalService = new SavingGoalService(db, NullLogger<SavingGoalService>.Instance);
            var notifService = new NotificationsService(new ConfigurationBuilder().Build(), db, NullLogger<NotificationsService>.Instance);
            var savingsService = new SavingsService(db, notifService, NullLogger<SavingsService>.Instance);

            var mockGemini = new MockGeminiService
            {
                Handler = (msg, hist, dt) => Task.FromResult(new GeminiExtractionResult
                {
                    Intent = "add_expense",
                    Item = "lunch",
                    Amount = null,
                    MissingFields = new List<string> { "amount" },
                    Reply = "How much did your lunch cost?"
                })
            };

            var chatBot = new ChatBotService(mockGemini, expenseService, goalService, savingsService, db, CreateMemoryCache(), NullLogger<ChatBotService>.Instance);

            // Act
            var response = await chatBot.ProcessChatAsync(userId, new ChatRequest { Message = "I had lunch" });

            // Assert
            Assert.False(response.Saved);
            Assert.Equal("How much did your lunch cost?", response.Reply);
            Assert.Null(response.Record);

            var count = await db.Expenses.CountAsync();
            Assert.Equal(0, count);
        }

        [Fact]
        public async Task SuggestProducts_MissingBudget_PromptsFollowUpQuestion()
        {
            // Arrange
            var db = CreateInMemoryDbContext();
            var user = await CreateTestUserAsync(db);
            var userId = user.Id;
            var expenseService = new ExpenseService(db, NullLogger<ExpenseService>.Instance);
            var goalService = new SavingGoalService(db, NullLogger<SavingGoalService>.Instance);
            var notifService = new NotificationsService(new ConfigurationBuilder().Build(), db, NullLogger<NotificationsService>.Instance);
            var savingsService = new SavingsService(db, notifService, NullLogger<SavingsService>.Instance);

            var mockGemini = new MockGeminiService
            {
                Handler = (msg, hist, dt) => Task.FromResult(new GeminiExtractionResult
                {
                    Intent = "suggest_products",
                    Item = "laptop",
                    Amount = null,
                    MissingFields = new List<string> { "amount" },
                    Reply = "What is your budget for the laptop in ₹?"
                })
            };

            var chatBot = new ChatBotService(mockGemini, expenseService, goalService, savingsService, db, CreateMemoryCache(), NullLogger<ChatBotService>.Instance);

            // Act
            var response = await chatBot.ProcessChatAsync(userId, new ChatRequest { Message = "Suggest a laptop" });

            // Assert
            Assert.False(response.Saved);
            Assert.Contains("budget", response.Reply, StringComparison.OrdinalIgnoreCase);
            Assert.Null(response.Suggestions);
        }

        [Fact]
        public async Task SuggestProducts_WithBudget_ReturnsSuggestions()
        {
            // Arrange
            var db = CreateInMemoryDbContext();
            var user = await CreateTestUserAsync(db);
            var userId = user.Id;
            var expenseService = new ExpenseService(db, NullLogger<ExpenseService>.Instance);
            var goalService = new SavingGoalService(db, NullLogger<SavingGoalService>.Instance);
            var notifService = new NotificationsService(new ConfigurationBuilder().Build(), db, NullLogger<NotificationsService>.Instance);
            var savingsService = new SavingsService(db, notifService, NullLogger<SavingsService>.Instance);

            var mockGemini = new MockGeminiService
            {
                Handler = (msg, hist, dt) => Task.FromResult(new GeminiExtractionResult
                {
                    Intent = "suggest_products",
                    Item = "laptop",
                    Amount = 60000m,
                    MissingFields = new List<string>(),
                    Reply = "Looking up top options under ₹60,000..."
                }),
                SuggestProductsHandler = (item, budget) => Task.FromResult(new ProductSuggestionResult
                {
                    Status = ProductSuggestionStatus.Success,
                    IsLive = true,
                    Suggestions = new List<ProductSuggestion>
                    {
                        new ProductSuggestion
                        {
                            Name = "Acer Aspire 5",
                            ApproxPrice = 52990m,
                            Reason = "Great Core i5 performance and display.",
                            SourceUrl = "https://example.com/acer",
                            SearchUrl = "https://www.amazon.in/s?k=Acer+Aspire+5",
                            FlipkartUrl = "https://www.flipkart.com/search?q=Acer+Aspire+5"
                        },
                        new ProductSuggestion
                        {
                            Name = "Lenovo IdeaPad Slim 3",
                            ApproxPrice = 58990m,
                            Reason = "Reliable daily multitasking laptop with fast SSD.",
                            SourceUrl = "https://example.com/lenovo",
                            SearchUrl = "https://www.amazon.in/s?k=Lenovo+IdeaPad+Slim+3",
                            FlipkartUrl = "https://www.flipkart.com/search?q=Lenovo+IdeaPad+Slim+3"
                        }
                    }
                })
            };

            var chatBot = new ChatBotService(mockGemini, expenseService, goalService, savingsService, db, CreateMemoryCache(), NullLogger<ChatBotService>.Instance);

            // Act
            var response = await chatBot.ProcessChatAsync(userId, new ChatRequest { Message = "Suggest a laptop under 60000" });

            // Assert
            Assert.False(response.Saved);
            Assert.True(response.IsLive);
            Assert.NotNull(response.Suggestions);
            Assert.Equal(2, response.Suggestions.Count);
            Assert.Equal("Acer Aspire 5", response.Suggestions[0].Name);
            Assert.Equal(52990m, response.Suggestions[0].ApproxPrice);
            Assert.Equal("https://www.flipkart.com/search?q=Acer+Aspire+5", response.Suggestions[0].FlipkartUrl);
        }

        [Fact]
        public async Task SuggestProducts_BudgetFiltering_DiscardsItemsAboveBudgetOrZeroPrice()
        {
            // Arrange
            var step1ResponseJson = @"{
                ""candidates"": [
                    {
                        ""content"": {
                            ""parts"": [
                                { ""text"": ""Here are 3 laptops: Acer Aspire for 52000, Dell Inspiron for 68000, and Broken Laptop for 0."" }
                            ]
                        },
                        ""groundingMetadata"": {
                            ""groundingChunks"": [
                                { ""web"": { ""uri"": ""https://www.acer.com/in-en/laptops"" } },
                                { ""web"": { ""uri"": ""https://www.dell.com/en-in"" } }
                            ]
                        }
                    }
                ]
            }";

            var step2ResponseJson = @"{
                ""candidates"": [
                    {
                        ""content"": {
                            ""parts"": [
                                { ""text"": ""[{\""name\"": \""Acer Aspire 5\"", \""approxPrice\"": 52000, \""reason\"": \""Solid i5 laptop\"", \""sourceUrl\"": \""https://www.acer.com/in-en/laptops\""}, {\""name\"": \""Dell Inspiron\"", \""approxPrice\"": 68000, \""reason\"": \""Over budget\"", \""sourceUrl\"": \""https://www.dell.com/en-in\""}, {\""name\"": \""Invalid Item\"", \""approxPrice\"": 0, \""reason\"": \""Zero price item\""}]"" }
                            ]
                        }
                    }
                ]
            }";

            int callIndex = 0;
            var handler = new MockHttpMessageHandler
            {
                ResponseFactory = req =>
                {
                    callIndex++;
                    var json = callIndex == 1 ? step1ResponseJson : step2ResponseJson;
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
                    };
                }
            };

            var client = new HttpClient(handler);
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { { "GEMINI_API_KEY", "test-key" } })
                .Build();

            var service = new GeminiChatService(client, config, NullLogger<GeminiChatService>.Instance);

            // Act
            var result = await service.SuggestProductsAsync("laptop", 60000m);

            // Assert: Only Acer Aspire (52,000) should be included. Dell Inspiron (68,000 > 60,000) and Invalid Item (0) must be discarded!
            Assert.Equal(ProductSuggestionStatus.Success, result.Status);
            Assert.Single(result.Suggestions);
            Assert.Equal("Acer Aspire 5", result.Suggestions[0].Name);
            Assert.Equal(52000m, result.Suggestions[0].ApproxPrice);
        }

        [Fact]
        public async Task SuggestProducts_UrlValidation_OnlyHttpsGroundingUrlsAccepted()
        {
            // Arrange
            var step1ResponseJson = @"{
                ""candidates"": [
                    {
                        ""content"": {
                            ""parts"": [
                                { ""text"": ""Found headphones under 3000"" }
                            ]
                        },
                        ""groundingMetadata"": {
                            ""groundingChunks"": [
                                { ""web"": { ""uri"": ""https://www.boat-lifestyle.com/products"" } },
                                { ""web"": { ""uri"": ""http://insecure.example.com"" } }
                            ]
                        }
                    }
                ]
            }";

            var step2ResponseJson = @"{
                ""candidates"": [
                    {
                        ""content"": {
                            ""parts"": [
                                { ""text"": ""[{\""name\"": \""boAt Rockerz 450\"", \""approxPrice\"": 1499, \""reason\"": \""Great bass\"", \""sourceUrl\"": \""https://www.boat-lifestyle.com/products\""}, {\""name\"": \""Insecure Link Model\"", \""approxPrice\"": 1999, \""reason\"": \""Has http link\"", \""sourceUrl\"": \""http://insecure.example.com\""}, {\""name\"": \""Hallucinated Model\"", \""approxPrice\"": 2499, \""reason\"": \""Ungrounded link\"", \""sourceUrl\"": \""https://hallucinated-store.com/item\""}]"" }
                            ]
                        }
                    }
                ]
            }";

            int callIndex = 0;
            var handler = new MockHttpMessageHandler
            {
                ResponseFactory = req =>
                {
                    callIndex++;
                    var json = callIndex == 1 ? step1ResponseJson : step2ResponseJson;
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
                    };
                }
            };

            var client = new HttpClient(handler);
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { { "GEMINI_API_KEY", "test-key" } })
                .Build();

            var service = new GeminiChatService(client, config, NullLogger<GeminiChatService>.Instance);

            // Act
            var result = await service.SuggestProductsAsync("headphones", 3000m);

            // Assert
            Assert.Equal(3, result.Suggestions.Count);

            // boAt Rockerz 450 has valid https grounding url
            Assert.Equal("https://www.boat-lifestyle.com/products", result.Suggestions[0].SourceUrl);

            // Insecure Link Model had http (not https) -> must be null
            Assert.Null(result.Suggestions[1].SourceUrl);

            // Hallucinated Model had an https url NOT in groundingChunks -> must be null
            Assert.Null(result.Suggestions[2].SourceUrl);

            // All should have valid Amazon and Flipkart search URLs generated
            foreach (var item in result.Suggestions)
            {
                Assert.StartsWith("https://www.amazon.in/s?k=", item.SearchUrl);
                Assert.NotNull(item.FlipkartUrl);
                Assert.StartsWith("https://www.flipkart.com/search?q=", item.FlipkartUrl);
            }
        }

        [Fact]
        public async Task SuggestProducts_Grounded429_TriggersFallbackAndReturnsUngroundedSuggestions()
        {
            // Arrange: Grounded search returns 429 TooManyRequests
            // Then Fallback request (call 2) succeeds with valid JSON (no search tool)
            var fallbackResponseJson = @"{
                ""candidates"": [
                    {
                        ""content"": {
                            ""parts"": [
                                { ""text"": ""[{\""name\"": \""Redmi Note 13\"", \""approxPrice\"": 17999, \""reason\"": \""Great battery and AMOLED screen\""}]"" }
                            ]
                        }
                    }
                ]
            }";

            var handler = new MockHttpMessageHandler
            {
                ResponseFactory = req =>
                {
                    var contentStr = req.Content != null ? req.Content.ReadAsStringAsync().Result : "";
                    if (contentStr.Contains("googleSearch"))
                    {
                        // Grounded search fails with 429
                        return new HttpResponseMessage(HttpStatusCode.TooManyRequests)
                        {
                            Content = new StringContent(@"{""error"": {""code"": 429, ""message"": ""Resource has been exhausted (e.g. check quota).""}}")
                        };
                    }
                    // Fallback call
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(fallbackResponseJson, System.Text.Encoding.UTF8, "application/json")
                    };
                }
            };

            var client = new HttpClient(handler);
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { { "GEMINI_API_KEY", "test-key" } })
                .Build();

            var service = new GeminiChatService(client, config, NullLogger<GeminiChatService>.Instance);

            // Act
            var result = await service.SuggestProductsAsync("phone", 20000m);

            // Assert
            Assert.Equal(ProductSuggestionStatus.Success, result.Status);
            Assert.False(result.IsLive); // Fallback is marked as isLive = false
            Assert.Single(result.Suggestions);
            var product = result.Suggestions[0];
            Assert.Equal("Redmi Note 13", product.Name);
            Assert.Equal(17999m, product.ApproxPrice);
            Assert.Null(product.SourceUrl); // In fallback mode, sourceUrl MUST be null
            Assert.StartsWith("https://www.amazon.in/s?k=", product.SearchUrl);
            Assert.NotNull(product.FlipkartUrl);
            Assert.StartsWith("https://www.flipkart.com/search?q=", product.FlipkartUrl);
        }

        [Fact]
        public async Task SuggestProducts_Fallback_NeverReturnsSourceUrl()
        {
            // Arrange: Verify that even if model returns a sourceUrl in fallback JSON, it is stripped/set to null
            var fallbackResponseJson = @"{
                ""candidates"": [
                    {
                        ""content"": {
                            ""parts"": [
                                { ""text"": ""[{\""name\"": \""Sony WH-CH520\"", \""approxPrice\"": 4490, \""reason\"": \""Up to 50 hours battery life\"", \""sourceUrl\"": \""https://sony.com/headphones\""}]"" }
                            ]
                        }
                    }
                ]
            }";

            var handler = new MockHttpMessageHandler
            {
                ResponseFactory = req =>
                {
                    var contentStr = req.Content != null ? req.Content.ReadAsStringAsync().Result : "";
                    if (contentStr.Contains("googleSearch"))
                    {
                        // Grounded search fails
                        return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                        {
                            Content = new StringContent(@"{""error"": {""code"": 503, ""message"": ""Overloaded""}}")
                        };
                    }
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(fallbackResponseJson, System.Text.Encoding.UTF8, "application/json")
                    };
                }
            };

            var client = new HttpClient(handler);
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { { "GEMINI_API_KEY", "test-key" } })
                .Build();

            var service = new GeminiChatService(client, config, NullLogger<GeminiChatService>.Instance);

            // Act
            var result = await service.SuggestProductsAsync("headphones", 5000m);

            // Assert
            Assert.Equal(ProductSuggestionStatus.Success, result.Status);
            Assert.False(result.IsLive);
            Assert.Single(result.Suggestions);
            Assert.Null(result.Suggestions[0].SourceUrl); // MUST be null in fallback
            Assert.NotNull(result.Suggestions[0].FlipkartUrl);
        }

        [Fact]
        public async Task ChatBotService_CacheHit_OnRepeatedQuery_CallsGeminiOnlyOnce()
        {
            // Arrange
            var db = CreateInMemoryDbContext();
            var user = await CreateTestUserAsync(db);
            var userId = user.Id;
            var expenseService = new ExpenseService(db, NullLogger<ExpenseService>.Instance);
            var goalService = new SavingGoalService(db, NullLogger<SavingGoalService>.Instance);
            var notifService = new NotificationsService(new ConfigurationBuilder().Build(), db, NullLogger<NotificationsService>.Instance);
            var savingsService = new SavingsService(db, notifService, NullLogger<SavingsService>.Instance);
            var memoryCache = CreateMemoryCache();

            int geminiSuggestCalls = 0;
            var mockGemini = new MockGeminiService
            {
                Handler = (msg, hist, dt) => Task.FromResult(new GeminiExtractionResult
                {
                    Intent = "suggest_products",
                    Item = "laptop",
                    Amount = 50000m,
                    MissingFields = new List<string>(),
                    Reply = "Here are suggestions"
                }),
                SuggestProductsHandler = (item, budget) =>
                {
                    geminiSuggestCalls++;
                    return Task.FromResult(new ProductSuggestionResult
                    {
                        Status = ProductSuggestionStatus.Success,
                        IsLive = true,
                        Suggestions = new List<ProductSuggestion>
                        {
                            new ProductSuggestion
                            {
                                Name = "HP 15s",
                                ApproxPrice = 45000m,
                                Reason = "Solid everyday laptop"
                            }
                        }
                    });
                }
            };

            var chatBot = new ChatBotService(mockGemini, expenseService, goalService, savingsService, db, memoryCache, NullLogger<ChatBotService>.Instance);

            // Act 1: First request
            var resp1 = await chatBot.ProcessChatAsync(userId, new ChatRequest { Message = "Suggest a laptop under 50000" });

            // Act 2: Second request with same item and budget
            var resp2 = await chatBot.ProcessChatAsync(userId, new ChatRequest { Message = "Suggest a laptop under 50000" });

            // Assert: Only 1 Gemini call was made, second was served from cache
            Assert.Equal(1, geminiSuggestCalls);
            Assert.NotNull(resp1.Suggestions);
            Assert.NotNull(resp2.Suggestions);
            Assert.Equal("HP 15s", resp2.Suggestions[0].Name);
        }

        [Fact]
        public async Task ChatBotService_UserSearchLimit_Exceeded_ReturnsFriendlyMessage()
        {
            // Arrange
            var db = CreateInMemoryDbContext();
            var user = await CreateTestUserAsync(db);
            var userId = user.Id;
            var expenseService = new ExpenseService(db, NullLogger<ExpenseService>.Instance);
            var goalService = new SavingGoalService(db, NullLogger<SavingGoalService>.Instance);
            var notifService = new NotificationsService(new ConfigurationBuilder().Build(), db, NullLogger<NotificationsService>.Instance);
            var savingsService = new SavingsService(db, notifService, NullLogger<SavingsService>.Instance);
            var memoryCache = CreateMemoryCache();

            var mockGemini = new MockGeminiService
            {
                Handler = (msg, hist, dt) => Task.FromResult(new GeminiExtractionResult
                {
                    Intent = "suggest_products",
                    Item = msg, // Different item each time so cache doesn't skip rate check
                    Amount = 1000m,
                    MissingFields = new List<string>(),
                    Reply = "Searching"
                }),
                SuggestProductsHandler = (item, budget) => Task.FromResult(new ProductSuggestionResult
                {
                    Status = ProductSuggestionStatus.Success,
                    Suggestions = new List<ProductSuggestion> { new ProductSuggestion { Name = item, ApproxPrice = budget } }
                })
            };

            var chatBot = new ChatBotService(mockGemini, expenseService, goalService, savingsService, db, memoryCache, NullLogger<ChatBotService>.Instance);

            // Make 5 successful searches
            for (int i = 1; i <= 5; i++)
            {
                var resp = await chatBot.ProcessChatAsync(userId, new ChatRequest { Message = $"item{i}" });
                Assert.NotNull(resp.Suggestions);
            }

            // Act: 6th search in the same hour
            var resp6 = await chatBot.ProcessChatAsync(userId, new ChatRequest { Message = "item6" });

            // Assert: Rate limit exceeded friendly message
            Assert.Null(resp6.Suggestions);
            Assert.Contains("reached your limit of 5 product searches per hour", resp6.Reply);
        }

        [Fact]
        public async Task SuggestProducts_BothGroundedAndFallbackFail_ReturnsBusyMessage()
        {
            // Arrange
            var db = CreateInMemoryDbContext();
            var user = await CreateTestUserAsync(db);
            var userId = user.Id;
            var expenseService = new ExpenseService(db, NullLogger<ExpenseService>.Instance);
            var goalService = new SavingGoalService(db, NullLogger<SavingGoalService>.Instance);
            var notifService = new NotificationsService(new ConfigurationBuilder().Build(), db, NullLogger<NotificationsService>.Instance);
            var savingsService = new SavingsService(db, notifService, NullLogger<SavingsService>.Instance);
            var memoryCache = CreateMemoryCache();

            var mockGemini = new MockGeminiService
            {
                Handler = (msg, hist, dt) => Task.FromResult(new GeminiExtractionResult
                {
                    Intent = "suggest_products",
                    Item = "laptop",
                    Amount = 50000m,
                    MissingFields = new List<string>(),
                    Reply = "Looking"
                }),
                SuggestProductsHandler = (item, budget) => Task.FromResult(new ProductSuggestionResult
                {
                    Status = ProductSuggestionStatus.Overloaded,
                    ErrorMessage = "The AI service is busy right now. Please try again in a minute."
                })
            };

            var chatBot = new ChatBotService(mockGemini, expenseService, goalService, savingsService, db, memoryCache, NullLogger<ChatBotService>.Instance);

            // Act
            var response = await chatBot.ProcessChatAsync(userId, new ChatRequest { Message = "Suggest a laptop under 50000" });

            // Assert: Must say "The AI service is busy right now. Please try again in a minute."
            // and NOT say "couldn't find products"
            Assert.Equal("The AI service is busy right now. Please try again in a minute.", response.Reply);
            Assert.Null(response.Suggestions);
        }

        [Fact]
        public async Task CreateGoalFromSuggestion_ValidInput_CreatesSavingGoal()
        {
            // Arrange
            var db = CreateInMemoryDbContext();
            var user = await CreateTestUserAsync(db);
            var mockProfile = new MockProfileContextService { CurrentUserId = user.Id };
            var goalService = new SavingGoalService(db, NullLogger<SavingGoalService>.Instance);
            var notifService = new NotificationsService(new ConfigurationBuilder().Build(), db, NullLogger<NotificationsService>.Instance);
            var savingsService = new SavingsService(db, notifService, NullLogger<SavingsService>.Instance);
            var expenseService = new ExpenseService(db, NullLogger<ExpenseService>.Instance);
            var chatBot = new ChatBotService(new MockGeminiService(), expenseService, goalService, savingsService, db, CreateMemoryCache(), NullLogger<ChatBotService>.Instance);

            var controller = new ChatController(chatBot, goalService, mockProfile, NullLogger<ChatController>.Instance);

            // Act
            var result = await controller.CreateGoalFromSuggestion(new CreateGoalFromSuggestionRequest
            {
                Name = "Asus Vivobook 15",
                Price = 45990m
            });

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            var resp = Assert.IsType<CreateGoalFromSuggestionResponse>(okResult.Value);
            Assert.True(resp.Success);
            Assert.NotNull(resp.GoalId);
            Assert.Contains("Asus Vivobook 15", resp.Message);

            var goalInDb = await db.SavingGoals.FindAsync(resp.GoalId.Value);
            Assert.NotNull(goalInDb);
            Assert.Equal("Asus Vivobook 15", goalInDb.GoalName);
            Assert.Equal(45990m, goalInDb.TargetAmount);
            Assert.Equal(0m, goalInDb.CurrentSavedAmount);
            Assert.Equal(GoalStatus.Active, goalInDb.Status);
        }

        [Fact]
        public async Task CreateGoalFromSuggestion_InvalidPriceOrName_ReturnsBadRequest()
        {
            // Arrange
            var db = CreateInMemoryDbContext();
            var user = await CreateTestUserAsync(db);
            var mockProfile = new MockProfileContextService { CurrentUserId = user.Id };
            var goalService = new SavingGoalService(db, NullLogger<SavingGoalService>.Instance);
            var notifService = new NotificationsService(new ConfigurationBuilder().Build(), db, NullLogger<NotificationsService>.Instance);
            var savingsService = new SavingsService(db, notifService, NullLogger<SavingsService>.Instance);
            var expenseService = new ExpenseService(db, NullLogger<ExpenseService>.Instance);
            var chatBot = new ChatBotService(new MockGeminiService(), expenseService, goalService, savingsService, db, CreateMemoryCache(), NullLogger<ChatBotService>.Instance);

            var controller = new ChatController(chatBot, goalService, mockProfile, NullLogger<ChatController>.Instance);

            // Act 1: Empty name
            var res1 = await controller.CreateGoalFromSuggestion(new CreateGoalFromSuggestionRequest
            {
                Name = "",
                Price = 5000m
            });
            Assert.IsType<BadRequestObjectResult>(res1);

            // Act 2: Negative price
            var res2 = await controller.CreateGoalFromSuggestion(new CreateGoalFromSuggestionRequest
            {
                Name = "Tablet",
                Price = -500m
            });
            Assert.IsType<BadRequestObjectResult>(res2);

            // Act 3: Excessively huge price
            var res3 = await controller.CreateGoalFromSuggestion(new CreateGoalFromSuggestionRequest
            {
                Name = "Luxury Island",
                Price = 9999999999m
            });
            Assert.IsType<BadRequestObjectResult>(res3);

            // Act 4: Name exceeding 150 characters
            var res4 = await controller.CreateGoalFromSuggestion(new CreateGoalFromSuggestionRequest
            {
                Name = new string('A', 151),
                Price = 5000m
            });
            Assert.IsType<BadRequestObjectResult>(res4);
        }

        [Fact]
        public async Task CreateGoalFromSuggestion_Unauthenticated_ReturnsUnauthorized()
        {
            // Arrange
            var db = CreateInMemoryDbContext();
            var mockProfile = new MockProfileContextService { CurrentUserId = null };
            var goalService = new SavingGoalService(db, NullLogger<SavingGoalService>.Instance);
            var notifService = new NotificationsService(new ConfigurationBuilder().Build(), db, NullLogger<NotificationsService>.Instance);
            var savingsService = new SavingsService(db, notifService, NullLogger<SavingsService>.Instance);
            var expenseService = new ExpenseService(db, NullLogger<ExpenseService>.Instance);
            var chatBot = new ChatBotService(new MockGeminiService(), expenseService, goalService, savingsService, db, CreateMemoryCache(), NullLogger<ChatBotService>.Instance);

            var controller = new ChatController(chatBot, goalService, mockProfile, NullLogger<ChatController>.Instance);

            // Act
            var result = await controller.CreateGoalFromSuggestion(new CreateGoalFromSuggestionRequest
            {
                Name = "Phone",
                Price = 20000m
            });

            // Assert
            Assert.IsType<UnauthorizedObjectResult>(result);
        }

        [Fact]
        public async Task UnknownIntent_DoesNotSave_ReturnsHelpfulReply()
        {
            // Arrange
            var db = CreateInMemoryDbContext();
            var user = await CreateTestUserAsync(db);
            var userId = user.Id;
            var expenseService = new ExpenseService(db, NullLogger<ExpenseService>.Instance);
            var goalService = new SavingGoalService(db, NullLogger<SavingGoalService>.Instance);
            var notifService = new NotificationsService(new ConfigurationBuilder().Build(), db, NullLogger<NotificationsService>.Instance);
            var savingsService = new SavingsService(db, notifService, NullLogger<SavingsService>.Instance);

            var mockGemini = new MockGeminiService
            {
                Handler = (msg, hist, dt) => Task.FromResult(new GeminiExtractionResult
                {
                    Intent = "unknown",
                    MissingFields = new List<string>(),
                    Reply = "Hi! I can help you add expenses, set goals, or deposit savings."
                })
            };

            var chatBot = new ChatBotService(mockGemini, expenseService, goalService, savingsService, db, CreateMemoryCache(), NullLogger<ChatBotService>.Instance);

            // Act
            var response = await chatBot.ProcessChatAsync(userId, new ChatRequest { Message = "Hello, what's the weather?" });

            // Assert
            Assert.False(response.Saved);
            Assert.Contains("help you add expenses", response.Reply);
        }

        [Fact]
        public async Task InvalidAmount_ZeroOrNegative_RejectsAndReturnsErrorMessage()
        {
            // Arrange
            var db = CreateInMemoryDbContext();
            var user = await CreateTestUserAsync(db);
            var userId = user.Id;
            var expenseService = new ExpenseService(db, NullLogger<ExpenseService>.Instance);
            var goalService = new SavingGoalService(db, NullLogger<SavingGoalService>.Instance);
            var notifService = new NotificationsService(new ConfigurationBuilder().Build(), db, NullLogger<NotificationsService>.Instance);
            var savingsService = new SavingsService(db, notifService, NullLogger<SavingsService>.Instance);

            var mockGemini = new MockGeminiService
            {
                Handler = (msg, hist, dt) => Task.FromResult(new GeminiExtractionResult
                {
                    Intent = "add_expense",
                    Item = "lunch",
                    Amount = -50m,
                    MissingFields = new List<string>(),
                    Reply = "Added expense"
                })
            };

            var chatBot = new ChatBotService(mockGemini, expenseService, goalService, savingsService, db, CreateMemoryCache(), NullLogger<ChatBotService>.Instance);

            // Act
            var response = await chatBot.ProcessChatAsync(userId, new ChatRequest { Message = "lunch -50" });

            // Assert
            Assert.False(response.Saved);
            Assert.Contains("positive amount", response.Reply, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task ChatController_Unauthenticated_ReturnsUnauthorized()
        {
            // Arrange
            var mockProfile = new MockProfileContextService { CurrentUserId = null };
            var mockGemini = new MockGeminiService();
            var db = CreateInMemoryDbContext();
            var expenseService = new ExpenseService(db, NullLogger<ExpenseService>.Instance);
            var goalService = new SavingGoalService(db, NullLogger<SavingGoalService>.Instance);
            var notifService = new NotificationsService(new ConfigurationBuilder().Build(), db, NullLogger<NotificationsService>.Instance);
            var savingsService = new SavingsService(db, notifService, NullLogger<SavingsService>.Instance);
            var chatBot = new ChatBotService(mockGemini, expenseService, goalService, savingsService, db, CreateMemoryCache(), NullLogger<ChatBotService>.Instance);

            var controller = new ChatController(chatBot, goalService, mockProfile, NullLogger<ChatController>.Instance);

            // Act
            var result = await controller.Post(new ChatRequest { Message = "lunch 250" });

            // Assert
            var unauth = Assert.IsType<UnauthorizedObjectResult>(result);
            var chatResponse = Assert.IsType<ChatResponse>(unauth.Value);
            Assert.False(chatResponse.Saved);
            Assert.Contains("log in", chatResponse.Reply, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task GeminiChatService_MissingApiKey_ReturnsFriendlyConfigurationError()
        {
            // Arrange
            var config = new ConfigurationBuilder().Build(); // No GEMINI_API_KEY
            var client = new HttpClient();
            var service = new GeminiChatService(client, config, NullLogger<GeminiChatService>.Instance);

            // Act
            var result = await service.ProcessWithGeminiAsync("lunch 250", null, DateTime.UtcNow);

            // Assert
            Assert.Equal("unknown", result.Intent);
            Assert.Contains("GEMINI_API_KEY", result.Reply);
        }

        [Fact]
        public async Task GeminiChatService_429RateLimit_RetriesAndReturnsFriendlyMessage()
        {
            // Arrange
            var handler = new MockHttpMessageHandler
            {
                ResponseFactory = req => new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            };
            var httpClient = new HttpClient(handler);
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    { "GEMINI_API_KEY", "test-key" }
                })
                .Build();

            var service = new GeminiChatService(httpClient, config, NullLogger<GeminiChatService>.Instance);

            // Act
            var result = await service.ProcessWithGeminiAsync("lunch 250", null, DateTime.UtcNow);

            // Assert: verify it retried (2 requests sent)
            Assert.Equal(2, handler.RequestCount);
            Assert.Equal("unknown", result.Intent);
            Assert.Contains("busy right now", result.Reply, StringComparison.OrdinalIgnoreCase);
        }

        #region Multilingual (Tamil, Tanglish & Mixed) Unit Tests

        [Fact]
        public async Task ChatBot_Tamil_AddExpense_CreatesExpenseRecordAndReturnsTamilReply()
        {
            // Arrange: User sends Tamil script: "மதிய உணவிற்கு 250 செலவு"
            var db = CreateInMemoryDbContext();
            var user = await CreateTestUserAsync(db);
            var userId = user.Id;
            var expenseService = new ExpenseService(db, NullLogger<ExpenseService>.Instance);
            var goalService = new SavingGoalService(db, NullLogger<SavingGoalService>.Instance);
            var notifService = new NotificationsService(new ConfigurationBuilder().Build(), db, NullLogger<NotificationsService>.Instance);
            var savingsService = new SavingsService(db, notifService, NullLogger<SavingsService>.Instance);

            var mockGemini = new MockGeminiService
            {
                Handler = (msg, hist, dt) => Task.FromResult(new GeminiExtractionResult
                {
                    Intent = "add_expense",
                    Item = "Lunch", // Model normalizes to English
                    Amount = 250m,
                    Category = "Food & Dining",
                    Date = dt.ToString("yyyy-MM-dd"),
                    MissingFields = new List<string>(),
                    Reply = "மதிய உணவிற்காக ₹250 சேர்க்கப்பட்டது!" // Model replies in Tamil script
                })
            };

            var chatBot = new ChatBotService(mockGemini, expenseService, goalService, savingsService, db, CreateMemoryCache(), NullLogger<ChatBotService>.Instance);

            // Act
            var response = await chatBot.ProcessChatAsync(userId, new ChatRequest { Message = "மதிய உணவிற்கு 250 செலவு" });

            // Assert
            Assert.True(response.Saved);
            Assert.Contains("₹250", response.Reply);
            Assert.Contains("மதிய", response.Reply);

            var expenseInDb = await db.Expenses.FirstOrDefaultAsync(e => e.UserId == userId);
            Assert.NotNull(expenseInDb);
            Assert.Equal(250m, expenseInDb.Amount);
            Assert.Equal("Lunch", expenseInDb.Description);
        }

        [Fact]
        public async Task ChatBot_Tanglish_AddExpense_MapsCategoryAndAmountCorrectly()
        {
            // Arrange: User sends Tanglish: "petrol ku 500 pottaen inniku"
            var db = CreateInMemoryDbContext();
            var user = await CreateTestUserAsync(db);
            var userId = user.Id;
            var expenseService = new ExpenseService(db, NullLogger<ExpenseService>.Instance);
            var goalService = new SavingGoalService(db, NullLogger<SavingGoalService>.Instance);
            var notifService = new NotificationsService(new ConfigurationBuilder().Build(), db, NullLogger<NotificationsService>.Instance);
            var savingsService = new SavingsService(db, notifService, NullLogger<SavingsService>.Instance);

            var today = DateTime.UtcNow.Date;
            var mockGemini = new MockGeminiService
            {
                Handler = (msg, hist, dt) => Task.FromResult(new GeminiExtractionResult
                {
                    Intent = "add_expense",
                    Item = "Petrol",
                    Amount = 500m,
                    Category = "Fuel & Vehicle",
                    Date = today.ToString("yyyy-MM-dd"),
                    MissingFields = new List<string>(),
                    Reply = "Petrol-kaga ₹500 add panniyaachu! 👍"
                })
            };

            var chatBot = new ChatBotService(mockGemini, expenseService, goalService, savingsService, db, CreateMemoryCache(), NullLogger<ChatBotService>.Instance);

            // Act
            var response = await chatBot.ProcessChatAsync(userId, new ChatRequest { Message = "petrol ku 500 pottaen inniku" });

            // Assert
            Assert.True(response.Saved);
            Assert.Contains("₹500", response.Reply);
            Assert.Contains("add panniyaachu", response.Reply, StringComparison.OrdinalIgnoreCase);

            var expenseInDb = await db.Expenses.FirstOrDefaultAsync(e => e.UserId == userId);
            Assert.NotNull(expenseInDb);
            Assert.Equal(500m, expenseInDb.Amount);
            Assert.Equal("Petrol", expenseInDb.Description);
            Assert.Equal(today, expenseInDb.ExpenseDate.Date);
        }

        [Fact]
        public async Task ChatBot_Tanglish_AddExpense_WithPastDate_ParsesDateCorrectly()
        {
            // Arrange: User sends Tanglish with relative past time: "nethu maligai saaman 1200 vaanginaen"
            var db = CreateInMemoryDbContext();
            var user = await CreateTestUserAsync(db);
            var userId = user.Id;
            var expenseService = new ExpenseService(db, NullLogger<ExpenseService>.Instance);
            var goalService = new SavingGoalService(db, NullLogger<SavingGoalService>.Instance);
            var notifService = new NotificationsService(new ConfigurationBuilder().Build(), db, NullLogger<NotificationsService>.Instance);
            var savingsService = new SavingsService(db, notifService, NullLogger<SavingsService>.Instance);

            var yesterday = DateTime.UtcNow.Date.AddDays(-1);
            var mockGemini = new MockGeminiService
            {
                Handler = (msg, hist, dt) => Task.FromResult(new GeminiExtractionResult
                {
                    Intent = "add_expense",
                    Item = "Groceries", // translated item name
                    Amount = 1200m,
                    Category = "Food & Dining",
                    Date = yesterday.ToString("yyyy-MM-dd"),
                    MissingFields = new List<string>(),
                    Reply = "Nethu vaangina groceries-kaga ₹1200 add pannittaen!"
                })
            };

            var chatBot = new ChatBotService(mockGemini, expenseService, goalService, savingsService, db, CreateMemoryCache(), NullLogger<ChatBotService>.Instance);

            // Act
            var response = await chatBot.ProcessChatAsync(userId, new ChatRequest { Message = "nethu maligai saaman 1200 vaanginaen" });

            // Assert
            Assert.True(response.Saved);
            var expenseInDb = await db.Expenses.FirstOrDefaultAsync(e => e.UserId == userId);
            Assert.NotNull(expenseInDb);
            Assert.Equal(1200m, expenseInDb.Amount);
            Assert.Equal("Groceries", expenseInDb.Description);
            Assert.Equal(yesterday, expenseInDb.ExpenseDate.Date);
        }

        [Fact]
        public async Task ChatBot_Tamil_SetGoal_WithInitialSavings_CreatesGoalAndSavingRecord()
        {
            // Arrange: User sends Tamil script: "புதிய லேப்டாப் வாங்க 60000 இலக்கு வை, 15000 சேமித்துள்ளேன்"
            var db = CreateInMemoryDbContext();
            var user = await CreateTestUserAsync(db);
            var userId = user.Id;
            var expenseService = new ExpenseService(db, NullLogger<ExpenseService>.Instance);
            var goalService = new SavingGoalService(db, NullLogger<SavingGoalService>.Instance);
            var notifService = new NotificationsService(new ConfigurationBuilder().Build(), db, NullLogger<NotificationsService>.Instance);
            var savingsService = new SavingsService(db, notifService, NullLogger<SavingsService>.Instance);

            var mockGemini = new MockGeminiService
            {
                Handler = (msg, hist, dt) => Task.FromResult(new GeminiExtractionResult
                {
                    Intent = "set_goal",
                    Item = "Laptop",
                    Amount = 60000m,
                    CurrentSavings = 15000m,
                    Date = "2026-12-31",
                    MissingFields = new List<string>(),
                    Reply = "லேப்டாப் வாங்குவதற்கு ₹60,000 இலக்கு வைக்கப்பட்டது! (சேமிப்பு ₹15,000)"
                })
            };

            var chatBot = new ChatBotService(mockGemini, expenseService, goalService, savingsService, db, CreateMemoryCache(), NullLogger<ChatBotService>.Instance);

            // Act
            var response = await chatBot.ProcessChatAsync(userId, new ChatRequest { Message = "புதிய லேப்டாப் வாங்க 60000 இலக்கு வை, 15000 சேமித்துள்ளேன்" });

            // Assert
            Assert.True(response.Saved);
            Assert.Contains("லேப்டாப்", response.Reply);

            var goalInDb = await db.SavingGoals.FirstOrDefaultAsync(g => g.UserId == userId);
            Assert.NotNull(goalInDb);
            Assert.Equal("Laptop", goalInDb.GoalName);
            Assert.Equal(60000m, goalInDb.TargetAmount);
            Assert.Equal(15000m, goalInDb.CurrentSavedAmount);

            var savingInDb = await db.Savings.FirstOrDefaultAsync(s => s.SavingGoalId == goalInDb.Id);
            Assert.NotNull(savingInDb);
            Assert.Equal(15000m, savingInDb.Amount);
        }

        [Fact]
        public async Task ChatBot_Tanglish_SetGoal_WithRelativeTargetDate_SetsGoalCorrectly()
        {
            // Arrange: User sends Tanglish: "bike vaanganum 100000 december kulla"
            var db = CreateInMemoryDbContext();
            var user = await CreateTestUserAsync(db);
            var userId = user.Id;
            var expenseService = new ExpenseService(db, NullLogger<ExpenseService>.Instance);
            var goalService = new SavingGoalService(db, NullLogger<SavingGoalService>.Instance);
            var notifService = new NotificationsService(new ConfigurationBuilder().Build(), db, NullLogger<NotificationsService>.Instance);
            var savingsService = new SavingsService(db, notifService, NullLogger<SavingsService>.Instance);

            var mockGemini = new MockGeminiService
            {
                Handler = (msg, hist, dt) => Task.FromResult(new GeminiExtractionResult
                {
                    Intent = "set_goal",
                    Item = "Bike",
                    Amount = 100000m,
                    Date = $"{dt.Year}-12-31",
                    MissingFields = new List<string>(),
                    Reply = "Bike-kaga ₹1,00,000 goal set panniyaachu! December kulla save pannidalaam."
                })
            };

            var chatBot = new ChatBotService(mockGemini, expenseService, goalService, savingsService, db, CreateMemoryCache(), NullLogger<ChatBotService>.Instance);

            // Act
            var response = await chatBot.ProcessChatAsync(userId, new ChatRequest { Message = "bike vaanganum 100000 december kulla" });

            // Assert
            Assert.True(response.Saved);
            Assert.Contains("Bike", response.Reply);

            var goalInDb = await db.SavingGoals.FirstOrDefaultAsync(g => g.UserId == userId);
            Assert.NotNull(goalInDb);
            Assert.Equal("Bike", goalInDb.GoalName);
            Assert.Equal(100000m, goalInDb.TargetAmount);
            Assert.Equal(12, goalInDb.TargetDate.Month);
        }

        [Fact]
        public async Task ChatBot_Tanglish_AddSavings_DepositsToActiveGoal()
        {
            // Arrange: User sends Tanglish: "savings la anjaayiram serthu vei" (where anjaayiram = 5000)
            var db = CreateInMemoryDbContext();
            var user = await CreateTestUserAsync(db);
            var userId = user.Id;
            var expenseService = new ExpenseService(db, NullLogger<ExpenseService>.Instance);
            var goalService = new SavingGoalService(db, NullLogger<SavingGoalService>.Instance);
            var notifService = new NotificationsService(new ConfigurationBuilder().Build(), db, NullLogger<NotificationsService>.Instance);
            var savingsService = new SavingsService(db, notifService, NullLogger<SavingsService>.Instance);

            var goal = new SavingGoal
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                GoalName = "Bike",
                TargetAmount = 100000m,
                CurrentSavedAmount = 10000m,
                TargetDate = DateTime.UtcNow.AddMonths(6),
                Status = GoalStatus.Active
            };
            db.SavingGoals.Add(goal);
            await db.SaveChangesAsync();

            var mockGemini = new MockGeminiService
            {
                Handler = (msg, hist, dt) => Task.FromResult(new GeminiExtractionResult
                {
                    Intent = "add_savings",
                    Amount = 5000m, // model parsed 'anjaayiram' -> 5000
                    MissingFields = new List<string>(),
                    Reply = "Un savings la ₹5,000 serthu vachen bro! 👍"
                })
            };

            var chatBot = new ChatBotService(mockGemini, expenseService, goalService, savingsService, db, CreateMemoryCache(), NullLogger<ChatBotService>.Instance);

            // Act
            var response = await chatBot.ProcessChatAsync(userId, new ChatRequest { Message = "savings la anjaayiram serthu vei" });

            // Assert
            Assert.True(response.Saved);
            Assert.Contains("₹5,000", response.Reply);

            var updatedGoal = await db.SavingGoals.FindAsync(goal.Id);
            Assert.NotNull(updatedGoal);
            Assert.Equal(15000m, updatedGoal.CurrentSavedAmount);
        }

        [Fact]
        public async Task ChatBot_MixedLanguage_SuggestProducts_ReturnsProductSuggestions()
        {
            // Arrange: Mixed language: "oru nalla laptop suggest pannu 50000 budget kulla"
            var db = CreateInMemoryDbContext();
            var user = await CreateTestUserAsync(db);
            var userId = user.Id;
            var expenseService = new ExpenseService(db, NullLogger<ExpenseService>.Instance);
            var goalService = new SavingGoalService(db, NullLogger<SavingGoalService>.Instance);
            var notifService = new NotificationsService(new ConfigurationBuilder().Build(), db, NullLogger<NotificationsService>.Instance);
            var savingsService = new SavingsService(db, notifService, NullLogger<SavingsService>.Instance);

            var mockGemini = new MockGeminiService
            {
                Handler = (msg, hist, dt) => Task.FromResult(new GeminiExtractionResult
                {
                    Intent = "suggest_products",
                    Item = "laptop",
                    Amount = 50000m,
                    MissingFields = new List<string>(),
                    Reply = "₹50,000 budget-kulla best laptops paakuren..."
                }),
                SuggestProductsHandler = (item, budget) => Task.FromResult(new ProductSuggestionResult
                {
                    Status = ProductSuggestionStatus.Success,
                    IsLive = true,
                    Suggestions = new List<ProductSuggestion>
                    {
                        new ProductSuggestion
                        {
                            Name = "Lenovo IdeaPad Slim 3",
                            ApproxPrice = 46990m,
                            Reason = "Best performance in budget",
                            SearchUrl = "https://www.amazon.in/s?k=Lenovo+IdeaPad+Slim+3",
                            FlipkartUrl = "https://www.flipkart.com/search?q=Lenovo+IdeaPad+Slim+3"
                        }
                    }
                })
            };

            var chatBot = new ChatBotService(mockGemini, expenseService, goalService, savingsService, db, CreateMemoryCache(), NullLogger<ChatBotService>.Instance);

            // Act
            var response = await chatBot.ProcessChatAsync(userId, new ChatRequest { Message = "oru nalla laptop suggest pannu 50000 budget kulla" });

            // Assert
            Assert.False(response.Saved);
            Assert.NotNull(response.Suggestions);
            Assert.Single(response.Suggestions);
            Assert.Equal("Lenovo IdeaPad Slim 3", response.Suggestions[0].Name);
            Assert.Contains("laptops paakuren", response.Reply);
        }

        [Fact]
        public async Task ChatBot_Tanglish_MissingAmount_PromptsFollowUpInUserLanguage()
        {
            // Arrange: User sends Tanglish without an amount: "lunch sapten selavu aachu"
            var db = CreateInMemoryDbContext();
            var user = await CreateTestUserAsync(db);
            var userId = user.Id;
            var expenseService = new ExpenseService(db, NullLogger<ExpenseService>.Instance);
            var goalService = new SavingGoalService(db, NullLogger<SavingGoalService>.Instance);
            var notifService = new NotificationsService(new ConfigurationBuilder().Build(), db, NullLogger<NotificationsService>.Instance);
            var savingsService = new SavingsService(db, notifService, NullLogger<SavingsService>.Instance);

            var mockGemini = new MockGeminiService
            {
                Handler = (msg, hist, dt) => Task.FromResult(new GeminiExtractionResult
                {
                    Intent = "add_expense",
                    Item = "Lunch",
                    Amount = null,
                    MissingFields = new List<string> { "amount" },
                    Reply = "Lunch-ku evlo ruba selavu aachu bro?" // Asks clarifying question in user's language
                })
            };

            var chatBot = new ChatBotService(mockGemini, expenseService, goalService, savingsService, db, CreateMemoryCache(), NullLogger<ChatBotService>.Instance);

            // Act
            var response = await chatBot.ProcessChatAsync(userId, new ChatRequest { Message = "lunch sapten selavu aachu" });

            // Assert
            Assert.False(response.Saved);
            Assert.Contains("evlo ruba", response.Reply, StringComparison.OrdinalIgnoreCase);
            Assert.Null(response.Record);

            var count = await db.Expenses.CountAsync();
            Assert.Equal(0, count); // Did not guess or save an amount
        }

        #endregion
    }
}
