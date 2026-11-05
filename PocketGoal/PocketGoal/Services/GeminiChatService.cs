using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using PocketGoal.Models.Chat;

namespace PocketGoal.Services
{
    public interface IGeminiChatService
    {
        Task<GeminiExtractionResult> ProcessWithGeminiAsync(string userMessage, List<ChatMessage>? history, DateTime currentDate, CancellationToken cancellationToken = default);
        Task<ProductSuggestionResult> SuggestProductsAsync(string item, decimal budget, CancellationToken cancellationToken = default);
        Task<PriceLookupResult> LookupPriceAsync(string item, CancellationToken cancellationToken = default);
        Task<string> PhraseUserDataAnswerAsync(string userMessage, string facts, string? languageStyle, CancellationToken cancellationToken = default);
    }

    public class GeminiChatService : IGeminiChatService
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<GeminiChatService> _logger;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        // Circuit Breaker: skip grounded search for 10m after 3 429s in 5m
        private static readonly object _circuitBreakerLock = new();
        private static readonly List<DateTime> _grounded429Timestamps = new();
        private static DateTime _circuitBreakerUntil = DateTime.MinValue;

        public static bool IsCircuitBreakerOpen()
        {
            lock (_circuitBreakerLock)
            {
                return DateTime.UtcNow < _circuitBreakerUntil;
            }
        }

        public static void RecordGrounded429()
        {
            lock (_circuitBreakerLock)
            {
                var now = DateTime.UtcNow;
                _grounded429Timestamps.Add(now);
                _grounded429Timestamps.RemoveAll(t => t < now.AddMinutes(-5));

                if (_grounded429Timestamps.Count >= 3)
                {
                    _circuitBreakerUntil = now.AddMinutes(10);
                }
            }
        }

        public static void ResetCircuitBreakerForTesting()
        {
            lock (_circuitBreakerLock)
            {
                _grounded429Timestamps.Clear();
                _circuitBreakerUntil = DateTime.MinValue;
            }
        }


        private class ProductSuggestionExtractionDto
        {
            [JsonPropertyName("name")]
            public string? Name { get; set; }

            [JsonPropertyName("approxPrice")]
            public decimal ApproxPrice { get; set; }

            [JsonPropertyName("reason")]
            public string? Reason { get; set; }

            [JsonPropertyName("sourceUrl")]
            public string? SourceUrl { get; set; }
        }

        private class PriceLookupExtractionDto
        {
            [JsonPropertyName("productName")]
            public string? ProductName { get; set; }

            [JsonPropertyName("approxPrice")]
            public decimal ApproxPrice { get; set; }

            [JsonPropertyName("variantNote")]
            public string? VariantNote { get; set; }

            [JsonPropertyName("sourceUrl")]
            public string? SourceUrl { get; set; }
        }

        public GeminiChatService(
            HttpClient httpClient,
            IConfiguration configuration,
            ILogger<GeminiChatService> logger)
        {
            _httpClient = httpClient;
            _configuration = configuration;
            _logger = logger;
        }

        private string? GetApiKey()
        {
            return Environment.GetEnvironmentVariable("GEMINI_API_KEY")
                   ?? _configuration["GEMINI_API_KEY"]
                   ?? _configuration["Gemini:ApiKey"];
        }

        private string GetModel()
        {
            var model = Environment.GetEnvironmentVariable("GEMINI_MODEL")
                        ?? _configuration["GEMINI_MODEL"]
                        ?? _configuration["Gemini:Model"];

            return string.IsNullOrWhiteSpace(model) ? "gemini-1.5-flash" : model.Trim();
        }

        public async Task<GeminiExtractionResult> ProcessWithGeminiAsync(
            string userMessage,
            List<ChatMessage>? history,
            DateTime currentDate,
            CancellationToken cancellationToken = default)
        {
            var apiKey = GetApiKey();
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                _logger.LogError("GEMINI_API_KEY environment variable is not configured. Unable to call Google Gemini API.");
                return new GeminiExtractionResult
                {
                    Intent = "unknown",
                    Reply = "AI Assistant is currently offline because the GEMINI_API_KEY is not configured. Please set the GEMINI_API_KEY in your environment.",
                    MissingFields = new List<string>()
                };
            }

            var model = GetModel();
            var dateStr = currentDate.ToString("yyyy-MM-dd");
            var dayOfWeek = currentDate.DayOfWeek.ToString();

