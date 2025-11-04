using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using JobPortal.Areas.JobSeeker.Models;
using JobPortal.Areas.Shared.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace JobPortal.Areas.JobSeeker.Controllers
{
    [Area("JobSeeker")]
    public class InboxController : Controller
    {
        private readonly AppDbContext _db;
        private const int DefaultPageSize = 10;
        private const int MaxPageSize = 20;

        public InboxController(AppDbContext db)
        {
            _db = db;
        }

        // GET: /JobSeeker/Inbox
        [HttpGet]
        public async Task<IActionResult> Index(string? q, string? filter, int page = 1, int pageSize = 10)
        {
            // Get the logged-in candidate
            var userIdStr = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrEmpty(userIdStr))
                return RedirectToAction("Login", "Account", new { area = "JobSeeker" });

            int candidateId = int.Parse(userIdStr);
            ViewData["Title"] = "Inbox";

            // Base query: conversations belonging to this candidate
            var baseQuery = _db.conversations
                .Where(c => c.candidate_id == candidateId);

            // Optional filter: show only unread conversations
            if (filter == "unread")
            {
                baseQuery = baseQuery.Where(c => c.unread_for_candidate > 0);
            }

            // Optional search by job title, recruiter, or snippet
            if (!string.IsNullOrWhiteSpace(q))
            {
                var qTrim = q.Trim();
                baseQuery = baseQuery.Where(c =>
                    (c.job_title != null && c.job_title.Contains(qTrim)) ||
                    (c.candidate_name != null && c.candidate_name.Contains(qTrim)) ||
                    (c.last_snippet != null && c.last_snippet.Contains(qTrim))
                );
            }

            // Pagination setup
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 5, 20);

            var totalCount = await baseQuery.CountAsync();
            var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
            if (page > totalPages) page = totalPages;

            var skip = (page - 1) * pageSize;

            // Fetch the conversations
            var conversations = await baseQuery
                .OrderByDescending(c => c.last_message_at ?? c.created_at)
                .Skip(skip)
                .Take(pageSize)
                .ToListAsync();

            // ✅ Build proper view model instead of using ViewBag
            var vm = new InboxIndexVM
            {
                Threads = conversations.Select(c => new ThreadItemVM(
                    c.conversation_id,
                    c.job_title ?? "(Unknown Job)",
                    $"Recruiter #{c.recruiter_id}",
                    c.last_snippet ?? "",
                    (c.last_message_at ?? c.created_at).ToString("yyyy-MM-dd HH:mm"),
                    c.unread_for_candidate
                )).ToList(),

                Page = page,
                PageSize = pageSize,
                TotalCount = totalCount,
                TotalPages = totalPages,
                Query = q ?? "",
                Filter = filter ?? ""
            };

            return View(vm);

        }


        // GET: /JobSeeker/Inbox/Thread/{id}
        [HttpGet]
        public async Task<IActionResult> Thread(int id)
        {
            var userIdStr = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrEmpty(userIdStr))
                return RedirectToAction("Login", "Account", new { area = "JobSeeker" });

            int candidateId = int.Parse(userIdStr);

            var convo = await _db.conversations
                .FirstOrDefaultAsync(c => c.conversation_id == id && c.candidate_id == candidateId);

            if (convo == null)
                return NotFound();

            // Get messages linked to this conversation
            var messages = await _db.messages
                .Where(m => m.conversation_id == convo.conversation_id)
                .OrderBy(m => m.msg_timestamp)
                .ToListAsync();

            // Mark unread messages as read
            var unread = messages.Where(m => m.receiver_id == candidateId && m.is_read == false).ToList();
            if (unread.Any())
            {
                unread.ForEach(m => m.is_read = false);
                convo.unread_for_candidate = 0;
                await _db.SaveChangesAsync();
            }

            var messageList = messages.Select(m => new MessageItemVM(
                m.message_id,
                m.sender_id == candidateId ? "You" : $"Recruiter #{m.sender_id}",
                m.msg_content,
                m.msg_timestamp.ToString("yyyy-MM-dd HH:mm"),
                m.sender_id != candidateId
            )).ToList();

            var vm = new InboxThreadVM
            {
                ThreadId = convo.conversation_id,
                JobTitle = convo.job_title,
                RecruiterName = $"Recruiter #{convo.recruiter_id}",
                Messages = messageList
            };

            return View(vm);
        }

        // POST: /JobSeeker/Inbox/SendMessage
        [HttpPost]
        public async Task<IActionResult> SendMessage(MessagePostVM vm)
        {
            var userIdStr = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrEmpty(userIdStr))
                return RedirectToAction("Login", "Account", new { area = "JobSeeker" });

            int candidateId = int.Parse(userIdStr);

            if (!ModelState.IsValid)
                return RedirectToAction("Thread", new { id = vm.ThreadId });

            var convo = await _db.conversations.FirstOrDefaultAsync(c => c.conversation_id == vm.ThreadId && c.candidate_id == candidateId);
            if (convo == null)
                return NotFound();

            // Create new message
            var message = new message
            {
                conversation_id = vm.ThreadId,
                sender_id = candidateId,
                receiver_id = convo.recruiter_id ?? 0,
                msg_content = vm.MessageText,
                msg_timestamp = DateTime.Now,
                is_read = false
            };

            _db.messages.Add(message);

            // Update conversation
            convo.last_message_at = DateTime.Now;
            convo.last_snippet = vm.MessageText.Length > 100 ? vm.MessageText.Substring(0, 100) : vm.MessageText;
            convo.unread_for_recruiter += 1;

            await _db.SaveChangesAsync();

            TempData["Message"] = "Message sent successfully!";
            return RedirectToAction("Thread", new { id = vm.ThreadId });
        }
    }
}
