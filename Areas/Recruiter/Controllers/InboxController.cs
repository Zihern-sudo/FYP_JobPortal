// File: Areas/Recruiter/Controllers/InboxController.cs
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using JobPortal.Areas.Shared.Models;          // DbContext + entities
using JobPortal.Areas.Recruiter.Models;       // VMs
using JobPortal.Areas.Shared.Extensions;      // TryGetUserId extension

namespace JobPortal.Areas.Recruiter.Controllers
{
    [Area("Recruiter")]
    public class InboxController : Controller
    {
        private readonly AppDbContext _db;
        public InboxController(AppDbContext db) => _db = db;

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            if (!this.TryGetUserId(out var recruiterId, out var early)) return early!;

            ViewData["Title"] = "Inbox";

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
                    JobTitle: c.job_title ?? c.job_listing.job_title,
                    Participant: participant,
                    LastSnippet: c.last_snippet ?? "",
                    LastAt: lastAt.ToString("yyyy-MM-dd HH:mm"),
                    UnreadCount: c.unread_for_recruiter
                ));
            }

            threads = threads.OrderByDescending(t => t.LastAt).ToList();

            ViewBag.Threads = threads;
            return View();
        }

        // GET: /Recruiter/Inbox/Thread/{id}?before=ISO8601&draft=...
        [HttpGet]
        public async Task<IActionResult> Thread(int id, string? before = null, string? draft = null)
        {
            if (!this.TryGetUserId(out var recruiterId, out var early)) return early!;

            var conv = await _db.conversations
                .Include(c => c.job_listing)
                .ThenInclude(j => j.company) // needed for company_name
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
                var lastNonRecruiter = await _db.messages
                    .Where(m => m.conversation_id == id && m.sender_id != recruiterId)
                    .OrderByDescending(m => m.msg_timestamp)
                    .Select(m => m.sender_id)
                    .FirstOrDefaultAsync();
                otherId = lastNonRecruiter == 0 ? recruiterId : lastNonRecruiter;
            }

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

            var msgsAsc = batch.OrderBy(m => m.msg_timestamp).ToList();

            var vms = msgsAsc.Select(m => new NoteVM(
                Id: m.message_id,
                Author: m.sender_id == recruiterId ? "You" : $"{m.sender.first_name} {m.sender.last_name}".Trim(),
                Text: m.msg_content,
                CreatedAt: m.msg_timestamp.ToString("yyyy-MM-dd HH:mm"),
                FromRecruiter: m.sender_id == recruiterId
            )).ToList();

            var unread = await _db.messages
                .Where(m => m.conversation_id == id && m.receiver_id == recruiterId && !m.is_read)
                .ToListAsync();

            if (unread.Count > 0)
            {
                unread.ForEach(m => m.is_read = true);
                conv.unread_for_recruiter = 0;
                await _db.SaveChangesAsync();
            }

            var beforeCursor = batch.Count == pageSize
                ? batch.Last().msg_timestamp.ToString("o")
                : null;

            ViewData["Title"] = $"Thread #{id} — {(conv.job_title ?? conv.job_listing.job_title)}";
            ViewBag.Messages = vms;
            ViewBag.ThreadId = id;
            ViewBag.OtherUser = !string.IsNullOrWhiteSpace(conv.candidate_name)
                                ? conv.candidate_name
                                : $"User #{otherId}";
            ViewBag.OtherUserId = otherId;
            ViewBag.BeforeCursor = beforeCursor;

            // expose helpers for client-side placeholder assist
            var otherFirst = (ViewBag.OtherUser as string ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "there";
            ViewBag.OtherUserFirst = otherFirst;
            ViewBag.JobTitle = conv.job_title ?? conv.job_listing.job_title ?? string.Empty;
            var recruiter = await _db.users.Where(u => u.user_id == recruiterId).Select(u => new { u.first_name, u.last_name }).FirstOrDefaultAsync();
            ViewBag.RecruiterNameFirst = $"{recruiter?.first_name} {recruiter?.last_name}".Trim();
            ViewBag.Company = conv.job_listing.company?.company_name ?? ""; // FIX: company is on job_listing.company

            // prefill message box if redirected from Templates/Fill
            ViewBag.Draft = string.IsNullOrWhiteSpace(draft) ? null : draft;

            return View();
        }

        // POST: /Recruiter/Inbox/Send
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Send(
            int id,
            string text,
            string? date = null,
            string? time = null,
            Dictionary<string, string>? tokens = null)
        {
            if (!this.TryGetUserId(out var recruiterId, out var early)) return early!;

            if (string.IsNullOrWhiteSpace(text))
            {
                TempData["Message"] = "Message is empty.";
                return RedirectToAction(nameof(Thread), new { id });
            }

            var conv = await _db.conversations
                .Include(c => c.job_listing)
                .ThenInclude(j => j.company) // FIX: need company for placeholder
                .FirstOrDefaultAsync(c =>
                    c.conversation_id == id &&
                    ((c.recruiter_id != null && c.recruiter_id == recruiterId) ||
                     (c.recruiter_id == null && c.job_listing.user_id == recruiterId)));

            if (conv == null) return NotFound();

            int otherId = conv.candidate_id ?? await _db.messages
                .Where(m => m.conversation_id == id)
                .OrderByDescending(m => m.msg_timestamp)
                .Select(m => m.sender_id == recruiterId ? m.receiver_id : m.sender_id)
                .FirstOrDefaultAsync();

            if (otherId == 0 || otherId == recruiterId)
            {
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

            // --- Smart token merge ---
            var candidateName = conv.candidate_name ?? $"User #{otherId}";
            var firstName = candidateName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "there";
            var jobTitle = conv.job_title ?? conv.job_listing.job_title ?? string.Empty;
            var recruiterRow = await _db.users
                .Where(u => u.user_id == recruiterId)
                .Select(u => new { u.first_name, u.last_name })
                .FirstOrDefaultAsync();
            var recruiterName = $"{recruiterRow?.first_name} {recruiterRow?.last_name}".Trim();
            var company = conv.job_listing.company?.company_name ?? ""; // FIX

            var context = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["FirstName"] = firstName,
                ["JobTitle"] = jobTitle,
                ["RecruiterName"] = recruiterName,
                ["Company"] = company,
                ["Date"] = date ?? "",
                ["DueDate"] = tokens != null && tokens.TryGetValue("DueDate", out var dd) ? dd : "",
                ["Time"] = time ?? ""
            };

            tokens ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            string processed = text;

            processed = ReplaceInsensitive(processed, "Hi User", $"Hi {firstName}");

            var tokenRegex = new Regex(@"\{\{\s*([A-Za-z0-9_]+)\s*\}\}", RegexOptions.Compiled);
            var matches = tokenRegex.Matches(processed);
            foreach (Match m in matches)
            {
                var token = m.Groups[1].Value;
                string? value = null;

                if (tokens.TryGetValue(token, out var v1) && !string.IsNullOrWhiteSpace(v1))
                    value = v1.Trim();
                else if (context.TryGetValue(token, out var v2) && !string.IsNullOrWhiteSpace(v2))
                    value = v2.Trim();

                if (value != null)
                {
                    processed = ReplaceInsensitive(processed, "{{" + token + "}}", value);
                }
            }

            var now = DateTime.Now;

            var msg = new message
            {
                conversation_id = id,
                sender_id = recruiterId,
                receiver_id = otherId,
                msg_content = processed,
                msg_timestamp = now,
                is_read = false
            };

            _db.messages.Add(msg);

            conv.last_message_at = now;
            conv.last_snippet = processed.Length > 200 ? processed.Substring(0, 200) : processed;

            if (otherId == conv.recruiter_id) conv.unread_for_recruiter++;
            else conv.unread_for_candidate++;

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

        // WHY: case-insensitive, culture-invariant token replacement
        private static string ReplaceInsensitive(string input, string search, string replace)
            => Regex.Replace(input,
                             Regex.Escape(search),
                             replace.Replace("$", "$$"),
                             RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
