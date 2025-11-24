// File: Areas/Recruiter/Controllers/JobsController.cs
using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using JobPortal.Areas.Shared.Models;
using JobPortal.Areas.Recruiter.Models;   // ViewModels, enums
using JobPortal.Areas.Shared.Extensions;  // TryGetUserId extension
using System.Text.Json;
using System.Collections.Generic;

namespace JobPortal.Areas.Recruiter.Controllers
{
    [Area("Recruiter")]
    public class JobsController : Controller
    {
        private readonly AppDbContext _db;
        public JobsController(AppDbContext db) => _db = db;

        private const string TEMP_APPLY_KEY = "tpl_apply_payload";
        private const int DefaultPageSize = 20;
        private const int MaxPageSize = 100;

        // Gate: recruiter can post only if company is Verified/Active
        private async Task<(bool Allowed, string? Reason, int? CompanyId)> CanPostJobAsync(int recruiterId)
        {
            var co = await _db.companies
                .Where(c => c.user_id == recruiterId)
                .Select(c => new { c.company_id, c.company_status })
                .FirstOrDefaultAsync();

            if (co == null)
                return (false, "You must create a company profile and get it verified before posting jobs.", null);

            var status = (co.company_status ?? "").Trim();
            var ok = status.Equals("Verified", StringComparison.OrdinalIgnoreCase)
                  || status.Equals("Active", StringComparison.OrdinalIgnoreCase);

            return ok
                ? (true, null, co.company_id)
                : (false, "Your company profile is not verified yet. Please complete it and wait for Admin verification.", co.company_id);
        }

        private async Task CreateApprovalForJobAsync(int jobId, int submittedByUserId)
        {
            var row = new job_post_approval
            {
                user_id = submittedByUserId,
                job_listing_id = jobId,
                approval_status = "Pending",
                comments = null,
                date_approved = null
            };
            _db.job_post_approvals.Add(row);
            await _db.SaveChangesAsync();
        }

        private sealed class TplDto
        {
            public string? title { get; set; }
            public string? description { get; set; }
            public string? must { get; set; }
            public string? nice { get; set; }
            public string? status { get; set; }
        }

        // LIST + toolbar state (hides New buttons when company not verified/active)
        public async Task<IActionResult> Index(string? q, string? status, string? order, int page = 1, int pageSize = DefaultPageSize)
        {
            if (!this.TryGetUserId(out var recruiterId, out var early)) return early!;

            ViewData["Title"] = "Jobs";
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 5, MaxPageSize);

            var query = _db.job_listings
                .Include(j => j.company)
                .Where(j => j.user_id == recruiterId);

            if (!string.IsNullOrWhiteSpace(q))
            {
                var qTrim = q.Trim();
                query = query.Where(j =>
                    j.job_title.Contains(qTrim) ||
                    (j.company != null && j.company.company_location != null && j.company.company_location.Contains(qTrim))
                );
            }

            if (!string.IsNullOrWhiteSpace(status))
                query = query.Where(j => j.job_status == status);

            query = order?.Equals("asc", StringComparison.OrdinalIgnoreCase) == true
                ? query.OrderBy(j => j.job_listing_id)
                : order?.Equals("desc", StringComparison.OrdinalIgnoreCase) == true
                    ? query.OrderByDescending(j => j.job_listing_id)
                    : query.OrderByDescending(j => j.date_posted);

            var totalCount = await query.CountAsync();
            var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
            if (page > totalPages) page = totalPages;

            var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

            // Latest approval per job (current page)
            var jobIds = items.Select(j => j.job_listing_id).ToList();
            var latestStatuses = await _db.job_post_approvals
                .Where(a => jobIds.Contains(a.job_listing_id))
                .GroupBy(a => a.job_listing_id)
                .Select(g => g.OrderByDescending(x => x.approval_id)
                              .Select(x => new { x.job_listing_id, x.approval_status })
                              .FirstOrDefault()!)
                .ToListAsync();

