// File: Areas/Recruiter/Controllers/BulkController.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using JobPortal.Areas.Shared.Models;
using JobPortal.Areas.Recruiter.Models;
using JobPortal.Areas.Shared.Extensions; // <-- add this
using Microsoft.AspNetCore.Hosting;                     // IWebHostEnvironment
using Microsoft.Extensions.Logging;                    // ILogger<T>
using JobPortal.Services;                              // INotificationService
using JobPortal.Areas.Shared.Models.Extensions;        // TryNotifyAsync()

namespace JobPortal.Areas.Recruiter.Controllers
{
    [Area("Recruiter")]
    public class BulkController : Controller
    {
        // MODIFIED: Added pagination constants
        private const int DefaultPageSize = 20;
        private const int MaxPageSize = 100;

        private readonly AppDbContext _db;
        private readonly IWebHostEnvironment _env;
        private readonly INotificationService _notif;           // notify
        private readonly ILogger<BulkController> _logger;       // log

        public BulkController(AppDbContext db,
                              IWebHostEnvironment env,
                              INotificationService notif,
                              ILogger<BulkController> logger)
        {
            _db = db;
            _env = env;
            _notif = notif;
            _logger = logger;
        }

        // GET: /Recruiter/Bulk
        // MODIFIED: Added pagination, search, and filter parameters
        public async Task<IActionResult> Index(string? q, string? stage, string? sort, int page = 1, int pageSize = DefaultPageSize)
        {
            if (!this.TryGetUserId(out var recruiterId, out var early)) return early!;

            ViewData["Title"] = "Bulk Actions";

            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 5, MaxPageSize);

            // normalize sort (default newest on top)
            sort = (sort ?? "id_desc").Trim().ToLowerInvariant();
            if (sort != "id_asc" && sort != "id_desc") sort = "id_desc";

            var baseQuery = _db.job_applications
                .Include(a => a.user)
                .Include(a => a.job_listing)
                .Where(a => a.job_listing.user_id == recruiterId);

            if (!string.IsNullOrWhiteSpace(stage))
            {
                if (stage == "New")
                    baseQuery = baseQuery.Where(a => a.application_status == "Submitted" || a.application_status == null);
                else if (stage == "Hired/Rejected")
                    baseQuery = baseQuery.Where(a => a.application_status == "Hired" || a.application_status == "Rejected");
                else
                    baseQuery = baseQuery.Where(a => a.application_status == stage);
            }

            if (!string.IsNullOrWhiteSpace(q))
            {
                var qTrim = q.Trim();
                baseQuery = baseQuery.Where(a =>
                    (a.user.first_name + " " + a.user.last_name).Contains(qTrim) ||
                    a.user.email.Contains(qTrim) ||
                    a.job_listing.job_title.Contains(qTrim));
            }

            var totalCount = await baseQuery.CountAsync();
            var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
            if (page > totalPages) page = totalPages;
            var skip = (page - 1) * pageSize;

            // === ID sort toggle ===
            IOrderedQueryable<job_application> ordered = sort == "id_asc"
                ? baseQuery.OrderBy(a => a.application_id)
                : baseQuery.OrderByDescending(a => a.application_id);

            var apps = await ordered
                .Skip(skip)
                .Take(pageSize)
                .ToListAsync();

            var jobIds = apps.Select(a => a.job_listing_id).Distinct().ToList();
            var userIds = apps.Select(a => a.user_id).Distinct().ToList();

            var evals = await _db.ai_resume_evaluations
                .Where(ev => jobIds.Contains(ev.job_listing_id))
                .Join(_db.resumes,
                      ev => ev.resume_id,
                      r => r.resume_id,
                      (ev, r) => new { ev.job_listing_id, r.user_id, r.upload_date, ev.match_score })
                .Where(e => userIds.Contains(e.user_id))
                .ToListAsync();

