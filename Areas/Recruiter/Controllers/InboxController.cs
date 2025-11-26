// ============================================
// File: Areas/Recruiter/Controllers/InboxController.cs
// CHANGES: DI + new Moderate() action; no unrelated edits
// ============================================
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Net.Http;                   // NEW
using System.Net.Http.Headers;           // NEW
using System.Text;                        // NEW
using System.Text.Json;                   // NEW
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;       // NEW
using JobPortal.Areas.Shared.Models;          // DbContext + entities
using JobPortal.Areas.Recruiter.Models;       // VMs
using JobPortal.Areas.Shared.Extensions;      // TryGetUserId extension
using JobPortal.Areas.Shared.Options;         // OpenAIOptions (already registered)
using Microsoft.Extensions.Logging;           // ILogger<T> (NEW)
using JobPortal.Services;                     // INotificationService (NEW)
using JobPortal.Areas.Shared.Models.Extensions; // TryNotifyAsync() (NEW)

namespace JobPortal.Areas.Recruiter.Controllers
{
    [Area("Recruiter")]
    public class InboxController : Controller
    {
        private const int DefaultPageSize = 20;
        private const int MaxPageSize = 100;

        private readonly AppDbContext _db;

        // NEW: for OpenAI moderation
        private readonly IHttpClientFactory _httpFactory;
        private readonly IOptions<OpenAIOptions> _openAi;

        // NEW: notifications + logging
        private readonly INotificationService _notif;
        private readonly ILogger<InboxController> _logger;

        // OLD ctor kept; wire new DI
        public InboxController(AppDbContext db, IHttpClientFactory httpFactory, IOptions<OpenAIOptions> openAi,
                               INotificationService notif, ILogger<InboxController> logger)
        {
            _db = db;
            _httpFactory = httpFactory;
            _openAi = openAi;
            _notif = notif;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> Index(string? q, string? filter, string? sort, int page = 1, int pageSize = DefaultPageSize)
        {
            if (!this.TryGetUserId(out var recruiterId, out var early)) return early!;

            ViewData["Title"] = "Inbox";

            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 5, MaxPageSize);

            sort = (sort ?? "id_desc").Trim().ToLowerInvariant();
            if (sort != "id_asc" && sort != "id_desc") sort = "id_desc";

            var baseQuery = _db.conversations
                .Include(c => c.job_listing)
                .Where(c =>
                    (c.recruiter_id != null && c.recruiter_id == recruiterId) ||
                    (c.recruiter_id == null && c.job_listing.user_id == recruiterId));

            if (filter == "unread")
            {
                baseQuery = baseQuery.Where(c => c.unread_for_recruiter > 0);
            }

            if (!string.IsNullOrWhiteSpace(q))
            {
                var qTrim = q.Trim();
                baseQuery = baseQuery.Where(c =>
                    (c.job_title != null && c.job_title.Contains(qTrim)) ||
                    (c.job_listing.job_title != null && c.job_listing.job_title.Contains(qTrim)) ||
                    (c.candidate_name != null && c.candidate_name.Contains(qTrim)) ||
                    (c.last_snippet != null && c.last_snippet.Contains(qTrim))
                );
            }

            var totalCount = await baseQuery.CountAsync();
            var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
            if (page > totalPages) page = totalPages;
            var skip = (page - 1) * pageSize;

            var ordered = sort == "id_asc"
                ? baseQuery.OrderBy(c => c.conversation_id)
                : baseQuery.OrderByDescending(c => c.conversation_id);

            var convs = await ordered
                .Skip(skip)
                .Take(pageSize)
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

            var vm = new InboxIndexVM
            {
                Items = threads,
                Page = page,
                PageSize = pageSize,
                TotalCount = totalCount,
                TotalPages = totalPages,
                Query = q ?? string.Empty,
                Filter = filter ?? string.Empty,
                Sort = sort
            };

            return View(vm);
        }

