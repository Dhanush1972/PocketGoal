using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PocketGoal.Models
{
    public class Saving
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public Guid SavingGoalId { get; set; }

        [ForeignKey(nameof(SavingGoalId))]
        public virtual SavingGoal? SavingGoal { get; set; }

        [Required(ErrorMessage = "Amount is required")]
        [Range(0.01, 1000000000, ErrorMessage = "Amount must be greater than 0")]
        [Column(TypeName = "decimal(18,2)")]
        public decimal Amount { get; set; }

        [Required(ErrorMessage = "Saved date is required")]
        public DateTime SavedDate { get; set; } = DateTime.UtcNow;

        [StringLength(250)]
        public string? Notes { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
