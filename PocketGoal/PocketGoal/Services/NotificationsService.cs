using Microsoft.EntityFrameworkCore;
using PocketGoal.Data;
using PocketGoal.Models;
using PocketGoal.ViewModels;

namespace PocketGoal.Services
{
    public interface INotificationsService
    {
        Task<bool> SendEmailNotificationAsync(string recipientEmail, string subject, string bodyHtml);
        Task<bool> SendSmsNotificationAsync(string recipientPhone, string message);
        Task SendGoalCompletedAlertAsync(UserProfile user, SavingGoal goal);
        Task SendBehindPaceAlertAsync(UserProfile user, SavingGoal goal, decimal requiredMonthly);
        Task SendMonthlyReminderAsync(UserProfile user, SavingGoal goal, decimal requiredMonthly);
        Task SendUpcomingDeadlineReminderAsync(UserProfile user, SavingGoal goal, int daysRemaining, decimal remainingAmount);
        Task<List<NotificationPreviewItem>> GenerateActiveRemindersForUserAsync(Guid userId);
        Task<(bool Success, string Message)> TriggerSimulatedNotificationAsync(Guid userId, TriggerNotificationRequest request);
    }

    public class NotificationsService : INotificationsService
    {
        private readonly IConfiguration _configuration;
        private readonly ApplicationDbContext _dbContext;
        private readonly ILogger<NotificationsService> _logger;

        public NotificationsService(
            IConfiguration configuration,
            ApplicationDbContext dbContext,
            ILogger<NotificationsService> logger)
        {
            _configuration = configuration;
            _dbContext = dbContext;
            _logger = logger;
        }

        public async Task<bool> SendEmailNotificationAsync(string recipientEmail, string subject, string bodyHtml)
        {
            try
            {
                // In production, configure SMTP / SendGrid / Postmark / Supabase Auth Mailer
                // We securely log the simulated email dispatch without leaking secrets.
                _logger.LogInformation("[PocketGoal Email Dispatch] To: {Email} | Subject: {Subject}", recipientEmail, subject);
                await Task.CompletedTask;
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send email to {Email}", recipientEmail);
                return false;
            }
        }

        public async Task<bool> SendSmsNotificationAsync(string recipientPhone, string message)
        {
            try
            {
                // In production, configure Twilio / AWS SNS / Textlocal / Fast2SMS
                _logger.LogInformation("[PocketGoal SMS Dispatch] To: {Phone} | Message: {Message}", recipientPhone, message);
                await Task.CompletedTask;
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send SMS to {Phone}", recipientPhone);
                return false;
            }
        }

        public async Task SendGoalCompletedAlertAsync(UserProfile user, SavingGoal goal)
        {
            var subject = $"🎉 Congratulations! You reached your goal for {goal.GoalName}!";
            var emailBody = $@"
                <div style='font-family: sans-serif; max-width: 600px; padding: 20px; border: 1px solid #e2e8f0; border-radius: 12px;'>
                    <h2 style='color: #10b981;'>🎉 Goal Achieved!</h2>
                    <p>Hi <strong>{user.Name}</strong>,</p>
                    <p>Incredible news! You have successfully saved <strong>₹{goal.CurrentSavedAmount:N2}</strong> for your goal <strong>{goal.GoalName}</strong>.</p>
                    <p>Your target amount of ₹{goal.TargetAmount:N2} is now 100% funded. It's time to celebrate and make your purchase!</p>
                    <hr style='border: none; border-top: 1px solid #e2e8f0; margin: 20px 0;'/>
                    <p style='color: #64748b; font-size: 13px;'>PocketGoal - Personal Finance & Savings Tracker</p>
                </div>";

            var smsBody = $"PocketGoal: 🎉 Congrats {user.Name}! You have reached your saving goal of ₹{goal.TargetAmount:N0} for '{goal.GoalName}'! 100% completed!";

            await SendEmailNotificationAsync(user.Email, subject, emailBody);
            await SendSmsNotificationAsync(user.PhoneNumber, smsBody);
        }

