// File: Areas/Shared/Services/NotificationService.cs
using JobPortal.Areas.Shared.Models; // AppDbContext, notification, user, company, etc.
using Microsoft.EntityFrameworkCore;

namespace JobPortal.Services
{
    public interface INotificationService
    {
        // Core
        Task<int> SendAsync(int userId, string title, string message, string? type = null, DateTime? createdAt = null);
        Task<int> SendManyAsync(IEnumerable<int> userIds, string title, string message, string? type = null, DateTime? createdAt = null);

        // Convenience
        Task<int> SendToAdminsAsync(string title, string message, string? type = "System");
        Task<int> SendToCompanyRecruitersAsync(int companyId, string title, string message, string? type = "Employer");

        // Common reads/ops
        Task<int> CountUnreadAsync(int userId);
        Task MarkAsReadAsync(int userId, int notificationId);
        Task MarkAllAsReadAsync(int userId);

        Task<List<notification>> PeekAsync(int userId, int take = 8);
    }

    public sealed class NotificationService : INotificationService
    {
        private readonly AppDbContext _db;
        public NotificationService(AppDbContext db) => _db = db;

        public async Task<int> SendAsync(int userId, string title, string message, string? type = null, DateTime? createdAt = null)
        {
            if (userId <= 0) return 0;
            var n = new notification
            {
                user_id = userId,
                notification_title = title ?? string.Empty,
                notification_msg = message ?? string.Empty,
                notification_type = type ?? "General",
                notification_date_created = (createdAt ?? DateTime.UtcNow),
                notification_read_status = false
            };
            _db.notifications.Add(n);
            return await _db.SaveChangesAsync();
        }

        public async Task<int> SendManyAsync(IEnumerable<int> userIds, string title, string message, string? type = null, DateTime? createdAt = null)
        {
            var when = createdAt ?? DateTime.UtcNow;
            var list = userIds.Where(id => id > 0)
                              .Select(id => new notification
                              {
                                  user_id = id,
                                  notification_title = title ?? string.Empty,
                                  notification_msg = message ?? string.Empty,
                                  notification_type = type ?? "General",
                                  notification_date_created = when,
                                  notification_read_status = false
                              })
                              .ToList();
            if (list.Count == 0) return 0;
            _db.notifications.AddRange(list);
            return await _db.SaveChangesAsync();
        }

        public async Task<int> SendToAdminsAsync(string title, string message, string? type = "System")
        {
            // Assumes user.user_role holds "Admin"
            var adminIds = await _db.users
                .AsNoTracking()
                .Where(u => u.user_role == "Admin")
                .Select(u => u.user_id)
                .ToListAsync();

            return await SendManyAsync(adminIds, title, message, type);
        }

        public async Task<int> SendToCompanyRecruitersAsync(int companyId, string title, string message, string? type = "Employer")
        {
            // Simplest mapping: recruiters are users tied to the company table (adjust to your schema)
            var recruiterIds = await _db.users
                .AsNoTracking()
                .Where(u => u.user_role == "Recruiter" && _db.companies
                    .Any(c => c.company_id == companyId && c.user_id == u.user_id))
                .Select(u => u.user_id)
                .ToListAsync();

            return await SendManyAsync(recruiterIds, title, message, type);
        }

        public Task<int> CountUnreadAsync(int userId) =>
            _db.notifications.AsNoTracking().CountAsync(n => n.user_id == userId && !n.notification_read_status);

        public async Task MarkAsReadAsync(int userId, int notificationId)
        {
            var row = await _db.notifications.FirstOrDefaultAsync(n => n.notification_id == notificationId && n.user_id == userId);
            if (row is null) return;
            if (!row.notification_read_status)
            {
                row.notification_read_status = true;
                await _db.SaveChangesAsync();
            }
        }

        public async Task MarkAllAsReadAsync(int userId)
        {
            var unread = await _db.notifications.Where(n => n.user_id == userId && !n.notification_read_status).ToListAsync();
            if (unread.Count == 0) return;
            foreach (var n in unread) n.notification_read_status = true;
            await _db.SaveChangesAsync();
        }

        public Task<List<notification>> PeekAsync(int userId, int take = 8) =>
            _db.notifications.AsNoTracking()
                .Where(n => n.user_id == userId)
                .OrderBy(n => n.notification_read_status)
                .ThenByDescending(n => n.notification_date_created)
                .Take(take)
                .ToListAsync();
    }
}
