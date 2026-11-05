using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
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
        Task<ChatResponse> ProcessChatAsync(Guid userId, ChatRequest request, CancellationToken cancellationToken = default);
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

        // In-flight task sharing dictionaries to prevent duplicate Gemini calls for identical lookups
        private static readonly ConcurrentDictionary<string, Task<PriceLookupResult>> _inFlightPriceLookups = new(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, Task<ProductSuggestionResult>> _inFlightSuggestionLookups = new(StringComparer.OrdinalIgnoreCase);

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

        public async Task<ChatResponse> ProcessChatAsync(Guid userId, ChatRequest request, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(request?.Message))
            {
                return new ChatResponse
                {
                    Saved = false,
                    Reply = "Please enter a message."
                };
            }

            // Budget total chat processing time safeguard (40s, so 15s grounded + 10s fallback has full time)
            using var overallCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            overallCts.CancelAfter(TimeSpan.FromSeconds(40));
            var ct = overallCts.Token;

            try
            {
                var now = DateTime.UtcNow;
                var extraction = await _geminiChatService.ProcessWithGeminiAsync(request.Message, request.History, now, ct);

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

                var intent = (extraction.Intent ?? string.Empty).ToLowerInvariant();

                // 3. Validate Amount only for transactional intents that require upfront amount
                var requiresUpfrontAmount = intent is "add_expense" or "set_goal" or "add_savings" or "suggest_products";
                if (requiresUpfrontAmount)
                {
                    if (!extraction.Amount.HasValue || extraction.Amount.Value <= 0)
                    {
                        return new ChatResponse
                        {
                            Saved = false,
                            Reply = "Please provide a valid positive amount in ₹.",
                            Record = null
                        };
                    }
                }

                // 4. Handle specific intents
                switch (intent)
                {
                    case "add_expense":
                        return await HandleAddExpenseAsync(userId, extraction, now);

                    case "set_goal":
                        return await HandleSetGoalAsync(userId, extraction, now);

                    case "add_savings":
                        return await HandleAddSavingsAsync(userId, extraction, now);

                    case "suggest_products":
                        return await HandleSuggestProductsAsync(userId, extraction, ct);

                    case "set_goal_lookup_price":
                        return await HandleSetGoalLookupPriceAsync(userId, extraction, ct);

                    case "general_question":
                        return HandleGeneralQuestion(extraction);

                    case "query_spending":
                        return await HandleQuerySpendingAsync(userId, extraction, request.Message, ct);

                    case "query_goals":
                        return await HandleQueryGoalsAsync(userId, extraction, request.Message, ct);

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
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _logger.LogInformation("Chat request was cancelled by client for user {UserId}", userId);
                throw;
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Chat request timed out after 25s budget for user {UserId}", userId);
                return new ChatResponse
                {
                    Saved = false,
                    Reply = "The AI service is taking longer than expected. Please try again in a moment.",
                    Record = null
                };
            }
        }

        private async Task<ChatResponse> HandleAddExpenseAsync(Guid userId, GeminiExtractionResult extraction, DateTime now)
        {
            var amount = extraction.Amount!.Value;
            var item = !string.IsNullOrWhiteSpace(extraction.Item) ? extraction.Item.Trim() : "Expense";

            var expenseDate = now;
            if (!string.IsNullOrWhiteSpace(extraction.Date) &&
                DateTime.TryParse(extraction.Date, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
            {
                expenseDate = DateTime.SpecifyKind(parsedDate, DateTimeKind.Utc);
            }

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

            if (matchedCategory == null)
            {
                var lowerItem = item.ToLowerInvariant();
                if (lowerItem.Contains("food") || lowerItem.Contains("lunch") || lowerItem.Contains("dinner") ||
                    lowerItem.Contains("breakfast") || lowerItem.Contains("tea") || lowerItem.Contains("coffee") ||
                    lowerItem.Contains("snack") || lowerItem.Contains("grocer") || lowerItem.Contains("maligai"))
                {
                    matchedCategory = categories.FirstOrDefault(c => c.Name.Contains("Food", StringComparison.OrdinalIgnoreCase));
                }
                else if (lowerItem.Contains("fuel") || lowerItem.Contains("petrol") || lowerItem.Contains("diesel"))
                {
                    matchedCategory = categories.FirstOrDefault(c => c.Name.Contains("Fuel", StringComparison.OrdinalIgnoreCase));
                }
                else if (lowerItem.Contains("travel") || lowerItem.Contains("bus") || lowerItem.Contains("train") ||
                         lowerItem.Contains("flight") || lowerItem.Contains("cab") || lowerItem.Contains("auto") || lowerItem.Contains("uber"))
                {
                    matchedCategory = categories.FirstOrDefault(c => c.Name.Contains("Travel", StringComparison.OrdinalIgnoreCase));
                }
                else if (lowerItem.Contains("bill") || lowerItem.Contains("electric") || lowerItem.Contains("recharge") || lowerItem.Contains("rent"))
                {
                    matchedCategory = categories.FirstOrDefault(c => c.Name.Contains("Bills", StringComparison.OrdinalIgnoreCase));
                }
                else if (lowerItem.Contains("movie") || lowerItem.Contains("cinema") || lowerItem.Contains("game"))
                {
                    matchedCategory = categories.FirstOrDefault(c => c.Name.Contains("Entertainment", StringComparison.OrdinalIgnoreCase));
                }
                else if (lowerItem.Contains("medicine") || lowerItem.Contains("doctor") || lowerItem.Contains("hospital"))
                {
                    matchedCategory = categories.FirstOrDefault(c => c.Name.Contains("Health", StringComparison.OrdinalIgnoreCase));
                }
            }

            matchedCategory ??= categories.FirstOrDefault(c => c.Name.Equals("Other", StringComparison.OrdinalIgnoreCase))
                                ?? categories.FirstOrDefault();

            if (matchedCategory == null)
            {
                matchedCategory = await _expenseService.CreateCustomCategoryAsync(userId, new CreateCategoryViewModel
                {
                    Name = "Other",
                    Icon = "bi-three-dots",
                    ColorHex = "#64748b"
                });
            }

            var (success, errorMessage, expense) = await _expenseService.CreateExpenseAsync(userId, new ExpenseCreateEditViewModel
            {
                Amount = amount,
                Description = item,
                CategoryId = matchedCategory.Id,
                ExpenseDate = expenseDate
            });

            if (!success || expense == null)
            {
                return new ChatResponse
                {
                    Saved = false,
                    Reply = errorMessage ?? "Failed to save the expense. Please try again."
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

            var targetDate = now.AddMonths(6);
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

        private async Task<ChatResponse> HandleSuggestProductsAsync(
            Guid userId,
            GeminiExtractionResult extraction,
            CancellationToken cancellationToken)
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

            // 2. In-memory cache for repeated searches (30 minutes)
            var normalizedKey = $"product_search_{item.ToLowerInvariant()}_{budget}";
            if (_memoryCache.TryGetValue(normalizedKey, out ProductSuggestionResult? cachedResult) && cachedResult != null)
            {
                _logger.LogInformation("Serving product suggestions for '{Item}', budget {Budget} from 30-min cache.", item, budget);
                return FormatSuggestionResponse(item, budget, cachedResult, extraction.Reply);
            }

            // 3. In-flight task sharing: if an identical lookup is already running, reuse that task
            var inFlightKey = $"{userId}:{item.ToLowerInvariant()}:{budget}";
            ProductSuggestionResult result;

            try
            {
                var inFlightTask = _inFlightSuggestionLookups.GetOrAdd(inFlightKey, key => Task.Run(async () =>
                {
                    try
                    {
                        _memoryCache.Set(userRateLimitKey, searchCount + 1, TimeSpan.FromHours(1));
                        return await _geminiChatService.SuggestProductsAsync(item, budget, CancellationToken.None);
                    }
                    finally
                    {
                        _inFlightSuggestionLookups.TryRemove(key, out var _);
                    }
                }));

                result = await inFlightTask.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during suggestion lookup in-flight task for '{Item}'", item);
                result = new ProductSuggestionResult
                {
                    Status = ProductSuggestionStatus.Overloaded,
                    ErrorMessage = "The AI service is busy right now. Please try again in a minute."
                };
            }

            // ONLY cache successful suggestions with items found. Never cache failures or empty results!
            if (result.Status == ProductSuggestionStatus.Success && result.Suggestions.Count > 0)
            {
                _memoryCache.Set(normalizedKey, result, TimeSpan.FromMinutes(30));
            }

            return FormatSuggestionResponse(item, budget, result, extraction.Reply);
        }

        private async Task<ChatResponse> HandleSetGoalLookupPriceAsync(
            Guid userId,
            GeminiExtractionResult extraction,
            CancellationToken cancellationToken)
        {
            var item = !string.IsNullOrWhiteSpace(extraction.Item) ? extraction.Item.Trim() : null;
            if (string.IsNullOrWhiteSpace(item))
            {
                return new ChatResponse
                {
                    Saved = false,
                    Reply = !string.IsNullOrWhiteSpace(extraction.Reply)
                        ? extraction.Reply
                        : "Which product would you like to search the price for and add as a goal?"
                };
            }

            // 1. Per-user rate limit: 5 searches per user per hour
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
                    Reply = "You have reached your limit of 5 product searches per hour. Please try again later or set the goal with a price directly!",
                    Record = null
                };
            }

            // 2. In-memory cache for repeated lookups (30 minutes)
            var normalizedItem = item.ToLowerInvariant();
            var cacheKey = $"price_lookup_{normalizedItem}";
            if (_memoryCache.TryGetValue(cacheKey, out PriceLookupResult? cachedResult) && cachedResult != null)
            {
                return FormatPriceLookupResponse(item, cachedResult, extraction.Reply);
            }

            // 3. In-flight task sharing: if an identical lookup is already running, share that single task
            var inFlightKey = $"{userId}:{normalizedItem}";
            PriceLookupResult result;

            try
            {
                var inFlightTask = _inFlightPriceLookups.GetOrAdd(inFlightKey, key => Task.Run(async () =>
                {
                    try
                    {
                        _memoryCache.Set(userRateLimitKey, searchCount + 1, TimeSpan.FromHours(1));
                        return await _geminiChatService.LookupPriceAsync(item, CancellationToken.None);
                    }
                    finally
                    {
                        _inFlightPriceLookups.TryRemove(key, out var _);
                    }
                }));

                result = await inFlightTask.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during price lookup in-flight task for '{Item}'", item);
                result = new PriceLookupResult
                {
                    Status = ProductSuggestionStatus.Overloaded,
                    ErrorMessage = "The AI service is busy right now. Please try again in a minute."
                };
            }

            // ONLY cache successful price lookups with valid price. Never cache failures or not found!
            if (result.Status == ProductSuggestionStatus.Success && result.ApproxPrice > 0)
            {
                _memoryCache.Set(cacheKey, result, TimeSpan.FromMinutes(30));
            }

            return FormatPriceLookupResponse(item, result, extraction.Reply);
        }

        private static ChatResponse FormatPriceLookupResponse(string item, PriceLookupResult result, string? modelReply)
        {
            if (result.Status == ProductSuggestionStatus.Success && result.ApproxPrice > 0)
            {
                var cleanName = !string.IsNullOrWhiteSpace(result.ProductName) ? result.ProductName : item;
                var searchUrl = $"https://www.amazon.in/s?k={Uri.EscapeDataString(cleanName)}";
                var flipkartUrl = $"https://www.flipkart.com/search?q={Uri.EscapeDataString(cleanName)}";

                var card = new PriceLookupCard
                {
                    ProductName = cleanName,
                    ApproxPrice = result.ApproxPrice,
                    VariantNote = result.VariantNote,
                    SourceUrl = result.SourceUrl,
                    SearchUrl = searchUrl,
                    FlipkartUrl = flipkartUrl,
                    IsLive = result.IsLive
                };

                string reply;
                if (!string.IsNullOrWhiteSpace(modelReply) &&
                    !modelReply.Contains("I can't browse", StringComparison.OrdinalIgnoreCase) &&
                    !modelReply.Contains("I cannot browse", StringComparison.OrdinalIgnoreCase))
                {
                    reply = modelReply;
                }
                else if (!result.IsLive)
                {
                    reply = $"That is taking longer than expected, so here is an estimate: '{cleanName}' is about ₹{result.ApproxPrice:N0}. Would you like to set this as your savings goal?";
                }
                else
                {
                    reply = $"'{cleanName}' is about ₹{result.ApproxPrice:N0}. Would you like to set this as your savings goal?";
                }

                return new ChatResponse
                {
                    Saved = false,
                    Reply = reply,
                    PriceLookup = card,
                    IsLive = result.IsLive
                };
            }

            if (result.Status == ProductSuggestionStatus.NoProductsFound)
            {
                return new ChatResponse
                {
                    Saved = false,
                    Reply = $"I couldn't find a price online for '{item}'. What target price would you like to set for this goal? Once you let me know, I'll save the goal for you!"
                };
            }

            return new ChatResponse
            {
                Saved = false,
                Reply = "The AI service is busy right now. Please try again in a minute."
            };
        }

        private static ChatResponse HandleGeneralQuestion(GeminiExtractionResult extraction)
        {
            var reply = !string.IsNullOrWhiteSpace(extraction.Reply)
                ? extraction.Reply.Trim()
                : "I can help you track expenses, manage savings goals, and look up product prices! For example: 'Add expense: lunch 250' or 'Suggest a laptop under 60000'.";

            return new ChatResponse
            {
                Saved = false,
                Reply = reply
            };
        }

        private async Task<ChatResponse> HandleQuerySpendingAsync(
            Guid userId,
            GeminiExtractionResult extraction,
            string userMessage,
            CancellationToken cancellationToken)
        {
            var now = DateTime.UtcNow;
            var expenseIndex = await _expenseService.GetExpensesIndexAsync(userId, now.Month, now.Year, null);

            if (expenseIndex.AllTimeTotal == 0 && (expenseIndex.Expenses == null || expenseIndex.Expenses.Count == 0))
            {
                return new ChatResponse
                {
                    Saved = false,
                    Reply = "You don't have any recorded expenses yet. You can start logging expenses anytime by typing: 'Add expense: lunch, 250'!"
                };
            }

            var factsSb = new StringBuilder();
            factsSb.AppendLine($"- Current Month ({now:MMMM yyyy}) Spending: ₹{expenseIndex.CurrentMonthTotal:N0}");
            factsSb.AppendLine($"- Previous Month Spending: ₹{expenseIndex.PreviousMonthTotal:N0}");
            factsSb.AppendLine($"- Today's Spending: ₹{expenseIndex.TodayTotal:N0}");
            factsSb.AppendLine($"- All-time Total Spending: ₹{expenseIndex.AllTimeTotal:N0}");

            ExpenseCategorySummaryViewModel? catSummary = null;
            if (!string.IsNullOrWhiteSpace(extraction.Category))
            {
                catSummary = expenseIndex.CategorySummaries.FirstOrDefault(c =>
                    string.Equals(c.CategoryName, extraction.Category, StringComparison.OrdinalIgnoreCase) ||
                    c.CategoryName.Contains(extraction.Category, StringComparison.OrdinalIgnoreCase));
            }
            else if (!string.IsNullOrWhiteSpace(extraction.Item))
            {
                catSummary = expenseIndex.CategorySummaries.FirstOrDefault(c =>
                    string.Equals(c.CategoryName, extraction.Item, StringComparison.OrdinalIgnoreCase) ||
                    c.CategoryName.Contains(extraction.Item, StringComparison.OrdinalIgnoreCase));
            }

            if (catSummary != null)
            {
                factsSb.AppendLine($"- Spending on {catSummary.CategoryName}: ₹{catSummary.TotalAmount:N0} ({catSummary.ExpenseCount} expenses)");
            }
            else if (expenseIndex.CategorySummaries.Count > 0)
            {
                var topCats = string.Join(", ", expenseIndex.CategorySummaries.Take(3).Select(c => $"{c.CategoryName}: ₹{c.TotalAmount:N0}"));
                factsSb.AppendLine($"- Top Spending Categories this month: {topCats}");
            }

            var facts = factsSb.ToString();

            // Have Gemini phrase the response in the user's language using strictly these backend numbers
            var phrased = await _geminiChatService.PhraseUserDataAnswerAsync(userMessage, facts, extraction.Reply, cancellationToken);
            if (string.IsNullOrWhiteSpace(phrased) || phrased == facts)
            {
                if (catSummary != null)
                {
                    phrased = $"You've spent ₹{catSummary.TotalAmount:N0} on {catSummary.CategoryName} this month.";
                }
                else if (userMessage.Contains("today", StringComparison.OrdinalIgnoreCase) || userMessage.Contains("inniku", StringComparison.OrdinalIgnoreCase))
                {
                    phrased = $"You've spent ₹{expenseIndex.TodayTotal:N0} today.";
                }
                else
                {
                    phrased = $"You've spent ₹{expenseIndex.CurrentMonthTotal:N0} this month ({now:MMMM yyyy}). Your all-time total spending is ₹{expenseIndex.AllTimeTotal:N0}.";
                }
            }

            return new ChatResponse
            {
                Saved = false,
                Reply = phrased
            };
        }

        private async Task<ChatResponse> HandleQueryGoalsAsync(
            Guid userId,
            GeminiExtractionResult extraction,
            string userMessage,
            CancellationToken cancellationToken)
        {
            var allGoals = await _savingGoalService.GetGoalsForUserAsync(userId);

            if (allGoals.Count == 0)
            {
                return new ChatResponse
                {
                    Saved = false,
                    Reply = "You don't have any savings goals yet. You can set one by typing: 'Set a goal to buy a laptop for 60000 by December'!"
                };
            }

            var totalSaved = allGoals.Sum(g => g.CurrentSavedAmount);
            var totalTarget = allGoals.Sum(g => g.TargetAmount);
            var activeGoals = allGoals.Where(g => g.Status == GoalStatus.Active).ToList();
            var completedCount = allGoals.Count(g => g.IsCompleted);

            var factsSb = new StringBuilder();
            factsSb.AppendLine($"- Total Saved across all goals: ₹{totalSaved:N0}");
            factsSb.AppendLine($"- Total Target across all goals: ₹{totalTarget:N0}");
            factsSb.AppendLine($"- Total Goals: {allGoals.Count} ({activeGoals.Count} active, {completedCount} completed)");

            SavingGoalViewModel? specificGoal = null;
            if (!string.IsNullOrWhiteSpace(extraction.Item))
            {
                var targetItem = extraction.Item.Trim();
                specificGoal = allGoals.FirstOrDefault(g =>
                    string.Equals(g.GoalName, targetItem, StringComparison.OrdinalIgnoreCase) ||
                    g.GoalName.Contains(targetItem, StringComparison.OrdinalIgnoreCase) ||
                    targetItem.Contains(g.GoalName, StringComparison.OrdinalIgnoreCase));
            }

            if (specificGoal != null)
            {
                factsSb.AppendLine($"- Specific Goal '{specificGoal.GoalName}': Saved ₹{specificGoal.CurrentSavedAmount:N0} of ₹{specificGoal.TargetAmount:N0} ({specificGoal.ProgressPercentage:N0}%). Remaining: ₹{specificGoal.RemainingAmount:N0}, Target Date: {specificGoal.TargetDate:MMM yyyy}");
            }
            else if (activeGoals.Count > 0)
            {
                var closest = activeGoals.OrderByDescending(g => g.ProgressPercentage).FirstOrDefault();
                if (closest != null)
                {
                    factsSb.AppendLine($"- Closest Goal to completion: '{closest.GoalName}' with ₹{closest.CurrentSavedAmount:N0} saved of ₹{closest.TargetAmount:N0} ({closest.ProgressPercentage:N0}% complete, ₹{closest.RemainingAmount:N0} remaining).");
                }
            }

            var facts = factsSb.ToString();

            // Have Gemini phrase the response in the user's language using strictly these backend numbers
            var phrased = await _geminiChatService.PhraseUserDataAnswerAsync(userMessage, facts, extraction.Reply, cancellationToken);
            if (string.IsNullOrWhiteSpace(phrased) || phrased == facts)
            {
                if (specificGoal != null)
                {
                    phrased = $"For your '{specificGoal.GoalName}' goal, you have saved ₹{specificGoal.CurrentSavedAmount:N0} of ₹{specificGoal.TargetAmount:N0} ({specificGoal.ProgressPercentage:N0}%). Remaining: ₹{specificGoal.RemainingAmount:N0}.";
                }
                else if (userMessage.Contains("closest", StringComparison.OrdinalIgnoreCase) && activeGoals.Count > 0)
                {
                    var closest = activeGoals.OrderByDescending(g => g.ProgressPercentage).First();
                    phrased = $"Your closest goal is '{closest.GoalName}' at {closest.ProgressPercentage:N0}% complete (₹{closest.CurrentSavedAmount:N0} of ₹{closest.TargetAmount:N0}).";
                }
                else
                {
                    phrased = $"You have saved ₹{totalSaved:N0} in total across {allGoals.Count} goals (target ₹{totalTarget:N0}). You have {activeGoals.Count} active goals.";
                }
            }

            return new ChatResponse
            {
                Saved = false,
                Reply = phrased
            };
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
