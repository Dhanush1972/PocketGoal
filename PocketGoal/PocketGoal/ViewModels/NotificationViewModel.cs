using PocketGoal.Models;

namespace PocketGoal.ViewModels
{
    public class NotificationCenterViewModel
    {
        public UserProfile User { get; set; } = null!;
        public List<NotificationPreviewItem> GeneratedReminders { get; set; } = new();
        public bool EmailNotificationsEnabled { get; set; } = true;
        public bool SmsNotificationsEnabled { get; set; } = true;
        public string EmailProviderName { get; set; } = "Configured System Provider (SMTP/Supabase)";
        public string SmsProviderName { get; set; } = "Configured SMS Provider";
        public string? SuccessMessage { get; set; }
    }

    public class NotificationPreviewItem
    {
        public Guid GoalId { get; set; }
        public string GoalName { get; set; } = string.Empty;
        public NotificationType Type { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public string MessageBody { get; set; } = string.Empty;
        public string SmsBody { get; set; } = string.Empty;
        public string UrgencyBadge { get; set; } = "bg-info";
        public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    }

    public class TriggerNotificationRequest
    {
        public Guid GoalId { get; set; }
        public NotificationType Type { get; set; }
        public NotificationChannel Channel { get; set; } = NotificationChannel.Both;
    }
}