            var systemInstruction = $@"You are PocketGoal AI, an intelligent multilingual personal finance tracking assistant for the PocketGoal app.
Today's date is {dayOfWeek}, {dateStr} (format YYYY-MM-DD). Use this current date to resolve all relative dates.

Language and Multilingual Understanding:
1. Language Support: Users may write in English, Tamil script (தமிழ்), Tanglish (Tamil written in English/Latin letters), or mix them. Understand the intended meaning accurately across all of these languages and code-mixed inputs.
2. Structured JSON Output Always in English:
   - Always return the JSON field values in English: 'intent', 'item', 'category'.
   - 'amount' must be a plain positive numeric value without symbols or commas (e.g. 500, 25000). Leave null if unknown or not required.
   - 'date' must be formatted as YYYY-MM-DD.
   - Keep the 'item' name in a clean, simple, capitalized form in English, translated to English if it is a common word (e.g., 'petrol', 'groceries', 'lunch', 'tea and snacks', 'laptop', 'bike', 'rent', 'electricity bill', 'mobile recharge').
3. Reply Style and Language Matching:
   - Always write the 'reply' field in the SAME language and style the user used:
     * Tamil script input -> Tamil script reply (e.g., 'மதிய உணவிற்காக ₹250 சேர்க்கப்பட்டது!').
     * Tanglish input -> Tanglish reply (e.g., 'Lunch-kaga ₹250 add panniyaachu! 👍', 'Bike vaangurathuku ₹1,00,000 goal set pannittaen!').
     * English input -> English reply (e.g., 'Added ₹250 for lunch!').
     * Mixed input -> Match the dominant conversational style.
   - Keep replies short, natural, warm, and friendly. Always use ₹ (INR) as the currency symbol in replies.
4. Tamil and Tanglish Money, Number, and Time Vocabularies:
   - Currency: ruba, rubai, roobai, rooba, rs, inr, ₹, ரூபாய்.
   - Numbers: onnu (1), rendu (2), anju (5), pathu (10), iruvathu (20), ambathu (50), nooru (100), anju nooru / ainooru (500), aayiram (1000), rendaayiram (2000), anjaayiram (5000), pathaayiram (10000), iruvathaayiram (20000), ambathaayiram (50000), oru latcham / latcham (100000).
   - Relative Dates & Time:
     * 'ippo', 'inniku', 'indru', 'இன்று' -> Today ({dateStr}).
     * 'nethu', 'netru', 'நேற்று' -> Yesterday.
     * 'naalaikku', 'naalai', 'நாளை' -> Tomorrow.
     * 'ma', 'maasam', 'maatham', 'மாதம்' -> Month.
     * 'varusham', 'aandu', 'வருடம்' -> Year.
     * 'kulla' -> By / before (e.g., 'december kulla' -> by end of December {currentDate.Year}).

5. Common Phrases & Intent Mapping:
   - 'add_expense': User is recording an expense or money spent.
     Keywords/Phrases: 'selavu', 'selavu aachu', 'kudutha', 'vaangina', 'pottaen', 'kattina', 'theenthuduchu', 'spent', 'bought', 'paid'.
     Examples: 'petrol ku 500 pottaen', 'lunch ku 250 selavu', 'tea 20 ruba', 'inniku maligai saaman 1200 vaanginaen', 'நேற்று மளிகைக்கு 500 செலவு'.
     Required fields: 'item', 'amount'. Optional: 'category', 'date'.

   - 'set_goal': User wants to create or set a saving goal/target with a known budget/target amount.
     Keywords/Phrases: 'vaanganum', 'goal vei', 'target vei', 'set goal', 'plan pannirukkaen', 'சேமிப்பு இலக்கு'.
     Examples: 'laptop vaanganum 60000, december kulla', 'bike vaanga goal vei 100000', 'puthu phone vaanga target 25000'.
     Required fields: 'item', 'amount'. Optional: 'date', 'current_savings'.

   - 'add_savings': User wants to add/deposit money into savings or a saving goal.
     Keywords/Phrases: 'savings la serthu vei', 'semichen', 'serthu vachen', 'savings add pannu', 'சேமிப்பில் சேர்'.
     Examples: 'savings la 5000 serthu vei', '5000 semichen', 'laptop goal ku 2000 pottaen'.
     Required fields: 'amount'. Optional: 'item', 'date'.

   - 'suggest_products': User wants product recommendations or buying suggestions under a certain budget.
     Keywords/Phrases: 'suggest pannu', 'sollu', 'nalla phone edhu', 'best recommendation', 'budget kulla'.
     Examples: 'best phone sollu 20000 kulla', 'oru laptop suggest pannu 50000 budget', '3000 kulla nalla headphones sollu'.
     Required fields: 'item', 'amount'. If budget/amount is missing, list 'amount' in 'missing_fields' and ask a clarifying question in the user's language.

   - 'set_goal_lookup_price': User wants to set a product as a goal but DOES NOT KNOW the price, or asks to search the internet/online for the product's price.
     CRITICAL: The backend CAN and DOES search the live web for prices through an automated grounded search tool. You must NEVER say 'I can't browse the internet', 'I cannot search online', or ask the user to provide a budget when they don't know the price. Return this intent with the product name in 'item'.
     Examples:
     * 'I want to add this product as goal but I don't know the price, could you search the internet and add it'
     * 'iPad Air 11 inch goal add pannu, price theriyala, search pannu'
     * 'I don't know the price, search online' (item comes from recent conversation history, e.g. 'iPad Air 11 inch')
     * 'bike vaanganum, price enna nu paathu goal la podu'
     * 'price theriyala, search pannu' (resolve item from previous messages)
     * 'could you check the online price for Royal Enfield Hunter 350 and set a goal?'
     Context resolution: Use the recent conversation history to resolve referential terms like 'this product', 'it', 'search for the price', 'price theriyala search pannu'. If the item cannot be resolved from the current message or recent history, add 'item' to 'missing_fields' and ask which product in 'reply'. 'amount' is NOT required for this intent.

   - 'general_question':
     * Questions about PocketGoal features, personal finance, budgeting, saving strategies, shopping, or goal planning (e.g., 'how do I add savings?', 'is it better to save weekly or monthly?', 'how long to save 60000 if I save 5000 a month?').
       Answer briefly (maximum 4 sentences) in the user's language and style.
       If the question involves investments, stocks, or financial assets, provide basic general info and append: 'I'm not a financial advisor.' Never give legal, tax, or formal investment advice.
     * UNRELATED topics outside personal finance, shopping, products, and PocketGoal (e.g., general knowledge, world news, sports, coding, cooking):
       Answer politely in ONE short sentence stating that you specialize in expenses, goals, savings, and product prices, and offer a concrete example of what the user can type (e.g., 'I help with expenses, savings goals, and product prices! Try: ""Add expense: lunch 250"" or ""Suggest a phone under 20000"".').

   - 'query_spending': User is asking to check their own logged spending or expenses (e.g., 'how much did I spend this month?', 'what is my spending on food?', 'how much have I spent today?', 'what are my total expenses?').
     The BACKEND will compute the exact numbers from the user's database. Populate 'category' (if specified, e.g., 'Food & Dining') or 'item'. Provide a polite acknowledgment in 'reply'.

   - 'query_goals': User is asking to check their savings progress or goals (e.g., 'what is my total savings?', 'which goal is closest?', 'how many active goals do I have?', 'how much do I have saved for laptop?').
     The BACKEND will compute the exact numbers from the user's database. Populate 'item' if a specific goal was named (e.g., 'laptop'). Provide a polite acknowledgment in 'reply'.

