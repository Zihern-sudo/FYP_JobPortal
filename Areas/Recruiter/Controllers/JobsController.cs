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

namespace JobPortal.Areas.Recruiter.Controllers
{
    [Area("Recruiter")]
    public class JobsController : Controller
    {
        private readonly AppDbContext _db;
        public JobsController(AppDbContext db) => _db = db;

        private const string TEMP_APPLY_KEY = "tpl_apply_payload";

        // Keep existing paging defaults
        private const int DefaultPageSize = 20;
        private const int MaxPageSize = 100;

        // NEW: Guarantee 1 recruiter -> 1 company for inserts (post-DB cleanup).
        // If a recruiter has no company yet, create a minimal placeholder so FK is valid.
        private async Task<int> EnsureCompanyForRecruiterAsync(int recruiterId)
        {
            var companyId = await _db.companies
                .Where(c => c.user_id == recruiterId)
                .Select(c => (int?)c.company_id)
                .FirstOrDefaultAsync();

            if (companyId.HasValue)
                return companyId.Value;

            // Create a minimal company row (only when missing).
            var user = await _db.users
                .Where(u => u.user_id == recruiterId)
                .Select(u => new { u.first_name, u.last_name })
                .FirstOrDefaultAsync();

            var displayName = (user is null)
                ? $"Recruiter {recruiterId}"
                : $"{user.first_name} {user.last_name}".Trim();

            var co = new company
            {
                user_id = recruiterId,
                company_name = $"{displayName}'s Company",
                company_industry = null,
                company_location = null,
                company_description = null,
                company_status = "Active"
            };

            _db.companies.Add(co);
            await _db.SaveChangesAsync();
            return co.company_id;
        }

        // Payload passed from JobPostTemplatesController.Apply via TempData
        private sealed class TplDto
        {
            public string? title { get; set; }
            public string? description { get; set; }
            public string? must { get; set; }
            public string? nice { get; set; }
            public string? status { get; set; }
        }

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
            {
                query = query.Where(j => j.job_status == status);
            }

            if (string.Equals(order, "asc", StringComparison.OrdinalIgnoreCase))
                query = query.OrderBy(j => j.job_listing_id);
            else if (string.Equals(order, "desc", StringComparison.OrdinalIgnoreCase))
                query = query.OrderByDescending(j => j.job_listing_id);
            else
                query = query.OrderByDescending(j => j.date_posted);

            var totalCount = await query.CountAsync();
            var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
            if (page > totalPages) page = totalPages;

            var skip = (page - 1) * pageSize;
            var jobs = await query.Skip(skip).Take(pageSize).ToListAsync();

            var vm = new JobsIndexVM
            {
                Items = jobs,
                Page = page,
                PageSize = pageSize,
                TotalCount = totalCount,
                TotalPages = totalPages,
                Query = q ?? string.Empty,
                Status = status ?? string.Empty,
                Order = order ?? string.Empty
            };

            return View(vm);
        }

        // --- CREATE (GET/POST) ---