        public async Task SendBehindPaceAlertAsync(UserProfile user, SavingGoal goal, decimal requiredMonthly)
        {
            var subject = $"⚠️ Savings Alert: Stay on track for your {goal.GoalName}";
            var emailBody = $@"
                <div style='font-family: sans-serif; max-width: 600px; padding: 20px; border: 1px solid #e2e8f0; border-radius: 12px;'>
                    <h2 style='color: #f59e0b;'>⚠️ Goal Pace Alert</h2>
                    <p>Hi <strong>{user.Name}</strong>,</p>
                    <p>You're slightly behind pace on your saving goal for <strong>{goal.GoalName}</strong>.</p>
                    <p>To reach your target of ₹{goal.TargetAmount:N2} by {goal.TargetDate:MMM dd, yyyy}, we recommend setting aside <strong>₹{requiredMonthly:N2}/month</strong>.</p>
                    <p>Every small deposit counts towards your target!</p>
                    <hr style='border: none; border-top: 1px solid #e2e8f0; margin: 20px 0;'/>
                    <p style='color: #64748b; font-size: 13px;'>PocketGoal - Personal Finance & Savings Tracker</p>
                </div>";

            var smsBody = $"PocketGoal Alert: To stay on track for '{goal.GoalName}' by {goal.TargetDate:MMM yyyy}, save ₹{requiredMonthly:N0} this month. Keep going!";

            await SendEmailNotificationAsync(user.Email, subject, emailBody);
            await SendSmsNotificationAsync(user.PhoneNumber, smsBody);
        }

        public async Task SendMonthlyReminderAsync(UserProfile user, SavingGoal goal, decimal requiredMonthly)
        {
            var subject = $"📅 Monthly Savings Reminder for {goal.GoalName}";
            var emailBody = $@"
                <div style='font-family: sans-serif; max-width: 600px; padding: 20px; border: 1px solid #e2e8f0; border-radius: 12px;'>
                    <h2 style='color: #3b82f6;'>📅 Monthly Savings Check-in</h2>
                    <p>Hi <strong>{user.Name}</strong>,</p>
                    <p>This is your friendly monthly reminder for your purchase goal <strong>{goal.GoalName}</strong>.</p>
                    <p>Recommended monthly deposit: <strong>₹{requiredMonthly:N2}</strong>.</p>
                    <p>Current progress: ₹{goal.CurrentSavedAmount:N2} of ₹{goal.TargetAmount:N2} ({(goal.TargetAmount > 0 ? (goal.CurrentSavedAmount / goal.TargetAmount * 100m) : 0):N1}%).</p>
                    <hr style='border: none; border-top: 1px solid #e2e8f0; margin: 20px 0;'/>
                    <p style='color: #64748b; font-size: 13px;'>PocketGoal - Personal Finance & Savings Tracker</p>
                </div>";

            var smsBody = $"PocketGoal Reminder: Save ₹{requiredMonthly:N0} this month for '{goal.GoalName}'. You are already ₹{goal.CurrentSavedAmount:N0} saved!";

            await SendEmailNotificationAsync(user.Email, subject, emailBody);
            await SendSmsNotificationAsync(user.PhoneNumber, smsBody);
        }

