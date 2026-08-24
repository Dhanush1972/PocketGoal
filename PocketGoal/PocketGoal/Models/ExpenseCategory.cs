using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PocketGoal.Models
{
    public class ExpenseCategory
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        // Nullable UserId: if null, it's a default/system category available to everyone
        public Guid? UserId { get; set; }

        [ForeignKey(nameof(UserId))]
        public virtual UserProfile? User { get; set; }

        [Required(ErrorMessage = "Category name is required")]
        [StringLength(80)]
        public string Name { get; set; } = string.Empty;

        [StringLength(50)]
        public string? Icon { get; set; } = "bi-tag";

        [StringLength(20)]
        public string? ColorHex { get; set; } = "#4f46e5";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation
        public virtual ICollection<Expense> Expenses { get; set; } = new List<Expense>();
    }
}