            string MapStage(string raw)
            {
                var s = (raw ?? "").Trim();
                if (s.Equals("Submitted", StringComparison.OrdinalIgnoreCase)) return "New";
                if (s.Equals("AI-Screened", StringComparison.OrdinalIgnoreCase)) return "AI-Screened";
                if (s.Equals("Shortlisted", StringComparison.OrdinalIgnoreCase)) return "Shortlisted";
                if (s.Equals("Interview", StringComparison.OrdinalIgnoreCase)) return "Interview";
                if (s.Equals("Offer", StringComparison.OrdinalIgnoreCase)) return "Offer";
                if (s.Equals("Hired", StringComparison.OrdinalIgnoreCase)) return "Hired/Rejected";
                if (s.Equals("Rejected", StringComparison.OrdinalIgnoreCase)) return "Hired/Rejected";
                return "New";
            }

            var items = apps.Select(a =>
            {
                var score = evals
                    .Where(e => e.user_id == a.user_id && e.job_listing_id == a.job_listing_id)
                    .OrderByDescending(e => e.upload_date)
                    .Select(e => (int?)(e.match_score ?? 0))
                    .FirstOrDefault() ?? 0;

                var fullName = $"{a.user.first_name} {a.user.last_name}".Trim();
                return new CandidateItemVM(
                    Id: a.application_id,
                    Name: string.IsNullOrWhiteSpace(fullName) ? $"User #{a.user_id}" : fullName,
                    Stage: MapStage(a.application_status),
                    Score: score,
                    AppliedAt: a.date_updated.ToString("yyyy-MM-dd HH:mm"),
                    LowConfidence: false,
                    Override: false
                );
            }).ToList();

            var vm = new BulkIndexVM
            {
                Items = items,
                Page = page,
                PageSize = pageSize,
                TotalCount = totalCount,
                TotalPages = totalPages,
                Query = q ?? string.Empty,
                Stage = stage ?? string.Empty,
                Sort = sort // <-- pass through to view
            };

            return View(vm);
        }

        // POST: /Recruiter/Bulk/MoveToShortlisted
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MoveToShortlisted([FromForm] int[] selectedIds)
        {
            if (!this.TryGetUserId(out _, out var early)) return early!;

            if (selectedIds == null || selectedIds.Length == 0)
            {
                TempData["Message"] = "No candidates selected.";
                return RedirectToAction(nameof(Index));
            }

            var apps = await _db.job_applications
                .Where(a => selectedIds.Contains(a.application_id))
                .ToListAsync();

            foreach (var a in apps)
            {
                a.application_status = "Shortlisted";
                a.date_updated = MyTime.NowMalaysia();
            }

            await _db.SaveChangesAsync();

            // notify all candidates (non-blocking, batched)
            var userIds = apps.Select(a => a.user_id).Distinct().ToArray();
            await this.TryNotifyAsync(_notif, _logger, () =>
                _notif.SendManyAsync(
                    userIds,
                    title: "You were shortlisted",
                    message: "Your application status changed to Shortlisted.",
                    type: "System"
                )
            );

            TempData["Message"] = $"Moved {apps.Count} candidate(s) to Shortlisted.";
            return RedirectToAction(nameof(Index));
        }

