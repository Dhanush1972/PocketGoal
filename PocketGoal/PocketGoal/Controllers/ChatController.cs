using Microsoft.AspNetCore.Mvc;
using PocketGoal.Models;
using PocketGoal.Models.Chat;
using PocketGoal.Services;
using PocketGoal.ViewModels;

namespace PocketGoal.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ChatController : ControllerBase
    {
        private readonly IChatBotService _chatBotService;
        private readonly ISavingGoalService _savingGoalService;
        private readonly IProfileContextService _profileContext;
        private readonly ILogger<ChatController> _logger;

        public ChatController(
            IChatBotService chatBotService,
            ISavingGoalService savingGoalService,
            IProfileContextService profileContext,
            ILogger<ChatController> logger)
        {
            _chatBotService = chatBotService;
            _savingGoalService = savingGoalService;
            _profileContext = profileContext;
            _logger = logger;
        }

        [HttpPost]
        public async Task<IActionResult> Post([FromBody] ChatRequest request)
        {
            var userId = _profileContext.GetCurrentUserId();
            if (!userId.HasValue)
            {
                return Unauthorized(new ChatResponse
                {
                    Saved = false,
                    Reply = "Please log in to your PocketGoal account to add expenses, set goals, or record savings."
                });
            }

            if (request == null || string.IsNullOrWhiteSpace(request.Message))
            {
                return BadRequest(new ChatResponse
                {
                    Saved = false,
                    Reply = "Please enter a message to get started."
                });
            }

            try
            {
                var response = await _chatBotService.ProcessChatAsync(userId.Value, request);
                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception while processing chat message for user {UserId}", userId.Value);
                return StatusCode(500, new ChatResponse
                {
                    Saved = false,
                    Reply = "An unexpected error occurred while processing your request. Please try again."
                });
            }
        }

        [HttpPost("create-goal-from-suggestion")]
        public async Task<IActionResult> CreateGoalFromSuggestion([FromBody] CreateGoalFromSuggestionRequest request)
        {
            var userId = _profileContext.GetCurrentUserId();
            if (!userId.HasValue)
            {
                return Unauthorized(new CreateGoalFromSuggestionResponse
                {
                    Success = false,
                    Message = "Please log in to your PocketGoal account to set a savings goal."
                });
            }

            if (request == null || string.IsNullOrWhiteSpace(request.Name))
            {
                return BadRequest(new CreateGoalFromSuggestionResponse
                {
                    Success = false,
                    Message = "Product name is required."
                });
            }

            var trimmedName = request.Name.Trim();
            if (trimmedName.Length > 150)
            {
                return BadRequest(new CreateGoalFromSuggestionResponse
                {
                    Success = false,
                    Message = "Product name cannot exceed 150 characters."
                });
            }

            if (request.Price <= 0 || request.Price > 100000000m)
            {
                return BadRequest(new CreateGoalFromSuggestionResponse
                {
                    Success = false,
                    Message = "Product price must be greater than 0 and under ₹10,00,00,000."
                });
            }

            try
            {
                var targetDate = DateTime.SpecifyKind(DateTime.UtcNow.AddMonths(6), DateTimeKind.Utc);
                var goal = await _savingGoalService.CreateGoalAsync(userId.Value, new SavingGoalCreateEditViewModel
                {
                    GoalName = trimmedName,
                    TargetAmount = request.Price,
                    TargetDate = targetDate,
                    Description = "Created from PocketGoal AI product suggestion",
                    Status = GoalStatus.Active
                });

                return Ok(new CreateGoalFromSuggestionResponse
                {
                    Success = true,
                    Message = $"Goal '{goal.GoalName}' set for ₹{goal.TargetAmount:N0} by {goal.TargetDate:MMM yyyy}! 🎯",
                    GoalId = goal.Id
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create goal from suggestion for user {UserId}", userId.Value);
                return StatusCode(500, new CreateGoalFromSuggestionResponse
                {
                    Success = false,
                    Message = "An error occurred while creating your savings goal. Please try again."
                });
            }
        }
    }
}
