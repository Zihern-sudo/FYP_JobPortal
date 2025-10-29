using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using JobPortal.Areas.Shared.Models;          // DbContext + entities
using JobPortal.Areas.Recruiter.Models;       // VMs

namespace JobPortal.Areas.Recruiter.Controllers
{
    [Area("Recruiter")]
    public class InboxController : Controller
    {
        private readonly AppDbContext _db;
        public InboxController(AppDbContext db) => _db = db;

        // GET: /Recruiter/Inbox
        // Now uses cached conversation fields (last_message_at, last_snippet, unread_for_recruiter, candidate_name)
        [HttpGet]
        public async Task<IActionResult> Index()
        {
            ViewData["Title"] = "Inbox";

            var recruiterId = 3; // TODO: replace with logged-in recruiter id

            // Prefer conversations where recruiter_id is set; otherwise fall back to job owner for safety
            var convs = await _db.conversations
                .Include(c => c.job_listing)
                .Where(c =>
                    (c.recruiter_id != null && c.recruiter_id == recruiterId) ||
                    (c.recruiter_id == null && c.job_listing.user_id == recruiterId))
                .ToListAsync();

            var threads = new List<ThreadListItemVM>(convs.Count);

            foreach (var c in convs)
            {
                var lastAt = c.last_message_at ?? c.created_at;
                var participant = !string.IsNullOrWhiteSpace(c.candidate_name)
                                    ? c.candidate_name!
                                    : "(no messages yet)";

                threads.Add(new ThreadListItemVM(
                    Id: c.conversation_id,
                    JobTitle: c.job_title ?? c.job_listing.job_title,   // cached title if present
                    Participant: participant,
                    LastSnippet: c.last_snippet ?? "",
                    LastAt: lastAt.ToString("yyyy-MM-dd HH:mm"),
                    UnreadCount: c.unread_for_recruiter
                ));
            }

            // sort newest first by cached timestamp string (same format)
            threads = threads.OrderByDescending(t => t.LastAt).ToList();

            ViewBag.Threads = threads;
            return View();
        }

        // GET: /Recruiter/Inbox/Thread/{id}?before=ISO8601
        // Keyset pagination + unread clearing and keeps cached counters in sync
        [HttpGet]
        public async Task<IActionResult> Thread(int id, string? before = null)
        {
            var recruiterId = 3; // TODO

            var conv = await _db.conversations
                .Include(c => c.job_listing)
                .FirstOrDefaultAsync(c =>
                    c.conversation_id == id &&
                    ((c.recruiter_id != null && c.recruiter_id == recruiterId) ||
                     (c.recruiter_id == null && c.job_listing.user_id == recruiterId)));

            if (conv == null) return NotFound();

            // Determine the other participant
            int otherId;
            if (conv.candidate_id.HasValue)
            {
                otherId = conv.candidate_id.Value;
            }
            else
            {
                // Fallback: infer from last message if candidate_id wasn't cached
                var lastNonRecruiter = await _db.messages
                    .Where(m => m.conversation_id == id && m.sender_id != recruiterId)
                    .OrderByDescending(m => m.msg_timestamp)
                    .Select(m => m.sender_id)
                    .FirstOrDefaultAsync();
                otherId = lastNonRecruiter == 0 ? recruiterId : lastNonRecruiter;
            }

            // Load newest N, then “Load older” using keyset on timestamp
            const int pageSize = 50;

            var q = _db.messages
                .Where(m => m.conversation_id == id);

            if (!string.IsNullOrEmpty(before) && DateTime.TryParse(before, out var ts))
                q = q.Where(m => m.msg_timestamp < ts);

            var batch = await q
                .OrderByDescending(m => m.msg_timestamp)
                .Take(pageSize)
                .Include(m => m.sender)
                .Include(m => m.receiver)
                .ToListAsync();

            // oldest-first for display
            var msgsAsc = batch.OrderBy(m => m.msg_timestamp).ToList();

            var vms = msgsAsc.Select(m => new NoteVM(
                Id: m.message_id,
                Author: m.sender_id == recruiterId ? "You" : $"{m.sender.first_name} {m.sender.last_name}".Trim(),
                Text: m.msg_content,
                CreatedAt: m.msg_timestamp.ToString("yyyy-MM-dd HH:mm"),
                FromRecruiter: m.sender_id == recruiterId
            )).ToList();

            // Mark unread as read for recruiter, and clear cached counter
            var unread = await _db.messages
                .Where(m => m.conversation_id == id && m.receiver_id == recruiterId && !m.is_read)
                .ToListAsync();

            if (unread.Count > 0)
            {
                unread.ForEach(m => m.is_read = true);
                conv.unread_for_recruiter = 0; // keep denormalized counter in sync
                await _db.SaveChangesAsync();
            }

            var beforeCursor = batch.Count == pageSize
                ? batch.Last().msg_timestamp.ToString("o")
                : null;

            ViewData["Title"] = $"Thread #{id} — {(conv.job_title ?? conv.job_listing.job_title)}";
            ViewBag.Messages = vms;                 // keep your existing view contract
            ViewBag.ThreadId = id;
            ViewBag.OtherUser = !string.IsNullOrWhiteSpace(conv.candidate_name)
                                ? conv.candidate_name
                                : $"User #{otherId}";
            ViewBag.OtherUserId = otherId;
            ViewBag.BeforeCursor = beforeCursor;

            return View();
        }

