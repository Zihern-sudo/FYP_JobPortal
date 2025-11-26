// File: Areas/Admin/Controllers/CompaniesController.cs  (ASYNC UPDATED - FIXED CALLS)
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using JobPortal.Areas.Admin.Models;
using JobPortal.Areas.Shared.Models;
using JobPortal.Areas.Shared.Extensions; // TryGetUserId()
using JobPortal.Services;                // INotificationService
using System;
using System.Linq;
using System.Text;

// NEW for AI Company Profile Check
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using Microsoft.Extensions.Caching.Memory;
using JobPortal.Areas.Shared.Options; // OpenAIOptions
using System.Text.RegularExpressions;  // <-- NEW (evidence gating)

// NEW: file ops for draft→live
using System.IO;
using System.Globalization;

namespace JobPortal.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class CompaniesController : Controller
    {
        private readonly AppDbContext _db;
        private readonly INotificationService _notif;

        // NEW: AI deps (kept minimal)
        private readonly IHttpClientFactory _httpFactory;
        private readonly IOptions<OpenAIOptions> _openAi;
        private readonly IMemoryCache _cache;

        public CompaniesController(
            AppDbContext db,
            INotificationService notif,
            // NEW deps are optional in DI; if not configured, API falls back gracefully
            IHttpClientFactory httpFactory = null!,
            IOptions<OpenAIOptions> openAi = null!,
            IMemoryCache cache = null!)
        {
            _db = db;
            _notif = notif;
            _httpFactory = httpFactory!;
            _openAi = openAi!;
            _cache = cache!;
        }

        // File: Areas/Admin/Controllers/CompaniesController.cs (Index UPDATED)
        // GET: /Admin/Companies
        public IActionResult Index(
            string status = "All",
            string q = "",
            DateTime? from = null,
            DateTime? to = null,
            int page = 1,
            int pageSize = 10,
            string? sort = null)
        {
            ViewData["Title"] = "Companies";

            // normalize sort (default newest first by ID)
            sort = (sort ?? "id_desc").Trim().ToLowerInvariant();
            if (sort != "id_asc" && sort != "id_desc") sort = "id_desc";

            // Auto-flag Incomplete (missing required fields)
            AutoFlagIncompleteCompanies();

            var baseQuery = _db.companies.AsNoTracking();

            int all = baseQuery.Count();
            int verified = baseQuery.Count(c => c.company_status == "Verified");
            int unverified = baseQuery.Count(c => c.company_status == null || c.company_status == "" || c.company_status == "Pending");
            int incomplete = baseQuery.Count(c => c.company_status == "Incomplete");
            int rejected = baseQuery.Count(c => c.company_status == "Rejected");

            var qset = baseQuery;

            switch ((status ?? "All").Trim())
            {
                case "Verified": qset = qset.Where(c => c.company_status == "Verified"); break;
                case "Unverified": qset = qset.Where(c => c.company_status == null || c.company_status == "" || c.company_status == "Pending"); break;
                case "Incomplete": qset = qset.Where(c => c.company_status == "Incomplete"); break;
                case "Rejected": qset = qset.Where(c => c.company_status == "Rejected"); break;
                case "Active": qset = qset.Where(c => c.company_status == "Active"); break;
                default: break;
            }

            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = q.Trim();
                qset = qset.Where(c =>
                    EF.Functions.Like(c.company_name, $"%{term}%") ||
                    EF.Functions.Like(c.company_industry ?? "", $"%{term}%") ||
                    EF.Functions.Like(c.company_location ?? "", $"%{term}%"));
            }

            if (from.HasValue || to.HasValue)
            {
                var toExclusive = to?.Date.AddDays(1);
                qset = qset.Where(c => c.job_listings.Any(j =>
                    (!from.HasValue || j.date_posted >= from.Value.Date) &&
                    (!toExclusive.HasValue || j.date_posted < toExclusive.Value)));
            }

            // ORDER BY company_id per toggle BEFORE paging (default: id_desc)
            qset = sort == "id_asc"
                ? qset.OrderBy(c => c.company_id)
                : qset.OrderByDescending(c => c.company_id);

            var projected = qset
                .Select(c => new CompanyRow
                {
                    Id = c.company_id,
                    Name = c.company_name,
                    Industry = c.company_industry,
                    Location = c.company_location,
                    Status = c.job_listings.Any(j => j.job_status == "Open")
                                ? "Active"
                                : (c.company_status ?? "Pending"),
                    Jobs = c.job_listings.Count()
                });

            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 5, 100);
            int total = projected.Count();
            var items = projected.Skip((page - 1) * pageSize).Take(pageSize).ToList();

            var vm = new CompaniesIndexViewModel
            {
                Status = status ?? "All",
                Query = q ?? "",
                AllCount = all,
                VerifiedCount = verified,
                UnverifiedCount = unverified,
                IncompleteCount = incomplete,
                RejectedCount = rejected,
                Items = new PagedResult<CompanyRow>
                {
                    Items = items,
                    Page = page,
                    PageSize = pageSize,
                    TotalItems = total
                },
                // pass sort to the view
                Sort = sort
            };

            ViewBag.From = from?.ToString("yyyy-MM-dd");
            ViewBag.To = to?.ToString("yyyy-MM-dd");

            return View(vm);
        }


        // POST: bulk verify/reject (ASYNC)
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Bulk(string actionType, int[] ids, string? comments, string status = "All", string q = "", int page = 1)
        {
            if (ids == null || ids.Length == 0)
            {
                Flash("warning", "No companies selected.");
                return RedirectToAction(nameof(Index), new { status, q, page });
            }

            var setTo = actionType?.Equals("Verify", StringComparison.OrdinalIgnoreCase) == true
                ? "Verified"
                : actionType?.Equals("Reject", StringComparison.OrdinalIgnoreCase) == true
                    ? "Rejected"
                    : null;

            if (setTo == null)
            {
                Flash("danger", "Unknown bulk action.");
                return RedirectToAction(nameof(Index), new { status, q, page });
            }

            var companies = _db.companies.Where(c => ids.Contains(c.company_id)).ToList();
            if (!companies.Any())
            {
                Flash("warning", "No matching companies found.");
                return RedirectToAction(nameof(Index), new { status, q, page });
            }

            // NEW: apply draft→live for each company when verifying
            if (setTo == "Verified")
            {
                foreach (var c in companies)
                    await ApplyDraftToLiveAsync(c); // why: ensure approved content is what goes live
                await _db.SaveChangesAsync();
            }

            foreach (var c in companies) c.company_status = setTo;
            await _db.SaveChangesAsync();

            if (this.TryGetUserId(out var adminId, out _))
            {
                var now = MyTime.NowMalaysia();
                var auditType = setTo switch
                {
                    "Verified" => "Admin.Company.Verify",
                    "Rejected" => "Admin.Company.Reject",
                    _ => $"Admin.Company.Status:{setTo}"
                };

                foreach (var _ in companies)
                {
                    _db.admin_logs.Add(new admin_log
                    {
                        user_id = adminId,
                        action_type = auditType,
                        timestamp = now
                    });
                }
                await _db.SaveChangesAsync();
            }

            // notify owners (Verify/Reject handled in bulk)
            var notifyTasks = companies
                .Where(c => c.user_id > 0)
                .Select(c =>
                {
                    var title = setTo == "Verified"
                        ? "Company profile approved"
                        : "Company profile rejected";

                    var msg = setTo == "Verified"
                        ? $"Your company profile \"{c.company_name}\" has been approved. You can now post jobs."
                        : $"Your company profile \"{c.company_name}\" has been rejected.{(string.IsNullOrWhiteSpace(comments) ? "" : $" Reason: {comments.Trim()}")}";

                    var type = setTo == "Verified" ? "Info" : "Review";
                    return _notif.SendAsync(c.user_id, title, msg, type: type);  // ← fixed
                });

            try { await Task.WhenAll(notifyTasks); }
            catch { /* best effort */ }

            Flash("success", $"{companies.Count} compan{(companies.Count == 1 ? "y" : "ies")} {setTo.ToLowerInvariant()}.");
            return RedirectToAction(nameof(Index), new { status, q, page });
        }

        // CSV export (respects status/search/date-range)
        [HttpGet]
        public IActionResult ExportCsv(string status = "All", string q = "", DateTime? from = null, DateTime? to = null)
        {
            var qset = _db.companies.AsNoTracking();

            switch ((status ?? "All").Trim())
            {
                case "Verified": qset = qset.Where(c => c.company_status == "Verified"); break;
                case "Unverified": qset = qset.Where(c => c.company_status == null || c.company_status == "" || c.company_status == "Pending"); break;
                case "Incomplete": qset = qset.Where(c => c.company_status == "Incomplete"); break;
                case "Rejected": qset = qset.Where(c => c.company_status == "Rejected"); break;
                case "Active": qset = qset.Where(c => c.company_status == "Active"); break;
                default: break;
            }

            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = q.Trim();
                qset = qset.Where(c =>
                    EF.Functions.Like(c.company_name, $"%{term}%") ||
                    EF.Functions.Like(c.company_industry ?? "", $"%{term}%") ||
                    EF.Functions.Like(c.company_location ?? "", $"%{term}%"));
            }

            if (from.HasValue || to.HasValue)
            {
                var toExclusive = to?.Date.AddDays(1);
                qset = qset.Where(c => c.job_listings.Any(j =>
                    (!from.HasValue || j.date_posted >= from.Value.Date) &&
                    (!toExclusive.HasValue || j.date_posted < toExclusive.Value)));
            }

            var rows = qset
                .OrderBy(c => c.company_name)
                .Select(c => new
                {
                    c.company_name,
                    c.company_industry,
                    c.company_location,
                    Status = c.job_listings.Any(j => j.job_status == "Open") ? "Active" : (c.company_status ?? "Unverified"),
                    Jobs = c.job_listings.Count()
                })
                .ToList();

            static string Esc(string? s)
            {
                if (string.IsNullOrEmpty(s)) return "";
                var needs = s.Contains(',') || s.Contains('"') || s.Contains('\n') || s.Contains('\r');
                var t = s.Replace("\"", "\"\"");
                return needs ? $"\"{t}\"" : t;
            }

            var sb = new StringBuilder();
            sb.AppendLine("Name,Industry,Location,Status,Jobs");
            foreach (var r in rows)
                sb.AppendLine($"{Esc(r.company_name)},{Esc(r.company_industry)},{Esc(r.company_location)},{Esc(r.Status)},{r.Jobs}");

            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
            var fileName = $"Companies_{MyTime.NowMalaysia():yyyyMMdd_HHmm}.csv";
            return File(bytes, "text/csv; charset=utf-8", fileName);
        }

        // GET: /Admin/Companies/Preview/5
        public IActionResult Preview(int id)
        {
            var c = _db.companies
                .Include(x => x.job_listings)
                .FirstOrDefault(x => x.company_id == id);

            if (c == null) return NotFound();

            // NEW: Prefer DRAFT content for preview so admin reviews what will go live
            var draft = LoadDraft(c.user_id);
            var useDraft = draft != null || GetDraftPhotoPath(c.user_id) != null;

            var jobs = c.job_listings
                .OrderByDescending(j => j.date_posted)
                .Take(10)
                .Select(j => new ApprovalRow
                {
                    Id = j.job_listing_id,
                    JobId = j.job_listing_id,
                    JobTitle = j.job_title,
                    Company = c.company_name,
                    Status = j.job_status,
                    Date = j.date_posted
                })
                .ToList();

            var vm = new CompanyPreviewViewModel
            {
                Id = c.company_id,
                Name = useDraft ? (draft?.company_name ?? c.company_name) : c.company_name,
                Industry = useDraft ? (draft?.company_industry ?? c.company_industry) : c.company_industry,
                Location = useDraft ? (draft?.company_location ?? c.company_location) : c.company_location,
                Description = useDraft ? (draft?.company_description ?? c.company_description) : c.company_description,
                Status = c.job_listings.Any(j => j.job_status == "Open") ? "Active" : (c.company_status ?? "Pending"),
                RecentJobs = jobs
            };

            // after: var vm = new CompanyPreviewViewModel { ... };

            var draftSavedAtMy = draft != null ? MyTime.ToMalaysiaTime(draft.saved_at) : (DateTime?)null;
            ViewBag.UsingDraft = useDraft;             // true if any draft text or draft photo is being shown
            ViewBag.DraftSavedAt = draftSavedAtMy;     // DateTime? in MYT for display

            // expose the photo path for the view (draft first, else live)
            var draftPhotoWeb = GetDraftPhotoWebPath(c.user_id);
            ViewBag.CompanyPhotoUrl = draftPhotoWeb ?? c.company_photo;

            ViewData["Title"] = "Company Preview";
            return View(vm);

        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Verify(int id)
        {
            var c = _db.companies.FirstOrDefault(x => x.company_id == id);
            if (c == null) return NotFound();

            // NEW: apply draft→live before status flip
            await ApplyDraftToLiveAsync(c);
            await _db.SaveChangesAsync();

            return await SetStatus(id, "Verified", "Company verified.");
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> MarkIncomplete(int id)
            => await SetStatus(id, "Incomplete", "Company marked as incomplete.");

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Reject(int id, string? comments)
            => await SetStatus(id, "Rejected", string.IsNullOrWhiteSpace(comments) ? "Company rejected." : $"Company rejected: {comments}", comments);

        private void AutoFlagIncompleteCompanies()
        {
            var toFlag = _db.companies
                .Where(c =>
                    c.company_status != "Incomplete" &&
                    (string.IsNullOrWhiteSpace(c.company_name) ||
                     string.IsNullOrWhiteSpace(c.company_location)))
                .ToList();

            if (toFlag.Count == 0) return;

            foreach (var c in toFlag)
                c.company_status = "Incomplete";

            _db.SaveChanges();
        }

        // NOTE: recruiterComments used when status == "Rejected"
        private async Task<IActionResult> SetStatus(int id, string status, string logMsg, string? recruiterComments = null)
        {
            var c = _db.companies.FirstOrDefault(x => x.company_id == id);
            if (c == null) return NotFound();

            c.company_status = status;
            await _db.SaveChangesAsync();

            if (this.TryGetUserId(out var adminId, out _))
            {
                var actionType = status switch
                {
                    "Verified" => "Admin.Company.Verify",
                    "Incomplete" => "Admin.Company.MarkIncomplete",
                    "Rejected" => "Admin.Company.Reject",
                    _ => $"Admin.Company.Status:{status}"
                };

                _db.admin_logs.Add(new admin_log
                {
                    user_id = adminId,
                    action_type = actionType,
                    timestamp = MyTime.NowMalaysia()
                });
                await _db.SaveChangesAsync();
            }

            // ---- Notify the company owner (recruiter) for Verified / Rejected / Incomplete ----
            if (c.user_id > 0 && (status == "Verified" || status == "Rejected" || status == "Incomplete"))
            {
                string title, message, type;

                if (status == "Verified")
                {
                    title = "Company profile approved";
                    message = $"Your company profile \"{c.company_name}\" has been approved. You can now post jobs.";
                    type = "Info";

                    // NEW: after verify, cleanup drafts (best-effort)
                    try { CleanupDraft(c.user_id); } catch { /* ignore */ }
                }
                else if (status == "Rejected")
                {
                    title = "Company profile rejected";
                    message = $"Your company profile \"{c.company_name}\" has been rejected.{(string.IsNullOrWhiteSpace(recruiterComments) ? "" : $" Reason: {recruiterComments.Trim()}")}";
                    type = "Review";
                    // keep draft so recruiter can edit and resubmit
                }
                else // Incomplete
                {
                    title = "Company profile incomplete";
                    message = $"Your company profile \"{c.company_name}\" is marked as incomplete. Please fill in all required details (e.g., name and location) and resubmit for approval.";
                    type = "Review";
                    // keep draft
                }

                try { await _notif.SendAsync(c.user_id, title, message, type: type); }  // ← fixed
                catch { /* ignore notification failure */ }
            }
            // ---------------------------------------------------------------------

            Flash("success", logMsg);
            return RedirectToAction(nameof(Preview), new { id });
        }

        private void Flash(string type, string message)
        {
            TempData["Flash.Type"] = type;
            TempData["Flash.Message"] = message;
        }

        // ======================================================================
        // NEW: AI Company Profile Check endpoint (cached & conservative)
        // ======================================================================
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> CompanyPolicyCheck(int id, bool force = false)
        {
            // Graceful fallback if DI not wired
            if (_httpFactory == null || _openAi == null || _cache == null)
            {
                return Json(new AiCompanyProfileCheckResultVM
                {
                    Pass = false,
                    Summary = "AI check unavailable (service not configured). Please review manually.",
                    Items = new() { new AiCompanyProfileCheckItemVM { Issue = "Service not configured", Severity = "Warning" } },
                    FromCache = false,
                    CachedAt = MyTime.NowMalaysia()
                });
            }

            // --- Normalize "force" (form/query) and evict cache when forced ---
            bool forceRun =
                force ||
                string.Equals(Request.Form["force"], "1", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(Request.Form["force"], "true", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(Request.Query["force"], "1", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(Request.Query["force"], "true", StringComparison.OrdinalIgnoreCase);

            string cacheKey = $"ai:company:{id}";
            if (forceRun)
            {
                _cache.Remove(cacheKey); // ensure a fresh OpenAI call
            }
            else if (_cache.TryGetValue(cacheKey, out AiCompanyProfileCheckResultVM cached) && cached != null)
            {
                // Avoid mutating cached instance: return a shallow copy flagged as fromCache
                var copy = new AiCompanyProfileCheckResultVM
                {
                    Pass = cached.Pass,
                    Summary = cached.Summary,
                    Items = cached.Items?.ToList() ?? new(),
                    FromCache = true,
                    CachedAt = cached.CachedAt
                };
                return Json(copy);
            }
            // -------------------------------------------------------------------

            var data = _db.companies
                .AsNoTracking()
                .Where(c => c.company_id == id)
                .Select(c => new
                {
                    c.company_name,
                    c.company_industry,
                    c.company_location,
                    c.company_description
                })
                .FirstOrDefault();

            if (data == null)
            {
                return Json(new AiCompanyProfileCheckResultVM
                {
                    Pass = false,
                    Summary = "Company not found.",
                    Items = new(),
                    FromCache = false,
                    CachedAt = MyTime.NowMalaysia()
                });
            }

            var opts = _openAi.Value;
            if (string.IsNullOrWhiteSpace(opts.ApiKey))
            {
                var noKey = new AiCompanyProfileCheckResultVM
                {
                    Pass = false,
                    Summary = "AI check unavailable (no API key). Please review manually.",
                    Items = new(),
                    FromCache = false,
                    CachedAt = MyTime.NowMalaysia()
                };
                _cache.Set(cacheKey, noKey, TimeSpan.FromMinutes(10));
                return Json(noKey);
            }

            var http = _httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(Math.Max(5, opts.TimeoutSeconds));
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", opts.ApiKey);

            // ---- Evidence gating (regex) on profile text ----
            string fullText = $"{data.company_name}\n{data.company_industry}\n{data.company_location}\n{data.company_description ?? ""}";
            string[] redWordPatterns =
            {
                @"whats\s*app|telegram|line\s?app|wechat|discord",
                @"contact\s+(?:me|us)\s+at\s+[^@\s]+\s*@|gmail\.com|yahoo\.com|hotmail\.com",
                @"activation\s*fee|onboarding\s*fee|deposit\b|pay\s+to\s+apply|unlock\s+client\s+list",
                @"bank\s+account|iban|swift|wire\s+transfer",
                @"passport|nric|ic\s*number|id\s*card|identity\s*document|mykad",
                @"guaranteed\s+(?:income|earnings)|get[-\s]?rich|lottery",
                @"mlm|multi[-\s]?level|pyramid\s+scheme",
                @"bypass\s+(?:this\s+)?platform|talk\s+outside|skip\s+platform"
            };
            bool HasEvidence(string s)
            {
                foreach (var pat in redWordPatterns)
                    if (Regex.IsMatch(s, pat, RegexOptions.IgnoreCase | RegexOptions.Multiline)) return true;
                return false;
            }
            bool evidenceFound = HasEvidence(fullText);

            // Company-specific checks (true = OK, false = problem)
            var payload = new
            {
                model = string.IsNullOrWhiteSpace(opts.ModelText) ? "gpt-4o-mini" : opts.ModelText,
                temperature = 0,
                response_format = new { type = "json_object" },
                messages = new object[]
                {
                    new {
                        role = "system",
                        content =
@"You audit **company profiles** (not job posts) for professionalism and concrete business legitimacy.
OUTPUT STRICT JSON ONLY with this schema:

{
  ""summary"": string,
  ""checks"": {
    ""contact_off_platform"": boolean,          // true ONLY if the profile does NOT ask to contact via WhatsApp/Telegram/personal email
    ""requests_money_or_bank"": boolean,        // true ONLY if no fees/deposits/bank transfers are requested
    ""requests_id_documents"": boolean,         // true ONLY if no ID/passport/NRIC scans requested
    ""asks_sensitive_personal_data"": boolean,  // true ONLY if no sensitive personal data requested
    ""vague_or_no_business_info"": boolean,     // true ONLY if business info is sufficiently concrete & normal
    ""unrealistic_claims_or_lottery"": boolean, // true ONLY if profile avoids 'guaranteed income', 'lottery', etc.
    ""mlm_or_pyramid_indications"": boolean,    // true ONLY if no MLM/pyramid hints
    ""impersonation_or_trademark"": boolean,    // true ONLY if no impersonation of known brands
    ""skip_platform_instruction"": boolean,     // true ONLY if no instruction to bypass this platform
    ""clarity"": boolean                        // true ONLY if profile is reasonably clear/coherent
  },
  ""items"": [{""issue"": string, ""severity"": ""Advice|Warning|Violation""}]
}

IMPORTANT:
- **Default every check to true unless the profile text clearly shows that problem.**
- Do **NOT** guess or infer hidden details; be conservative.
- Keep items 0–5, short and specific.
"
                    },

                    // Few-shot GOOD (should PASS)
                    new {
                        role = "user",
                        content =
@"Company: Northbridge Software Sdn Bhd
Industry: Information Technology
Location: Kuala Lumpur, MY
Profile Description:
We build B2B workflow tools for mid-sized manufacturers. Registered since 2016. Our team integrates ERP and maintains APIs. No fees to apply; all recruiting handled on this platform. Contact via in-app messaging."
                    },
                    new {
                        role = "assistant",
                        content =
@"{
  ""summary"": ""Legitimate software SME with clear services and no red flags."",
  ""checks"": {
    ""contact_off_platform"": true,
    ""requests_money_or_bank"": true,
    ""requests_id_documents"": true,
    ""asks_sensitive_personal_data"": true,
    ""vague_or_no_business_info"": true,
    ""unrealistic_claims_or_lottery"": true,
    ""mlm_or_pyramid_indications"": true,
    ""impersonation_or_trademark"": true,
    ""skip_platform_instruction"": true,
    ""clarity"": true
  },
  ""items"": []
}"
                    },

                    // Few-shot BAD (should FAIL)
                    new {
                        role = "user",
                        content =
@"Company: FastPay Global Network
Industry: Financial Services
Location: —
Profile Description:
Guaranteed monthly earnings! Message our recruiter on WhatsApp +1 555 9999. Small activation fee required to unlock client access. Send NRIC and bank details for verification. We sometimes use big brand names in marketing."
                    },
                    new {
                        role = "assistant",
                        content =
@"{
  ""summary"": ""Multiple high-risk red flags: guaranteed earnings, off-platform WhatsApp, fees, ID and bank requests, possible impersonation."",
  ""checks"": {
    ""contact_off_platform"": false,
    ""requests_money_or_bank"": false,
    ""requests_id_documents"": false,
    ""asks_sensitive_personal_data"": false,
    ""vague_or_no_business_info"": false,
    ""unrealistic_claims_or_lottery"": false,
    ""mlm_or_pyramid_indications"": true,
    ""impersonation_or_trademark"": false,
    ""skip_platform_instruction"": false,
    ""clarity"": true
  },
  ""items"": [
    { ""issue"": ""Requests contact via WhatsApp"", ""severity"": ""Violation"" },
    { ""issue"": ""Activation/other fees"", ""severity"": ""Violation"" },
    { ""issue"": ""Requests NRIC/ID and bank details"", ""severity"": ""Violation"" },
    { ""issue"": ""Guaranteed earnings claim"", ""severity"": ""Warning"" },
    { ""issue"": ""Possible brand impersonation"", ""severity"": ""Warning"" }
  ]
}"
                    },

                    // Actual item
                    new {
                        role = "user",
                        content =
$@"Company: {data.company_name}
Industry: {data.company_industry}
Location: {data.company_location}

Profile Description:
{data.company_description}"
                    }
                }
            };

            using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            try
            {
                using var resp = await http.PostAsync("https://api.openai.com/v1/chat/completions", content);
                var body = await resp.Content.ReadAsStringAsync();

                if (!resp.IsSuccessStatusCode)
                {
                    var down = new AiCompanyProfileCheckResultVM
                    {
                        Pass = false,
                        Summary = "AI check temporarily unavailable. Please review manually.",
                        Items = new(),
                        FromCache = false,
                        CachedAt = MyTime.NowMalaysia()
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
                    var malformed = new AiCompanyProfileCheckResultVM
                    {
                        Pass = false,
                        Summary = "AI response malformed. Please review manually.",
                        Items = new(),
                        FromCache = false,
                        CachedAt = MyTime.NowMalaysia()
                    };
                    _cache.Set(cacheKey, malformed, TimeSpan.FromMinutes(10));
                    return Json(malformed);
                }

                bool finalPass = true; // default to pass unless explicit, evidenced violation
                string finalSummary = "Profile looks professional.";
                var finalItems = new System.Collections.Generic.List<AiCompanyProfileCheckItemVM>();

                try
                {
                    using var parsed = JsonDocument.Parse(contentJson ?? "{}");

                    string summaryText = parsed.RootElement.TryGetProperty("summary", out var s) && s.ValueKind == JsonValueKind.String
                        ? s.GetString() ?? ""
                        : "";

                    if (parsed.RootElement.TryGetProperty("items", out var arr) && arr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var el in arr.EnumerateArray())
                        {
                            var issue = el.TryGetProperty("issue", out var iProp) && iProp.ValueKind == JsonValueKind.String ? iProp.GetString() ?? "" : "";
                            var sev = el.TryGetProperty("severity", out var sv) && sv.ValueKind == JsonValueKind.String ? sv.GetString() ?? "Advice" : "Advice";
                            if (!string.IsNullOrWhiteSpace(issue))
                                finalItems.Add(new AiCompanyProfileCheckItemVM { Issue = issue, Severity = string.IsNullOrWhiteSpace(sev) ? "Advice" : sev });
                        }
                    }

                    bool anyExplicitFalse = false;
                    if (parsed.RootElement.TryGetProperty("checks", out var checks) && checks.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var kv in checks.EnumerateObject())
                        {
                            if (kv.Value.ValueKind == JsonValueKind.False) anyExplicitFalse = true;
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(summaryText))
                        finalSummary = summaryText;

                    bool hasViolation = finalItems.Any(i => string.Equals(i.Severity, "Violation", StringComparison.OrdinalIgnoreCase));

                    if (hasViolation)
                    {
                        if (evidenceFound)
                        {
                            finalPass = false; // evidence-backed violations
                        }
                        else
                        {
                            foreach (var it in finalItems.Where(i => i.Severity.Equals("Violation", StringComparison.OrdinalIgnoreCase)))
                                it.Severity = "Warning";
                            finalPass = true;
                            if (string.IsNullOrWhiteSpace(finalSummary) || finalSummary.Contains("issues", StringComparison.OrdinalIgnoreCase))
                                finalSummary = "Minor concerns noted, but no explicit red flags in the profile text.";
                        }
                    }
                    else if (anyExplicitFalse && evidenceFound)
                    {
                        finalPass = false;
                        if (string.IsNullOrWhiteSpace(finalSummary)) finalSummary = "Detected explicit red flags in profile.";
                        if (finalItems.Count == 0)
                            finalItems.Add(new AiCompanyProfileCheckItemVM { Issue = "Profile contains explicit red-flag signals.", Severity = "Violation" });
                    }
                    else
                    {
                        finalPass = true;
                        if (string.IsNullOrWhiteSpace(finalSummary)) finalSummary = "Profile looks professional.";
                    }

                    if (finalItems.Count > 8) finalItems = finalItems.Take(8).ToList();
                }
                catch
                {
                    finalPass = true;
                    finalSummary = "Profile looks professional.";
                    finalItems = new();
                }

                var outPayload = new AiCompanyProfileCheckResultVM
                {
                    Pass = finalPass,
                    Summary = finalSummary,
                    Items = finalItems,
                    FromCache = false,
                    CachedAt = MyTime.NowMalaysia()
                };

                _cache.Set(cacheKey, outPayload, TimeSpan.FromHours(12));
                return Json(outPayload);
            }
            catch
            {
                var timeout = new AiCompanyProfileCheckResultVM
                {
                    Pass = false,
                    Summary = "AI check timed out. Please review manually.",
                    Items = new(),
                    FromCache = false,
                    CachedAt = MyTime.NowMalaysia()
                };
                _cache.Set(cacheKey, timeout, TimeSpan.FromMinutes(10));
                return Json(timeout);
            }
        }

        // ======================================================================
        // =============== DRAFT helpers: read/copy/cleanup ======================
        // ======================================================================

        // Minimal DTO matching the recruiter's draft JSON
        private sealed class CompanyDraftPayload
        {
            public string company_name { get; set; } = "";
            public string? company_industry { get; set; }
            public string? company_location { get; set; }
            public string? company_description { get; set; }
            public DateTime saved_at { get; set; } = MyTime.NowMalaysia();
        }

        private string GetDraftDir(int uid)
        {
            var dir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "company", "_drafts", uid.ToString(CultureInfo.InvariantCulture));
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            return dir;
        }

        private string GetLiveDir()
        {
            var dir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "company");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            return dir;
        }

        private string GetDraftJsonPath(int uid) => Path.Combine(GetDraftDir(uid), $"company_{uid}.json");

        private string? GetDraftPhotoPath(int uid)
        {
            var dir = GetDraftDir(uid);
            var f = Directory.GetFiles(dir, $"company_{uid}.*")
                             .FirstOrDefault(x => !x.EndsWith(".json", StringComparison.OrdinalIgnoreCase));
            return f;
        }

        private string? GetDraftPhotoWebPath(int uid)
        {
            var p = GetDraftPhotoPath(uid);
            return p == null ? null : "/uploads/company/_drafts/" + uid + "/" + Path.GetFileName(p);
        }

        private CompanyDraftPayload? LoadDraft(int uid)
        {
            var jsonPath = GetDraftJsonPath(uid);
            if (!System.IO.File.Exists(jsonPath)) return null;

            try
            {
                var json = System.IO.File.ReadAllText(jsonPath);
                return JsonSerializer.Deserialize<CompanyDraftPayload>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            catch { return null; }
        }

        private async Task ApplyDraftToLiveAsync(company c)
        {
            if (c == null) return;

            // If no draft exists, nothing to apply
            var payload = LoadDraft(c.user_id);
            var draftPhoto = GetDraftPhotoPath(c.user_id);
            if (payload == null && string.IsNullOrWhiteSpace(draftPhoto))
                return;

            // 1) Copy text fields from payload (if present)
            if (payload != null)
            {
                c.company_name = (payload.company_name ?? "").Trim();
                c.company_industry = payload.company_industry?.Trim();
                c.company_location = payload.company_location?.Trim();
                c.company_description = payload.company_description?.Trim();
            }

            // 2) Copy draft photo → live folder (overwrite)
            if (!string.IsNullOrWhiteSpace(draftPhoto) && System.IO.File.Exists(draftPhoto))
            {
                var liveDir = GetLiveDir();

                // remove existing live files for this user
                foreach (var f in Directory.GetFiles(liveDir, $"company_{c.user_id}.*"))
                    try { System.IO.File.Delete(f); } catch { /* ignore */ }

                var ext = Path.GetExtension(draftPhoto).ToLowerInvariant();
                if (string.IsNullOrWhiteSpace(ext)) ext = ".jpg";
                var liveFileName = $"company_{c.user_id}{ext}";
                var livePath = Path.Combine(liveDir, liveFileName);

                // copy/overwrite
                System.IO.File.Copy(draftPhoto, livePath, overwrite: true);

                // update DB path (clean web path)
                c.company_photo = "/uploads/company/" + liveFileName;
            }

            // 3) Do not set status here; caller handles status + SaveChanges
        }

        private void CleanupDraft(int uid)
        {
            var dir = GetDraftDir(uid);
            if (!Directory.Exists(dir)) return;

            try
            {
                foreach (var f in Directory.GetFiles(dir, $"company_{uid}.*"))
                    System.IO.File.Delete(f);
                // optional: remove empty dir
                if (!Directory.EnumerateFileSystemEntries(dir).Any())
                    Directory.Delete(dir);
            }
            catch { /* ignore */ }
        }
    }
}