   - 'unknown': Greetings or completely unrecognizable input.

6. Validation & Missing Fields Rules:
   - If required fields are missing from the current input and cannot be inferred from recent context, list the missing field names in 'missing_fields' (e.g., ['amount'] or ['item']) and ask a friendly clarifying follow-up question in the user's language and style in 'reply'.
   - NEVER guess or convert an amount that was not clearly stated. If the amount is unclear or missing, ask for it in the user's language instead of guessing.
   - Categories must be mapped to one of: 'Food & Dining', 'Travel & Commute', 'Fuel & Vehicle', 'Shopping', 'Entertainment & Movies', 'Bills & Utilities', 'Subscriptions', 'Health & Medical', 'Education', 'Other'.
   - Always output strict JSON matching the schema.";

            var contents = new List<object>();

            if (history != null && history.Count > 0)
            {
                var recentHistory = history.TakeLast(6).ToList();
                foreach (var hist in recentHistory)
                {
                    if (string.IsNullOrWhiteSpace(hist.Content)) continue;
                    var role = hist.Role?.ToLowerInvariant() == "user" ? "user" : "model";
                    contents.Add(new
                    {
                        role = role,
                        parts = new[] { new { text = hist.Content } }
                    });
                }
            }

            contents.Add(new
            {
                role = "user",
                parts = new[] { new { text = userMessage } }
            });

