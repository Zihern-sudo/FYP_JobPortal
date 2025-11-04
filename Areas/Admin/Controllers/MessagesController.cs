// File: Areas/Admin/Controllers/MessagesController.cs
using Microsoft.AspNetCore.Mvc;
using JobPortal.Areas.Shared.Models;
using JobPortal.Areas.Admin.Models;
using JobPortal.Areas.Shared.Extensions; // TryGetUserId()
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System;

namespace JobPortal.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class MessagesController : Controller
    {
        private readonly AppDbContext _db;
        public MessagesController(AppDbContext db) => _db = db;

        // GET: /Admin/Messages?q=...&flaggedOnly=true&page=1&pageSize=10
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
            {
                query = query.Where(c => c.conversation_monitors.Any(m => m.flag));
            }

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
                    Flagged = c.conversation_monitors.Any(m => m.flag),
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

        public IActionResult Thread(int id)
        {
            ViewData["Title"] = $"Thread #{id}";

            // Load messages + sender info
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

            if (messages.Count == 0)
            {
                var emptyVm = new ConversationThreadViewModel
                {
                    ConversationId = id,
                    Messages = messages
                };
                return View(emptyVm);
            }

            // Derive participants (first two distinct senders)
            var participants = messages
                .Select(m => new { m.SenderId, m.SenderName, m.SenderRole })
                .Distinct()
                .Take(2)
                .ToList();

            Participant? pA = null;
            Participant? pB = null;

            foreach (var p in participants)
            {
                // fetch latest status from users
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

            var vm = new ConversationThreadViewModel
            {
                ConversationId = id,
                Messages = messages,
                ParticipantA = pA,
                ParticipantB = pB
            };

            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ToggleFlag(int conversationId)
        {
            var monitor = _db.conversation_monitors.FirstOrDefault(m => m.conversation_id == conversationId);
            if (monitor == null)
            {
                monitor = new conversation_monitor
                {
                    conversation_id = conversationId,
                    flag = true
                };
                _db.conversation_monitors.Add(monitor);
            }
            else
            {
                monitor.flag = !monitor.flag;
            }
            _db.SaveChanges();

            TempData["Flash.Type"] = "success";
            TempData["Flash.Message"] = "Conversation flag toggled.";
            return RedirectToAction(nameof(Thread), new { id = conversationId });
        }

        // Clear unread counters and mark all messages as read
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

        // --- NEW: Restrict / Allow messaging for a user (uses existing user_status) ---

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult RestrictMessaging(int conversationId, int userId)
        {
            var user = _db.users.FirstOrDefault(u => u.user_id == userId);
            if (user == null) return NotFound();

            user.user_status = "Suspended"; // why: reuse existing status to block messaging
            _db.SaveChanges();

            if (this.TryGetUserId(out var adminId, out _))
            {
                _db.admin_logs.Add(new admin_log
                {
                    user_id = adminId,
                    action_type = $"Messaging-Restrict:{userId}",
                    timestamp = DateTime.UtcNow
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

            // If account is otherwise suspended for a reason, this sets to Active; tweak if you need another state.
            user.user_status = "Active";
            _db.SaveChanges();

            if (this.TryGetUserId(out var adminId, out _))
            {
                _db.admin_logs.Add(new admin_log
                {
                    user_id = adminId,
                    action_type = $"Messaging-Allow:{userId}",
                    timestamp = DateTime.UtcNow
                });
                _db.SaveChanges();
            }

            TempData["Flash.Type"] = "success";
            TempData["Flash.Message"] = $"Messaging re-enabled for {user.first_name} {user.last_name}.";
            return RedirectToAction(nameof(Thread), new { id = conversationId });
        }
    }
}
