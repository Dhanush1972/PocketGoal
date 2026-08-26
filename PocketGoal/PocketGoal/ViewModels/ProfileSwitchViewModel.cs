using System.ComponentModel.DataAnnotations;
using PocketGoal.Models;

namespace PocketGoal.ViewModels
{
    public class ProfileSwitchViewModel
    {
        public List<UserProfile> AvailableProfiles { get; set; } = new List<UserProfile>();
        public Guid? ActiveProfileId { get; set; }

        // For switching to a listed profile
        public Guid? SelectedProfileId { get; set; }

        [DataType(DataType.Password)]
        public string? SwitchPassword { get; set; }

        // For direct lookup & login by Email or Phone
        [Display(Name = "Email or Phone Number")]
        public string? EmailOrPhone { get; set; }

        [DataType(DataType.Password)]
        [Display(Name = "Password")]
        public string? LookupPassword { get; set; }
    }
}
