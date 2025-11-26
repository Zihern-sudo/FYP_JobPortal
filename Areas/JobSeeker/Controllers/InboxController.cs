// File: Areas/JobSeeker/Controllers/InboxController.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using JobPortal.Areas.JobSeeker.Models;
using JobPortal.Areas.Shared.Models;
using JobPortal.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using JobPortal.Areas.Shared.Options; // OpenAIOptions
using JobPortal.Areas.Shared.Extensions;


namespace JobPortal.Areas.JobSeeker.Controllers
{
    [Area("JobSeeker")]
    public class InboxController : Controller
    {
        private readonly AppDbContext _db;
        private readonly ChatbotService _chatbot;
        private const int DefaultPageSize = 10;
        private const int MaxPageSize = 20;

        private readonly IHttpClientFactory _httpFactory;     // NEW
        private readonly IOptions<OpenAIOptions> _openAi;      // NEW


        public InboxController(ChatbotService chatbot, AppDbContext db, IHttpClientFactory httpFactory, IOptions<OpenAIOptions> openAi)
        {
            _chatbot = chatbot;
            _db = db;
            _httpFactory = httpFactory;      // NEW
            _openAi = openAi;                // NEW
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
        public async Task<IActionResult> Thread(int id, string? prefill = null)
        {
            var userIdStr = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrEmpty(userIdStr))
                return RedirectToAction("Login", "Account", new { area = "JobSeeker" });

            int candidateId = int.Parse(userIdStr);

            var convo = await _db.conversations
                .FirstOrDefaultAsync(c => c.conversation_id == id && c.candidate_id == candidateId);

            if (convo == null)
                return NotFound();

            // NEW: expose block state to the view
            ViewBag.IsBlocked = convo.is_blocked;
            ViewBag.BlockedReason = convo.blocked_reason ?? string.Empty;

            // Get messages linked to this conversation
            var messages = await _db.messages
                .Where(m => m.conversation_id == convo.conversation_id)
                .OrderBy(m => m.msg_timestamp)
                .ToListAsync();

            // Mark unread messages as read (keep existing behavior)
            var unread = messages.Where(m => m.receiver_id == candidateId && m.is_read == false).ToList();
            if (unread.Any())
            {
                unread.ForEach(m => m.is_read = true);
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

            // Prefilled message to the View
            ViewBag.PrefillMessage = prefill;

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

            // NEW: hard block enforcement
            if (convo.is_blocked)
            {
                TempData["Message"] = string.IsNullOrWhiteSpace(convo.blocked_reason)
                    ? "Chat is blocked by admin."
                    : $"Chat is blocked by admin: {convo.blocked_reason}";
                return RedirectToAction("Thread", new { id = vm.ThreadId });
            }

            // Server-side AI moderation (backstop if JS fails)
            var ai = await CheckAiForCandidateAsync(vm.ThreadId, candidateId, vm.MessageText ?? "");
            if (!ai.Allowed)
            {
                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                    return Json(new { ok = false, reason = ai.Reason, category = ai.Category });

                TempData["Message"] = $"Message blocked: {ai.Reason}";
                return RedirectToAction("Thread", new { id = vm.ThreadId });
            }


            // Create new message
            var message = new message
            {
                conversation_id = vm.ThreadId,
                sender_id = candidateId,
                receiver_id = convo.recruiter_id ?? 0,
                msg_content = vm.MessageText,
                msg_timestamp = MyTime.NowMalaysia(),
                is_read = false
            };

            _db.messages.Add(message);

            // Update conversation
            convo.last_message_at = MyTime.NowMalaysia();
            convo.last_snippet = vm.MessageText.Length > 100 ? vm.MessageText.Substring(0, 100) : vm.MessageText;
            convo.unread_for_recruiter += 1;

            await _db.SaveChangesAsync();
            TempData["Message"] = "Message sent successfully!";

            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                return Json(new { ok = true });

            return RedirectToAction("Thread", new { id = vm.ThreadId });

        }

        // === NEW: AI moderation for outbound jobseeker messages ===
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Moderate([FromBody] MessageModerationCheckRequestVM req)
        {
            var userIdStr = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrEmpty(userIdStr))
                return Json(new MessageModerationCheckResultVM { Allowed = false, Reason = "Not logged in.", Category = "Auth" });

            int candidateId = int.Parse(userIdStr);

            if (req is null || string.IsNullOrWhiteSpace(req.Text))
                return Json(new MessageModerationCheckResultVM { Allowed = false, Reason = "Message is empty.", Category = "Empty" });

            // Ownership check
            var conv = await _db.conversations
                .FirstOrDefaultAsync(c => c.conversation_id == req.ThreadId && c.candidate_id == candidateId);

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
                "You are the content policy judge for a recruitment chat. " +
                "Given ONE outbound candidate message, decide if it violates policy. " +
                "Return STRICT JSON ONLY: {\"allowed\": boolean, \"reason\": string, \"category\": string}. " +
                "Use categories: OffPlatform, Payment, PersonalData, Threat, Harassment, Discrimination, Explicit, Spam, Other. " +
                "Block only for clear violations; neutral/safety reminders are allowed." },
            new { role = "user", content = req.Text.Trim() }
                }
            };

            using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            try
            {
                using var resp = await http.PostAsync("https://api.openai.com/v1/chat/completions", content);
                var body = await resp.Content.ReadAsStringAsync();
                if (!resp.IsSuccessStatusCode)
                    return Json(new MessageModerationCheckResultVM { Allowed = true, Reason = "AI check temporarily unavailable.", Category = "Bypass" });

                string? msg = null;
                try
                {
                    using var top = JsonDocument.Parse(body);
                    msg = top.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
                }
                catch
                {
                    return Json(new MessageModerationCheckResultVM { Allowed = true, Reason = "AI response malformed.", Category = "Bypass" });
                }

                bool allowed = true;
                string reason = "Safe.";
                string category = "Other";
                try
                {
                    using var md = JsonDocument.Parse(string.IsNullOrWhiteSpace(msg) ? "{}" : msg!);
                    var root = md.RootElement;
                    if (root.ValueKind == JsonValueKind.Object &&
                        root.TryGetProperty("allowed", out var aProp) &&
                        (aProp.ValueKind == JsonValueKind.True || aProp.ValueKind == JsonValueKind.False))
                        allowed = aProp.GetBoolean();

                    if (root.TryGetProperty("reason", out var rProp) && rProp.ValueKind == JsonValueKind.String)
                        reason = rProp.GetString() ?? reason;
                    if (root.TryGetProperty("category", out var cProp) && cProp.ValueKind == JsonValueKind.String)
                        category = cProp.GetString() ?? category;
                }
                catch { /* fail-open */ }

                return Json(new MessageModerationCheckResultVM { Allowed = allowed, Reason = reason, Category = category });
            }
            catch
            {
                return Json(new MessageModerationCheckResultVM { Allowed = true, Reason = "AI check timed out.", Category = "Bypass" });
            }
        }

        // Server-side enforcement: reuse OpenAI moderation for candidate messages
        private async Task<(bool Allowed, string Reason, string Category)> CheckAiForCandidateAsync(int threadId, int candidateId, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return (false, "Message is empty.", "Empty");

            var conv = await _db.conversations
                .FirstOrDefaultAsync(c => c.conversation_id == threadId && c.candidate_id == candidateId);
            if (conv == null) return (false, "Thread not found or access denied.", "Access");
            if (conv.is_blocked)
            {
                var reason = string.IsNullOrWhiteSpace(conv.blocked_reason)
                    ? "Chat is blocked by admin."
                    : $"Chat is blocked by admin: {conv.blocked_reason}";
                return (false, reason, "AdminBlock");
            }

            var opts = _openAi.Value;
            if (string.IsNullOrWhiteSpace(opts.ApiKey))
                return (true, "AI check unavailable (no API key).", "Bypass");

            var http = _httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(Math.Max(1, opts.TimeoutSeconds));
            http.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", opts.ApiKey);
            var modelName = string.IsNullOrWhiteSpace(opts.ModelText) ? "gpt-4o-mini" : opts.ModelText;

            var payload = new
            {
                model = modelName,
                temperature = 0,
                response_format = new { type = "json_object" },
                messages = new object[]
                {
            new { role = "system", content =
                "You are the content policy judge for a recruitment chat. " +
                "Given ONE outbound candidate message, decide if it violates policy. " +
                "Return STRICT JSON ONLY: {\"allowed\": boolean, \"reason\": string, \"category\": string}. " +
                "Use categories: OffPlatform, Payment, PersonalData, Threat, Harassment, Discrimination, Explicit, Spam, Other. " +
                "Block only for clear violations; neutral/safety reminders are allowed." },
            new { role = "user", content = text.Trim() }
                }
            };

            using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            try
            {
                using var resp = await http.PostAsync("https://api.openai.com/v1/chat/completions", content);
                var body = await resp.Content.ReadAsStringAsync();
                if (!resp.IsSuccessStatusCode) return (true, "AI check temporarily unavailable.", "Bypass");

                string? msg;
                try
                {
                    using var top = JsonDocument.Parse(body);
                    msg = top.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
                }
                catch { return (true, "AI response malformed.", "Bypass"); }

                bool allowed = true; string reason = "Safe."; string category = "Other";
                try
                {
                    using var md = JsonDocument.Parse(string.IsNullOrWhiteSpace(msg) ? "{}" : msg!);
                    var root = md.RootElement;
                    if (root.ValueKind == JsonValueKind.Object &&
                        root.TryGetProperty("allowed", out var aProp) &&
                        (aProp.ValueKind == JsonValueKind.True || aProp.ValueKind == JsonValueKind.False))
                        allowed = aProp.GetBoolean();
                    if (root.TryGetProperty("reason", out var rProp) && rProp.ValueKind == JsonValueKind.String)
                        reason = rProp.GetString() ?? reason;
                    if (root.TryGetProperty("category", out var cProp) && cProp.ValueKind == JsonValueKind.String)
                        category = cProp.GetString() ?? category;
                }
                catch { /* keep defaults */ }

                return (allowed, reason, category);
            }
            catch { return (true, "AI check timed out.", "Bypass"); }
        }



        [HttpPost]
        public async Task<IActionResult> AskAI(string question)
        {
            var answer = await _chatbot.AskAsync(question);
            return Json(new { reply = answer });
        }
    }
}
