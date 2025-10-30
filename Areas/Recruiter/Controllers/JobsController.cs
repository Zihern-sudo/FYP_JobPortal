using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using JobPortal.Areas.Shared.Models;
using JobPortal.Areas.Recruiter.Models;   // ViewModels, enums
using JobPortal.Areas.Shared.Extensions;  // TryGetUserId extension

namespace JobPortal.Areas.Recruiter.Controllers
{
    [Area("Recruiter")]
    public class JobsController : Controller
    {
        private readonly AppDbContext _db;
        public JobsController(AppDbContext db) => _db = db;

        // Single source of truth for splitting/combining requirements
        private const string NICE_DELIM = "\n||NICE||\n";

        private static (string must, string? nice) SplitReq(string? stored)
        {
            if (string.IsNullOrWhiteSpace(stored)) return ("", null);
            var parts = stored.Split(NICE_DELIM);
            var must = parts[0].Trim();
            var nice = parts.Length > 1 ? parts[1].Trim() : null;
            return (must, string.IsNullOrWhiteSpace(nice) ? null : nice);
        }

        private static string CombineReq(string? must, string? nice)
        {
            must = (must ?? "").Trim();
            nice = (nice ?? "").Trim();
            return string.IsNullOrEmpty(nice) ? must : $"{must}{NICE_DELIM}{nice}";
        }

        // LIST
        public async Task<IActionResult> Index(string? order)
        {
            if (!this.TryGetUserId(out var recruiterId, out var early)) return early!;

            ViewData["Title"] = "Jobs";

            // base query
            var query = _db.job_listings.Where(j => j.user_id == recruiterId);

            // sort by id only when explicitly requested; otherwise default to date_posted desc
            if (string.Equals(order, "asc", StringComparison.OrdinalIgnoreCase))
            {
                query = query.OrderBy(j => j.job_listing_id);
                ViewBag.Order = "asc";
            }
            else if (string.Equals(order, "desc", StringComparison.OrdinalIgnoreCase))
            {
                query = query.OrderByDescending(j => j.job_listing_id);
                ViewBag.Order = "desc";
            }
            else
            {
                query = query.OrderByDescending(j => j.date_posted);
                ViewBag.Order = null; // no highlight in the UI
            }

            var jobs = await query.ToListAsync();
            return View(jobs);
        }

        // --- CREATE (GET/POST) ---

        [HttpGet]
        public IActionResult Add()
        {
            if (!this.TryGetUserId(out _, out var early)) return early!;
            var vm = new JobCreateVm { job_status = JobStatus.Open };
            return View(vm);
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

            // TODO: If company is tied to recruiter, fetch it; keeping existing default for now.
            var companyId = 1;

            var entity = new job_listing
            {
                job_title = vm.job_title,
                job_description = vm.job_description,
                job_requirements = CombineReq(vm.job_requirements, vm.job_requirements_nice),
                salary_min = vm.salary_min,
                salary_max = vm.salary_max,
                job_status = vm.job_status.ToString(),
                user_id = recruiterId,
                company_id = companyId,
                date_posted = DateTime.Now
            };

            _db.job_listings.Add(entity);
            await _db.SaveChangesAsync();

            TempData["Message"] = "New job added successfully!";
            return RedirectToAction(nameof(Index));
        }

        // --- EDIT (GET/POST) ---

        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            if (!this.TryGetUserId(out var recruiterId, out var early)) return early!;

            ViewData["Title"] = "Edit Job";

            var job = await _db.job_listings
                .Where(j => j.user_id == recruiterId && j.job_listing_id == id)
                .FirstOrDefaultAsync();

            if (job == null) return NotFound();

            var (must, nice) = SplitReq(job.job_requirements);

            var vm = new JobEditVm
            {
                job_listing_id = job.job_listing_id,
                job_status = Enum.TryParse<JobStatus>(job.job_status, true, out var s) ? s : JobStatus.Open,
                job_title = job.job_title,
                job_description = job.job_description,
                job_requirements = must,
                job_requirements_nice = nice,
                date_posted = job.date_posted
            };

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

            // Quick status buttons (if any) still work
            if (!string.IsNullOrWhiteSpace(setStatus) &&
                Enum.TryParse<JobStatus>(setStatus, true, out var quick))
            {
                vm.job_status = quick;
            }

            // Persist editable fields
            job.job_status = vm.job_status.ToString();
            job.job_title = vm.job_title;
            job.job_description = vm.job_description;
            job.job_requirements = CombineReq(vm.job_requirements, vm.job_requirements_nice);

            await _db.SaveChangesAsync();

            TempData["Message"] = $"Job #{job.job_listing_id} updated.";
            return RedirectToAction(nameof(Index));
        }

        // --- PREVIEW ---

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
            var (must, nice) = SplitReq(job.job_requirements);

            var item = new JobItemVM(
                Id: job.job_listing_id,
                Title: job.job_title,
                Location: job.company?.company_location ?? string.Empty,
                Status: statusEnum,
                CreatedAt: job.date_posted.ToString("yyyy-MM-dd HH:mm")
            );

            var req = new JobRequirementVM(
                MustHaves: string.IsNullOrWhiteSpace(must) ? "(not specified)" : must,
                NiceToHaves: nice
            );

            ViewBag.Job = item;
            ViewBag.Req = req;
            ViewBag.Desc = job.job_description;
            ViewBag.SalaryMin = job.salary_min;
            ViewBag.SalaryMax = job.salary_max;
            ViewBag.Company = job.company?.company_name;

            return View();
        }

        // --- PIPELINE (Kanban) ---

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

            // Pull applications for this job
            var apps = await _db.job_applications
                .Include(a => a.user)
                .Where(a => a.job_listing_id == id)
                .OrderByDescending(a => a.date_updated)
                .ToListAsync();

            // Get latest match score per applicant for this job (if any)
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

            // Robust mapping of DB status -> board column (trim + case-insensitive)
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
                Override: false
            )).ToList();

            ViewBag.Job = item;
            ViewBag.Cands = cands;

            return View();
        }
    }
}