        public async Task SendUpcomingDeadlineReminderAsync(UserProfile user, SavingGoal goal, int daysRemaining, decimal remainingAmount)
        {
            var subject = $"⏳ Upcoming Deadline: {goal.GoalName} target date in {daysRemaining} days";
            var emailBody = $@"
                <div style='font-family: sans-serif; max-width: 600px; padding: 20px; border: 1px solid #e2e8f0; border-radius: 12px;'>
                    <h2 style='color: #6366f1;'>⏳ Target Deadline Approaching</h2>
                    <p>Hi <strong>{user.Name}</strong>,</p>
                    <p>Your target date for <strong>{goal.GoalName}</strong> is approaching in <strong>{daysRemaining} days</strong> ({goal.TargetDate:MMM dd, yyyy}).</p>
                    <p>Remaining amount needed to complete goal: <strong>₹{remainingAmount:N2}</strong>.</p>
                    <hr style='border: none; border-top: 1px solid #e2e8f0; margin: 20px 0;'/>
                    <p style='color: #64748b; font-size: 13px;'>PocketGoal - Personal Finance & Savings Tracker</p>
                </div>";

            var smsBody = $"PocketGoal: Only {daysRemaining} days left until your '{goal.GoalName}' target date! Remaining amount: ₹{remainingAmount:N0}.";

            await SendEmailNotificationAsync(user.Email, subject, emailBody);
            await SendSmsNotificationAsync(user.PhoneNumber, smsBody);
        }

        public async Task<List<NotificationPreviewItem>> GenerateActiveRemindersForUserAsync(Guid userId)
        {
            var goals = await _dbContext.SavingGoals
                .Include(g => g.Savings)
                .Where(g => g.UserId == userId && g.Status == GoalStatus.Active)
                .ToListAsync();

            var list = new List<NotificationPreviewItem>();
            var now = DateTime.UtcNow;

            foreach (var goal in goals)
            {
                var totalSaved = goal.Savings.Sum(s => s.Amount);
                var remaining = Math.Max(0m, goal.TargetAmount - totalSaved);
                var daysRemaining = (int)Math.Max(0, (goal.TargetDate.Date - now.Date).TotalDays);
                var monthsRemaining = Math.Max(0.5, daysRemaining / 30.4375);
                var monthlyReq = remaining > 0 ? Math.Round(remaining / (decimal)monthsRemaining, 2) : 0m;

                if (totalSaved >= goal.TargetAmount)
                {
                    list.Add(new NotificationPreviewItem
                    {
                        GoalId = goal.Id,
                        GoalName = goal.GoalName,
                        Type = NotificationType.GoalCompleted,
                        Title = $"Goal Completed: {goal.GoalName}",
                        Subject = $"🎉 Congratulations! You reached your goal for {goal.GoalName}!",
                        MessageBody = $"You have successfully saved the full ₹{goal.TargetAmount:N2} for {goal.GoalName}!",
                        SmsBody = $"PocketGoal: 🎉 Congrats! You have completed your saving goal for '{goal.GoalName}'!",
                        UrgencyBadge = "bg-success",
                        GeneratedAt = DateTime.UtcNow
                    });
                }
                else
                {
                    // Monthly Reminder
                    list.Add(new NotificationPreviewItem
                    {
                        GoalId = goal.Id,
                        GoalName = goal.GoalName,
                        Type = NotificationType.MonthlyReminder,
                        Title = $"Monthly Savings Plan: {goal.GoalName}",
                        Subject = $"📅 Monthly Savings Reminder for {goal.GoalName}",
                        MessageBody = $"Recommended monthly deposit for this month: ₹{monthlyReq:N2} to stay on track by {goal.TargetDate:MMM dd, yyyy}.",
                        SmsBody = $"PocketGoal: Save ₹{monthlyReq:N0} this month for '{goal.GoalName}' to hit your target by {goal.TargetDate:MMM yyyy}.",
                        UrgencyBadge = "bg-primary",
                        GeneratedAt = DateTime.UtcNow
                    });

                    // If days remaining <= 30
                    if (daysRemaining <= 30 && daysRemaining > 0)
                    {
                        list.Add(new NotificationPreviewItem
                        {
                            GoalId = goal.Id,
                            GoalName = goal.GoalName,
                            Type = NotificationType.UpcomingDeadline,
                            Title = $"Target Approaching: {goal.GoalName}",
                            Subject = $"⏳ Deadline in {daysRemaining} days for {goal.GoalName}",
                            MessageBody = $"Only {daysRemaining} days left! You have ₹{remaining:N2} remaining to reach ₹{goal.TargetAmount:N2}.",
                            SmsBody = $"PocketGoal: {daysRemaining} days left for '{goal.GoalName}'. ₹{remaining:N0} remaining.",
                            UrgencyBadge = "bg-warning text-dark",
                            GeneratedAt = DateTime.UtcNow
                        });
                    }

                    // Check if behind pace
                    var totalDaysSpan = Math.Max(1, (goal.TargetDate.Date - goal.CreatedAt.Date).TotalDays);
                    var elapsedDays = Math.Max(0, (now.Date - goal.CreatedAt.Date).TotalDays);
                    var expectedSaved = goal.TargetAmount * (decimal)(elapsedDays / totalDaysSpan);

                    if (totalSaved < expectedSaved * 0.85m && elapsedDays > 7)
                    {
                        list.Add(new NotificationPreviewItem
                        {
                            GoalId = goal.Id,
                            GoalName = goal.GoalName,
                            Type = NotificationType.BehindPaceAlert,
                            Title = $"Pace Alert: {goal.GoalName}",
                            Subject = $"⚠️ Savings Alert: Catch up on your {goal.GoalName} goal",
                            MessageBody = $"You are behind pace. Recommended boost: deposit ₹{monthlyReq:N2} to keep your target date {goal.TargetDate:MMM dd, yyyy}.",
                            SmsBody = $"PocketGoal: You're slightly behind for '{goal.GoalName}'. Save ₹{monthlyReq:N0} to stay on schedule.",
                            UrgencyBadge = "bg-danger",
                            GeneratedAt = DateTime.UtcNow
                        });
                    }
                }
            }

            return list;
        }

