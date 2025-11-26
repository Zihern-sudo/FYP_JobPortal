// File: Areas/Admin/Controllers/MessagesController.cs
using Microsoft.AspNetCore.Mvc;
using JobPortal.Areas.Shared.Models;
using JobPortal.Areas.Admin.Models;
using JobPortal.Areas.Shared.Extensions; // TryGetUserId()
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System;
// MessagesController.cs — add this using at the top
using Microsoft.AspNetCore.Mvc.Rendering; // <-- for SelectListItem

namespace JobPortal.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class MessagesController : Controller
    {
        private readonly AppDbContext _db;
        public MessagesController(AppDbContext db) => _db = db;

        private static bool IsAjaxRequest(HttpRequest r) =>
            string.Equals(r.Headers["X-Requested-With"], "XMLHttpRequest", System.StringComparison.OrdinalIgnoreCase);

        // Resolve a valid existing user_id for conversation_monitor.user_id (FK NOT NULL)
        private int ResolveActorUserId(int? adminId)
        {
            // prefer the logged-in admin id if it exists in DB
            if (adminId.HasValue && _db.users.Any(u => u.user_id == adminId.Value))
                return adminId.Value;

            // fallback to any admin account
            var anyAdmin = _db.users.Where(u => u.user_role == "Admin")
                                    .Select(u => u.user_id)
                                    .FirstOrDefault();
            if (anyAdmin != 0) return anyAdmin;

            // last resort: any user (prevents FK failure in dev data)
            var anyUser = _db.users.Select(u => u.user_id).FirstOrDefault();
            return anyUser; // if 0, SaveChanges will still fail -> indicates empty users table
        }

        // ---------- Index (unchanged) ----------
        public IActionResult Index(string? q = null, bool flaggedOnly = false, int page = 1, int pageSize = 10)
        {
            ViewData["Title"] = "Conversation Monitor";

            var query = _db.conversations
                .AsNoTracking()
                .Include(c => c.job_listing).ThenInclude(j => j.company)
                .Include(c => c.conversation_monitors)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(q))
            {
                var like = $"%{q}%";
                query = query.Where(c =>
                    EF.Functions.Like(c.job_title ?? "", like) ||
                    EF.Functions.Like(c.job_listing.company.company_name, like));
            }

            if (flaggedOnly)
                query = query.Where(c => c.is_blocked || c.conversation_monitors.Any(m => m.flag));

            query = query.OrderByDescending(c => c.last_message_at ?? c.created_at);

            var total = query.Count();

            var items = query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(c => new ConversationListViewModel
                {
                    ConversationId = c.conversation_id,
                    CompanyName = c.job_listing.company.company_name,
                    JobTitle = c.job_title ?? "Unknown Job",
                    LastMessageAt = c.last_message_at,
                    LastSnippet = c.last_snippet,
                    MessageCount = _db.messages.Count(m => m.conversation_id == c.conversation_id),
                    Flagged = c.is_blocked || c.conversation_monitors.Any(m => m.flag),
                    UnreadForRecruiter = c.unread_for_recruiter,
                    UnreadForCandidate = c.unread_for_candidate
                })
                .ToList();

            var vm = new MessagesIndexViewModel
            {
                Filter = new MessagesFilterViewModel
                {
                    Q = q ?? string.Empty,
                    FlaggedOnly = flaggedOnly,
                    Page = page,
                    PageSize = pageSize
                },
                Items = new PagedResult<ConversationListViewModel>
                {
                    Items = items,
                    Page = page,
                    PageSize = pageSize,
                    TotalItems = total
                }
            };

            return View(vm);
        }

        // ---------- Thread (includes block state) ----------
        public IActionResult Thread(int id)
        {
            ViewData["Title"] = $"Thread #{id}";

            var conv = _db.conversations.AsNoTracking().FirstOrDefault(c => c.conversation_id == id);
            if (conv == null) return NotFound();

            var messages = _db.messages
                .AsNoTracking()
                .Where(m => m.conversation_id == id)
                .Include(m => m.sender)
                .OrderBy(m => m.msg_timestamp)
                .Select(m => new MessageViewModel
                {
                    SenderId = m.sender_id,
                    SenderRole = m.sender.user_role,
                    SenderName = m.sender.first_name + " " + m.sender.last_name,
                    Text = m.msg_content,
                    Timestamp = m.msg_timestamp
                })
                .ToList();

            Participant? pA = null;
            Participant? pB = null;
            if (messages.Count > 0)
            {
                var participants = messages.Select(m => new { m.SenderId, m.SenderName, m.SenderRole })
                                           .Distinct()
                                           .Take(2)
                                           .ToList();
                foreach (var p in participants)
                {
                    var u = _db.users.AsNoTracking().FirstOrDefault(x => x.user_id == p.SenderId);
                    var status = u?.user_status ?? "Active";
                    var part = new Participant
                    {
                        UserId = p.SenderId,
                        Name = p.SenderName,
                        Role = p.SenderRole ?? "User",
                        Status = status
                    };
                    if (pA == null) pA = part;
                    else if (pB == null) pB = part;
                }
            }

            var vm = new ConversationThreadViewModel
            {
                ConversationId = id,
                Messages = messages,
                ParticipantA = pA,
                ParticipantB = pB,
                IsBlocked = conv.is_blocked,
                BlockedReason = conv.blocked_reason
            };

            return View(vm);
        }

        // ---------- NEW: preset reasons ----------
        private static readonly (string Key, string Label)[] _presetReasonItems = new[]
        {
            ("inappropriate", "Inappropriate text"),
            ("spam", "Spam / scam content"),
            ("harassment", "Harassment or abusive behaviour"),
            ("personal_info", "Sharing personal/sensitive information"),
            ("other", "Other (specify)")
        };

        private static List<SelectListItem> GetPresetReasons(string? selected = null)
        {
            return _presetReasonItems
                .Select(x => new SelectListItem { Value = x.Key, Text = x.Label, Selected = x.Key == selected })
                .ToList();
        }

        // ---------- NEW: GET modal (AJAX) ----------
        [HttpGet]
        public IActionResult FlagModal(int conversationId)
        {
            var exists = _db.conversations.Any(c => c.conversation_id == conversationId);
            if (!exists) return NotFound();

            var vm = new FlagConversationViewModel
            {
                ConversationId = conversationId,
                PresetReasons = GetPresetReasons()
            };
            return PartialView("_FlagModal", vm);
        }

        // ---------- UPDATED: POST flag ----------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult FlagConversation(FlagConversationViewModel form)
        {
            if (!ModelState.IsValid)
            {
                if (IsAjaxRequest(Request)) return BadRequest(new { ok = false, error = "Invalid form." });
                TempData["Flash.Type"] = "danger";
                TempData["Flash.Message"] = "Invalid form.";
                return RedirectToAction(nameof(Thread), new { id = form.ConversationId });
            }

            var conv = _db.conversations.FirstOrDefault(c => c.conversation_id == form.ConversationId);
            if (conv == null)
            {
                if (IsAjaxRequest(Request)) return NotFound(new { ok = false, error = "Conversation not found." });
                TempData["Flash.Type"] = "danger";
                TempData["Flash.Message"] = "Conversation not found.";
                return RedirectToAction(nameof(Index));
            }

            var reason = form.ResolveReason();
            if (string.IsNullOrWhiteSpace(reason))
            {
                if (IsAjaxRequest(Request)) return BadRequest(new { ok = false, error = "Reason is required." });
                TempData["Flash.Type"] = "danger";
                TempData["Flash.Message"] = "Reason is required.";
                return RedirectToAction(nameof(Thread), new { id = form.ConversationId });
            }

            var now = MyTime.NowMalaysia();
            int? adminId = null;
            if (this.TryGetUserId(out var aid, out _)) adminId = aid;
            var actorUserId = ResolveActorUserId(adminId);

            // Persist current state
            conv.is_blocked = true;
            conv.blocked_reason = reason;
            conv.flagged_at = now;
            conv.flagged_by_user_id = adminId; // keep if your model has this column

            // Append monitor row — IMPORTANT: set user_id to satisfy FK
            _db.conversation_monitors.Add(new conversation_monitor
            {
                conversation_id = conv.conversation_id,
                user_id = actorUserId,         // required FK to user(user_id)
                flag = true,
                flag_reason = reason,
                flagged_at = now,
                flagged_by_user_id = adminId,  // keep if column exists in your model
                date_reviewed = now
            });

            // Audit log for conversation flag (standardised)
            if (adminId.HasValue)
            {
                _db.admin_logs.Add(new admin_log
                {
                    user_id = adminId.Value,
                    action_type = $"Admin.Messaging.BlockConversation:{conv.conversation_id}",
                    timestamp = now
                });
            }

            _db.SaveChanges();

            if (IsAjaxRequest(Request))
                return Json(new { ok = true, blocked = true, reason, flaggedAt = now, conversationId = conv.conversation_id });

            TempData["Flash.Type"] = "warning";
            TempData["Flash.Message"] = $"Conversation blocked. Reason: {reason}";
            return RedirectToAction(nameof(Thread), new { id = conv.conversation_id });
        }

        // ---------- UPDATED: POST unflag ----------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult UnflagConversation(int conversationId)
        {
            var conv = _db.conversations.FirstOrDefault(c => c.conversation_id == conversationId);
            if (conv == null)
            {
                if (IsAjaxRequest(Request)) return NotFound(new { ok = false, error = "Conversation not found." });
                TempData["Flash.Type"] = "danger";
                TempData["Flash.Message"] = "Conversation not found.";
                return RedirectToAction(nameof(Index));
            }

            var now = MyTime.NowMalaysia();
            int? adminId = null;
            if (this.TryGetUserId(out var aid, out _)) adminId = aid;
            var actorUserId = ResolveActorUserId(adminId);

            conv.is_blocked = false;
            conv.blocked_reason = null;
            conv.flagged_at = null;
            conv.flagged_by_user_id = null;

            _db.conversation_monitors.Add(new conversation_monitor
            {
                conversation_id = conv.conversation_id,
                user_id = actorUserId,        // required FK to user(user_id)
                flag = false,
                flag_reason = "Unblocked by admin",
                flagged_at = now,
                flagged_by_user_id = adminId, // keep if column exists
                date_reviewed = now
            });

            // Audit log for conversation unflag (standardised)
            if (adminId.HasValue)
            {
                _db.admin_logs.Add(new admin_log
                {
                    user_id = adminId.Value,
                    action_type = $"Admin.Messaging.UnblockConversation:{conv.conversation_id}",
                    timestamp = now
                });
            }

            _db.SaveChanges();

            if (IsAjaxRequest(Request))
                return Json(new { ok = true, blocked = false, conversationId = conv.conversation_id });

            TempData["Flash.Type"] = "success";
            TempData["Flash.Message"] = "Conversation unblocked.";
            return RedirectToAction(nameof(Thread), new { id = conversationId });
        }

        // ---------- Existing actions kept below ----------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult MarkAllRead(int conversationId)
        {
            var conv = _db.conversations.FirstOrDefault(c => c.conversation_id == conversationId);
            if (conv == null) return NotFound();

            conv.unread_for_recruiter = 0;
            conv.unread_for_candidate = 0;

            var msgs = _db.messages.Where(m => m.conversation_id == conversationId && !m.is_read).ToList();
            foreach (var m in msgs) m.is_read = true;

            _db.SaveChanges();

            TempData["Flash.Type"] = "success";
            TempData["Flash.Message"] = "All messages marked as read.";
            return RedirectToAction(nameof(Thread), new { id = conversationId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult RestrictMessaging(int conversationId, int userId)
        {
            var user = _db.users.FirstOrDefault(u => u.user_id == userId);
            if (user == null) return NotFound();

            user.user_status = "Suspended";
            _db.SaveChanges();

            if (this.TryGetUserId(out var adminId, out _))
            {
                _db.admin_logs.Add(new admin_log
                {
                    user_id = adminId,
                    action_type = $"Admin.Messaging.RestrictUser:{userId}",
                    timestamp = MyTime.NowMalaysia()
                });
                _db.SaveChanges();
            }

            TempData["Flash.Type"] = "warning";
            TempData["Flash.Message"] = $"Messaging restricted for {user.first_name} {user.last_name}.";
            return RedirectToAction(nameof(Thread), new { id = conversationId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult AllowMessaging(int conversationId, int userId)
        {
            var user = _db.users.FirstOrDefault(u => u.user_id == userId);
            if (user == null) return NotFound();

            user.user_status = "Active";
            _db.SaveChanges();

            if (this.TryGetUserId(out var adminId, out _))
            {
                _db.admin_logs.Add(new admin_log
                {
                    user_id = adminId,
                    action_type = $"Admin.Messaging.AllowUser:{userId}",
                    timestamp = MyTime.NowMalaysia()
                });
                _db.SaveChanges();
            }

            TempData["Flash.Type"] = "success";
            TempData["Flash.Message"] = $"Messaging re-enabled for {user.first_name} {user.last_name}.";
            return RedirectToAction(nameof(Thread), new { id = conversationId });
        }
    }
}
