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
        Task<GeminiExtractionResult> ProcessWithGeminiAsync(string userMessage, List<ChatMessage>? history, DateTime currentDate);
        Task<ProductSuggestionResult> SuggestProductsAsync(string item, decimal budget);
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

        public async Task<GeminiExtractionResult> ProcessWithGeminiAsync(string userMessage, List<ChatMessage>? history, DateTime currentDate)
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
   - 'amount' must be a plain positive numeric value without symbols or commas (e.g. 500, 25000).
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
   - 'set_goal': User wants to create or set a saving goal/target.
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
   - 'unknown': Chit-chat, greeting (e.g. 'vanakkam', 'hi da', 'epdi irukka'), general questions, or unclear commands.
6. Validation & Missing Fields Rules:
   - If required fields are missing from the current input and cannot be inferred from recent context, list the missing field names in 'missing_fields' (e.g., ['amount'] or ['item']) and ask a friendly clarifying follow-up question in the user's language and style in 'reply'.
   - NEVER guess or convert an amount that was not clearly stated. If the amount is unclear or missing, ask for it in the user's language instead of guessing.
   - Categories must be mapped to one of: 'Food & Dining', 'Travel & Commute', 'Fuel & Vehicle', 'Shopping', 'Entertainment & Movies', 'Bills & Utilities', 'Subscriptions', 'Health & Medical', 'Education', 'Other'.
   - Use recent conversation history to resolve follow-ups (e.g. User says 'oru phone suggest pannu', Bot asks 'Budget evlo?', User answers '20000').
   - Always output strict JSON matching the schema.";

            var contents = new List<object>();

            if (history != null && history.Count > 0)
            {
                var recentHistory = history.TakeLast(4).ToList();
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
                                @enum = new[] { "add_expense", "set_goal", "add_savings", "suggest_products", "unknown" }
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
            return await CallGeminiExtractionWithRetryAsync(url, requestBody);
        }

        public async Task<ProductSuggestionResult> SuggestProductsAsync(string item, decimal budget)
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

            // 1. ATTEMPT GROUNDED SEARCH
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

            var groundedOutcome = await ExecuteGroundedSearchAsync(url, groundedRequestBody);

            if (groundedOutcome.Success)
            {
                // Step B: Convert grounded text to JSON
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

                var rawJson = await CallGeminiRawStringWithRetryAsync(url, conversionRequestBody);
                if (!string.IsNullOrWhiteSpace(rawJson))
                {
                    try
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
                        else
                        {
                            // Grounded search ran, but no valid products found within budget
                            return new ProductSuggestionResult
                            {
                                Status = ProductSuggestionStatus.NoProductsFound,
                                Suggestions = new List<ProductSuggestion>(),
                                IsLive = true
                            };
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to parse grounded search conversion JSON.");
                    }
                }
            }

            // 2. FALLBACK: Grounded search failed (429, 5xx, or network error).
            // Make a standard Gemini call WITHOUT search tool
            _logger.LogInformation("Grounded search failed (Reason: {Reason}). Proceeding to ungrounded fallback for '{Item}', budget {Budget}.",
                groundedOutcome.FailureReason, item, budget);

            return await ExecuteFallbackSuggestionsAsync(url, item, budget);
        }

        private class GroundedOutcome
        {
            public bool Success { get; set; }
            public string Text { get; set; } = string.Empty;
            public List<string> GroundingUrls { get; set; } = new();
            public string FailureReason { get; set; } = string.Empty;
        }

        private async Task<GroundedOutcome> ExecuteGroundedSearchAsync(string url, object requestPayload)
        {
            var jsonPayload = JsonSerializer.Serialize(requestPayload);

            for (int attempt = 1; attempt <= 2; attempt++)
            {
                try
                {
                    using var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
                    var response = await _httpClient.PostAsync(url, content);

                    if (response.StatusCode == HttpStatusCode.TooManyRequests)
                    {
                        var errorBody = await response.Content.ReadAsStringAsync();
                        var parsedErr = ExtractSafeErrorMessage(errorBody);
                        _logger.LogWarning("Gemini grounded search returned 429 Too Many Requests (attempt {Attempt}/2). Status: 429, Message: {Message}",
                            attempt, parsedErr);

                        // Check retry hint
                        var retryDelay = ExtractRetryDelay(response, errorBody);
                        if (attempt == 1 && retryDelay > 0 && retryDelay <= 5)
                        {
                            _logger.LogInformation("Retrying grounded search after hint delay of {Delay}s", retryDelay);
                            await Task.Delay(TimeSpan.FromSeconds(retryDelay));
                            continue;
                        }

                        // Do not retry indefinitely or wait > 5s: go to fallback
                        return new GroundedOutcome { Success = false, FailureReason = "429 RateLimit" };
                    }

                    if ((int)response.StatusCode >= 500)
                    {
                        _logger.LogWarning("Gemini grounded search returned server error {StatusCode} (attempt {Attempt}/2).", response.StatusCode, attempt);
                        if (attempt == 1)
                        {
                            await Task.Delay(1500);
                            continue;
                        }
                        return new GroundedOutcome { Success = false, FailureReason = $"{response.StatusCode} Overloaded" };
                    }

                    if (!response.IsSuccessStatusCode)
                    {
                        var errorBody = await response.Content.ReadAsStringAsync();
                        var safeMsg = ExtractSafeErrorMessage(errorBody);
                        _logger.LogError("Gemini grounded search failed with status {StatusCode}: {Error}", response.StatusCode, safeMsg);
                        return new GroundedOutcome { Success = false, FailureReason = $"{response.StatusCode} Error" };
                    }

                    var responseBody = await response.Content.ReadAsStringAsync();
                    var (text, urls) = ParseGroundedSearchResponse(responseBody);
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        return new GroundedOutcome { Success = false, FailureReason = "EmptyResponse" };
                    }

                    return new GroundedOutcome
                    {
                        Success = true,
                        Text = text,
                        GroundingUrls = urls
                    };
                }
                catch (HttpRequestException ex)
                {
                    _logger.LogWarning(ex, "Network error during Gemini grounded search (attempt {Attempt}/2).", attempt);
                    if (attempt == 1)
                    {
                        await Task.Delay(1500);
                        continue;
                    }
                    return new GroundedOutcome { Success = false, FailureReason = "NetworkError" };
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unexpected error in grounded search.");
                    return new GroundedOutcome { Success = false, FailureReason = "Exception" };
                }
            }

            return new GroundedOutcome { Success = false, FailureReason = "FailedAttempts" };
        }

        private async Task<ProductSuggestionResult> ExecuteFallbackSuggestionsAsync(string url, string item, decimal budget)
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

            var rawJson = await CallGeminiRawStringWithRetryAsync(url, fallbackRequestBody);

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

                // Discard if approxPrice > budget or approxPrice <= 0
                if (dto.ApproxPrice <= 0 || dto.ApproxPrice > budget) continue;

                // Ensure reason is max 15 words
                var reason = (dto.Reason ?? string.Empty).Trim();
                var words = reason.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (words.Length > 15)
                {
                    reason = string.Join(" ", words.Take(15)) + "...";
                }

                // In fallback mode (isLive == false) or if ungrounded, sourceUrl MUST be null
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
            // 1. Check HTTP Retry-After header
            if (response.Headers.RetryAfter?.Delta.HasValue == true)
            {
                return (int)response.Headers.RetryAfter.Delta.Value.TotalSeconds;
            }

            // 2. Parse JSON response for retryDelay (e.g. "4s" or 4)
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

        private async Task<string> CallGeminiRawStringWithRetryAsync(string url, object requestPayload)
        {
            var jsonPayload = JsonSerializer.Serialize(requestPayload);

            for (int attempt = 1; attempt <= 2; attempt++)
            {
                try
                {
                    using var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
                    var response = await _httpClient.PostAsync(url, content);

                    if (response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500)
                    {
                        var errBody = await response.Content.ReadAsStringAsync();
                        var safeMsg = ExtractSafeErrorMessage(errBody);
                        _logger.LogWarning("Gemini API call returned {StatusCode} (attempt {Attempt}/2). Message: {Message}",
                            response.StatusCode, attempt, safeMsg);

                        if (attempt == 1)
                        {
                            await Task.Delay(2000);
                            continue;
                        }
                        return string.Empty;
                    }

                    if (!response.IsSuccessStatusCode)
                    {
                        var errorContent = await response.Content.ReadAsStringAsync();
                        var safeMsg = ExtractSafeErrorMessage(errorContent);
                        _logger.LogError("Gemini API call failed with status {StatusCode}: {Error}", response.StatusCode, safeMsg);
                        return string.Empty;
                    }

                    var responseBody = await response.Content.ReadAsStringAsync();
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
                catch (HttpRequestException ex)
                {
                    _logger.LogWarning(ex, "Network error during Gemini raw call (attempt {Attempt}/2).", attempt);
                    if (attempt == 1)
                    {
                        await Task.Delay(1500);
                        continue;
                    }
                    return string.Empty;
                }
            }

            return string.Empty;
        }

        private async Task<GeminiExtractionResult> CallGeminiExtractionWithRetryAsync(string url, object requestPayload)
        {
            var jsonPayload = JsonSerializer.Serialize(requestPayload);

            for (int attempt = 1; attempt <= 2; attempt++)
            {
                try
                {
                    using var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
                    var response = await _httpClient.PostAsync(url, content);

                    if (response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500)
                    {
                        var errBody = await response.Content.ReadAsStringAsync();
                        var safeMsg = ExtractSafeErrorMessage(errBody);
                        _logger.LogWarning("Gemini API returned {StatusCode} (attempt {Attempt}/2). Message: {Message}",
                            response.StatusCode, attempt, safeMsg);

                        if (attempt == 1)
                        {
                            await Task.Delay(2000);
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
                        var errorContent = await response.Content.ReadAsStringAsync();
                        var safeMsg = ExtractSafeErrorMessage(errorContent);
                        _logger.LogError("Gemini API call failed with status {StatusCode}: {Error}", response.StatusCode, safeMsg);
                        return new GeminiExtractionResult
                        {
                            Intent = "unknown",
                            Reply = "The AI service is busy right now. Please try again in a minute.",
                            MissingFields = new List<string>()
                        };
                    }

                    var responseBody = await response.Content.ReadAsStringAsync();
                    return ParseGeminiResponse(responseBody);
                }
                catch (HttpRequestException ex)
                {
                    _logger.LogError(ex, "Network error occurred while calling Gemini API (attempt {Attempt}/2).", attempt);
                    if (attempt == 1)
                    {
                        await Task.Delay(1500);
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