        [HttpGet]
        public async Task<IActionResult> Thread(int id, string? before = null, string? draft = null)
        {
            if (!this.TryGetUserId(out var recruiterId, out var early)) return early!;

            var conv = await _db.conversations
                .Include(c => c.job_listing)
                .ThenInclude(j => j.company)
                .FirstOrDefaultAsync(c =>
                    c.conversation_id == id &&
                    ((c.recruiter_id != null && c.recruiter_id == recruiterId) ||
                     (c.recruiter_id == null && c.job_listing.user_id == recruiterId)));

            if (conv == null) return NotFound();

            ViewBag.IsBlocked = conv.is_blocked;
            ViewBag.BlockedReason = conv.blocked_reason ?? "";

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

            var otherFirst = (ViewBag.OtherUser as string ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "there";
            ViewBag.OtherUserFirst = otherFirst;
            ViewBag.JobTitle = conv.job_title ?? conv.job_listing.job_title ?? string.Empty;
            var recruiter = await _db.users.Where(u => u.user_id == recruiterId).Select(u => new { u.first_name, u.last_name }).FirstOrDefaultAsync();
            ViewBag.RecruiterNameFirst = $"{recruiter?.first_name} {recruiter?.last_name}".Trim();
            ViewBag.Company = conv.job_listing.company?.company_name ?? "";

            ViewBag.Draft = string.IsNullOrWhiteSpace(draft) ? null : draft;

            return View();
        }

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
                // If AJAX request, return JSON; otherwise do normal redirect
                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                    return Json(new { ok = true });

                return RedirectToAction(nameof(Thread), new { id });
            }

            var conv = await _db.conversations
                .Include(c => c.job_listing)
                .ThenInclude(j => j.company)
                .FirstOrDefaultAsync(c =>
                    c.conversation_id == id &&
                    ((c.recruiter_id != null && c.recruiter_id == recruiterId) ||
                     (c.recruiter_id == null && c.job_listing.user_id == recruiterId)));

            if (conv == null) return NotFound();

            if (conv.is_blocked)
            {
                var reason = string.IsNullOrWhiteSpace(conv.blocked_reason) ? "Chat is blocked by admin." : $"Chat is blocked by admin: {conv.blocked_reason}";
                TempData["Message"] = reason;
                return RedirectToAction(nameof(Thread), new { id });
            }

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

            var candidateName = conv.candidate_name ?? $"User #{otherId}";
            var firstName = candidateName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "there";
            var jobTitle = conv.job_title ?? conv.job_listing.job_title ?? string.Empty;
            var recruiterRow = await _db.users
                .Where(u => u.user_id == recruiterId)
                .Select(u => new { u.first_name, u.last_name })
                .FirstOrDefaultAsync();
            var recruiterName = $"{recruiterRow?.first_name} {recruiterRow?.last_name}".Trim();
            var company = conv.job_listing.company?.company_name ?? "";

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

            var now = MyTime.NowMalaysia();

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

            // NEW: notify candidate about the new message (non-blocking)
            var preview = processed.Length > 140 ? processed.Substring(0, 140) + "…" : processed;
            await this.TryNotifyAsync(_notif, _logger, () =>
                _notif.SendAsync(
                    userId: otherId,
                    title: "New message from recruiter",
                    message: preview,
                    type: "Message"
                )
            );

            return RedirectToAction(nameof(Thread), new { id });
        }