            var requestBody = new
            {
                systemInstruction = new
                {
                    parts = new[] { new { text = systemInstruction } }
                },
                contents = contents,
                generationConfig = new
                {
                    temperature = 0,
                    responseMimeType = "application/json",
                    responseSchema = new
                    {
                        type = "OBJECT",
                        properties = new
                        {
                            intent = new
                            {
                                type = "STRING",
                                @enum = new[]
                                {
                                    "add_expense",
                                    "set_goal",
                                    "add_savings",
                                    "suggest_products",
                                    "set_goal_lookup_price",
                                    "general_question",
                                    "query_spending",
                                    "query_goals",
                                    "unknown"
                                }
                            },
                            item = new { type = "STRING" },
                            amount = new { type = "NUMBER" },
                            category = new { type = "STRING" },
                            date = new { type = "STRING" },
                            current_savings = new { type = "NUMBER" },
                            missing_fields = new
                            {
                                type = "ARRAY",
                                items = new { type = "STRING" }
                            },
                            reply = new { type = "STRING" }
                        },
                        required = new[] { "intent", "missing_fields", "reply" }
                    }
                }
            };

            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}";
            return await CallGeminiExtractionWithRetryAsync(url, requestBody, cancellationToken);
        }

        public async Task<ProductSuggestionResult> SuggestProductsAsync(
            string item,
            decimal budget,
            CancellationToken cancellationToken = default)
        {
            var apiKey = GetApiKey();
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                _logger.LogError("GEMINI_API_KEY environment variable is not configured.");
                return new ProductSuggestionResult
                {
                    Status = ProductSuggestionStatus.Failed,
                    ErrorMessage = "AI Assistant is currently offline because the GEMINI_API_KEY is not configured."
                };
            }

            var model = GetModel();
            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}";

            // Circuit Breaker check: if repeated 429s tripped circuit breaker, skip grounded search
            if (IsCircuitBreakerOpen())
            {
                _logger.LogWarning("Product suggestions: circuit breaker active (grounded search quota exhausted), skipping directly to fallback for '{Item}'", item);
                using var cbFallbackCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cbFallbackCts.CancelAfter(TimeSpan.FromSeconds(10));
                return await ExecuteFallbackSuggestionsAsync(url, item, budget, cbFallbackCts.Token);
            }

            // 1. ATTEMPT GROUNDED SEARCH (hard 15s limit, dedicated CTS)
            GroundedOutcome? groundedOutcome = null;
            using (var groundedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                groundedCts.CancelAfter(TimeSpan.FromSeconds(15));
                try
                {
                    var groundedPrompt = $"Find 3 real, currently available products in India for '{item}' that cost at or below ₹{budget:N0} (prices in INR). " +
                                         $"For each product, mention the exact product model name, current approximate price in INR, and a brief 1-sentence reason why it is recommended. " +
                                         $"Include real Indian retail/market links if available.";

                    var groundedRequestBody = new
                    {
                        contents = new[]
                        {
                            new
                            {
                                role = "user",
                                parts = new[] { new { text = groundedPrompt } }
                            }
                        },
                        tools = new object[]
                        {
                            new
                            {
                                googleSearch = new { }
                            }
                        }
                    };

                    groundedOutcome = await ExecuteGroundedSearchAsync(url, groundedRequestBody, item, "Product suggestions", groundedCts.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    _logger.LogWarning("Product suggestions: grounded search timeout for '{Item}', using fallback", item);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Product suggestions: grounded search error for '{Item}', using fallback", item);
                }
            }

            if (groundedOutcome != null && groundedOutcome.Success)
            {
                using var conversionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                conversionCts.CancelAfter(TimeSpan.FromSeconds(8));

                var validUrlsList = groundedOutcome.GroundingUrls.Count > 0 ? string.Join("\n", groundedOutcome.GroundingUrls) : "None";

                var conversionPrompt = $@"Convert the following product information into up to 3 product suggestions at or below the budget of ₹{budget:N0} INR.

Verified source URLs available from search:
{validUrlsList}

Rules:
1. 'name': Exact product brand and model name.
2. 'approxPrice': Numeric price in INR. Must be > 0 and <= {budget}.
3. 'reason': Maximum 15 words explaining why it is recommended.
4. 'sourceUrl': Must be one of the exact verified source URLs listed above that corresponds to this product. If none matches, return null.

Product information:
{groundedOutcome.Text}";

                var conversionRequestBody = new
                {
                    contents = new[]
                    {
                        new
                        {
                            role = "user",
                            parts = new[] { new { text = conversionPrompt } }
                        }
                    },
                    generationConfig = new
                    {
                        temperature = 0,
                        responseMimeType = "application/json",
                        responseSchema = new
                        {
                            type = "ARRAY",
                            items = new
                            {
                                type = "OBJECT",
                                properties = new
                                {
                                    name = new { type = "STRING" },
                                    approxPrice = new { type = "NUMBER" },
                                    reason = new { type = "STRING" },
                                    sourceUrl = new { type = "STRING" }
                                },
                                required = new[] { "name", "approxPrice", "reason" }
                            }
                        }
                    }
                };

                try
                {
                    var rawJson = await CallGeminiRawStringWithRetryAsync(url, conversionRequestBody, conversionCts.Token);
                    if (!string.IsNullOrWhiteSpace(rawJson))
                    {
                        var extractedDtos = JsonSerializer.Deserialize<List<ProductSuggestionExtractionDto>>(rawJson, JsonOptions)
                                            ?? new List<ProductSuggestionExtractionDto>();

                        var urlLookup = new HashSet<string>(groundedOutcome.GroundingUrls, StringComparer.OrdinalIgnoreCase);
                        var results = FilterAndMapSuggestions(extractedDtos, budget, urlLookup, isLive: true);

                        if (results.Count > 0)
                        {
                            return new ProductSuggestionResult
                            {
                                Status = ProductSuggestionStatus.Success,
                                Suggestions = results,
                                IsLive = true
                            };
                        }
                    }

                    _logger.LogWarning("Product suggestions: grounded search conversion failed for '{Item}', using fallback", item);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    _logger.LogWarning("Product suggestions: grounded search conversion timeout for '{Item}', using fallback", item);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Product suggestions: grounded search conversion error for '{Item}', using fallback", item);
                }
            }

            // 2. FALLBACK: Grounded search failed. Fresh dedicated 10s budget linked only to request token.
            _logger.LogInformation("Grounded search failed (Reason: {Reason}). Proceeding to ungrounded fallback for '{Item}', budget {Budget}.",
                groundedOutcome?.FailureReason ?? "Failed", item, budget);

            using var fallbackCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            fallbackCts.CancelAfter(TimeSpan.FromSeconds(10));
            return await ExecuteFallbackSuggestionsAsync(url, item, budget, fallbackCts.Token);
        }

        public async Task<PriceLookupResult> LookupPriceAsync(string item, CancellationToken cancellationToken = default)
        {
            var apiKey = GetApiKey();
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                _logger.LogError("GEMINI_API_KEY environment variable is not configured.");
                return new PriceLookupResult
                {
                    Status = ProductSuggestionStatus.Failed,
                    ErrorMessage = "AI Assistant is currently offline because the GEMINI_API_KEY is not configured."
                };
            }

            var model = GetModel();
            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}";

            // Circuit Breaker check: if repeated 429s tripped circuit breaker, skip grounded search entirely
            if (IsCircuitBreakerOpen())
            {
                _logger.LogWarning("Price lookup: circuit breaker active (grounded search quota exhausted), skipping directly to fallback for '{Item}'", item);
                using var cbFallbackCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cbFallbackCts.CancelAfter(TimeSpan.FromSeconds(10));
                return await ExecuteFallbackPriceLookupAsync(url, item, cbFallbackCts.Token);
            }

            // STEP 1: ATTEMPT GROUNDED SEARCH (hard 15s limit, dedicated CTS)
            GroundedOutcome? groundedOutcome = null;
            using (var groundedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                groundedCts.CancelAfter(TimeSpan.FromSeconds(15));
                try
                {
                    var groundedPrompt = $"Find the current retail price in India (in INR) for the product '{item}'. " +
                                         $"Find the price of the base or most common variant. " +
                                         $"State the exact product brand, model, and variant name (e.g., 'Apple iPad Air 11-inch (128GB, M2)'), " +
                                         $"the current approximate price in INR (numbers only, e.g. 59900), and a brief note on which variant was used. " +
                                         $"Include real Indian retail/market links if available.";

                    var groundedRequestBody = new
                    {
                        contents = new[]
                        {
                            new
                            {
                                role = "user",
                                parts = new[] { new { text = groundedPrompt } }
                            }
                        },
                        tools = new object[]
                        {
                            new
                            {
                                googleSearch = new { }
                            }
                        }
                    };

                    groundedOutcome = await ExecuteGroundedSearchAsync(url, groundedRequestBody, item, "Price lookup", groundedCts.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    _logger.LogWarning("Price lookup: grounded search timeout for '{Item}', using fallback", item);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Price lookup: grounded search error for '{Item}', using fallback", item);
                }
            }

            // If grounded search succeeded, try conversion (8s limit)
            if (groundedOutcome != null && groundedOutcome.Success)
            {
                using var conversionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                conversionCts.CancelAfter(TimeSpan.FromSeconds(8));

                try
                {
                    var validUrlsList = groundedOutcome.GroundingUrls.Count > 0
                        ? string.Join("\n", groundedOutcome.GroundingUrls)
                        : "None";

                    var conversionPrompt = $@"Convert the following product price information for '{item}' into structured JSON.

Verified source URLs available from search:
{validUrlsList}

Rules:
1. 'productName': Exact product brand and model name with variant (e.g. 'Apple iPad Air 11-inch (128GB)').
2. 'approxPrice': Numeric price in INR (must be > 0).
3. 'variantNote': Brief note on the variant used (e.g. 'Base 128GB Wi-Fi variant').
4. 'sourceUrl': Must be one of the exact verified source URLs listed above that corresponds to this product. If none matches or none provided, return null.

Product price information:
{groundedOutcome.Text}";

                    var conversionRequestBody = new
                    {
                        contents = new[]
                        {
                            new
                            {
                                role = "user",
                                parts = new[] { new { text = conversionPrompt } }
                            }
                        },
                        generationConfig = new
                        {
                            temperature = 0,
                            responseMimeType = "application/json",
                            responseSchema = new
                            {
                                type = "OBJECT",
                                properties = new
                                {
                                    productName = new { type = "STRING" },
                                    approxPrice = new { type = "NUMBER" },
                                    variantNote = new { type = "STRING" },
                                    sourceUrl = new { type = "STRING" }
                                },
                                required = new[] { "productName", "approxPrice" }
                            }
                        }
                    };

                    var rawJson = await CallGeminiRawStringWithRetryAsync(url, conversionRequestBody, conversionCts.Token);
                    if (!string.IsNullOrWhiteSpace(rawJson))
                    {
                        var dto = JsonSerializer.Deserialize<PriceLookupExtractionDto>(rawJson, JsonOptions);
                        if (dto != null && dto.ApproxPrice > 0 && !string.IsNullOrWhiteSpace(dto.ProductName))
                        {
                            string? verifiedSourceUrl = null;
                            if (!string.IsNullOrWhiteSpace(dto.SourceUrl) &&
                                dto.SourceUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
                                groundedOutcome.GroundingUrls.Contains(dto.SourceUrl, StringComparer.OrdinalIgnoreCase))
                            {
                                verifiedSourceUrl = dto.SourceUrl;
                            }

                            return new PriceLookupResult
                            {
                                Status = ProductSuggestionStatus.Success,
                                ProductName = dto.ProductName.Trim(),
                                ApproxPrice = dto.ApproxPrice,
                                VariantNote = dto.VariantNote?.Trim(),
                                SourceUrl = verifiedSourceUrl,
                                IsLive = true
                            };
                        }
                    }

                    _logger.LogWarning("Price lookup: grounded search conversion failed for '{Item}', using fallback", item);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    _logger.LogWarning("Price lookup: grounded search conversion timeout for '{Item}', using fallback", item);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Price lookup: grounded search conversion error for '{Item}', using fallback", item);
                }
            }

            // STEP 2: FALLBACK ESTIMATE (hard 10s limit, dedicated CTS linked ONLY to request token)
            // A slow grounded search NEVER starves this fallback because fallbackCts has its own fresh 10s timer.
            using var fallbackCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            fallbackCts.CancelAfter(TimeSpan.FromSeconds(10));
            return await ExecuteFallbackPriceLookupAsync(url, item, fallbackCts.Token);
        }

        private async Task<PriceLookupResult> ExecuteFallbackPriceLookupAsync(string url, string item, CancellationToken cancellationToken)
        {
            var fallbackPrompt = $@"Estimate the typical retail price in India (in INR) for the product '{item}'.
Give the typical base or most common variant name, realistic approximate price in INR, and variant note.
Do not include any source URLs.

Rules:
1. 'productName': Exact product brand and model name with variant (e.g. 'Apple iPad Air 11-inch (128GB)').
2. 'approxPrice': Realistic approximate price in INR (must be a positive number). If the product cannot be determined or does not exist, set approxPrice to 0.
3. 'variantNote': Brief note on the variant used (e.g. 'Base 128GB Wi-Fi variant').";

            var fallbackRequestBody = new
            {
                contents = new[]
                {
                    new
                    {
                        role = "user",
                        parts = new[] { new { text = fallbackPrompt } }
                    }
                },
                generationConfig = new
                {
                    temperature = 0,
                    responseMimeType = "application/json",
                    responseSchema = new
                    {
                        type = "OBJECT",
                        properties = new
                        {
                            productName = new { type = "STRING" },
                            approxPrice = new { type = "NUMBER" },
                            variantNote = new { type = "STRING" }
                        },
                        required = new[] { "productName", "approxPrice" }
                    }
                }
            };

            try
            {
                var rawJson = await CallGeminiRawStringWithRetryAsync(url, fallbackRequestBody, cancellationToken);
                if (!string.IsNullOrWhiteSpace(rawJson))
                {
                    var dto = JsonSerializer.Deserialize<PriceLookupExtractionDto>(rawJson, JsonOptions);
                    if (dto != null)
                    {
                        if (dto.ApproxPrice > 0 && !string.IsNullOrWhiteSpace(dto.ProductName))
                        {
                            return new PriceLookupResult
                            {
                                Status = ProductSuggestionStatus.Success,
                                ProductName = dto.ProductName.Trim(),
                                ApproxPrice = dto.ApproxPrice,
                                VariantNote = dto.VariantNote?.Trim(),
                                SourceUrl = null, // In fallback mode, sourceUrl is ALWAYS null.
                                IsLive = false
                            };
                        }

                        // Explicit indication that product price is not found
                        _logger.LogInformation("Price lookup: AI returned non-positive price for '{Item}'", item);
                        return new PriceLookupResult
                        {
                            Status = ProductSuggestionStatus.NoProductsFound,
                            ErrorMessage = "Could not estimate price for product.",
                            IsLive = false
                        };
                    }
                }

                _logger.LogWarning("Price lookup: fallback failed (empty response) for '{Item}'", item);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("Price lookup: fallback failed (timeout) for '{Item}'", item);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Price lookup: fallback failed (error) for '{Item}'", item);
            }

            // Fallback failed due to timeout, rate limit, error, or failure to parse
            // MUST return Overloaded or Failed, NOT NoProductsFound!
            return new PriceLookupResult
            {
                Status = ProductSuggestionStatus.Overloaded,
                ErrorMessage = "The AI service is busy right now. Please try again in a minute.",
                IsLive = false
            };
        }

        public async Task<string> PhraseUserDataAnswerAsync(
            string userMessage,
            string facts,
            string? languageStyle,
            CancellationToken cancellationToken = default)
        {
            var apiKey = GetApiKey();
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return facts;
            }

            var model = GetModel();
            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}";

            var prompt = $@"You are PocketGoal AI assistant.
