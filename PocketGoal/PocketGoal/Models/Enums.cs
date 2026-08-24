namespace PocketGoal.Models
{
    public enum GoalStatus
    {
        Active = 1,
        Completed = 2,
        Paused = 3,
        Cancelled = 4
    }

    public enum GoalPaceStatus
    {
        OnTrack = 1,
        Behind = 2,
        Completed = 3,
        Overdue = 4
    }

    public enum NotificationType
    {
        MonthlyReminder = 1,
        BehindPaceAlert = 2,
        GoalCompleted = 3,
        UpcomingDeadline = 4
    }

    public enum NotificationChannel
    {
        Email = 1,
        Sms = 2,
        Both = 3
    }
}