            var statusMap = latestStatuses
                .Where(x => x != null)
                .ToDictionary(x => x.job_listing_id, x => x.approval_status);

            // Expose allow-post flag to the view (controls toolbar buttons)
            var gate = await CanPostJobAsync(recruiterId);
            ViewBag.AllowPost = gate.Allowed;

            var vm = new JobsIndexVM
            {
                Items = items,
                Page = page,
                PageSize = pageSize,
                TotalCount = totalCount,
                TotalPages = totalPages,
                Query = q ?? string.Empty,
                Status = status ?? string.Empty,
                Order = order ?? string.Empty,
                LatestApprovalStatuses = statusMap
            };

            return View(vm);
        }

        // CREATE (GET)
        [HttpGet]
        public async Task<IActionResult> Add(string? title = null, string? description = null, string? must = null, string? nice = null, string? status = null, bool? fromTemplate = null)
        {
            if (!this.TryGetUserId(out var recruiterId, out var early)) return early!;

            // BLOCK: require Verified/Active company before opening the Add form
            var gate = await CanPostJobAsync(recruiterId);
            if (!gate.Allowed)
            {
                TempData["Message"] = gate.Reason ?? "Company verification required before posting jobs.";
                return RedirectToAction(nameof(Index));
            }

            var vm = new JobCreateVm
            {
                job_type = "Full Time",
                work_mode = "On-site"
            };

            if (TempData.Peek(TEMP_APPLY_KEY) is string payload)
            {
                try
                {
                    var dto = JsonSerializer.Deserialize<TplDto>(payload) ?? new TplDto();
                    vm.job_title = dto.title ?? "";
                    vm.job_description = dto.description ?? "";
                    vm.job_requirements = dto.must ?? "";
                    vm.job_requirements_nice = dto.nice ?? "";
                    vm.job_status = Enum.TryParse<JobStatus>(dto.status ?? "", true, out var s) ? s : JobStatus.Open;
                }
                catch
                {
                    vm.job_title = title ?? "";
                    vm.job_description = description ?? "";
                    vm.job_requirements = must ?? "";
                    vm.job_requirements_nice = nice ?? "";
                    vm.job_status = Enum.TryParse<JobStatus>(status ?? "", true, out var s) ? s : JobStatus.Open;
                }
            }
            else
            {
                vm.job_title = title ?? "";
                vm.job_description = description ?? "";
                vm.job_requirements = must ?? "";
                vm.job_requirements_nice = nice ?? "";
                vm.job_status = Enum.TryParse<JobStatus>(status ?? "", true, out var s) ? s : JobStatus.Open;
            }

            if (fromTemplate == true) TempData["Message"] = "Fields prefilled from template. Review and save.";
            return View("Add", vm);
        }

        // CREATE (POST)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Add(JobCreateVm vm)
        {
            if (!this.TryGetUserId(out var recruiterId, out var early)) return early!;
            if (!ModelState.IsValid) return View(vm);

            if (vm.salary_min.HasValue && vm.salary_max.HasValue && vm.salary_min.Value > vm.salary_max.Value)
            {
                ModelState.AddModelError(nameof(vm.salary_min), "Minimum salary cannot exceed maximum salary.");
                return View(vm);
            }

            // BLOCK: require Verified/Active company at creation
            var gate = await CanPostJobAsync(recruiterId);
            if (!gate.Allowed || gate.CompanyId == null)
            {
                TempData["Message"] = gate.Reason ?? "Company verification required before posting jobs.";
                return RedirectToAction(nameof(Index));
            }

            var entity = new job_listing
            {
                job_title = vm.job_title,
                job_description = vm.job_description,
                job_requirements = vm.job_requirements,
                job_requirements_nice = vm.job_requirements_nice,
                salary_min = vm.salary_min,
                salary_max = vm.salary_max,
                job_status = "Draft", // recruiter cannot open directly
                job_type = vm.job_type,
                work_mode = vm.work_mode,
                job_category = vm.job_category,
                user_id = recruiterId,
                company_id = gate.CompanyId.Value,
                date_posted = DateTime.UtcNow,
                expiry_date = vm.expiry_date
            };

            _db.job_listings.Add(entity);
            await _db.SaveChangesAsync();

            TempData["Message"] = "Job saved as Draft. Use 'Submit for Approval' on the Edit page.";
            return RedirectToAction(nameof(Edit), new { id = entity.job_listing_id });
        }

