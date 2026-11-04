using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using PocketGoal.Data;
using PocketGoal.Models;
using PocketGoal.Models.Chat;
using PocketGoal.ViewModels;

namespace PocketGoal.Services
{
    public interface IChatBotService
    {
        Task<ChatResponse> ProcessChatAsync(Guid userId, ChatRequest request);
    }

    public class ChatBotService : IChatBotService
    {
        private readonly IGeminiChatService _geminiChatService;
        private readonly IExpenseService _expenseService;
        private readonly ISavingGoalService _savingGoalService;
        private readonly ISavingsService _savingsService;
        private readonly ApplicationDbContext _dbContext;
        private readonly IMemoryCache _memoryCache;
        private readonly ILogger<ChatBotService> _logger;

        public ChatBotService(
            IGeminiChatService geminiChatService,
            IExpenseService expenseService,
            ISavingGoalService savingGoalService,
            ISavingsService savingsService,
            ApplicationDbContext dbContext,
            IMemoryCache memoryCache,
            ILogger<ChatBotService> logger)
        {
            _geminiChatService = geminiChatService;
            _expenseService = expenseService;
            _savingGoalService = savingGoalService;
            _savingsService = savingsService;
            _dbContext = dbContext;
            _memoryCache = memoryCache;
            _logger = logger;
        }

        public async Task<ChatResponse> ProcessChatAsync(Guid userId, ChatRequest request)
        {
            if (string.IsNullOrWhiteSpace(request?.Message))
            {
                return new ChatResponse
                {
                    Saved = false,
                    Reply = "Please enter a message."
                };
            }

            var now = DateTime.UtcNow;
            var extraction = await _geminiChatService.ProcessWithGeminiAsync(request.Message, request.History, now);

            // 1. Check for unknown intent
            if (string.Equals(extraction.Intent, "unknown", StringComparison.OrdinalIgnoreCase))
            {
                return new ChatResponse
                {
                    Saved = false,
                    Reply = !string.IsNullOrWhiteSpace(extraction.Reply)
                        ? extraction.Reply
                        : "I'm here to help you track expenses, set savings goals, and find products! For example: 'Add expense: lunch, 250' or 'Suggest a laptop under 60000'.",
                    Record = null
                };
            }

            // 2. Check for missing required fields
            if (extraction.MissingFields != null && extraction.MissingFields.Count > 0)
            {
                return new ChatResponse
                {
                    Saved = false,
                    Reply = !string.IsNullOrWhiteSpace(extraction.Reply)
                        ? extraction.Reply
                        : $"Could you please provide the missing {string.Join(", ", extraction.MissingFields)}?",
                    Record = null
                };
            }

            // 3. Validate Amount (must be positive number for all transactional intents)
            if (!extraction.Amount.HasValue || extraction.Amount.Value <= 0)
            {
                return new ChatResponse
                {
                    Saved = false,
                    Reply = "Please provide a valid positive amount in ₹.",
                    Record = null
                };
            }

            // 4. Handle specific intents
            switch (extraction.Intent.ToLowerInvariant())
            {
                case "add_expense":
                    return await HandleAddExpenseAsync(userId, extraction, now);

                case "set_goal":
                    return await HandleSetGoalAsync(userId, extraction, now);

                case "add_savings":
                    return await HandleAddSavingsAsync(userId, extraction, now);

                case "suggest_products":
                    return await HandleSuggestProductsAsync(userId, extraction);

                default:
                    return new ChatResponse
                    {
                        Saved = false,
                        Reply = !string.IsNullOrWhiteSpace(extraction.Reply)
                            ? extraction.Reply
                            : "I'm not sure how to handle that request. Please try again.",
                        Record = null
                    };
            }
        }