        // POST: /Recruiter/Inbox/Send
        // Writes one row and updates conversation summary + unread counters
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Send(int id, string text)
        {
            var recruiterId = 3; // TODO

            if (string.IsNullOrWhiteSpace(text))
            {
                TempData["Message"] = "Message is empty.";
                return RedirectToAction(nameof(Thread), new { id });
            }

            var conv = await _db.conversations
                .Include(c => c.job_listing)
                .FirstOrDefaultAsync(c =>
                    c.conversation_id == id &&
                    ((c.recruiter_id != null && c.recruiter_id == recruiterId) ||
                     (c.recruiter_id == null && c.job_listing.user_id == recruiterId)));

            if (conv == null) return NotFound();

            // Decide receiver: prefer cached candidate_id
            int otherId = conv.candidate_id ?? await _db.messages
                .Where(m => m.conversation_id == id)
                .OrderByDescending(m => m.msg_timestamp)
                .Select(m => m.sender_id == recruiterId ? m.receiver_id : m.sender_id)
                .FirstOrDefaultAsync();

            if (otherId == 0 || otherId == recruiterId)
            {
                // ultimate fallback: first distinct participant not recruiter
                otherId = await _db.messages
                    .Where(m => m.conversation_id == id)
                    .SelectMany(m => new[] { m.sender_id, m.receiver_id })
                    .Where(u => u != recruiterId)
                    .FirstOrDefaultAsync();

                if (otherId == 0)
                {
                    TempData["Message"] = "No recipient found for this thread.";
                    return RedirectToAction(nameof(Thread), new { id });
                }
            }

            var now = DateTime.Now;

            var msg = new message
            {
                conversation_id = id,
                sender_id = recruiterId,
                receiver_id = otherId,
                msg_content = text,
                msg_timestamp = now,
                is_read = false
            };

            _db.messages.Add(msg);

            // Keep denormalized conversation summary & unread counters in sync
            conv.last_message_at = now;
            conv.last_snippet = text.Length > 200 ? text.Substring(0, 200) : text;

            if (otherId == conv.recruiter_id) conv.unread_for_recruiter++;
            else conv.unread_for_candidate++;

            // cache participant info if missing
            if (conv.candidate_id == null && otherId != recruiterId)
            {
                conv.candidate_id = otherId;
                var other = await _db.users
                    .Where(u => u.user_id == otherId)
                    .Select(u => new { u.first_name, u.last_name })
                    .FirstOrDefaultAsync();
                if (other != null)
                    conv.candidate_name = $"{other.first_name} {other.last_name}".Trim();
            }
            if (string.IsNullOrWhiteSpace(conv.job_title))
            {
                conv.job_title = conv.job_listing.job_title;
            }
            if (conv.recruiter_id == null)
            {
                conv.recruiter_id = conv.job_listing.user_id;
            }

            await _db.SaveChangesAsync();

            return RedirectToAction(nameof(Thread), new { id });
        }
    }
}
