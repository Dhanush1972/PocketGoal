using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace PocketGoal.ViewModels
{
    public class ExpenseItemViewModel
    {
        public Guid Id { get; set; }
        public Guid CategoryId { get; set; }
        public string CategoryName { get; set; } = string.Empty;
        public string CategoryIcon { get; set; } = "bi-tag";
        public string CategoryColor { get; set; } = "#4f46e5";
        public decimal Amount { get; set; }
        public DateTime ExpenseDate { get; set; }
        public string? Description { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class ExpenseIndexViewModel
    {
        public List<ExpenseItemViewModel> Expenses { get; set; } = new();
        public List<ExpenseCategorySummaryViewModel> CategorySummaries { get; set; } = new();
        public decimal CurrentMonthTotal { get; set; }
        public decimal PreviousMonthTotal { get; set; }
        public decimal TodayTotal { get; set; }
        public decimal AllTimeTotal { get; set; }
        public int SelectedMonth { get; set; } = DateTime.UtcNow.Month;
        public int SelectedYear { get; set; } = DateTime.UtcNow.Year;
        public Guid? SelectedCategoryId { get; set; }
        public List<SelectListItem> CategoryOptions { get; set; } = new();
    }

    public class ExpenseCreateEditViewModel
    {
        public Guid? Id { get; set; }

        [Required(ErrorMessage = "Please select a category")]
        [Display(Name = "Expense Category")]
        public Guid CategoryId { get; set; }

        [Required(ErrorMessage = "Please enter an amount")]
        [Range(1, 1000000000, ErrorMessage = "Amount must be at least ₹1")]
        [Display(Name = "Amount (₹)")]
        public decimal Amount { get; set; }

        [Required(ErrorMessage = "Expense date is required")]
        [Display(Name = "Date of Expense")]
        public DateTime ExpenseDate { get; set; } = DateTime.UtcNow;

        [Display(Name = "Description / Note (Optional)")]
        [StringLength(250)]
        public string? Description { get; set; }

        public List<SelectListItem> CategoryOptions { get; set; } = new();
    }

    public class ExpenseCategorySummaryViewModel
    {
        public Guid CategoryId { get; set; }
        public string CategoryName { get; set; } = string.Empty;
        public string CategoryIcon { get; set; } = "bi-tag";
        public string CategoryColor { get; set; } = "#4f46e5";
        public decimal TotalAmount { get; set; }
        public int ExpenseCount { get; set; }
        public decimal PercentageOfTotal { get; set; }
    }

    public class CreateCategoryViewModel
    {
        [Required(ErrorMessage = "Category name is required")]
        [StringLength(80)]
        [Display(Name = "Category Name")]
        public string Name { get; set; } = string.Empty;

        [Display(Name = "Icon")]
        public string Icon { get; set; } = "bi-tag";

        [Display(Name = "Color Theme")]
        public string ColorHex { get; set; } = "#4f46e5";
    }
}
