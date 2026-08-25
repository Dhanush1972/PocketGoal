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
            var subject = $"🎉 Semma da! Un goal '{goal.GoalName}' reach panniyaachu!";
            var emailBody = $@"
                <div style='font-family: sans-serif; max-width: 600px; padding: 20px; border: 1px solid #e2e8f0; border-radius: 12px;'>
                    <h2 style='color: #10b981;'>🎉 Goal Achieved Da!</h2>
                    <p>Hi <strong>{user.Name}</strong>,</p>
                    <p>Semma news! Nee <strong>{goal.GoalName}</strong> goal-kaga total <strong>₹{goal.CurrentSavedAmount:N2}</strong> successfully save pannita.</p>
                    <p>Target amount ₹{goal.TargetAmount:N2} ippo 100% funded. Ippo celebrate panni un dream purchase panniko!</p>
                    <hr style='border: none; border-top: 1px solid #e2e8f0; margin: 20px 0;'/>
                    <p style='color: #64748b; font-size: 13px;'>PocketGoal - Un Personal Finance & Savings Partner</p>
                </div>";

            var smsBody = $"PocketGoal: 🎉 Semma da {user.Name}! '{goal.GoalName}' goal-kaga ₹{goal.TargetAmount:N0} full-ah save pannita. 100% completed!";

            await SendEmailNotificationAsync(user.Email, subject, emailBody);
            await SendSmsNotificationAsync(user.PhoneNumber, smsBody);
        }

        public async Task SendBehindPaceAlertAsync(UserProfile user, SavingGoal goal, decimal requiredMonthly)
        {
            var subject = $"⚠️ Savings Alert: '{goal.GoalName}' goal-ku konjam speed-up pannuvom!";
            var emailBody = $@"
                <div style='font-family: sans-serif; max-width: 600px; padding: 20px; border: 1px solid #e2e8f0; border-radius: 12px;'>
                    <h2 style='color: #f59e0b;'>⚠️ Goal Pace Alert</h2>
                    <p>Hi <strong>{user.Name}</strong>,</p>
                    <p>Un <strong>{goal.GoalName}</strong> saving goal-la konjam slow-ah irukka da.</p>
                    <p>{goal.TargetDate:MMM dd, yyyy}-kulla ₹{goal.TargetAmount:N2} target reach panna, indha month <strong>₹{requiredMonthly:N2}/month</strong> save pannu.</p>
                    <p>Small small savings-um periya difference tharum, keep going!</p>
                    <hr style='border: none; border-top: 1px solid #e2e8f0; margin: 20px 0;'/>
                    <p style='color: #64748b; font-size: 13px;'>PocketGoal - Un Personal Finance & Savings Partner</p>
                </div>";

            var smsBody = $"PocketGoal Alert: '{goal.GoalName}' goal-ku on track-la irukka indha month ₹{requiredMonthly:N0} save pannu da. Keep going!";

            await SendEmailNotificationAsync(user.Email, subject, emailBody);
            await SendSmsNotificationAsync(user.PhoneNumber, smsBody);
        }

        public async Task SendMonthlyReminderAsync(UserProfile user, SavingGoal goal, decimal requiredMonthly)
        {
            var subject = $"📅 Monthly Savings Check-in: {goal.GoalName}";
            var emailBody = $@"
                <div style='font-family: sans-serif; max-width: 600px; padding: 20px; border: 1px solid #e2e8f0; border-radius: 12px;'>
                    <h2 style='color: #3b82f6;'>📅 Monthly Savings Check-in</h2>
                    <p>Hi <strong>{user.Name}</strong>,</p>
                    <p>Idhu un <strong>{goal.GoalName}</strong> purchase goal-kaga friendly monthly reminder.</p>
                    <p>Indha month recommended deposit: <strong>₹{requiredMonthly:N2}</strong>.</p>
                    <p>Ippo varaikkum progress: ₹{goal.CurrentSavedAmount:N2} of ₹{goal.TargetAmount:N2} ({(goal.TargetAmount > 0 ? (goal.CurrentSavedAmount / goal.TargetAmount * 100m) : 0):N1}%).</p>
                    <hr style='border: none; border-top: 1px solid #e2e8f0; margin: 20px 0;'/>
                    <p style='color: #64748b; font-size: 13px;'>PocketGoal - Un Personal Finance & Savings Partner</p>
                </div>";

            var smsBody = $"PocketGoal: '{goal.GoalName}'-kaga indha month ₹{requiredMonthly:N0} save pannu da. Already ₹{goal.CurrentSavedAmount:N0} saved!";

            await SendEmailNotificationAsync(user.Email, subject, emailBody);
            await SendSmsNotificationAsync(user.PhoneNumber, smsBody);
        }

        public async Task SendUpcomingDeadlineReminderAsync(UserProfile user, SavingGoal goal, int daysRemaining, decimal remainingAmount)
        {
            var subject = $"⏳ Innum {daysRemaining} days dhaan irukku: {goal.GoalName}";
            var emailBody = $@"
                <div style='font-family: sans-serif; max-width: 600px; padding: 20px; border: 1px solid #e2e8f0; border-radius: 12px;'>
                    <h2 style='color: #6366f1;'>⏳ Target Date Close Aagudhu</h2>
                    <p>Hi <strong>{user.Name}</strong>,</p>
                    <p>Un <strong>{goal.GoalName}</strong> goal target date innum <strong>{daysRemaining} days</strong>-la varudhu ({goal.TargetDate:MMM dd, yyyy}).</p>
                    <p>Goal mudikka balance needed: <strong>₹{remainingAmount:N2}</strong>.</p>
                    <hr style='border: none; border-top: 1px solid #e2e8f0; margin: 20px 0;'/>
                    <p style='color: #64748b; font-size: 13px;'>PocketGoal - Un Personal Finance & Savings Partner</p>
                </div>";

            var smsBody = $"PocketGoal: Innum {daysRemaining} days dhaan for '{goal.GoalName}' target date! Balance to save: ₹{remainingAmount:N0}.";

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
                        Title = $"Goal Mudinjadhu: {goal.GoalName} 🎉",
                        Subject = $"🎉 Semma da! Un goal '{goal.GoalName}' reach panniyaachu!",
                        MessageBody = $"Nee {goal.GoalName} goal-kaga full target ₹{goal.TargetAmount:N2} save pannita da!",
                        SmsBody = $"PocketGoal: 🎉 Congrats! '{goal.GoalName}' goal-ah 100% complete pannita!",
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
                        Subject = $"📅 Monthly Savings Check-in: {goal.GoalName}",
                        MessageBody = $"Indha month recommended deposit: ₹{monthlyReq:N2} ({goal.TargetDate:MMM dd, yyyy}-kulla reach panna).",
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
                            Title = $"Target Date Nerungiduchu: {goal.GoalName} ⏰",
                            Subject = $"⏳ Innum {daysRemaining} days dhaan irukku: {goal.GoalName}",
                            MessageBody = $"Innum {daysRemaining} days dhaan irukku! ₹{goal.TargetAmount:N2} target-ku balance ₹{remaining:N2} to save.",
                            SmsBody = $"PocketGoal: Innum {daysRemaining} days dhaan for '{goal.GoalName}'. Balance ₹{remaining:N0}.",
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
                            Title = $"Konjam Slow-ah Irukka: {goal.GoalName} ⚠️",
                            Subject = $"⚠️ Savings Alert: '{goal.GoalName}' goal-ku konjam speed-up pannuvom!",
                            MessageBody = $"Pace konjam slow-ah irukku. Target date {goal.TargetDate:MMM dd, yyyy}-kaga indha month ₹{monthlyReq:N2} save pannu da.",
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
                    return (true, $"Monthly reminder {user.Email} and {user.PhoneNumber}-ku anuppiyaachu ('{goal.GoalName}') 🚀");

                case NotificationType.BehindPaceAlert:
                    await SendBehindPaceAlertAsync(user, goal, monthlyReq);
                    return (true, $"Behind-pace alert {user.Email} and {user.PhoneNumber}-ku anuppiyaachu ('{goal.GoalName}') ⚠️");

                case NotificationType.GoalCompleted:
                    await SendGoalCompletedAlertAsync(user, goal);
                    return (true, $"Goal completion alert {user.Email} and {user.PhoneNumber}-ku anuppiyaachu ('{goal.GoalName}') 🎉");

                case NotificationType.UpcomingDeadline:
                    await SendUpcomingDeadlineReminderAsync(user, goal, daysRemaining, remaining);
                    return (true, $"Upcoming deadline reminder {user.Email} and {user.PhoneNumber}-ku anuppiyaachu ('{goal.GoalName}') ⏳");

                default:
                    return (false, "Unknown notification type.");
            }
        }
    }
}
