// File: Areas/Admin/Controllers/ApprovalsController.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using JobPortal.Areas.Shared.Models;
using JobPortal.Areas.Admin.Models;
using JobPortal.Areas.Shared.Extensions; // For TryGetUserId
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

        // UPDATED: inject http client + options + memory cache (no other changes)
        public ApprovalsController(AppDbContext db, IHttpClientFactory httpFactory, IOptions<OpenAIOptions> openAi, IMemoryCache cache)
        {
            _db = db;
            _httpFactory = httpFactory;
            _openAi = openAi;
            _cache = cache;
        }

        // File: Areas/Admin/Controllers/ApprovalsController.cs  (Index action only)
        // GET: /Admin/Approvals?status=All&q=&page=1&pageSize=10
        public IActionResult Index(string? status = "All", string? q = null, int page = 1, int pageSize = 10, string? sort = null)
        {
            ViewData["Title"] = "Job Approvals";

            // normalize sort (default newest first by ID)
            sort = (sort ?? "id_desc").Trim().ToLowerInvariant();
            if (sort != "id_asc" && sort != "id_desc") sort = "id_desc";

            // Counts for tabs
            var counts = _db.job_post_approvals
                .AsNoTracking()
                .GroupBy(a => a.approval_status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToList();

            int pending = counts.FirstOrDefault(x => x.Status == "Pending")?.Count ?? 0;
            int approved = counts.FirstOrDefault(x => x.Status == "Approved")?.Count ?? 0;
            int changes = counts.FirstOrDefault(x => x.Status == "ChangesRequested")?.Count ?? 0;
            int rejected = counts.FirstOrDefault(x => x.Status == "Rejected")?.Count ?? 0;

            var query = _db.job_post_approvals
                .AsNoTracking()
                .Include(a => a.job_listing!.company)
                .AsQueryable();

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
            var items = query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(a => new ApprovalRow
                {
                    Id = a.approval_id,
                    JobId = a.job_listing_id,
                    JobTitle = a.job_listing!.job_title,
                    Company = a.job_listing!.company!.company_name,
                    Status = a.approval_status, // Pending/Approved/ChangesRequested/Rejected
                    Date = a.job_listing!.date_posted
                })
                .ToList();

            // NEW: map approver names for current page (only Approved items)
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
                // ADDED: pass sort to view
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
                .Select(a => new ApprovalPreviewViewModel
                {
                    Id = a.approval_id,
                    JobId = a.job_listing_id,
                    JobTitle = a.job_listing!.job_title,
                    Company = a.job_listing!.company!.company_name,
                    Status = a.approval_status,
                    Date = a.job_listing!.date_posted,
                    JobDescription = a.job_listing!.job_description,
                    JobRequirements = a.job_listing!.job_requirements,
                    Comments = a.comments
                })
                .FirstOrDefault();

            if (item == null) return NotFound();

            // NEW: provide company photo path to the view without changing VM
            var photo = _db.job_post_approvals
                .AsNoTracking()
                .Where(a => a.approval_id == id)
                .Select(a => a.job_listing!.company!.company_photo)
                .FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(photo))
                ViewBag.CompanyPhotoUrl = photo;

            // NEW: approver name for Approved items
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

            approval.approval_status = "Approved";
            approval.date_approved = DateTime.Now;
            approval.user_id = adminId; // NEW: persist approver
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
                action_type = $"Approved job #{approval.job_listing_id}",
                timestamp = DateTime.UtcNow
            });

            _db.SaveChanges();

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

            approval.approval_status = "Rejected";
            approval.date_approved = null;
            if (!string.IsNullOrWhiteSpace(comments))
                approval.comments = comments;

            if (approval.job_listing!.job_status == "Open")
                approval.job_listing.job_status = "Paused";

            _db.admin_logs.Add(new admin_log
            {
                user_id = adminId,
                action_type = $"Rejected job #{approval.job_listing_id}",
                timestamp = DateTime.UtcNow
            });

            _db.SaveChanges();

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

            approval.approval_status = "ChangesRequested";
            approval.date_approved = null;
            if (!string.IsNullOrWhiteSpace(comments))
                approval.comments = comments;

            if (approval.job_listing!.job_status != "Closed")
                approval.job_listing.job_status = "Draft";

            _db.admin_logs.Add(new admin_log
            {
                user_id = adminId,
                action_type = $"Requested changes for job #{approval.job_listing_id}",
                timestamp = DateTime.UtcNow
            });

            _db.SaveChanges();

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

            var now = DateTime.UtcNow;
            int changed = 0;

            foreach (var a in approvals)
            {
                switch (actionType)
                {
                    case "Approve":
                        a.approval_status = "Approved";
                        a.date_approved = now;
                        a.user_id = adminId; // NEW: persist approver
                        if (!string.IsNullOrWhiteSpace(comments))
                            a.comments = comments;

                        if (a.job_listing!.job_status == "Draft")
                        {
                            a.job_listing.job_status = "Open";
                            if (a.job_listing.company != null)
                                a.job_listing.company.company_status = "Active"; // persist Active
                        }

                        _db.admin_logs.Add(new admin_log { user_id = adminId, action_type = $"Approved job #{a.job_listing_id}", timestamp = now });
                        changed++;
                        break;

                    case "Reject":
                        a.approval_status = "Rejected";
                        a.date_approved = null;
                        if (!string.IsNullOrWhiteSpace(comments))
                            a.comments = comments;
                        if (a.job_listing!.job_status == "Open")
                            a.job_listing.job_status = "Paused";
                        _db.admin_logs.Add(new admin_log { user_id = adminId, action_type = $"Rejected job #{a.job_listing_id}", timestamp = now });
                        changed++;
                        break;

                    case "Changes":
                        a.approval_status = "ChangesRequested";
                        a.date_approved = null;
                        if (!string.IsNullOrWhiteSpace(comments))
                            a.comments = comments;
                        if (a.job_listing!.job_status != "Closed")
                            a.job_listing.job_status = "Draft";
                        _db.admin_logs.Add(new admin_log { user_id = adminId, action_type = $"Requested changes for job #{a.job_listing_id}", timestamp = now });
                        changed++;
                        break;
                }
            }

            if (changed > 0) _db.SaveChanges();

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
                    ["cachedAt"] = DateTime.UtcNow
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
                    ["cachedAt"] = DateTime.UtcNow
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
                        ["cachedAt"] = DateTime.UtcNow
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
                        ["cachedAt"] = DateTime.UtcNow
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
                            var issue = el.TryGetProperty("issue", out var iProp) && iProp.ValueKind == JsonValueKind.String ? iProp.GetString() ?? "" : "";
                            var sev = el.TryGetProperty("severity", out var sv) && sv.ValueKind == JsonValueKind.String ? sv.GetString() ?? "Advice" : "Advice";
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
                    ["cachedAt"] = DateTime.UtcNow
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
                    ["cachedAt"] = DateTime.UtcNow
                };
                _cache.Set(cacheKey, timeout, TimeSpan.FromMinutes(10));
                return Json(timeout);
            }
        }
    }
}