        // === NEW: Start a new (or reuse existing) conversation by application ===
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> StartFromApplication([FromForm] NewConversationPostVM vm)
        {
            if (!this.TryGetUserId(out var recruiterId, out var early)) return early!;
            if (vm is null || vm.ApplicationId <= 0) return BadRequest();

            // Load application + assert recruiter owns the job
            var app = await _db.job_applications
                .Include(a => a.job_listing)
                .FirstOrDefaultAsync(a => a.application_id == vm.ApplicationId);

            if (app == null) return NotFound();

            var job = app.job_listing;
            if (job == null || job.user_id != recruiterId)
            {
                // Why: recruiter must own the job to chat the applicant
                return Forbid();
            }

            var candidateId = app.user_id;

            // Try find existing thread for this job & candidate
            var existing = await _db.conversations
                .Where(c => c.job_listing_id == job.job_listing_id && c.candidate_id == candidateId)
                .OrderByDescending(c => c.conversation_id)
                .FirstOrDefaultAsync();

            if (existing != null)
            {
                var url = Url.Action(nameof(Thread), new { id = existing.conversation_id });
                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                    return Json(new { ok = true, url });
                return Redirect(url!);
            }


            // Create scaffold conversation
            var candidate = await _db.users
                .Where(u => u.user_id == candidateId)
                .Select(u => new { u.first_name, u.last_name })
                .FirstOrDefaultAsync();

            var now = MyTime.NowMalaysia();

            var conv = new conversation
            {
                job_listing_id = job.job_listing_id,
                recruiter_id = recruiterId,              // set recruiter to owner
                candidate_id = candidateId,
                job_title = job.job_title,
                candidate_name = candidate != null ? $"{candidate.first_name} {candidate.last_name}".Trim() : null,
                created_at = now,
                last_message_at = null,
                last_snippet = null,
                unread_for_candidate = 0,
                unread_for_recruiter = 0,
                is_blocked = false
            };

            _db.conversations.Add(conv);
            await _db.SaveChangesAsync();

            var createdUrl = Url.Action(nameof(Thread), new { id = conv.conversation_id });
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                return Json(new { ok = true, url = createdUrl });
            return Redirect(createdUrl!);
        }


        // === NEW: check if a thread already exists for this application (JSON only) ===
        [HttpGet]
        public async Task<IActionResult> ExistsForApplication(int applicationId)
        {
            if (!this.TryGetUserId(out var recruiterId, out var early)) return early!;

            var app = await _db.job_applications
                .Include(a => a.job_listing)
                .FirstOrDefaultAsync(a => a.application_id == applicationId);

            if (app == null || app.job_listing.user_id != recruiterId)
                return Json(new { exists = false });

            var existing = await _db.conversations
                .Where(c => c.job_listing_id == app.job_listing_id && c.candidate_id == app.user_id)
                .OrderByDescending(c => c.conversation_id)
                .FirstOrDefaultAsync();

            if (existing == null) return Json(new { exists = false });

            var url = Url.Action(nameof(Thread), new { id = existing.conversation_id });
            return Json(new { exists = true, url });
        }







