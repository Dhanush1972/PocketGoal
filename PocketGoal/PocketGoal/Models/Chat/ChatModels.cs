using System.Text.Json.Serialization;

namespace PocketGoal.Models.Chat
{
    public class ChatMessage
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = "user"; // "user" or "model"

        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;
    }

    public class ChatRequest
    {
        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;

        [JsonPropertyName("history")]
        public List<ChatMessage>? History { get; set; }
    }

    public class ProductSuggestion
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("approxPrice")]
        public decimal ApproxPrice { get; set; }

        [JsonPropertyName("reason")]
        public string Reason { get; set; } = string.Empty;

        [JsonPropertyName("sourceUrl")]
        public string? SourceUrl { get; set; }

        [JsonPropertyName("searchUrl")]
        public string SearchUrl { get; set; } = string.Empty;

        [JsonPropertyName("flipkartUrl")]
        public string? FlipkartUrl { get; set; }
    }

    public enum ProductSuggestionStatus
    {
        Success,
        NoProductsFound,
        RateLimited,
        Overloaded,
        Failed
    }

    public class ProductSuggestionResult
    {
        public ProductSuggestionStatus Status { get; set; }
        public List<ProductSuggestion> Suggestions { get; set; } = new();
        public bool IsLive { get; set; } = true;
        public string? ErrorMessage { get; set; }
    }

    public class ChatResponse
    {
        [JsonPropertyName("saved")]
        public bool Saved { get; set; }

        [JsonPropertyName("reply")]
        public string Reply { get; set; } = string.Empty;

        [JsonPropertyName("record")]
        public object? Record { get; set; }

        [JsonPropertyName("suggestions")]
        public List<ProductSuggestion>? Suggestions { get; set; }

        [JsonPropertyName("isLive")]
        public bool? IsLive { get; set; }
    }

    public class CreateGoalFromSuggestionRequest
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("price")]
        public decimal Price { get; set; }
    }

    public class CreateGoalFromSuggestionResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;

        [JsonPropertyName("goalId")]
        public Guid? GoalId { get; set; }
    }

    public class GeminiExtractionResult
    {
        [JsonPropertyName("intent")]
        public string Intent { get; set; } = "unknown"; // "add_expense" | "set_goal" | "add_savings" | "suggest_products" | "unknown"

        [JsonPropertyName("item")]
        public string? Item { get; set; }

        [JsonPropertyName("amount")]
        public decimal? Amount { get; set; }

        [JsonPropertyName("category")]
        public string? Category { get; set; }

        [JsonPropertyName("date")]
        public string? Date { get; set; } // YYYY-MM-DD

        [JsonPropertyName("current_savings")]
        public decimal? CurrentSavings { get; set; }

        [JsonPropertyName("missing_fields")]
        public List<string> MissingFields { get; set; } = new();

        [JsonPropertyName("reply")]
        public string Reply { get; set; } = string.Empty;
    }
}