        public async Task<(bool Success, string Message)> TriggerSimulatedNotificationAsync(Guid userId, TriggerNotificationRequest request)
        {
            var user = await _dbContext.UserProfiles.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null) return (false, "User profile not found.");

            var goal = await _dbContext.SavingGoals
                .Include(g => g.Savings)
                .FirstOrDefaultAsync(g => g.Id == request.GoalId && g.UserId == userId);

            if (goal == null) return (false, "Goal not found.");

            var totalSaved = goal.Savings.Sum(s => s.Amount);
            var remaining = Math.Max(0m, goal.TargetAmount - totalSaved);
            var daysRemaining = (int)Math.Max(0, (goal.TargetDate.Date - DateTime.UtcNow.Date).TotalDays);
            var monthsRemaining = Math.Max(0.5, daysRemaining / 30.4375);
            var monthlyReq = remaining > 0 ? Math.Round(remaining / (decimal)monthsRemaining, 2) : 0m;

            switch (request.Type)
            {
                case NotificationType.MonthlyReminder:
                    await SendMonthlyReminderAsync(user, goal, monthlyReq);
                    return (true, $"Monthly reminder sent to {user.Email} and {user.PhoneNumber} for '{goal.GoalName}'");

                case NotificationType.BehindPaceAlert:
                    await SendBehindPaceAlertAsync(user, goal, monthlyReq);
                    return (true, $"Behind-pace alert sent to {user.Email} and {user.PhoneNumber} for '{goal.GoalName}'");

                case NotificationType.GoalCompleted:
                    await SendGoalCompletedAlertAsync(user, goal);
                    return (true, $"Goal completion congratulatory alert sent to {user.Email} and {user.PhoneNumber} for '{goal.GoalName}'");

                case NotificationType.UpcomingDeadline:
                    await SendUpcomingDeadlineReminderAsync(user, goal, daysRemaining, remaining);
                    return (true, $"Upcoming deadline reminder sent to {user.Email} and {user.PhoneNumber} for '{goal.GoalName}'");

                default:
                    return (false, "Unknown notification type.");
            }
        }
    }
}
