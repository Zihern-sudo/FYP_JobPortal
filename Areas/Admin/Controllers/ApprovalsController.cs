// File: Areas/Admin/Controllers/ApprovalsController.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using JobPortal.Areas.Shared.Models;
using JobPortal.Areas.Admin.Models;
using JobPortal.Areas.Shared.Extensions; // TryGetUserId + MyTime
using System;
using System.Linq;
using System.Collections.Generic;

// NEW
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using System.Threading.Tasks;
using System.Net.Http.Headers;
using JobPortal.Areas.Shared.Options; // OpenAIOptions (existing in app)
using Microsoft.Extensions.Caching.Memory; // NEW for server-side cache
using JobPortal.Services; // <-- NEW: notifications

namespace JobPortal.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class ApprovalsController : Controller
    {
        private readonly AppDbContext _db;

        // NEW: use existing OpenAI config + factory
        private readonly IHttpClientFactory _httpFactory;
        private readonly IOptions<OpenAIOptions> _openAi;

        // NEW: server-side cache
        private readonly IMemoryCache _cache;

        // NEW: notifications
        private readonly INotificationService _notif;

        // UPDATED: inject http client + options + memory cache (no other changes)
        public ApprovalsController(AppDbContext db, IHttpClientFactory httpFactory, IOptions<OpenAIOptions> openAi, IMemoryCache cache, INotificationService notif)
        {
            _db = db;
            _httpFactory = httpFactory;
            _openAi = openAi;
            _cache = cache;
            _notif = notif;
        }

        // GET: /Admin/Approvals?status=All&q=&page=1&pageSize=10
        public IActionResult Index(string? status = "All", string? q = null, int page = 1, int pageSize = 10, string? sort = null)
        {
            ViewData["Title"] = "Job Approvals";

            // normalize sort (default newest first by ID)
            sort = (sort ?? "id_desc").Trim().ToLowerInvariant();
            if (sort != "id_asc" && sort != "id_desc") sort = "id_desc";

            // -----------------------------------------------------------------
            // Only show the LATEST approval per job_listing_id to avoid dups
            // -----------------------------------------------------------------
            var latestApprovalIds = _db.job_post_approvals
                .AsNoTracking()
                .GroupBy(a => a.job_listing_id)
                .Select(g => g.Max(x => x.approval_id));

            // Base query restricted to latest records only
            var baseQuery = _db.job_post_approvals
                .AsNoTracking()
                .Where(a => latestApprovalIds.Contains(a.approval_id))
                .Include(a => a.job_listing!.company)
                .AsQueryable();

            // Counts for tabs (computed on latest only)
            var countData = baseQuery
                .GroupBy(a => a.approval_status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToList();

            int pending = countData.FirstOrDefault(x => x.Status == "Pending")?.Count ?? 0;
            int approved = countData.FirstOrDefault(x => x.Status == "Approved")?.Count ?? 0;
            int changes = countData.FirstOrDefault(x => x.Status == "ChangesRequested")?.Count ?? 0;
            int rejected = countData.FirstOrDefault(x => x.Status == "Rejected")?.Count ?? 0;

            // Apply filters on the latest-only set
            var query = baseQuery;

            if (!string.IsNullOrWhiteSpace(status) && status != "All")
                query = query.Where(a => a.approval_status == status);

            if (!string.IsNullOrWhiteSpace(q))
            {
                var like = $"%{q}%";
                query = query.Where(a =>
                    EF.Functions.Like(a.job_listing!.job_title, like) ||
                    EF.Functions.Like(a.job_listing!.company!.company_name, like));
            }

            // Apply ID ordering per toggle (default: id_desc)
            query = sort == "id_asc"
                ? query.OrderBy(a => a.approval_id)
                : query.OrderByDescending(a => a.approval_id);

            var total = query.Count();

            // Build page items (submitted date pulled from job_listing.date_posted which you keep updated)
            var items = query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .AsEnumerable() // switch to client-side so MyTime works if needed later
                .Select(a => new ApprovalRow
                {
                    Id = a.approval_id,
                    JobId = a.job_listing_id,
                    JobTitle = a.job_listing!.job_title,
                    Company = a.job_listing!.company!.company_name,
                    Status = a.approval_status, // Pending/Approved/ChangesRequested/Rejected
                    Date = a.job_listing!.date_posted // always latest submission time
                })
                .ToList();

            // map approver names for current page (only Approved items)
            var pageIds = items.Select(i => i.Id).ToArray();
            var approvers = _db.job_post_approvals
                .AsNoTracking()
                .Where(a => pageIds.Contains(a.approval_id) && a.approval_status == "Approved")
                .Select(a => new
                {
                    a.approval_id,
                    First = a.user.first_name,
                    Last = a.user.last_name
                })
                .ToList()
                .ToDictionary(x => x.approval_id, x => $"{(x.First ?? "").Trim()} {(x.Last ?? "").Trim()}".Trim());
            ViewBag.Approvers = approvers;

            var vm = new ApprovalsIndexViewModel
            {
                Status = status ?? "All",
                Query = q ?? string.Empty,
                PendingCount = pending,
                ApprovedCount = approved,
                ChangesRequestedCount = changes,
                RejectedCount = rejected,
                Items = new PagedResult<ApprovalRow>
                {
                    Items = items,
                    Page = page,
                    PageSize = pageSize,
                    TotalItems = total
                },
                // pass sort to view
                Sort = sort
            };

            return View(vm);
        }

        public IActionResult Preview(int id)
        {
            ViewData["Title"] = "Job Preview";

            var item = _db.job_post_approvals
                .AsNoTracking()
                .Include(a => a.job_listing!.company)
                .Where(a => a.approval_id == id)
                .AsEnumerable() // for MyTime conversion
                .Select(a => new ApprovalPreviewViewModel
                {
                    Id = a.approval_id,
                    JobId = a.job_listing_id,
                    JobTitle = a.job_listing!.job_title,
                    Company = a.job_listing!.company!.company_name,
                    Status = a.approval_status,
                    // Convert stored time to Malaysia time for display
                    Date = a.job_listing!.date_posted,
                    JobDescription = a.job_listing!.job_description,
                    JobRequirements = a.job_listing!.job_requirements,
                    Comments = a.comments
                })
                .FirstOrDefault();

            if (item == null) return NotFound();

            // provide company photo path to the view without changing VM
            var photo = _db.job_post_approvals
                .AsNoTracking()
                .Where(a => a.approval_id == id)
                .Select(a => a.job_listing!.company!.company_photo)
                .FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(photo))
                ViewBag.CompanyPhotoUrl = photo;

            // approver name for Approved items
            if (string.Equals(item.Status, "Approved", StringComparison.OrdinalIgnoreCase))
            {
                var approver = _db.job_post_approvals
                    .AsNoTracking()
                    .Where(a => a.approval_id == id)
                    .Select(a => new { a.user.first_name, a.user.last_name })
                    .FirstOrDefault();
                if (approver != null)
                    ViewBag.ApprovedByName = $"{(approver.first_name ?? "").Trim()} {(approver.last_name ?? "").Trim()}".Trim();
            }

            return View(item);
        }

        // --- Single-item Actions -----------------------------------------------------

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Approve(int id, string? comments)
        {
            if (!this.TryGetUserId(out var adminId, out var early)) return early!;

            // Include company so we can persist status
            var approval = _db.job_post_approvals
                .Include(a => a.job_listing!)
                    .ThenInclude(j => j.company)
                .FirstOrDefault(a => a.approval_id == id);
            if (approval == null) return NotFound();

            var nowMy = MyTime.NowMalaysia();

            approval.approval_status = "Approved";
            approval.date_approved = nowMy;
            approval.user_id = adminId; // persist approver
            if (!string.IsNullOrWhiteSpace(comments))
                approval.comments = comments;

            if (approval.job_listing!.job_status == "Draft")
            {
                approval.job_listing.job_status = "Open";
                // persist: company is Active when at least one Open job exists
                if (approval.job_listing.company != null)
                    approval.job_listing.company.company_status = "Active";
            }

            _db.admin_logs.Add(new admin_log
            {
                user_id = adminId,
                action_type = "Admin.Job.Approve",
                timestamp = nowMy
            });

            _db.SaveChanges();

            // --- Notify recruiter (owner) ---
            try
            {
                var ownerId = approval.job_listing!.user_id;
                if (ownerId > 0)
                {
                    var title = "Job approved";
                    var msg = $"Your job \"{approval.job_listing!.job_title}\" has been approved and is now live.";
                    _notif.SendAsync(ownerId, title, msg, type: "Approval").GetAwaiter().GetResult();
                }
            }
            catch { /* best effort */ }

            TempData["Flash.Type"] = "success";
            TempData["Flash.Message"] = "Job approved.";
            return RedirectToAction(nameof(Preview), new { id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Reject(int id, string? comments)
        {
            if (!this.TryGetUserId(out var adminId, out var early)) return early!;

            var approval = _db.job_post_approvals
                .Include(a => a.job_listing)
                .FirstOrDefault(a => a.approval_id == id);
            if (approval == null) return NotFound();

            var nowMy = MyTime.NowMalaysia();

            approval.approval_status = "Rejected";
            approval.date_approved = null;
            if (!string.IsNullOrWhiteSpace(comments))
                approval.comments = comments;

            if (approval.job_listing!.job_status == "Open")
                approval.job_listing.job_status = "Paused";

            _db.admin_logs.Add(new admin_log
            {
                user_id = adminId,
                action_type = "Admin.Job.Reject",
                timestamp = nowMy
            });

            _db.SaveChanges();

            // --- Notify recruiter (owner) ---
            try
            {
                var ownerId = approval.job_listing!.user_id;
                if (ownerId > 0)
                {
                    var title = "Job rejected";
                    var reason = string.IsNullOrWhiteSpace(comments) ? "" : $" Reason: {comments.Trim()}";
                    var msg = $"Your job \"{approval.job_listing!.job_title}\" was rejected.{reason}";
                    _notif.SendAsync(ownerId, title, msg, type: "Review").GetAwaiter().GetResult();
                }
            }
            catch { /* best effort */ }

            TempData["Flash.Type"] = "danger";
            TempData["Flash.Message"] = "Job rejected.";
            return RedirectToAction(nameof(Preview), new { id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult RequestChanges(int id, string? comments)
        {
            if (!this.TryGetUserId(out var adminId, out var early)) return early!;

            var approval = _db.job_post_approvals
                .Include(a => a.job_listing)
                .FirstOrDefault(a => a.approval_id == id);
            if (approval == null) return NotFound();

            var nowMy = MyTime.NowMalaysia();

            approval.approval_status = "ChangesRequested";
            approval.date_approved = null;
            if (!string.IsNullOrWhiteSpace(comments))
                approval.comments = comments;

            if (approval.job_listing!.job_status != "Closed")
                approval.job_listing.job_status = "Draft";

            _db.admin_logs.Add(new admin_log
            {
                user_id = adminId,
                action_type = "Admin.Job.RequestChanges",
                timestamp = nowMy
            });

            _db.SaveChanges();

            // --- Notify recruiter (owner) ---
            try
            {
                var ownerId = approval.job_listing!.user_id;
                if (ownerId > 0)
                {
                    var title = "Changes requested for job";
                    var extra = string.IsNullOrWhiteSpace(comments) ? "" : $" Details: {comments.Trim()}";
                    var msg = $"Your job \"{approval.job_listing!.job_title}\" needs changes before it can go live.{extra}";
                    _notif.SendAsync(ownerId, title, msg, type: "Review").GetAwaiter().GetResult();
                }
            }
            catch { /* best effort */ }

            TempData["Flash.Type"] = "warning";
            TempData["Flash.Message"] = "Changes requested from recruiter.";
            return RedirectToAction(nameof(Preview), new { id });
        }

        // --- BULK ACTIONS ------------------------------------------------------------

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Bulk(string actionType, int[] ids, string? comments, string? returnStatus, string? q, int page = 1, int pageSize = 10)
        {
            if (!this.TryGetUserId(out var adminId, out var early)) return early!;

            if (ids == null || ids.Length == 0)
            {
                TempData["Flash.Type"] = "warning";
                TempData["Flash.Message"] = "Select at least one row.";
                return RedirectToAction(nameof(Index), new { status = returnStatus, q, page, pageSize });
            }

            // Load approvals + job + company for persistence
            var approvals = _db.job_post_approvals
                .Include(a => a.job_listing!)
                    .ThenInclude(j => j.company)
                .Where(a => ids.Contains(a.approval_id))
                .ToList();

            var now = MyTime.NowMalaysia();
            int changed = 0;

            foreach (var a in approvals)
            {
                switch (actionType)
                {
                    case "Approve":
                        a.approval_status = "Approved";
                        a.date_approved = now;
                        a.user_id = adminId; // persist approver
                        if (!string.IsNullOrWhiteSpace(comments))
                            a.comments = comments;

                        if (a.job_listing!.job_status == "Draft")
                        {
                            a.job_listing.job_status = "Open";
                            if (a.job_listing.company != null)
                                a.job_listing.company.company_status = "Active"; // persist Active
                        }

                        _db.admin_logs.Add(new admin_log
                        {
                            user_id = adminId,
                            action_type = "Admin.Bulk.Job.Update",
                            timestamp = now
                        });
                        changed++;
                        break;

                    case "Reject":
                        a.approval_status = "Rejected";
                        a.date_approved = null;
                        if (!string.IsNullOrWhiteSpace(comments))
                            a.comments = comments;
                        if (a.job_listing!.job_status == "Open")
                            a.job_listing.job_status = "Paused";
                        _db.admin_logs.Add(new admin_log
                        {
                            user_id = adminId,
                            action_type = "Admin.Bulk.Job.Update",
                            timestamp = now
                        });
                        changed++;
                        break;

                    case "Changes":
                        a.approval_status = "ChangesRequested";
                        a.date_approved = null;
                        if (!string.IsNullOrWhiteSpace(comments))
                            a.comments = comments;
                        if (a.job_listing!.job_status != "Closed")
                            a.job_listing.job_status = "Draft";
                        _db.admin_logs.Add(new admin_log
                        {
                            user_id = adminId,
                            action_type = "Admin.Bulk.Job.Update",
                            timestamp = now
                        });
                        changed++;
                        break;
                }
            }

            if (changed > 0) _db.SaveChanges();

            // --- Notify recruiters in bulk (best-effort, per item) ---
            try
            {
                foreach (var a in approvals)
                {
                    var ownerId = a.job_listing?.user_id ?? 0;
                    if (ownerId <= 0) continue;

                    if (string.Equals(actionType, "Approve", StringComparison.OrdinalIgnoreCase))
                    {
                        var title = "Job approved";
                        var msg = $"Your job \"{a.job_listing!.job_title}\" has been approved and is now live.";
                        _notif.SendAsync(ownerId, title, msg, type: "Approval").GetAwaiter().GetResult();
                    }
                    else if (string.Equals(actionType, "Reject", StringComparison.OrdinalIgnoreCase))
                    {
                        var title = "Job rejected";
                        var reason = string.IsNullOrWhiteSpace(comments) ? "" : $" Reason: {comments.Trim()}";
                        var msg = $"Your job \"{a.job_listing!.job_title}\" was rejected.{reason}";
                        _notif.SendAsync(ownerId, title, msg, type: "Review").GetAwaiter().GetResult();
                    }
                    else if (string.Equals(actionType, "Changes", StringComparison.OrdinalIgnoreCase))
                    {
                        var title = "Changes requested for job";
                        var extra = string.IsNullOrWhiteSpace(comments) ? "" : $" Details: {comments.Trim()}";
                        var msg = $"Your job \"{a.job_listing!.job_title}\" needs changes before it can go live.{extra}";
                        _notif.SendAsync(ownerId, title, msg, type: "Review").GetAwaiter().GetResult();
                    }
                }
            }
            catch { /* swallow notification errors in bulk */ }

            TempData["Flash.Type"] = "success";
            TempData["Flash.Message"] = $"{changed} item(s) updated ({actionType}).";
            return RedirectToAction(nameof(Index), new { status = returnStatus, q, page, pageSize });
        }

        // ========================================================================
        // NEW: AI Policy Check endpoint (JSON) with server-side caching
        // ========================================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> PolicyCheck(int id, bool force = false)
        {
            // Try cache first unless forced
            string cacheKey = $"ai:policy:job:{id}";
            if (!force && _cache.TryGetValue(cacheKey, out object? cachedObj) && cachedObj is not null)
            {
                // Return cached payload and mark it as fromCache
                var cachedPayload = cachedObj as Dictionary<string, object>;
                if (cachedPayload != null)
                {
                    cachedPayload["fromCache"] = true;
                    return Json(cachedPayload);
                }
            }

            // Load job content for this approval
            var data = await _db.job_post_approvals
                .AsNoTracking()
                .Include(a => a.job_listing!)
                    .ThenInclude(j => j.company)
                .Where(a => a.approval_id == id)
                .Select(a => new
                {
                    a.approval_id,
                    Title = a.job_listing!.job_title ?? "",
                    Company = a.job_listing!.company!.company_name ?? "",
                    Desc = a.job_listing!.job_description ?? "",
                    Reqs = a.job_listing!.job_requirements ?? ""
                })
                .FirstOrDefaultAsync();

            if (data == null)
            {
                var nf = new Dictionary<string, object>
                {
                    ["pass"] = false,
                    ["summary"] = "Item not found.",
                    ["items"] = new List<AiJobPolicyCheckItemVM>(),
                    ["fromCache"] = false,
                    ["cachedAt"] = MyTime.NowMalaysia()
                };
                return Json(nf);
            }

            var opts = _openAi.Value;
            if (string.IsNullOrWhiteSpace(opts.ApiKey))
            {
                var noKey = new Dictionary<string, object>
                {
                    ["pass"] = false,
                    ["summary"] = "AI check unavailable (no API key). Please review manually.",
                    ["items"] = new List<AiJobPolicyCheckItemVM>(),
                    ["fromCache"] = false,
                    ["cachedAt"] = MyTime.NowMalaysia()
                };
                // Cache this for a short time to avoid repeated calls
                _cache.Set(cacheKey, noKey, TimeSpan.FromMinutes(10));
                return Json(noKey);
            }

            var http = _httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(Math.Max(1, opts.TimeoutSeconds));
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", opts.ApiKey);

            // Ask the model to fill explicit checks; we compute pass from those booleans.
            var payload = new
            {
                model = string.IsNullOrWhiteSpace(opts.ModelText) ? "gpt-4o-mini" : opts.ModelText,
                temperature = 0,
                response_format = new { type = "json_object" },
                messages = new object[]
                {
                    new { role = "system", content =
                        "You audit job listings for professionalism and compliance with common global hiring norms. " +
                        "Return STRICT JSON ONLY with this exact schema:\n" +
                        "{\n  \"summary\": string,\n  \"checks\": {\n" +
                        "    \"off_platform\": boolean,\n" +
                        "    \"payment_or_bank\": boolean,\n" +
                        "    \"id_documents\": boolean,\n" +
                        "    \"personal_data\": boolean,\n" +
                        "    \"discrimination\": boolean,\n" +
                        "    \"availability_24_7\": boolean,\n" +
                        "    \"skip_platform\": boolean,\n" +
                        "    \"scammy_language\": boolean,\n" +
                        "    \"unrealistic_claims\": boolean,\n" +
                        "    \"clarity\": boolean\n" +
                        "  },\n  \"items\": [{\"issue\": string, \"severity\": \"Advice|Warning|Violation\"}]\n}\n" +
                        "Set a check to false whenever that problem appears. Keep items short (0–5)." },

                    // ---------------- FEW-SHOT EXAMPLES ----------------
                    new { role = "user", content =
                        "Company: ExampleCo\nTitle: Brand Ambassador – Fast Pay\n\n" +
                        "Description:\nPay a small onboarding fee to unlock client lists. Message me on WhatsApp +1 555 1234.\n\n" +
                        "Requirements:\nSend bank account and passport for verification. Must be under 30. Be available 24/7. Skip the platform and email me directly." },
                    new { role = "assistant", content =
                        "{\n" +
                        "  \"summary\": \"Multiple compliance issues detected (off-platform contact, fees, ID, age limit, 24/7, bypass platform).\",\n" +
                        "  \"checks\": {\n" +
                        "    \"off_platform\": false,\n" +
                        "    \"payment_or_bank\": false,\n" +
                        "    \"id_documents\": false,\n" +
                        "    \"personal_data\": true,\n" +
                        "    \"discrimination\": false,\n" +
                        "    \"availability_24_7\": false,\n" +
                        "    \"skip_platform\": false,\n" +
                        "    \"scammy_language\": true,\n" +
                        "    \"unrealistic_claims\": true,\n" +
                        "    \"clarity\": true\n" +
                        "  },\n" +
                        "  \"items\": [\n" +
                        "    {\"issue\":\"Requests off-platform contact (WhatsApp)\",\"severity\":\"Violation\"},\n" +
                        "    {\"issue\":\"Upfront payment/fee\",\"severity\":\"Violation\"},\n" +
                        "    {\"issue\":\"Requests passport/ID\",\"severity\":\"Violation\"},\n" +
                        "    {\"issue\":\"Age discrimination (under 30)\",\"severity\":\"Violation\"},\n" +
                        "    {\"issue\":\"24/7 availability pressure\",\"severity\":\"Violation\"},\n" +
                        "    {\"issue\":\"Instruction to bypass platform\",\"severity\":\"Violation\"}\n" +
                        "  ]\n" +
                        "}" },

                    // GOOD EXAMPLE
                    new { role = "user", content =
                        "Company: Acme Labs\nTitle: Software Engineer (Backend)\n\n" +
                        "Description:\nBuild and maintain .NET APIs, collaborate with Product, review code, write tests. Hybrid 2–3 days/week. Clear responsibilities and growth path.\n\n" +
                        "Requirements:\n2+ years C#/.NET, REST, SQL; unit testing experience; ability to work with code reviews; strong communication. Optional: Docker, Azure." },
                    new { role = "assistant", content =
                        "{\n" +
                        "  \"summary\": \"Professional and compliant; no significant issues identified.\",\n" +
                        "  \"checks\": {\n" +
                        "    \"off_platform\": true,\n" +
                        "    \"payment_or_bank\": true,\n" +
                        "    \"id_documents\": true,\n" +
                        "    \"personal_data\": true,\n" +
                        "    \"discrimination\": true,\n" +
                        "    \"availability_24_7\": true,\n" +
                        "    \"skip_platform\": true,\n" +
                        "    \"scammy_language\": true,\n" +
                        "    \"unrealistic_claims\": true,\n" +
                        "    \"clarity\": true\n" +
                        "  },\n" +
                        "  \"items\": []\n" +
                        "}" },

                    // ---- Actual item to evaluate ----
                    new { role = "user", content =
                        $"Company: {data.Company}\n" +
                        $"Title: {data.Title}\n\n" +
                        $"Description:\n{data.Desc}\n\n" +
                        $"Requirements:\n{data.Reqs}" }
                }
            };

            using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            try
            {
                using var resp = await http.PostAsync("https://api.openai.com/v1/chat/completions", content);
                var body = await resp.Content.ReadAsStringAsync();

                if (!resp.IsSuccessStatusCode)
                {
                    var down = new Dictionary<string, object>
                    {
                        ["pass"] = false,
                        ["summary"] = "AI check temporarily unavailable. Please review manually.",
                        ["items"] = new List<AiJobPolicyCheckItemVM>(),
                        ["rawNote"] = $"HTTP {resp.StatusCode}",
                        ["fromCache"] = false,
                        ["cachedAt"] = MyTime.NowMalaysia()
                    };
                    _cache.Set(cacheKey, down, TimeSpan.FromMinutes(10));
                    return Json(down);
                }

                string? contentJson = null;
                try
                {
                    using var doc = JsonDocument.Parse(body);
                    contentJson = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
                }
                catch
                {
                    var malformed = new Dictionary<string, object>
                    {
                        ["pass"] = false,
                        ["summary"] = "AI response malformed. Please review manually.",
                        ["items"] = new List<AiJobPolicyCheckItemVM>(),
                        ["fromCache"] = false,
                        ["cachedAt"] = MyTime.NowMalaysia()
                    };
                    _cache.Set(cacheKey, malformed, TimeSpan.FromMinutes(10));
                    return Json(malformed);
                }

                // Conservative defaults (do NOT auto-pass on parse problems)
                bool finalPass = false;
                string finalSummary = "AI could not evaluate.";
                var finalItems = new List<AiJobPolicyCheckItemVM>();

                try
                {
                    using var parsed = JsonDocument.Parse(contentJson ?? "{}");

                    string summaryText = parsed.RootElement.TryGetProperty("summary", out var s) && s.ValueKind == JsonValueKind.String
                        ? s.GetString() ?? ""
                        : "";

                    var items = new List<AiJobPolicyCheckItemVM>();
                    if (parsed.RootElement.TryGetProperty("items", out var arr) && arr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var el in arr.EnumerateArray())
                        {
                            var issue = el.TryGetProperty("issue", out var iProp) && iProp.ValueKind == JsonValueKind.String ? el.GetProperty("issue").GetString() ?? "" : "";
                            var sev = el.TryGetProperty("severity", out var sv) && sv.ValueKind == JsonValueKind.String ? el.GetProperty("severity").GetString() ?? "Advice" : "Advice";
                            if (!string.IsNullOrWhiteSpace(issue))
                                items.Add(new AiJobPolicyCheckItemVM { Issue = issue, Severity = string.IsNullOrWhiteSpace(sev) ? "Advice" : sev });
                        }
                    }

                    var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["off_platform"] = "Requests off-platform contact",
                        ["payment_or_bank"] = "Requests payment/bank transfer",
                        ["id_documents"] = "Asks for ID/IC/passport",
                        ["personal_data"] = "Asks for sensitive personal data",
                        ["discrimination"] = "Discriminatory criteria",
                        ["availability_24_7"] = "24/7 availability pressure",
                        ["skip_platform"] = "Instruction to bypass platform",
                        ["scammy_language"] = "Scammy/vague claims",
                        ["unrealistic_claims"] = "Unrealistic earnings/benefits",
                        ["clarity"] = "Unclear responsibilities/requirements"
                    };

                    bool anyFail = false;
                    if (parsed.RootElement.TryGetProperty("checks", out var checks) && checks.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var kv in checks.EnumerateObject())
                        {
                            if (kv.Value.ValueKind == JsonValueKind.False)
                            {
                                anyFail = true;
                                var label = labels.TryGetValue(kv.Name, out var nice) ? nice : kv.Name;
                                items.Add(new AiJobPolicyCheckItemVM { Issue = label, Severity = "Violation" });
                            }
                        }
                    }
                    else
                    {
                        anyFail = true;
                        items.Add(new AiJobPolicyCheckItemVM { Issue = "AI checks missing", Severity = "Warning" });
                    }

                    finalSummary = string.IsNullOrWhiteSpace(summaryText)
                        ? (anyFail ? "Issues detected." : "Looks good.")
                        : summaryText;
                    finalPass = !anyFail;
                    finalItems = items;
                }
                catch
                {
                    // keep conservative defaults
                }

                var payloadOut = new Dictionary<string, object>
                {
                    ["pass"] = finalPass,
                    ["summary"] = finalSummary,
                    ["items"] = finalItems,
                    ["fromCache"] = false,
                    ["cachedAt"] = MyTime.NowMalaysia()
                };

                // Cache the result (e.g., 12 hours)
                _cache.Set(cacheKey, payloadOut, TimeSpan.FromHours(12));

                return Json(payloadOut);
            }
            catch
            {
                var timeout = new Dictionary<string, object>
                {
                    ["pass"] = false,
                    ["summary"] = "AI check timed out. Please review manually.",
                    ["items"] = new List<AiJobPolicyCheckItemVM>(),
                    ["fromCache"] = false,
                    ["cachedAt"] = MyTime.NowMalaysia()
                };
                _cache.Set(cacheKey, timeout, TimeSpan.FromMinutes(10));
                return Json(timeout);
            }
        }
    }
}
