using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PocketGoal.Models
{
    public class SavingGoal
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public Guid UserId { get; set; }

        [ForeignKey(nameof(UserId))]
        public virtual UserProfile? User { get; set; }

        [Required(ErrorMessage = "Goal name is required")]
        [StringLength(150, ErrorMessage = "Goal name cannot exceed 150 characters")]
        public string GoalName { get; set; } = string.Empty;

        [StringLength(500)]
        public string? Description { get; set; }

        [Required(ErrorMessage = "Target amount is required")]
        [Range(1, 1000000000, ErrorMessage = "Target amount must be greater than 0")]
        [Column(TypeName = "decimal(18,2)")]
        public decimal TargetAmount { get; set; }

        [Required(ErrorMessage = "Target date is required")]
        public DateTime TargetDate { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal CurrentSavedAmount { get; set; } = 0m;

        public GoalStatus Status { get; set; } = GoalStatus.Active;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public virtual ICollection<Saving> Savings { get; set; } = new List<Saving>();

        // Computed helper properties (not mapped directly or calculated dynamically)
        [NotMapped]
        public decimal RemainingAmount => Math.Max(0m, TargetAmount - CurrentSavedAmount);

        [NotMapped]
        public decimal ProgressPercentage => TargetAmount > 0 ? Math.Min(100m, Math.Round((CurrentSavedAmount / TargetAmount) * 100m, 2)) : 0m;

        [NotMapped]
        public bool IsCompleted => CurrentSavedAmount >= TargetAmount || Status == GoalStatus.Completed;
    }
}