The user asked a question about their personal financial data:
""{userMessage}""

The backend calculated the EXACT factual numbers from their database:
{facts}

Instructions:
1. Answer the user's question using ONLY the factual numbers provided above.
2. Do NOT guess, compute, invent, or change any numbers.
3. If the facts indicate no data is available or 0, politely state that.
4. Reply in the user's language and style (English, Tamil script, or Tanglish).
5. Keep it friendly, concise, and natural (1 to 3 short sentences).";

            var requestBody = new
            {
                contents = new[]
                {
                    new
                    {
                        role = "user",
                        parts = new[] { new { text = prompt } }
                    }
                },
                generationConfig = new
                {
                    temperature = 0
                }
            };

            var resultText = await CallGeminiRawStringWithRetryAsync(url, requestBody, cancellationToken);
            if (!string.IsNullOrWhiteSpace(resultText))
            {
                return resultText.Trim();
            }

            return facts;
        }

        private class GroundedOutcome
        {
            public bool Success { get; set; }
            public string Text { get; set; } = string.Empty;
            public List<string> GroundingUrls { get; set; } = new();
            public string FailureReason { get; set; } = string.Empty;
        }

        private async Task<GroundedOutcome> ExecuteGroundedSearchAsync(
            string url,
            object requestPayload,
            string item,
            string operation,
            CancellationToken cancellationToken)
        {
            var jsonPayload = JsonSerializer.Serialize(requestPayload);

            if (cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("{Operation}: grounded search timeout for '{Item}', using fallback", operation, item);
                return new GroundedOutcome { Success = false, FailureReason = "Timeout" };
            }

            try
            {
                using var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync(url, content, cancellationToken);

                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    RecordGrounded429();
                    _logger.LogWarning("{Operation}: grounded search 429 for '{Item}', using fallback", operation, item);
                    return new GroundedOutcome { Success = false, FailureReason = "429 RateLimit" };
                }

                if ((int)response.StatusCode >= 500)
                {
                    _logger.LogWarning("{Operation}: grounded search {StatusCode} for '{Item}', using fallback", operation, (int)response.StatusCode, item);
                    return new GroundedOutcome { Success = false, FailureReason = $"{response.StatusCode} Overloaded" };
                }

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("{Operation}: grounded search error {StatusCode} for '{Item}', using fallback", operation, response.StatusCode, item);
                    return new GroundedOutcome { Success = false, FailureReason = $"{response.StatusCode} Error" };
                }

                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                var (text, urls) = ParseGroundedSearchResponse(responseBody);
                if (string.IsNullOrWhiteSpace(text))
                {
                    _logger.LogWarning("{Operation}: grounded search empty response for '{Item}', using fallback", operation, item);
                    return new GroundedOutcome { Success = false, FailureReason = "EmptyResponse" };
                }

                return new GroundedOutcome
                {
                    Success = true,
                    Text = text,
                    GroundingUrls = urls
                };
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("{Operation}: grounded search timeout for '{Item}', using fallback", operation, item);
                return new GroundedOutcome { Success = false, FailureReason = "Timeout" };
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "{Operation}: grounded search network error for '{Item}', using fallback", operation, item);
                return new GroundedOutcome { Success = false, FailureReason = "NetworkError" };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "{Operation}: grounded search unexpected error for '{Item}', using fallback", operation, item);
                return new GroundedOutcome { Success = false, FailureReason = "Exception" };
            }
        }

        private async Task<ProductSuggestionResult> ExecuteFallbackSuggestionsAsync(
            string url,
            string item,
            decimal budget,
            CancellationToken cancellationToken)
        {
            var fallbackPrompt = $@"Suggest up to 3 well-known, currently popular products in India for '{item}' that cost at or below ₹{budget:N0} INR (prices in INR).

Rules:
1. 'name': Exact product brand and model name.
2. 'approxPrice': Realistic approximate price in INR (must be > 0 and <= {budget}).
3. 'reason': Maximum 15 words explaining why it is recommended.
4. Do not include any source URLs.";

            var fallbackRequestBody = new
            {
                contents = new[]
                {
                    new
                    {
                        role = "user",
                        parts = new[] { new { text = fallbackPrompt } }
                    }
                },
                generationConfig = new
                {
                    temperature = 0,
                    responseMimeType = "application/json",
                    responseSchema = new
                    {
                        type = "ARRAY",
                        items = new
                        {
                            type = "OBJECT",
                            properties = new
                            {
                                name = new { type = "STRING" },
                                approxPrice = new { type = "NUMBER" },
                                reason = new { type = "STRING" }
                            },
                            required = new[] { "name", "approxPrice", "reason" }
                        }
                    }
                }
            };

            var rawJson = await CallGeminiRawStringWithRetryAsync(url, fallbackRequestBody, cancellationToken);

            if (string.IsNullOrWhiteSpace(rawJson))
            {
                _logger.LogWarning("Both grounded search and fallback failed for '{Item}', budget {Budget}", item, budget);
                return new ProductSuggestionResult
                {
                    Status = ProductSuggestionStatus.Overloaded,
                    ErrorMessage = "The AI service is busy right now. Please try again in a minute."
                };
            }

            try
            {
                var extractedDtos = JsonSerializer.Deserialize<List<ProductSuggestionExtractionDto>>(rawJson, JsonOptions)
                                    ?? new List<ProductSuggestionExtractionDto>();

                // In fallback mode, sourceUrl is ALWAYS null.
                var results = FilterAndMapSuggestions(extractedDtos, budget, validUrls: null, isLive: false);

                if (results.Count > 0)
                {
                    return new ProductSuggestionResult
                    {
                        Status = ProductSuggestionStatus.Success,
                        Suggestions = results,
                        IsLive = false
                    };
                }

                return new ProductSuggestionResult
                {
                    Status = ProductSuggestionStatus.NoProductsFound,
                    Suggestions = new List<ProductSuggestion>(),
                    IsLive = false
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse fallback suggestions JSON.");
                return new ProductSuggestionResult
                {
                    Status = ProductSuggestionStatus.Overloaded,
                    ErrorMessage = "The AI service is busy right now. Please try again in a minute."
                };
            }
        }

        private List<ProductSuggestion> FilterAndMapSuggestions(
            List<ProductSuggestionExtractionDto> dtos,
            decimal budget,
            HashSet<string>? validUrls,
            bool isLive)
        {
            var results = new List<ProductSuggestion>();

            foreach (var dto in dtos)
            {
                if (string.IsNullOrWhiteSpace(dto.Name)) continue;

                if (dto.ApproxPrice <= 0 || dto.ApproxPrice > budget) continue;

                var reason = (dto.Reason ?? string.Empty).Trim();
                var words = reason.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (words.Length > 15)
                {
                    reason = string.Join(" ", words.Take(15)) + "...";
                }

                string? sourceUrl = null;
                if (isLive && validUrls != null && !string.IsNullOrWhiteSpace(dto.SourceUrl))
                {
                    var cleanUrl = dto.SourceUrl.Trim();
                    if (cleanUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase) && validUrls.Contains(cleanUrl))
                    {
                        sourceUrl = cleanUrl;
                    }
                }

                var cleanName = dto.Name.Trim();
                var searchUrl = $"https://www.amazon.in/s?k={Uri.EscapeDataString(cleanName)}";
                var flipkartUrl = $"https://www.flipkart.com/search?q={Uri.EscapeDataString(cleanName)}";

                results.Add(new ProductSuggestion
                {
                    Name = cleanName,
                    ApproxPrice = dto.ApproxPrice,
                    Reason = reason,
                    SourceUrl = sourceUrl,
                    SearchUrl = searchUrl,
                    FlipkartUrl = flipkartUrl
                });

                if (results.Count >= 3) break;
            }

            return results;
        }

        private int ExtractRetryDelay(HttpResponseMessage response, string errorBody)
        {
            if (response.Headers.RetryAfter?.Delta.HasValue == true)
            {
                return (int)response.Headers.RetryAfter.Delta.Value.TotalSeconds;
            }

            try
            {
                var match = Regex.Match(errorBody, @"(?i)retry\s*after\s*(\d+)", RegexOptions.Compiled);
                if (match.Success && int.TryParse(match.Groups[1].Value, out var sec))
                {
                    return sec;
                }

                var delayMatch = Regex.Match(errorBody, @"(?i)""retryDelay""\s*:\s*""?(\d+)s?""?", RegexOptions.Compiled);
                if (delayMatch.Success && int.TryParse(delayMatch.Groups[1].Value, out var sec2))
                {
                    return sec2;
                }
            }
            catch { }

            return 0;
        }

        private string ExtractSafeErrorMessage(string errorBody)
        {
            try
            {
                using var doc = JsonDocument.Parse(errorBody);
                if (doc.RootElement.TryGetProperty("error", out var errEl) &&
                    errEl.TryGetProperty("message", out var msgEl))
                {
                    return msgEl.GetString() ?? "Unknown error";
                }
            }
            catch { }

            return "Status error";
        }

        private (string Text, List<string> GroundingUrls) ParseGroundedSearchResponse(string responseBody)
        {
            var groundingUrls = new List<string>();
            var textBuilder = new StringBuilder();

            try
            {
                using var doc = JsonDocument.Parse(responseBody);
                var root = doc.RootElement;

                if (!root.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
                {
                    return (string.Empty, groundingUrls);
                }

                var candidate = candidates[0];

                if (candidate.TryGetProperty("content", out var content) &&
                    content.TryGetProperty("parts", out var parts))
                {
                    foreach (var part in parts.EnumerateArray())
                    {
                        if (part.TryGetProperty("text", out var textEl))
                        {
                            textBuilder.AppendLine(textEl.GetString());
                        }
                    }
                }

                if (candidate.TryGetProperty("groundingMetadata", out var groundingMeta) &&
                    groundingMeta.TryGetProperty("groundingChunks", out var chunks))
                {
                    foreach (var chunk in chunks.EnumerateArray())
                    {
                        if (chunk.TryGetProperty("web", out var webEl) &&
                            webEl.TryGetProperty("uri", out var uriEl))
                        {
                            var uriStr = uriEl.GetString();
                            if (!string.IsNullOrWhiteSpace(uriStr) &&
                                uriStr.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
                                !groundingUrls.Contains(uriStr))
                            {
                                groundingUrls.Add(uriStr);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing grounded search response.");
            }

            return (textBuilder.ToString(), groundingUrls);
        }

        private async Task<string> CallGeminiRawStringWithRetryAsync(
            string url,
            object requestPayload,
            CancellationToken cancellationToken)
        {
            var jsonPayload = JsonSerializer.Serialize(requestPayload);

            for (int attempt = 1; attempt <= 2; attempt++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException(cancellationToken);
                }

                // 10 seconds hard limit on normal calls
                using var callCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                callCts.CancelAfter(TimeSpan.FromSeconds(10));

                try
                {
                    using var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
                    var response = await _httpClient.PostAsync(url, content, callCts.Token);

                    if (response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500)
                    {
                        var errBody = await response.Content.ReadAsStringAsync(cancellationToken);
                        var safeMsg = ExtractSafeErrorMessage(errBody);
                        _logger.LogWarning("Gemini API call returned {StatusCode} (attempt {Attempt}/2). Message: {Message}",
                            response.StatusCode, attempt, safeMsg);

                        if (attempt == 1 && !cancellationToken.IsCancellationRequested)
                        {
                            await Task.Delay(1500, cancellationToken);
                            continue;
                        }
                        return string.Empty;
                    }

                    if (!response.IsSuccessStatusCode)
                    {
                        var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                        var safeMsg = ExtractSafeErrorMessage(errorContent);
                        _logger.LogError("Gemini API call failed with status {StatusCode}: {Error}", response.StatusCode, safeMsg);
                        return string.Empty;
                    }

                    var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                    using var doc = JsonDocument.Parse(responseBody);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("candidates", out var candidates) &&
                        candidates.GetArrayLength() > 0 &&
                        candidates[0].TryGetProperty("content", out var candContent) &&
                        candContent.TryGetProperty("parts", out var parts) &&
                        parts.GetArrayLength() > 0 &&
                        parts[0].TryGetProperty("text", out var textEl))
                    {
                        return textEl.GetString() ?? string.Empty;
                    }

                    return string.Empty;
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    _logger.LogWarning("Gemini raw call timed out after 10 seconds (attempt {Attempt}/2).", attempt);
                    return string.Empty;
                }
                catch (HttpRequestException ex)
                {
                    _logger.LogWarning(ex, "Network error during Gemini raw call (attempt {Attempt}/2).", attempt);
                    if (attempt == 1 && !cancellationToken.IsCancellationRequested)
                    {
                        await Task.Delay(1000, cancellationToken);
                        continue;
                    }
                    return string.Empty;
                }
            }

            return string.Empty;
        }

        private async Task<GeminiExtractionResult> CallGeminiExtractionWithRetryAsync(
            string url,
            object requestPayload,
            CancellationToken cancellationToken)
        {
            var jsonPayload = JsonSerializer.Serialize(requestPayload);

            for (int attempt = 1; attempt <= 2; attempt++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException(cancellationToken);
                }

                // 10 seconds hard limit on extraction call
                using var callCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                callCts.CancelAfter(TimeSpan.FromSeconds(10));

                try
                {
                    using var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
                    var response = await _httpClient.PostAsync(url, content, callCts.Token);

                    if (response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500)
                    {
                        var errBody = await response.Content.ReadAsStringAsync(cancellationToken);
                        var safeMsg = ExtractSafeErrorMessage(errBody);
                        _logger.LogWarning("Gemini API returned {StatusCode} (attempt {Attempt}/2). Message: {Message}",
                            response.StatusCode, attempt, safeMsg);

                        if (attempt == 1 && !cancellationToken.IsCancellationRequested)
                        {
                            await Task.Delay(1500, cancellationToken);
                            continue;
                        }

                        return new GeminiExtractionResult
                        {
                            Intent = "unknown",
                            Reply = "The AI service is busy right now. Please try again in a minute.",
                            MissingFields = new List<string>()
                        };
                    }

                    if (!response.IsSuccessStatusCode)
                    {
                        var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                        var safeMsg = ExtractSafeErrorMessage(errorContent);
                        _logger.LogError("Gemini API call failed with status {StatusCode}: {Error}", response.StatusCode, safeMsg);
                        return new GeminiExtractionResult
                        {
                            Intent = "unknown",
                            Reply = "The AI service is busy right now. Please try again in a minute.",
                            MissingFields = new List<string>()
                        };
                    }

                    var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                    return ParseGeminiResponse(responseBody);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    _logger.LogWarning("Gemini extraction call timed out after 10 seconds (attempt {Attempt}/2).", attempt);
                    return new GeminiExtractionResult
                    {
                        Intent = "unknown",
                        Reply = "The AI service is taking longer than expected. Please try again.",
                        MissingFields = new List<string>()
                    };
                }
                catch (HttpRequestException ex)
                {
                    _logger.LogError(ex, "Network error occurred while calling Gemini API (attempt {Attempt}/2).", attempt);
                    if (attempt == 1 && !cancellationToken.IsCancellationRequested)
                    {
                        await Task.Delay(1000, cancellationToken);
                        continue;
                    }

                    return new GeminiExtractionResult
                    {
                        Intent = "unknown",
                        Reply = "Network error while connecting to PocketGoal AI. Please check your internet connection.",
                        MissingFields = new List<string>()
                    };
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unexpected error in Gemini API call.");
                    return new GeminiExtractionResult
                    {
                        Intent = "unknown",
                        Reply = "An unexpected error occurred while communicating with PocketGoal AI.",
                        MissingFields = new List<string>()
                    };
                }
            }

            return new GeminiExtractionResult
            {
                Intent = "unknown",
                Reply = "The AI service is busy right now. Please try again in a minute.",
                MissingFields = new List<string>()
            };
        }

        private GeminiExtractionResult ParseGeminiResponse(string responseBody)
        {
            try
            {
                using var doc = JsonDocument.Parse(responseBody);
                var root = doc.RootElement;

                if (!root.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
                {
                    _logger.LogWarning("Gemini response did not contain candidates: {Body}", responseBody);
                    return new GeminiExtractionResult
                    {
                        Intent = "unknown",
                        Reply = "I couldn't generate a response for that. Could you rephrase?",
                        MissingFields = new List<string>()
                    };
                }

                var firstCandidate = candidates[0];
                if (!firstCandidate.TryGetProperty("content", out var candidateContent) ||
                    !candidateContent.TryGetProperty("parts", out var parts) ||
                    parts.GetArrayLength() == 0)
                {
                    _logger.LogWarning("Gemini candidate did not contain parts: {Body}", responseBody);
                    return new GeminiExtractionResult
                    {
                        Intent = "unknown",
                        Reply = "I received an empty response from the AI. Please try again.",
                        MissingFields = new List<string>()
                    };
                }

                var textPart = parts[0].GetProperty("text").GetString();
                if (string.IsNullOrWhiteSpace(textPart))
                {
                    return new GeminiExtractionResult
                    {
                        Intent = "unknown",
                        Reply = "I received an empty response. Please try asking again.",
                        MissingFields = new List<string>()
                    };
                }

                var result = JsonSerializer.Deserialize<GeminiExtractionResult>(textPart, JsonOptions);
                if (result == null)
                {
                    _logger.LogError("Failed to deserialize Gemini output text: {Text}", textPart);
                    return new GeminiExtractionResult
                    {
                        Intent = "unknown",
                        Reply = "I had trouble understanding the response. Could you please rephrase?",
                        MissingFields = new List<string>()
                    };
                }

                result.MissingFields ??= new List<string>();
                return result;
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "JSON parsing error for Gemini response: {Body}", responseBody);
                return new GeminiExtractionResult
                {
                    Intent = "unknown",
                    Reply = "I received a malformed response from the AI. Please try rephrasing your message.",
                    MissingFields = new List<string>()
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing Gemini response.");
                return new GeminiExtractionResult
                {
                    Intent = "unknown",
                    Reply = "An error occurred while processing the response.",
                    MissingFields = new List<string>()
                };
            }
        }
    }
}
