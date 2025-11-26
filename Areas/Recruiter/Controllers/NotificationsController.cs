// ===============================================
// File: Areas/Recruiter/Controllers/NotificationsController.cs
// ===============================================
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using JobPortal.Areas.Shared.Models;             // AppDbContext, notification
using JobPortal.Areas.Recruiter.Models;          // PagedResult<>, NotificationListItemVM, RecruiterNotificationsIndexVM

namespace JobPortal.Areas.Recruiter.Controllers
{
    [Area("Recruiter")]
    public class NotificationsController : Controller
    {
        private readonly AppDbContext _db;
        public NotificationsController(AppDbContext db) => _db = db;

        // tolerant session key lookup
        private int CurrentUserId
        {
            get
            {
                var s = HttpContext?.Session;
                if (s == null) return 0;

                int? id = s.GetInt32("UserId") ?? s.GetInt32("RecruiterId");
                if (!id.HasValue)
                {
                    var raw = s.GetString("UserId") ?? s.GetString("RecruiterId");
                    if (int.TryParse(raw, out var parsed)) id = parsed;
                }
                return id ?? 0;
            }
        }

        [HttpGet]
        public IActionResult Index(int page = 1, int pageSize = 10, string filter = "all", string? type = null)
        {
            if (page <= 0) page = 1;
            if (pageSize <= 0 || pageSize > 100) pageSize = 10;

            var uid = CurrentUserId;

            IQueryable<notification> q = _db.notifications
                .AsNoTracking()
                .Where(n => n.user_id == uid);

            if (string.Equals(filter, "unread", StringComparison.OrdinalIgnoreCase))
                q = q.Where(n => !n.notification_read_status);

            // MySQL-friendly: LIKE instead of StringComparison overloads
            if (!string.IsNullOrWhiteSpace(type))
                q = q.Where(n => n.notification_type != null && EF.Functions.Like(n.notification_type!, type));

            q = q.OrderBy(n => n.notification_read_status)
                 .ThenByDescending(n => n.notification_date_created);

            var total = q.Count();

            var items = q.Skip((page - 1) * pageSize)
                         .Take(pageSize)
                         .Select(n => new NotificationListItemVM
                         {
                             Id = n.notification_id,
                             Title = n.notification_title,
                             TextPreview = n.notification_msg.Length > 140
                                 ? n.notification_msg.Substring(0, 140) + "..."
                                 : n.notification_msg,
                             Type = n.notification_type,
                             CreatedAt = n.notification_date_created,
                             IsRead = n.notification_read_status
                         })
                         .ToList();

            var types = _db.notifications
                           .AsNoTracking()
                           .Where(n => n.user_id == uid && n.notification_type != null && n.notification_type != "")
                           .Select(n => n.notification_type!)
                           .Distinct()
                           .OrderBy(s => s)
                           .ToList();

            var vm = new RecruiterNotificationsIndexVM
            {
                Items = new PagedResult<NotificationListItemVM>
                {
                    Items = items,
                    Page = page,
                    PageSize = pageSize,
                    TotalItems = total
                },
                UnreadCount = _db.notifications.Count(n => n.user_id == uid && !n.notification_read_status),

                Page = page,
                PageSize = pageSize,

                Filter = string.IsNullOrWhiteSpace(filter) ? "all" : filter.ToLowerInvariant(),
                Type = string.IsNullOrWhiteSpace(type) ? null : type,
                AvailableTypes = types
            };

            return View(vm);
        }

        [HttpGet]
        public IActionResult UnreadCount()
        {
            var uid = CurrentUserId;
            var count = (uid == 0) ? 0 : _db.notifications.Count(n => n.user_id == uid && !n.notification_read_status);
            return Json(new { count });
        }

        [HttpGet]
        public IActionResult Peek()
        {
            var uid = CurrentUserId;
            if (uid == 0) return Json(new { items = Array.Empty<object>() });

            var items = _db.notifications
                .AsNoTracking()
                .Where(n => n.user_id == uid)
                .OrderBy(n => n.notification_read_status)
                .ThenByDescending(n => n.notification_date_created)
                .Take(8)
                .Select(n => new
                {
                    id = n.notification_id,
                    title = n.notification_title,
                    textPreview = n.notification_msg.Length > 140
                        ? n.notification_msg.Substring(0, 140) + "..."
                        : n.notification_msg,
                    type = n.notification_type,
                    isRead = n.notification_read_status,
                    createdAt = n.notification_date_created.ToString("yyyy-MM-dd HH:mm"),
                    createdAtLocal = n.notification_date_created.ToString("HH:mm"),
                    url = Url.Action("Index", "Notifications", new { area = "Recruiter" })
                })
                .ToList();

            return Json(new { items });
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult MarkAsRead(int id)
        {
            var uid = CurrentUserId;
            var notif = _db.notifications.FirstOrDefault(n => n.notification_id == id && n.user_id == uid);
            if (notif == null) return NotFound();

            if (!notif.notification_read_status)
            {
                notif.notification_read_status = true;
                _db.SaveChanges();
            }
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult MarkAllAsRead()
        {
            var uid = CurrentUserId;
            var unread = _db.notifications.Where(n => n.user_id == uid && !n.notification_read_status);
            foreach (var n in unread) n.notification_read_status = true;
            _db.SaveChanges();
            return RedirectToAction(nameof(Index));
        }
    }
}
