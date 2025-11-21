using JobPortal.Areas.JobSeeker.Models;
using JobPortal.Areas.Shared.Models;
using Microsoft.EntityFrameworkCore;
using System.Text;
using JobPortal.Services;
using System.Net;
using System.Net.Mime;
using System.Net.Mail;

namespace JobPortal.Areas.Services
{
    public class JobSeekerNotificationService
    {
        private readonly AppDbContext _db;
        private readonly EmailService _emailService;
        private readonly IConfiguration _config;

        public JobSeekerNotificationService(AppDbContext db, EmailService emailService, IConfiguration config)
        {
            _db = db;
            _emailService = emailService;
            _config = config;
        }

        // ✅ Check favorited jobs for expiry in next X days, respecting user preferences
        public async Task CheckFavoritedJobExpiryAsync()
        {
            var upcomingExpiry = DateTime.Now.AddDays(3);

            // Join job_favourites -> users -> notification_preference -> job_listing
            var favorites = await (from f in _db.job_favourites
                                   join u in _db.users on f.user_id equals u.user_id
                                   join p in _db.notification_preferences on f.user_id equals p.user_id
                                   join j in _db.job_listings on f.job_listing_id equals j.job_listing_id
                                   where j.expiry_date != null
                                         && j.expiry_date <= upcomingExpiry
                                         && j.expiry_date > DateTime.Now
                                         && p.notif_reminders // only users who want reminders
                                   select new
                                   {
                                       Favourite = f,
                                       User = u,
                                       Preference = p,
                                       Job = j
                                   }).ToListAsync();

            foreach (var item in favorites)
            {
                var fav = item.Favourite;
                var user = item.User;
                var pref = item.Preference;
                var job = item.Job;

                // Check if an un-read or already sent notification exists for this job
                bool alreadyNotified = await _db.notifications.AnyAsync(n =>
                    n.user_id == fav.user_id &&
                    n.notification_type == "JobReminder" &&
                    n.notification_msg.Contains($"'{job.job_title}' expires") // or store job_listing_id in notification
                );

                if (alreadyNotified)
                    continue; // skip this job

                // In-app notification only if allowed
                if (pref.allow_inApp)
                {
                    var notification = new notification
                    {
                        user_id = fav.user_id,
                        notification_title = "Favorited Job Expiring Soon",
                        notification_msg = $"Your favorited job '{job.job_title}' expires on {job.expiry_date:yyyy-MM-dd}.",
                        notification_type = "JobReminder",
                        notification_date_created = DateTime.Now,
                        notification_read_status = false
                    };
                    _db.notifications.Add(notification);
                }
            }

            await _db.SaveChangesAsync();
        }


        // ✅ Notify about unread recruiter messages (manual trigger example)
        public async Task CheckUnreadRecruiterMessagesAsync(int userId)
        {
            var unreadMessages = await (from m in _db.messages
                                        join u in _db.users on m.sender_id equals u.user_id
                                        where m.receiver_id == userId && !m.is_read
                                        select new
                                        {
                                            Message = m,
                                            SenderName = u.first_name + " " + u.last_name,
                                            SenderEmail = u.email
                                        }).ToListAsync();

            foreach (var item in unreadMessages)
            {
                var msg = item.Message;
                string senderName = item.SenderName;

                // In-app notification
                var notification = new notification
                {
                    user_id = userId,
                    notification_title = "New Message from Recruiter",
                    notification_msg = $"You have a new message from {senderName}: {msg.msg_content}",
                    notification_type = "Message",
                    notification_date_created = DateTime.Now,
                    notification_read_status = false
                };
                _db.notifications.Add(notification);

                // Email notification
                var user = await _db.users.FindAsync(userId);
                if (user != null && !string.IsNullOrEmpty(user.email))
                {
                    string subject = "New Message from Recruiter";
                    string htmlBody = $"<p>You have a new message from {senderName}: <strong>{msg.msg_content}</strong></p>";
                    string textBody = $"You have a new message from {senderName}: {msg.msg_content}";
                    await SendViaSmtpAsync(user.email, subject, htmlBody, textBody);
                }
            }

            await _db.SaveChangesAsync();
        }

        // ✅ Helper to send email via your existing SMTP setup
        private async Task SendViaSmtpAsync(string toEmail, string subject, string htmlBody, string textBody, LinkedResource? inlineLogo = null)
        {
            var host = _config["Email:SmtpHost"];
            var portStr = _config["Email:SmtpPort"];
            var user = _config["Email:Username"];
            var pass = _config["Email:Password"];

            if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(pass))
                throw new InvalidOperationException("SMTP 'Email' settings are missing in configuration.");

            if (!int.TryParse(portStr, out var port)) port = 587;

            var fromDisplay = "Joboria Verification"; // branding
            var from = new MailAddress(user, fromDisplay, Encoding.UTF8);
            var to = new MailAddress(toEmail);

            using var msg = new MailMessage
            {
                From = from,
                Subject = subject,
                SubjectEncoding = Encoding.UTF8,
                BodyEncoding = Encoding.UTF8,
                HeadersEncoding = Encoding.UTF8
            };
            msg.To.Add(to);

            // Plain text (fallback for old clients)
            var plainView = AlternateView.CreateAlternateViewFromString(
                textBody, Encoding.UTF8, MediaTypeNames.Text.Plain);

            // HTML body (+ optional linked logo)
            var htmlView = AlternateView.CreateAlternateViewFromString(
                htmlBody, Encoding.UTF8, MediaTypeNames.Text.Html);

            if (inlineLogo != null)
            {
                htmlView.LinkedResources.Add(inlineLogo);
            }

            msg.AlternateViews.Add(plainView);
            msg.AlternateViews.Add(htmlView);
            msg.IsBodyHtml = true;

            using var client = new SmtpClient(host, port)
            {
                EnableSsl = true, // STARTTLS on 587
                Credentials = new NetworkCredential(user, pass),
                DeliveryMethod = SmtpDeliveryMethod.Network,
                Timeout = 20000
            };

            await client.SendMailAsync(msg);
        }
    }
}
