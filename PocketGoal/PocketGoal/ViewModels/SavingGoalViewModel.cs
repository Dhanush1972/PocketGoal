using System.ComponentModel.DataAnnotations;
using PocketGoal.Models;

namespace PocketGoal.ViewModels
{
    public class SavingGoalViewModel
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }

        [Required(ErrorMessage = "Goal name is required")]
        [Display(Name = "Goal / Item Name")]
        [StringLength(150)]
        public string GoalName { get; set; } = string.Empty;

        [Display(Name = "Description / Purpose")]
        [StringLength(500)]
        public string? Description { get; set; }

        [Required(ErrorMessage = "Target amount is required")]
        [Range(1, 1000000000, ErrorMessage = "Target amount must be greater than 0")]
        [Display(Name = "Target Amount (₹)")]
        public decimal TargetAmount { get; set; }

        [Required(ErrorMessage = "Target date is required")]
        [Display(Name = "Target Date")]
        public DateTime TargetDate { get; set; }

        [Display(Name = "Total Saved (₹)")]
        public decimal CurrentSavedAmount { get; set; }

        public GoalStatus Status { get; set; } = GoalStatus.Active;
        public GoalPaceStatus PaceStatus { get; set; } = GoalPaceStatus.OnTrack;

        public decimal RemainingAmount => Math.Max(0m, TargetAmount - CurrentSavedAmount);
        public decimal ProgressPercentage => TargetAmount > 0 ? Math.Min(100m, Math.Round((CurrentSavedAmount / TargetAmount) * 100m, 2)) : 0m;
        public bool IsCompleted => CurrentSavedAmount >= TargetAmount || Status == GoalStatus.Completed;

        // Calculations based on remaining duration
        public decimal RequiredMonthlySaving { get; set; }
        public decimal RequiredWeeklySaving { get; set; }
        public decimal RequiredDailySaving { get; set; }

        public int DaysRemaining { get; set; }
        public double MonthsRemaining { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        public List<SavingTransactionViewModel> SavingsHistory { get; set; } = new();
    }

    public class SavingGoalCreateEditViewModel
    {
        public Guid? Id { get; set; }

        [Required(ErrorMessage = "Goal name is required")]
        [Display(Name = "Goal Name")]
        [StringLength(150)]
        public string GoalName { get; set; } = string.Empty;

        [Display(Name = "Description (Optional)")]
        [StringLength(500)]
        public string? Description { get; set; }

        [Required(ErrorMessage = "Target amount is required")]
        [Range(1, 1000000000, ErrorMessage = "Target amount must be at least ₹1")]
        [Display(Name = "Target Amount (₹)")]
        public decimal TargetAmount { get; set; }

        [Required(ErrorMessage = "Target date is required")]
        [Display(Name = "Target Date")]
        public DateTime TargetDate { get; set; } = DateTime.UtcNow.AddMonths(4);

        [Display(Name = "Current Status")]
        public GoalStatus Status { get; set; } = GoalStatus.Active;
    }

    public class SavingTransactionViewModel
    {
        public Guid Id { get; set; }
        public Guid SavingGoalId { get; set; }
        public string GoalName { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public DateTime SavedDate { get; set; }
        public string? Notes { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class AddSavingViewModel
    {
        [Required]
        public Guid SavingGoalId { get; set; }

        public string GoalName { get; set; } = string.Empty;
        public decimal TargetAmount { get; set; }
        public decimal CurrentSavedAmount { get; set; }
        public decimal RemainingAmount => Math.Max(0m, TargetAmount - CurrentSavedAmount);

        [Required(ErrorMessage = "Please enter the deposit amount")]
        [Range(1, 1000000000, ErrorMessage = "Deposit amount must be at least ₹1")]
        [Display(Name = "Amount to Save (₹)")]
        public decimal Amount { get; set; }

        [Required(ErrorMessage = "Saved date is required")]
        [Display(Name = "Date of Saving")]
        public DateTime SavedDate { get; set; } = DateTime.UtcNow;

        [Display(Name = "Notes / Source of Money (Optional)")]
        [StringLength(250)]
        public string? Notes { get; set; }
    }
}