        // POST: /Recruiter/Bulk/ExportCvsZip
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ExportCvsZip([FromForm] int[] selectedIds)
        {
            if (!this.TryGetUserId(out _, out var early)) return early!;

            if (selectedIds == null || selectedIds.Length == 0)
            {
                TempData["Message"] = "No candidates selected.";
                return RedirectToAction(nameof(Index));
            }

            var apps = await _db.job_applications
                .Include(a => a.user)
                .Where(a => selectedIds.Contains(a.application_id))
                .ToListAsync();

            var userIds = apps.Select(a => a.user_id).Distinct().ToList();

            var allResumes = await _db.resumes
                .Where(r => userIds.Contains(r.user_id))
                .OrderByDescending(r => r.upload_date)
                .ToListAsync();

            var root = _env.WebRootPath ?? AppContext.BaseDirectory;
            var chosenByUser = new Dictionary<int, (string absPath, string ext)>();

            foreach (var uid in userIds)
            {
                var candidatesForUser = allResumes
                    .Where(r => r.user_id == uid)
                    .OrderByDescending(r => r.upload_date)
                    .ToList();

                foreach (var r in candidatesForUser)
                {
                    var rel = (r.file_path ?? "")
                        .Replace('\\', '/')
                        .TrimStart('/');

                    var abs = Path.Combine(root, rel);

                    if (System.IO.File.Exists(abs))
                    {
                        chosenByUser[uid] = (abs, Path.GetExtension(abs));
                        break;
                    }
                }
            }

            if (chosenByUser.Count == 0)
            {
                TempData["Message"] = "No valid resume files found on disk.";
                return RedirectToAction(nameof(Index));
            }

            using var ms = new MemoryStream();
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var kvp in chosenByUser)
                {
                    var uid = kvp.Key;
                    var absPath = kvp.Value.absPath;
                    var ext = kvp.Value.ext;

                    var appForUser = apps.FirstOrDefault(a => a.user_id == uid);
                    var first = appForUser?.user?.first_name ?? "";
                    var last = appForUser?.user?.last_name ?? "";

                    var safeBase = $"{first}_{last}".Trim();
                    if (string.IsNullOrWhiteSpace(safeBase))
                        safeBase = $"user_{uid}";
                    safeBase = safeBase.Replace(' ', '_');

                    var entryName = $"{safeBase}{ext}";

                    var entry = zip.CreateEntry(entryName, CompressionLevel.Fastest);
                    using var src = System.IO.File.OpenRead(absPath);
                    using var dest = entry.Open();
                    await src.CopyToAsync(dest);
                }
            }

            ms.Position = 0;
            var outName = $"CVs_{MyTime.NowMalaysia():yyyyMMdd_HHmm}.zip";
            return File(ms.ToArray(), "application/zip", outName);
        }

        // ======== NEW: Bulk Messaging ========

        // GET: /Recruiter/Bulk/TemplatesJson
        // Why: supply active templates for the modal dropdown without navigating away.
        [HttpGet]
        public async Task<IActionResult> TemplatesJson()
        {
            if (!this.TryGetUserId(out var recruiterId, out var early)) return early!;
            var items = await _db.templates.AsNoTracking()
                .Where(t => t.user_id == recruiterId && t.template_status == "Active" && !t.template_name.StartsWith("[JOB]"))
                .OrderByDescending(t => t.date_updated)
                .ThenByDescending(t => t.date_created)
                .Select(t => new
                {
                    id = t.template_id,
                    name = t.template_name,
                    subject = t.template_subject,
                    snippet = (t.template_body ?? "").Length <= 120 ? (t.template_body ?? "") : (t.template_body!.Substring(0, 120) + "…"),
                    body = t.template_body ?? ""
                })
                .ToListAsync();

            return Json(items);
        }

        // POST: /Recruiter/Bulk/SendMessages
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SendMessages([FromForm] BulkMessagePostVM vm)
        {
            if (!this.TryGetUserId(out var recruiterId, out var early)) return early!;

            if (vm.SelectedIds == null || vm.SelectedIds.Length == 0)
            {
                TempData["Message"] = "No candidates selected.";
                return RedirectToAction(nameof(Index));
            }

            // Resolve text: free-typed takes precedence, else template body
            string? baseText = vm.Text;
            if (string.IsNullOrWhiteSpace(baseText) && vm.TemplateId.HasValue)
            {
                var tpl = await _db.templates.AsNoTracking()
                    .FirstOrDefaultAsync(t => t.template_id == vm.TemplateId.Value && t.user_id == recruiterId && t.template_status == "Active");
                baseText = tpl?.template_body;
            }

            if (string.IsNullOrWhiteSpace(baseText))
            {
                TempData["Message"] = "Message is empty.";
                return RedirectToAction(nameof(Index));
            }

            var apps = await _db.job_applications
                .Include(a => a.user)
                .Include(a => a.job_listing)
                .ThenInclude(j => j.company)
                .Where(a => vm.SelectedIds.Contains(a.application_id))
                .ToListAsync();

            int sent = 0;

            // Pre-fetch recruiter name for token merge
            var recruiterRow = await _db.users
                .Where(u => u.user_id == recruiterId)
                .Select(u => new { u.first_name, u.last_name })
                .FirstOrDefaultAsync();
            var recruiterName = $"{recruiterRow?.first_name} {recruiterRow?.last_name}".Trim();

            foreach (var a in apps)
            {
                var candidateId = a.user_id;
                var job = a.job_listing;

                // Find an existing conversation for this recruiter/candidate/job
                var conv = await _db.conversations
                    .Include(c => c.job_listing)
                    .FirstOrDefaultAsync(c =>
                        c.job_listing_id == job.job_listing_id &&
                        ((c.recruiter_id != null && c.recruiter_id == recruiterId) || (c.recruiter_id == null && c.job_listing.user_id == recruiterId)) &&
                        (c.candidate_id == candidateId || c.candidate_id == null));

                if (conv == null)
                {
                    // WHY: ensure a container exists so messages appear in Inbox
                    conv = new conversation
                    {
                        job_listing_id = job.job_listing_id,
                        recruiter_id = job.user_id, // owner of job (the recruiter)
                        candidate_id = candidateId,
                        candidate_name = $"{a.user.first_name} {a.user.last_name}".Trim(),
                        job_title = job.job_title,
                        created_at = MyTime.NowMalaysia(),
                        last_message_at = MyTime.NowMalaysia(),
                        last_snippet = "",
                        unread_for_candidate = 0,
                        unread_for_recruiter = 0
                    };
                    _db.conversations.Add(conv);
                    await _db.SaveChangesAsync(); // need conversation_id for messages
                }

                // Token context
                var candidateName = $"{a.user.first_name} {a.user.last_name}".Trim();
                var firstName = (candidateName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "there");
                var jobTitle = job.job_title ?? string.Empty;
                var company = job.company?.company_name ?? "";

                string text = baseText!;
                text = ReplaceInsensitive(text, "Hi User", $"Hi {firstName}");

                var context = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["FirstName"] = firstName,
                    ["JobTitle"] = jobTitle,
                    ["RecruiterName"] = recruiterName,
                    ["Company"] = company,
                    ["Date"] = vm.Date ?? "",
                    ["Time"] = vm.Time ?? ""
                };

                var tokenRegex = new Regex(@"\{\{\s*([A-Za-z0-9_]+)\s*\}\}", RegexOptions.Compiled);
                var matches = tokenRegex.Matches(text);
                foreach (Match m in matches)
                {
                    var token = m.Groups[1].Value;
                    if (context.TryGetValue(token, out var val) && !string.IsNullOrWhiteSpace(val))
                    {
                        text = ReplaceInsensitive(text, "{{" + token + "}}", val);
                    }
                }

                var now = MyTime.NowMalaysia();

                var msg = new message
                {
                    conversation_id = conv.conversation_id,
                    sender_id = recruiterId,
                    receiver_id = candidateId,
                    msg_content = text,
                    msg_timestamp = now,
                    is_read = false
                };

                _db.messages.Add(msg);

                conv.last_message_at = now;
                conv.last_snippet = text.Length > 200 ? text.Substring(0, 200) : text;
                conv.unread_for_candidate += 1;

                sent++;
            }

            await _db.SaveChangesAsync();

            // notify all recipients (non-blocking, batched)
            var recipientIds = apps.Select(a => a.user_id).Distinct().ToArray();
            await this.TryNotifyAsync(_notif, _logger, () =>
                _notif.SendManyAsync(
                    recipientIds,
                    title: "New message from recruiter",
                    message: "You received a new message in your inbox.",
                    type: "Message"
                )
            );

            TempData["Message"] = $"Message sent to {sent} candidate(s).";
            return RedirectToAction(nameof(Index));
        }

        // WHY: case-insensitive, culture-invariant token replacement
        private static string ReplaceInsensitive(string input, string search, string replace)
            => Regex.Replace(input,
                             Regex.Escape(search),
                             replace.Replace("$", "$$"),
                             RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