        // Areas/Recruiter/Controllers/InboxController.cs
        // === Client-side AI message monitoring endpoint (AI-only + robust JSON parse, fail-open) ===
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Moderate([FromBody] MessageModerationCheckRequestVM req)
        {
            if (!this.TryGetUserId(out var recruiterId, out var early)) return early!;
            if (req is null || string.IsNullOrWhiteSpace(req.Text))
                return Json(new MessageModerationCheckResultVM { Allowed = false, Reason = "Message is empty.", Category = "Empty" });

            // Ownership check
            var conv = await _db.conversations
                .Include(c => c.job_listing)
                .FirstOrDefaultAsync(c =>
                    c.conversation_id == req.ThreadId &&
                    ((c.recruiter_id != null && c.recruiter_id == recruiterId) ||
                     (c.recruiter_id == null && c.job_listing.user_id == recruiterId)));
            if (conv == null)
                return Json(new MessageModerationCheckResultVM { Allowed = false, Reason = "Thread not found or access denied.", Category = "Access" });

            // Admin block
            if (conv.is_blocked)
            {
                var reason = string.IsNullOrWhiteSpace(conv.blocked_reason)
                    ? "Chat is blocked by admin."
                    : $"Chat is blocked by admin: {conv.blocked_reason}";
                return Json(new MessageModerationCheckResultVM { Allowed = false, Reason = reason, Category = "AdminBlock" });
            }

            // OpenAI config
            var opts = _openAi.Value;
            if (string.IsNullOrWhiteSpace(opts.ApiKey))
                return Json(new MessageModerationCheckResultVM { Allowed = true, Reason = "AI check unavailable (no API key).", Category = "Bypass" });

            var http = _httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(Math.Max(1, opts.TimeoutSeconds));
            http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", opts.ApiKey);

            var modelName = string.IsNullOrWhiteSpace(opts.ModelText) ? "gpt-4o-mini" : opts.ModelText;

            var payload = new
            {
                model = modelName,
                temperature = 0,
                response_format = new { type = "json_object" },
                messages = new object[]
                {
            new { role = "system", content =
                "You are the sole content policy judge for a recruitment chat. " +
                "Given ONE outbound recruiter message, decide if it violates policy. " +
                "Return STRICT JSON ONLY: {\"allowed\": boolean, \"reason\": string, \"category\": string}. " +
                "Use categories: OffPlatform, Payment, PersonalData, Threat, Harassment, Discrimination, Explicit, Spam, Other. " +
                "Block only for clear violations; neutral/safety reminders are allowed." },
            // Few-shot calibration (examples only)
            new { role = "user", content = "We’ll conduct the interview via this platform. No payment is required at any stage." },
            new { role = "assistant", content = "{\"allowed\": true, \"reason\": \"Platform/safety reminder.\", \"category\": \"Other\"}" },
            new { role = "user", content = "Share your phone number or WhatsApp so we can talk there." },
            new { role = "assistant", content = "{\"allowed\": false, \"reason\": \"Requests moving conversation off-platform (phone/WhatsApp).\", \"category\": \"OffPlatform\"}" },
            new { role = "user", content = "There is a placement fee. Send $50 to proceed." },
            new { role = "assistant", content = "{\"allowed\": false, \"reason\": \"Requests payment from candidate.\", \"category\": \"Payment\"}" },
            // Message to evaluate
            new { role = "user", content = req.Text.Trim() }
                }
            };

            using var content = new StringContent(System.Text.Json.JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8, "application/json");

            try
            {
                using var resp = await http.PostAsync("https://api.openai.com/v1/chat/completions", content);
                var body = await resp.Content.ReadAsStringAsync();
                if (!resp.IsSuccessStatusCode)
                    return Json(new MessageModerationCheckResultVM { Allowed = true, Reason = "AI check temporarily unavailable.", Category = "Bypass" });

                // Robust parse: explicitly read 'allowed', 'reason', 'category'; if missing -> allow (fail-open)
                string? msg = null;
                try
                {
                    using var top = System.Text.Json.JsonDocument.Parse(body);
                    msg = top.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
                }
                catch
                {
                    return Json(new MessageModerationCheckResultVM { Allowed = true, Reason = "AI response malformed.", Category = "Bypass" });
                }

                bool allowed = true; // fail-open default
                string reason = "Safe.";
                string category = "Other";

                try
                {
                    using var md = System.Text.Json.JsonDocument.Parse(string.IsNullOrWhiteSpace(msg) ? "{}" : msg!);
                    var root = md.RootElement;

                    if (root.ValueKind == System.Text.Json.JsonValueKind.Object &&
                        root.TryGetProperty("allowed", out var aProp) &&
                        (aProp.ValueKind == System.Text.Json.JsonValueKind.True || aProp.ValueKind == System.Text.Json.JsonValueKind.False))
                    {
                        allowed = aProp.GetBoolean();
                    }
                    // If "allowed" missing or not a bool → keep default true (fail-open)

                    if (root.TryGetProperty("reason", out var rProp) && rProp.ValueKind == System.Text.Json.JsonValueKind.String)
                        reason = rProp.GetString() ?? reason;

                    if (root.TryGetProperty("category", out var cProp) && cProp.ValueKind == System.Text.Json.JsonValueKind.String)
                        category = cProp.GetString() ?? category;
                }
                catch
                {
                    // Keep fail-open defaults
                }

                return Json(new MessageModerationCheckResultVM { Allowed = allowed, Reason = reason, Category = category });
            }
            catch
            {
                return Json(new MessageModerationCheckResultVM { Allowed = true, Reason = "AI check timed out.", Category = "Bypass" });
            }
        }



        private static string ReplaceInsensitive(string input, string search, string replace)
            => Regex.Replace(input,
                             Regex.Escape(search),
                             replace.Replace("$", "$$"),
                             RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