        // EDIT (GET)
        [HttpGet]
        public async Task<IActionResult> Edit(int id, string? title = null, string? description = null, string? must = null, string? nice = null, string? status = null, bool fromTemplate = false)
        {
            if (!this.TryGetUserId(out var recruiterId, out var early)) return early!;

            var job = await _db.job_listings.FirstOrDefaultAsync(j => j.user_id == recruiterId && j.job_listing_id == id);
            if (job == null) return NotFound();

            var statusEnum = Enum.TryParse<JobStatus>(job.job_status ?? "", true, out var parsedStatus) ? parsedStatus : JobStatus.Open;

            var vm = new JobEditVm
            {
                job_listing_id = job.job_listing_id,
                job_title = job.job_title,
                job_description = job.job_description,
                job_requirements = job.job_requirements,
                job_requirements_nice = job.job_requirements_nice,
                salary_min = job.salary_min,
                salary_max = job.salary_max,
                job_type = job.job_type,
                work_mode = job.work_mode,
                job_category = job.job_category,
                job_status = statusEnum,
                date_posted = job.date_posted,
                expiry_date = job.expiry_date
            };

            if (TempData.Peek(TEMP_APPLY_KEY) is string payload)
            {
                try
                {
                    var dto = JsonSerializer.Deserialize<TplDto>(payload) ?? new TplDto();
                    vm.job_title = dto.title ?? vm.job_title;
                    vm.job_description = dto.description ?? vm.job_description;
                    vm.job_requirements = dto.must ?? vm.job_requirements;
                    vm.job_requirements_nice = dto.nice ?? vm.job_requirements_nice;
                    if (!string.IsNullOrWhiteSpace(dto.status) && Enum.TryParse<JobStatus>(dto.status, true, out var s))
                        vm.job_status = s;
                }
                catch
                {
                    if (!string.IsNullOrWhiteSpace(title)) vm.job_title = title!;
                    if (!string.IsNullOrWhiteSpace(description)) vm.job_description = description!;
                    if (!string.IsNullOrWhiteSpace(must)) vm.job_requirements = must!;
                    if (!string.IsNullOrWhiteSpace(nice)) vm.job_requirements_nice = nice!;
                    if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<JobStatus>(status, true, out var parsed))
                        vm.job_status = parsed;
                }
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(title)) vm.job_title = title!;
                if (!string.IsNullOrWhiteSpace(description)) vm.job_description = description!;
                if (!string.IsNullOrWhiteSpace(must)) vm.job_requirements = must!;
                if (!string.IsNullOrWhiteSpace(nice)) vm.job_requirements_nice = nice!;
                if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<JobStatus>(status, true, out var parsed))
                    vm.job_status = parsed;
            }

            // Latest approval info for progress bar
            var latestApproval = await _db.job_post_approvals
                .Where(a => a.job_listing_id == id)
                .OrderByDescending(a => a.approval_id)
                .Select(a => new { a.approval_status, a.comments, a.date_approved })
                .FirstOrDefaultAsync();

            if (latestApproval != null)
            {
                ViewBag.ApprovalStatus = latestApproval.approval_status;
                ViewBag.ApprovalComments = latestApproval.comments;
                ViewBag.ApprovalUpdatedAt = latestApproval.date_approved;
            }

            // ===== NEW: if the job itself is Draft, force "ChangesRequested" in the view =====
            // This guarantees the Edit page shows 66% (ChangesRequested) immediately after any save,
            // even if there is no approval row yet or the latest row was "Pending".
            if (string.Equals(job.job_status, "Draft", StringComparison.OrdinalIgnoreCase))
            {
                ViewBag.ApprovalStatus = "ChangesRequested";
                if (ViewBag.ApprovalComments == null)
                    ViewBag.ApprovalComments = "Draft changes saved by recruiter. Resubmit for approval.";
                if (ViewBag.ApprovalUpdatedAt == null)
                    ViewBag.ApprovalUpdatedAt = DateTime.UtcNow;
            }

            ViewBag.FromTemplate = fromTemplate;
            return View(vm);
        }

        // EDIT (POST)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(JobEditVm vm, string? setStatus)
        {
            if (!this.TryGetUserId(out var recruiterId, out var early)) return early!;

            var job = await _db.job_listings
                .Where(j => j.user_id == recruiterId && j.job_listing_id == vm.job_listing_id)
                .FirstOrDefaultAsync();

            if (job == null) return NotFound();

            // Why: enforce annotations + IValidatableObject before saving
            if (!ModelState.IsValid)
                return View(vm);

            if (vm.salary_min.HasValue && vm.salary_max.HasValue && vm.salary_min.Value > vm.salary_max.Value)
            {
                ModelState.AddModelError(nameof(vm.salary_min), "Minimum salary cannot exceed maximum salary.");
                return View(vm);
            }

            // Keep as Draft until admin approves
            job.job_title = vm.job_title;
            job.job_description = vm.job_description;
            job.job_requirements = vm.job_requirements;
            job.job_requirements_nice = vm.job_requirements_nice;
            job.salary_min = vm.salary_min;
            job.salary_max = vm.salary_max;
            job.job_type = vm.job_type;
            job.work_mode = vm.work_mode;
            job.job_category = vm.job_category;
            job.expiry_date = vm.expiry_date;
            job.job_status = "Draft";

            await _db.SaveChangesAsync();

            TempData["Message"] = $"Job #{job.job_listing_id} saved as Draft.";
            return RedirectToAction(nameof(Edit), new { id = job.job_listing_id });
        }

        // SUBMIT FOR APPROVAL
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SubmitForApproval(int id)
        {
            if (!this.TryGetUserId(out var recruiterId, out var early)) return early!;

            // BLOCK: require Verified/Active company when submitting for approval
            var gate = await CanPostJobAsync(recruiterId);
            if (!gate.Allowed)
            {
                TempData["Message"] = gate.Reason ?? "Company verification required before submitting jobs for approval.";
                return RedirectToAction(nameof(Edit), new { id });
            }

            var job = await _db.job_listings
                .Where(j => j.user_id == recruiterId && j.job_listing_id == id)
                .FirstOrDefaultAsync();

            if (job == null) return NotFound();

            job.job_status = "Draft"; // enforce Draft at submission
            await _db.SaveChangesAsync();

            await CreateApprovalForJobAsync(job.job_listing_id, recruiterId);

            TempData["Message"] = $"Job #{job.job_listing_id} submitted for Admin approval.";
            return RedirectToAction(nameof(Edit), new { id = job.job_listing_id });
        }

        // PREVIEW (only for Open jobs)
        [HttpGet]
        public async Task<IActionResult> Preview(int id)
        {
            if (!this.TryGetUserId(out var recruiterId, out var early)) return early!;

            var job = await _db.job_listings
                .Include(j => j.company)
                .FirstOrDefaultAsync(j => j.user_id == recruiterId && j.job_listing_id == id);

            if (job == null) return NotFound();

            if (!string.Equals(job.job_status, "Open", StringComparison.OrdinalIgnoreCase))
            {
                TempData["Message"] = "Preview is available only for Open jobs. Submit for approval and wait for Admin approval.";
                return RedirectToAction(nameof(Edit), new { id });
            }

            var statusEnum = Enum.TryParse<JobStatus>(job.job_status ?? "", true, out var s) ? s : JobStatus.Open;

            var item = new JobItemVM(
                Id: job.job_listing_id,
                Title: job.job_title,
                Location: job.company?.company_location ?? string.Empty,
                Status: statusEnum,
                CreatedAt: job.date_posted.ToString("yyyy-MM-dd HH:mm")
            );

            var req = new JobRequirementVM(
                MustHaves: string.IsNullOrWhiteSpace(job.job_requirements) ? "(not specified)" : job.job_requirements!,
                NiceToHaves: job.job_requirements_nice
            );

            ViewBag.Job = item;
            ViewBag.Req = req;
            ViewBag.Desc = job.job_description;
            ViewBag.SalaryMin = job.salary_min;
            ViewBag.SalaryMax = job.salary_max;
            ViewBag.JobCategory = job.job_category;
            ViewBag.WorkMode = job.work_mode;
            ViewBag.Company = job.company?.company_name;
            ViewBag.ExpiryDate = job.expiry_date;

            return View();
        }

        // PIPELINE (only for Open jobs)
        [HttpGet]
        public async Task<IActionResult> Pipeline(int id)
        {
            if (!this.TryGetUserId(out var recruiterId, out var early)) return early!;

            var job = await _db.job_listings
                .Include(j => j.company)
                .FirstOrDefaultAsync(j => j.user_id == recruiterId && j.job_listing_id == id);

            if (job == null) return NotFound();

            if (!string.Equals(job.job_status, "Open", StringComparison.OrdinalIgnoreCase))
            {
                TempData["Message"] = "Pipeline is available only for Open jobs. Submit for approval and wait for Admin approval.";
                return RedirectToAction(nameof(Edit), new { id });
            }

            var statusEnum = Enum.TryParse<JobStatus>(job.job_status ?? "", true, out var s) ? s : JobStatus.Open;

            var item = new JobItemVM(
                Id: job.job_listing_id,
                Title: job.job_title,
                Location: job.company?.company_location ?? string.Empty,
                Status: statusEnum,
                CreatedAt: job.date_posted.ToString("yyyy-MM-dd HH:mm")
            );

            var apps = await _db.job_applications
                .Include(a => a.user)
                .Where(a => a.job_listing_id == id)
                .OrderByDescending(a => a.date_updated)
                .ToListAsync();

            var applicantIds = apps.Select(a => a.user_id).Distinct().ToList();
            var appIds = apps.Select(a => a.application_id).ToList();

            var scoreLookup = await _db.ai_resume_evaluations
                .Where(ev => ev.job_listing_id == id)
                .Join(_db.resumes, ev => ev.resume_id, r => r.resume_id,
                      (ev, r) => new { r.user_id, r.upload_date, ev.match_score })
                .Where(x => applicantIds.Contains(x.user_id))
                .GroupBy(x => x.user_id)
                .Select(g => new
                {
                    user_id = g.Key,
                    score = g.OrderByDescending(x => x.upload_date)
                             .Select(x => (int?)(x.match_score ?? 0))
                             .FirstOrDefault() ?? 0
                })
                .ToDictionaryAsync(x => x.user_id, x => x.score);

            var formerIds = await _db.job_applications
                .Include(a => a.job_listing)
                .Where(a => a.application_status == "Hired" && a.job_listing.company_id == job.company_id)
                .Select(a => a.user_id)
                .Distinct()
                .ToListAsync();

            // === NEW: latest manual override per application (for recency sort) ===
            var latestOverrideAt = await _db.ai_rank_overrides
                .Where(o => o.job_listing_id == id && appIds.Contains(o.application_id))
                .GroupBy(o => o.application_id)
                .Select(g => new { application_id = g.Key, at = g.Max(x => x.created_at) })
                .ToDictionaryAsync(x => x.application_id, x => (DateTime?)x.at);

            string MapStage(string dbStatusRaw)
            {
                var dbStatus = (dbStatusRaw ?? "").Trim();
                if (dbStatus.Equals("Submitted", StringComparison.OrdinalIgnoreCase)) return "New";
                if (dbStatus.Equals("Hired", StringComparison.OrdinalIgnoreCase)) return "Hired/Rejected";
                if (dbStatus.Equals("Rejected", StringComparison.OrdinalIgnoreCase)) return "Hired/Rejected";
                if (dbStatus.Equals("AI-Screened", StringComparison.OrdinalIgnoreCase)) return "AI-Screened";
                if (dbStatus.Equals("Shortlisted", StringComparison.OrdinalIgnoreCase)) return "Shortlisted";
                if (dbStatus.Equals("Interview", StringComparison.OrdinalIgnoreCase)) return "Interview";
                if (dbStatus.Equals("Offer", StringComparison.OrdinalIgnoreCase)) return "Offer";
                return "New";
            }

            var composed = apps.Select(a => new
            {
                VM = new CandidateItemVM(
                    Id: a.application_id,
                    Name: (a.user != null ? $"{a.user.first_name} {a.user.last_name}" : $"User #{a.user_id}").Trim(),
                    Stage: MapStage(a.application_status),
                    Score: scoreLookup.TryGetValue(a.user_id, out var sc) ? sc : 0,
                    AppliedAt: a.date_updated.ToString("yyyy-MM-dd HH:mm"),
                    LowConfidence: false,
                    Override: formerIds.Contains(a.user_id)
                ),
                OverrideAt = latestOverrideAt.TryGetValue(a.application_id, out var at) ? at : (DateTime?)null,
                Score = scoreLookup.TryGetValue(a.user_id, out var sc2) ? sc2 : 0,
                Applied = a.date_updated
            });

            // === NEW: sort by override recency → score → applied date ===
            var cands = composed
                .OrderByDescending(x => x.OverrideAt ?? DateTime.MinValue)
                .ThenByDescending(x => x.Score)
                .ThenByDescending(x => x.Applied)
                .Select(x => x.VM)
                .ToList();

            ViewBag.Job = item;
            ViewBag.Cands = cands;

            // NEW: supply requirement lines to the view for Scoring Rules
            static string[] SplitLines(string? s) =>
                string.IsNullOrWhiteSpace(s)
                    ? Array.Empty<string>()
                    : s.Replace("\r", "\n")
                       .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var must = SplitLines(job.job_requirements);
            var nice = SplitLines(job.job_requirements_nice);
            var reqs = must.Concat(nice)
                           .Where(x => !string.IsNullOrWhiteSpace(x))
                           .Distinct(StringComparer.OrdinalIgnoreCase)
                           .Take(20)
                           .ToArray();
            ViewBag.Reqs = reqs;

            return View();
        }

        // Save current job as template
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveAsTemplate(int id, string name)
        {
            if (!this.TryGetUserId(out var recruiterId, out var early)) return early!;
            if (string.IsNullOrWhiteSpace(name)) { TempData["Message"] = "Template name required."; return RedirectToAction(nameof(Edit), new { id }); }

            var job = await _db.job_listings.FirstOrDefaultAsync(j => j.user_id == recruiterId && j.job_listing_id == id);
            if (job == null) return NotFound();

            var dto = new
            {
                title = job.job_title,
                description = job.job_description,
                must = job.job_requirements,
                nice = job.job_requirements_nice,
                status = job.job_status
            };
            var body = JsonSerializer.Serialize(dto);

            var row = new template
            {
                user_id = recruiterId,
                template_name = "[JOB] " + name.Trim(),
                template_subject = null,
                template_body = body,
                template_status = "Active",
                date_created = DateTime.UtcNow,
                date_updated = DateTime.UtcNow
            };
            _db.templates.Add(row);
            await _db.SaveChangesAsync();

            TempData["Message"] = "Saved job as template.";
            return RedirectToAction(nameof(Edit), new { id });
        }

        // Move candidate within pipeline
        private static readonly Dictionary<string, string> _nextStage = new(StringComparer.OrdinalIgnoreCase)
        {
            ["AI-Screened"] = "Shortlisted",
            ["Shortlisted"] = "Interview",
            ["Interview"] = "Offer"
        };

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MoveCandidate(int jobId, int applicationId, string toStage)
        {
            if (!this.TryGetUserId(out var recruiterId, out var early)) return early!;
            if (string.IsNullOrWhiteSpace(toStage)) return BadRequest(new { ok = false, error = "Missing target stage." });

            if (toStage.Equals("Offer", StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { ok = false, error = "Use CreateOffer endpoint for Offer stage." });

            var job = await _db.job_listings
                .Where(j => j.user_id == recruiterId && j.job_listing_id == jobId)
                .FirstOrDefaultAsync();
            if (job is null) return NotFound(new { ok = false, error = "Job not found." });

            var app = await _db.job_applications
                .Where(a => a.job_listing_id == jobId && a.application_id == applicationId)
                .FirstOrDefaultAsync();
            if (app is null) return NotFound(new { ok = false, error = "Application not found." });

            var current = (app.application_status ?? "").Trim();
            if (!_nextStage.TryGetValue(current, out var allowed) || !allowed.Equals(toStage, StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { ok = false, error = $"Invalid transition: {current} → {toStage}." });

            app.application_status = toStage;
            app.date_updated = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            return Json(new { ok = true, applicationId, newStage = toStage });
        }

        // Create offer
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateOffer(Recruiter.Models.OfferFormVM vm)
        {
            if (!this.TryGetUserId(out var recruiterId, out var early)) return early!;
            if (vm is null || vm.ApplicationId <= 0) return BadRequest(new { ok = false, error = "Invalid payload." });

            var app = await _db.job_applications
                .Include(a => a.job_listing)
                .Where(a => a.application_id == vm.ApplicationId)
                .FirstOrDefaultAsync();

            if (app is null) return NotFound(new { ok = false, error = "Application not found." });
            if (app.job_listing.user_id != recruiterId) return Forbid();

            var current = (app.application_status ?? "").Trim();
            if (!_nextStage.TryGetValue(current, out var allowed) || !allowed.Equals("Offer", StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { ok = false, error = $"Invalid transition: {current} → Offer." });

            var offer = new Areas.Shared.Models.job_offer
            {
                application_id = app.application_id,
                offer_status = "Sent",
                salary_offer = vm.SalaryOffer,
                start_date = vm.StartDate.HasValue ? DateOnly.FromDateTime(vm.StartDate.Value.Date) : null,
                contract_type = vm.ContractType,
                notes = vm.Notes,
                candidate_token = Guid.NewGuid(),
                date_sent = DateTime.UtcNow,
                date_updated = DateTime.UtcNow
            };
            _db.job_offers.Add(offer);

            app.application_status = "Offer";
            app.date_updated = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            return Json(new
            {
                ok = true,
                applicationId = app.application_id,
                newStage = "Offer",
                offerId = offer.offer_id
            });
        }

        // AJAX: latest approval info (used by Edit + Index polling UIs)
        [HttpGet]
        public async Task<IActionResult> ApprovalInfo(int id)
        {
            if (!this.TryGetUserId(out var recruiterId, out var early)) return early!;

            var owns = await _db.job_listings
                .AnyAsync(j => j.user_id == recruiterId && j.job_listing_id == id);
            if (!owns) return NotFound();

            var a = await _db.job_post_approvals
                .Where(x => x.job_listing_id == id)
                .OrderByDescending(x => x.approval_id)
                .Select(x => new { status = x.approval_status, x.comments, x.date_approved })
                .FirstOrDefaultAsync();

            return Json(new
            {
                ok = true,
                status = a?.status ?? "None",
                comments = a?.comments,
                updatedAt = a?.date_approved
            });
        }

        // ===========================
        // ===== SCORING RULES =======
        // ===========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ScoringRules(int id, [FromForm] string[]? weight2)
        {
            if (!this.TryGetUserId(out var recruiterId, out var early)) return early!;

            var job = await _db.job_listings
                .Where(j => j.user_id == recruiterId && j.job_listing_id == id)
                .Select(j => new { j.job_listing_id })
                .FirstOrDefaultAsync();

            if (job is null) return NotFound(new { ok = false, error = "Job not found" });

            var norms = (weight2 ?? Array.Empty<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(30)
                .ToArray();

            var json = JsonSerializer.Serialize(new { weight2 = norms });

            var row = await _db.ai_scoring_rules
                .FirstOrDefaultAsync(r => r.user_id == recruiterId && r.job_listing_id == id);

            if (row == null)
            {
                row = new ai_scoring_rule
                {
                    user_id = recruiterId,
                    job_listing_id = id,
                    rule_json = json,
                    updated_at = DateTime.UtcNow
                };
                _db.ai_scoring_rules.Add(row);
            }
            else
            {
                row.rule_json = json;
                row.updated_at = DateTime.UtcNow;
            }

            await _db.SaveChangesAsync();

            return Json(new { ok = true, saved = norms.Length });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ReScore(int id)
        {
            if (!this.TryGetUserId(out var recruiterId, out var early)) return early!;

            var owns = await _db.job_listings
                .AnyAsync(j => j.user_id == recruiterId && j.job_listing_id == id);
            if (!owns) return NotFound(new { ok = false, error = "Job not found" });

            var row = await _db.ai_scoring_rules
                .Where(r => r.user_id == recruiterId && r.job_listing_id == id)
                .OrderByDescending(r => r.updated_at)
                .FirstOrDefaultAsync();

            string[] weight2 = Array.Empty<string>();
            if (row != null)
            {
                try
                {
                    using var doc = JsonDocument.Parse(row.rule_json ?? "{}");
                    if (doc.RootElement.TryGetProperty("weight2", out var arr) && arr.ValueKind == JsonValueKind.Array)
                        weight2 = arr.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
                }
                catch
                {
                    weight2 = Array.Empty<string>();
                }
            }

            return Json(new { ok = true, weight2 });
        }

        // =====================================
        // ===== Manual Ranking: OverrideRank ==
        // =====================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> OverrideRank(int id, int applicationId, string direction, string reason)
        {
            if (!this.TryGetUserId(out var recruiterId, out var early)) return early!;

            reason = (reason ?? "").Trim();
            if (string.IsNullOrWhiteSpace(direction))
                return BadRequest(new { ok = false, error = "Missing direction." });

            var dir = direction.Trim().ToLowerInvariant();
            var delta = dir == "down" ? -1 : 1; // default to up

            // Validate job ownership
            var job = await _db.job_listings
                .Where(j => j.user_id == recruiterId && j.job_listing_id == id)
                .Select(j => new { j.job_listing_id })
                .FirstOrDefaultAsync();
            if (job is null) return NotFound(new { ok = false, error = "Job not found." });

            // Validate application belongs to job
            var app = await _db.job_applications
                .Where(a => a.job_listing_id == id && a.application_id == applicationId)
                .FirstOrDefaultAsync();
            if (app is null) return NotFound(new { ok = false, error = "Application not found." });

            var now = DateTime.UtcNow;

            // Persist override (recency drives sort)
            _db.ai_rank_overrides.Add(new ai_rank_override
            {
                job_listing_id = id,
                application_id = applicationId,
                user_id = recruiterId,
                direction = (sbyte)delta,
                reason = string.IsNullOrWhiteSpace(reason) ? "-" : reason,
                created_at = now
            });

            // Optional: audit trail inside candidate notes
            if (!string.IsNullOrWhiteSpace(reason))
            {
                _db.job_seeker_notes.Add(new job_seeker_note
                {
                    application_id = applicationId,
                    job_recruiter_id = recruiterId,
                    job_seeker_id = app.user_id,
                    note_text = $"[Rank {(delta > 0 ? "UP" : "DOWN")}] {reason}",
                    created_at = now
                });
            }

            await _db.SaveChangesAsync();

            return Json(new { ok = true, applicationId, dir = (delta > 0 ? "up" : "down"), at = now.ToString("s") });
        }
    }
}