        private async Task<ChatResponse> HandleAddExpenseAsync(Guid userId, GeminiExtractionResult extraction, DateTime now)
        {
            var amount = extraction.Amount!.Value;
            var item = !string.IsNullOrWhiteSpace(extraction.Item) ? extraction.Item.Trim() : "Expense";

            // Parse or default date
            var expenseDate = now;
            if (!string.IsNullOrWhiteSpace(extraction.Date) &&
                DateTime.TryParse(extraction.Date, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
            {
                expenseDate = DateTime.SpecifyKind(parsedDate, DateTimeKind.Utc);
            }

            // Find best matching category
            var categories = await _expenseService.GetCategoriesForUserAsync(userId);
            ExpenseCategory? matchedCategory = null;

            if (!string.IsNullOrWhiteSpace(extraction.Category))
            {
                var targetCat = extraction.Category.Trim();
                matchedCategory = categories.FirstOrDefault(c =>
                    string.Equals(c.Name, targetCat, StringComparison.OrdinalIgnoreCase) ||
                    c.Name.Contains(targetCat, StringComparison.OrdinalIgnoreCase) ||
                    targetCat.Contains(c.Name, StringComparison.OrdinalIgnoreCase));
            }

            // Keyword based fallback match from item name if category wasn't explicitly matched
            if (matchedCategory == null)
            {
                var lowerItem = item.ToLowerInvariant();
                if (lowerItem.Contains("lunch") || lowerItem.Contains("dinner") || lowerItem.Contains("food") ||
                    lowerItem.Contains("groceries") || lowerItem.Contains("grocery") || lowerItem.Contains("snack") ||
                    lowerItem.Contains("tea") || lowerItem.Contains("coffee") || lowerItem.Contains("restaurant"))
                {
                    matchedCategory = categories.FirstOrDefault(c => c.Name.Contains("Food", StringComparison.OrdinalIgnoreCase));
                }
                else if (lowerItem.Contains("travel") || lowerItem.Contains("cab") || lowerItem.Contains("uber") ||
                         lowerItem.Contains("auto") || lowerItem.Contains("flight") || lowerItem.Contains("train"))
                {
                    matchedCategory = categories.FirstOrDefault(c => c.Name.Contains("Travel", StringComparison.OrdinalIgnoreCase));
                }
                else if (lowerItem.Contains("fuel") || lowerItem.Contains("petrol") || lowerItem.Contains("diesel"))
                {
                    matchedCategory = categories.FirstOrDefault(c => c.Name.Contains("Fuel", StringComparison.OrdinalIgnoreCase));
                }
                else if (lowerItem.Contains("bill") || lowerItem.Contains("electricity") || lowerItem.Contains("recharge") || lowerItem.Contains("wifi"))
                {
                    matchedCategory = categories.FirstOrDefault(c => c.Name.Contains("Bills", StringComparison.OrdinalIgnoreCase));
                }
            }

            // Fallback to "Other" or first available category
            matchedCategory ??= categories.FirstOrDefault(c => c.Name.Equals("Other", StringComparison.OrdinalIgnoreCase))
                               ?? categories.FirstOrDefault();

            if (matchedCategory == null)
            {
                return new ChatResponse
                {
                    Saved = false,
                    Reply = "Unable to find an appropriate expense category. Please create a category first."
                };
            }

            var (success, error, expense) = await _expenseService.CreateExpenseAsync(userId, new ExpenseCreateEditViewModel
            {
                Amount = amount,
                CategoryId = matchedCategory.Id,
                ExpenseDate = expenseDate,
                Description = item
            });

            if (!success || expense == null)
            {
                return new ChatResponse
                {
                    Saved = false,
                    Reply = error ?? "Failed to save the expense. Please try again."
                };
            }

            var dateDisplay = expenseDate.Date == now.Date ? "today" : expenseDate.ToString("dd MMM yyyy");
            var reply = !string.IsNullOrWhiteSpace(extraction.Reply)
                ? extraction.Reply.Trim()
                : $"Added ₹{amount:N0} for {item} ({matchedCategory.Name}) for {dateDisplay}! ✅";

            return new ChatResponse
            {
                Saved = true,
                Reply = reply,
                Record = new
                {
                    type = "expense",
                    id = expense.Id,
                    item = expense.Description,
                    amount = expense.Amount,
                    category = matchedCategory.Name,
                    date = expense.ExpenseDate.ToString("yyyy-MM-dd")
                }
            };
        }

        private async Task<ChatResponse> HandleSetGoalAsync(Guid userId, GeminiExtractionResult extraction, DateTime now)
        {
            var targetAmount = extraction.Amount!.Value;
            var goalName = !string.IsNullOrWhiteSpace(extraction.Item) ? extraction.Item.Trim() : "Savings Goal";

            // Parse target date
            var targetDate = now.AddMonths(6); // Default 6 months ahead if no date provided
            if (!string.IsNullOrWhiteSpace(extraction.Date) &&
                DateTime.TryParse(extraction.Date, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
            {
                if (parsedDate < now)
                {
                    parsedDate = new DateTime(now.Year, parsedDate.Month, Math.Min(parsedDate.Day, DateTime.DaysInMonth(now.Year, parsedDate.Month)), 0, 0, 0, DateTimeKind.Utc);
                    if (parsedDate < now)
                    {
                        parsedDate = parsedDate.AddYears(1);
                    }
                }
                targetDate = DateTime.SpecifyKind(parsedDate, DateTimeKind.Utc);
            }

            var goal = await _savingGoalService.CreateGoalAsync(userId, new SavingGoalCreateEditViewModel
            {
                GoalName = goalName,
                TargetAmount = targetAmount,
                TargetDate = targetDate,
                Description = "Created via PocketGoal AI"
            });

            decimal initialSavings = 0m;
            if (extraction.CurrentSavings.HasValue && extraction.CurrentSavings.Value > 0)
            {
                initialSavings = extraction.CurrentSavings.Value;
                await _savingsService.AddSavingAsync(userId, new AddSavingViewModel
                {
                    SavingGoalId = goal.Id,
                    GoalName = goal.GoalName,
                    TargetAmount = goal.TargetAmount,
                    CurrentSavedAmount = 0m,
                    Amount = initialSavings,
                    SavedDate = now,
                    Notes = "Initial savings recorded via PocketGoal AI"
                });
            }

            var initialNote = initialSavings > 0
                ? $" with initial savings of ₹{initialSavings:N0}"
                : "";

            var reply = !string.IsNullOrWhiteSpace(extraction.Reply)
                ? extraction.Reply.Trim()
                : $"Set a goal to buy {goalName} for ₹{targetAmount:N0} by {targetDate:MMM yyyy}{initialNote}! 🎯";

            return new ChatResponse
            {
                Saved = true,
                Reply = reply,
                Record = new
                {
                    type = "goal",
                    id = goal.Id,
                    name = goal.GoalName,
                    targetAmount = goal.TargetAmount,
                    targetDate = goal.TargetDate.ToString("yyyy-MM-dd"),
                    currentSavings = initialSavings
                }
            };
        }

        private async Task<ChatResponse> HandleAddSavingsAsync(Guid userId, GeminiExtractionResult extraction, DateTime now)
        {
            var amount = extraction.Amount!.Value;

            // Fetch active goals for user
            var activeGoals = await _dbContext.SavingGoals
                .Where(g => g.UserId == userId && g.Status == GoalStatus.Active)
                .OrderByDescending(g => g.CreatedAt)
                .ToListAsync();

            if (activeGoals.Count == 0)
            {
                return new ChatResponse
                {
                    Saved = false,
                    Reply = "You don't have any active savings goals yet. You can set one by typing: 'Set a goal to buy a laptop for 60000 by December'!"
                };
            }

            SavingGoal? targetGoal = null;

            if (!string.IsNullOrWhiteSpace(extraction.Item))
            {
                var queryItem = extraction.Item.Trim();
                targetGoal = activeGoals.FirstOrDefault(g =>
                    string.Equals(g.GoalName, queryItem, StringComparison.OrdinalIgnoreCase) ||
                    g.GoalName.Contains(queryItem, StringComparison.OrdinalIgnoreCase) ||
                    queryItem.Contains(g.GoalName, StringComparison.OrdinalIgnoreCase));
            }

            if (targetGoal == null && activeGoals.Count == 1)
            {
                targetGoal = activeGoals[0];
            }

            if (targetGoal == null)
            {
                var goalNames = string.Join(", ", activeGoals.Select(g => $"'{g.GoalName}'"));
                return new ChatResponse
                {
                    Saved = false,
                    Reply = $"Which goal would you like to add ₹{amount:N0} to? You have: {goalNames}."
                };
            }

            var savedDate = now;
            if (!string.IsNullOrWhiteSpace(extraction.Date) &&
                DateTime.TryParse(extraction.Date, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
            {
                savedDate = DateTime.SpecifyKind(parsedDate, DateTimeKind.Utc);
            }

            var (success, error, saving) = await _savingsService.AddSavingAsync(userId, new AddSavingViewModel
            {
                SavingGoalId = targetGoal.Id,
                GoalName = targetGoal.GoalName,
                TargetAmount = targetGoal.TargetAmount,
                CurrentSavedAmount = targetGoal.CurrentSavedAmount,
                Amount = amount,
                SavedDate = savedDate,
                Notes = "Added via PocketGoal AI"
            });

            if (!success || saving == null)
            {
                return new ChatResponse
                {
                    Saved = false,
                    Reply = error ?? "Failed to add to your savings. Please try again."
                };
            }

            var reply = !string.IsNullOrWhiteSpace(extraction.Reply)
                ? extraction.Reply.Trim()
                : $"Added ₹{amount:N0} to your '{targetGoal.GoalName}' savings! Total saved: ₹{targetGoal.CurrentSavedAmount:N0} of ₹{targetGoal.TargetAmount:N0}. 🎉";

            return new ChatResponse
            {
                Saved = true,
                Reply = reply,
                Record = new
                {
                    type = "saving",
                    id = saving.Id,
                    goalId = targetGoal.Id,
                    goalName = targetGoal.GoalName,
                    amount = saving.Amount,
                    totalSaved = targetGoal.CurrentSavedAmount
                }
            };
        }

        private async Task<ChatResponse> HandleSuggestProductsAsync(Guid userId, GeminiExtractionResult extraction)
        {
            var item = !string.IsNullOrWhiteSpace(extraction.Item) ? extraction.Item.Trim() : "product";
            var budget = extraction.Amount!.Value;

            // 1. Per-user limit: 5 suggestion searches per user per hour
            var userRateLimitKey = $"user_search_limit_{userId}";
            var searchCount = _memoryCache.GetOrCreate(userRateLimitKey, entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1);
                return 0;
            });

            if (searchCount >= 5)
            {
                return new ChatResponse
                {
                    Saved = false,
                    Reply = "You have reached your limit of 5 product searches per hour. Please try again later or check your existing goals!",
                    Record = null,
                    Suggestions = null
                };
            }

            // Increment count for this user
            _memoryCache.Set(userRateLimitKey, searchCount + 1, TimeSpan.FromHours(1));

            // 2. In-memory cache for repeated searches (30 minutes)
            var normalizedKey = $"product_search_{item.ToLowerInvariant()}_{budget}";
            if (_memoryCache.TryGetValue(normalizedKey, out ProductSuggestionResult? cachedResult) && cachedResult != null)
            {
                _logger.LogInformation("Serving product suggestions for '{Item}', budget {Budget} from 30-min cache.", item, budget);
                return FormatSuggestionResponse(item, budget, cachedResult, extraction.Reply);
            }

            // 3. Call Gemini grounded search (with fallback)
            var result = await _geminiChatService.SuggestProductsAsync(item, budget);

            // Cache if successful or valid no-products-found
            if (result.Status == ProductSuggestionStatus.Success || result.Status == ProductSuggestionStatus.NoProductsFound)
            {
                _memoryCache.Set(normalizedKey, result, TimeSpan.FromMinutes(30));
            }

            return FormatSuggestionResponse(item, budget, result, extraction.Reply);
        }

        private static ChatResponse FormatSuggestionResponse(string item, decimal budget, ProductSuggestionResult result, string? modelReply = null)
        {
            if (result.Status == ProductSuggestionStatus.Success)
            {
                var reply = !string.IsNullOrWhiteSpace(modelReply) && !modelReply.StartsWith("Looking up", StringComparison.OrdinalIgnoreCase)
                    ? modelReply.Trim()
                    : $"Here are top options for '{item}' under ₹{budget:N0}:";

                return new ChatResponse
                {
                    Saved = false,
                    Reply = reply,
                    Record = null,
                    Suggestions = result.Suggestions,
                    IsLive = result.IsLive
                };
            }

            if (result.Status == ProductSuggestionStatus.NoProductsFound)
            {
                return new ChatResponse
                {
                    Saved = false,
                    Reply = $"I couldn't find any products matching '{item}' within that budget of ₹{budget:N0}. Try increasing your budget or checking a different category.",
                    Record = null,
                    Suggestions = null
                };
            }

            // If both grounded search and fallback failed or were overloaded
            return new ChatResponse
            {
                Saved = false,
                Reply = "The AI service is busy right now. Please try again in a minute.",
                Record = null,
                Suggestions = null
            };
        }
    }
}
