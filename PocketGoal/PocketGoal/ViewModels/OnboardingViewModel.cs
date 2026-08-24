using System.ComponentModel.DataAnnotations;

namespace PocketGoal.ViewModels
{
    public class OnboardingViewModel
    {
        [Required(ErrorMessage = "Please enter your name")]
        [Display(Name = "Your Name")]
        [StringLength(100, MinimumLength = 2, ErrorMessage = "Name must be between 2 and 100 characters")]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessage = "Please enter your email address")]
        [EmailAddress(ErrorMessage = "Please enter a valid email address")]
        [Display(Name = "Email Address")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Please enter your phone number")]
        [Display(Name = "Phone Number")]
        [RegularExpression(@"^[0-9+\-\s]{8,15}$", ErrorMessage = "Please enter a valid phone number")]
        public string PhoneNumber { get; set; } = string.Empty;

        [Required(ErrorMessage = "What are you planning to buy?")]
        [Display(Name = "What are you planning to buy?")]
        [StringLength(150, ErrorMessage = "Goal name cannot exceed 150 characters")]
        public string GoalName { get; set; } = string.Empty;

        [Display(Name = "Notes / Description (Optional)")]
        public string? GoalDescription { get; set; }

        [Required(ErrorMessage = "Please enter the target price")]
        [Range(100, 1000000000, ErrorMessage = "Target price must be at least ₹100")]
        [Display(Name = "Target Price (₹)")]
        public decimal TargetPrice { get; set; }

        [Required(ErrorMessage = "Please select or enter the saving period")]
        [Range(1, 120, ErrorMessage = "Saving period must be between 1 and 120 months")]
        [Display(Name = "Saving Period (in Months)")]
        public int SavingPeriodMonths { get; set; } = 4;

        [Display(Name = "Initial Savings (₹)")]
        [Range(0, 1000000000, ErrorMessage = "Initial savings cannot be negative")]
        public decimal InitialSavings { get; set; } = 0m;
    }
}