        [HttpGet]
        public IActionResult Add(string? title = null, string? description = null, string? must = null, string? nice = null, string? status = null, bool? fromTemplate = null)
        {
            if (!this.TryGetUserId(out _, out var early)) return early!;

            var vm = new JobCreateVm
            {
                job_category = "Full Time",
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

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Add(JobCreateVm vm)
        {
            if (!this.TryGetUserId(out var recruiterId, out var early)) return early!;

            if (!ModelState.IsValid)
            {
                var firstError = ModelState.Values.SelectMany(v => v.Errors)
                    .FirstOrDefault()?.ErrorMessage;
                ViewBag.DebugError = firstError ?? "Validation failed.";
                return View(vm);
            }

            if (vm.salary_min.HasValue && vm.salary_max.HasValue && vm.salary_min.Value > vm.salary_max.Value)
            {
                ModelState.AddModelError(nameof(vm.salary_min), "Minimum salary cannot exceed maximum salary.");
                return View(vm);
            }

            // CHANGED: assign the recruiter's single company; create if missing.
            var companyId = await EnsureCompanyForRecruiterAsync(recruiterId);

            var entity = new job_listing
            {
                job_title = vm.job_title,
                job_description = vm.job_description,
                job_requirements = vm.job_requirements,
                job_requirements_nice = vm.job_requirements_nice,
                salary_min = vm.salary_min,
                salary_max = vm.salary_max,
                job_status = vm.job_status.ToString(),
                job_category = vm.job_category,
                work_mode = vm.work_mode,
                user_id = recruiterId,
                company_id = companyId,
                date_posted = DateTime.Now,
                expiry_date = vm.expiry_date
            };

            _db.job_listings.Add(entity);
            await _db.SaveChangesAsync();

            TempData["Message"] = "New job added successfully!";
            return RedirectToAction(nameof(Index));
        }

        // --- EDIT (GET/POST) ---

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
                job_category = job.job_category,
                work_mode = job.work_mode,
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

            ViewBag.FromTemplate = fromTemplate;
            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(JobEditVm vm, string? setStatus)
        {
            if (!this.TryGetUserId(out var recruiterId, out var early)) return early!;

            var job = await _db.job_listings
                .Where(j => j.user_id == recruiterId && j.job_listing_id == vm.job_listing_id)
                .FirstOrDefaultAsync();

            if (job == null) return NotFound();

            if (!string.IsNullOrWhiteSpace(setStatus) &&
                Enum.TryParse<JobStatus>(setStatus, true, out var quick))
            {
                vm.job_status = quick;
            }

            if (vm.salary_min.HasValue && vm.salary_max.HasValue && vm.salary_min.Value > vm.salary_max.Value)
            {
                ModelState.AddModelError(nameof(vm.salary_min), "Minimum salary cannot exceed maximum salary.");
                return View(vm);
            }

            job.job_status = vm.job_status.ToString();
            job.job_title = vm.job_title;
            job.job_description = vm.job_description;
            job.job_requirements = vm.job_requirements;
            job.job_requirements_nice = vm.job_requirements_nice;
            job.salary_min = vm.salary_min;
            job.salary_max = vm.salary_max;
            job.job_category = vm.job_category;
            job.work_mode = vm.work_mode;
            job.expiry_date = vm.expiry_date;

            await _db.SaveChangesAsync();

            TempData["Message"] = $"Job #{job.job_listing_id} updated.";
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> Preview(int id)
        {
            if (!this.TryGetUserId(out var recruiterId, out var early)) return early!;

            var job = await _db.job_listings
                .Include(j => j.company)
                .Where(j => j.user_id == recruiterId && j.job_listing_id == id)
                .FirstOrDefaultAsync();

            if (job == null) return NotFound();

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

            return View();
        }

        [HttpGet]
        public async Task<IActionResult> Pipeline(int id)
        {
            if (!this.TryGetUserId(out var recruiterId, out var early)) return early!;

            var job = await _db.job_listings
                .Include(j => j.company)
                .Where(j => j.user_id == recruiterId && j.job_listing_id == id)
                .FirstOrDefaultAsync();
            if (job == null) return NotFound();

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

            var cands = apps.Select(a => new CandidateItemVM(
                Id: a.application_id,
                Name: (a.user != null ? $"{a.user.first_name} {a.user.last_name}" : $"User #{a.user_id}").Trim(),
                Stage: MapStage(a.application_status),
                Score: scoreLookup.TryGetValue(a.user_id, out var sc) ? sc : 0,
                AppliedAt: a.date_updated.ToString("yyyy-MM-dd HH:mm"),
                LowConfidence: false,
                Override: formerIds.Contains(a.user_id)
            )).ToList();

            ViewBag.Job = item;
            ViewBag.Cands = cands;

            return View();
        }

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
                date_created = DateTime.Now,
                date_updated = DateTime.Now
            };
            _db.templates.Add(row);
            await _db.SaveChangesAsync();

            TempData["Message"] = "Saved job as template.";
            return RedirectToAction(nameof(Edit), new { id });
        }
    }
}
