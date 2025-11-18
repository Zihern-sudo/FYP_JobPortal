// File: Areas/Recruiter/Controllers/JobPostTemplatesController.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using JobPortal.Areas.Shared.Models;
using JobPortal.Areas.Recruiter.Models;
using JobPortal.Areas.Shared.Extensions;

namespace JobPortal.Areas.Recruiter.Controllers
{
    [Area("Recruiter")]
    public class JobPostTemplatesController : Controller
    {
        private readonly AppDbContext _db;
        public JobPostTemplatesController(AppDbContext db) => _db = db;

        private const string JOB_PREFIX = "[JOB] ";
        private const int RECENT_LIMIT = 5;
        private const string TEMP_APPLY_KEY = "tpl_apply_payload";

        private const int DefaultPageSize = 20;
        private const int MaxPageSize = 100;

        // EXTENDED: carry all job-listing aligned fields
        private sealed class JobTplDto
        {
            public string? title { get; set; }
            public string? description { get; set; }
            public string? must { get; set; }
            public string? nice { get; set; }

            public string? job_type { get; set; }              // Employment Type
            public string? work_mode { get; set; }
            public string? job_category { get; set; }
            public decimal? salary_min { get; set; }
            public decimal? salary_max { get; set; }
            public DateTime? expiry_date { get; set; }

            // kept for backward-compat in stored templates; not shown/edited in UI
            public string? status { get; set; }
        }

        private string RecentKey(int recruiterId) => $"recent_job_tpl_ids_{recruiterId}";

        private List<int> GetRecentIds(int recruiterId)
        {
            var s = HttpContext.Session.GetString(RecentKey(recruiterId));
            if (string.IsNullOrWhiteSpace(s)) return new List<int>();
            return s.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => int.TryParse(x, out var v) ? v : 0)
                    .Where(v => v > 0).ToList();
        }

        private void PushRecentId(int recruiterId, int templateId)
        {
            var list = GetRecentIds(recruiterId);
            list.Remove(templateId);
            list.Insert(0, templateId);
            if (list.Count > RECENT_LIMIT) list = list.Take(RECENT_LIMIT).ToList();
            HttpContext.Session.SetString(RecentKey(recruiterId), string.Join(",", list));
        }

        [HttpGet]
        public async Task<IActionResult> Modal(int jobId, string? q = null)
        {
            if (!this.TryGetUserId(out var recruiterId, out var early)) return early!;
            ViewBag.JobId = jobId;
            ViewBag.Query = q ?? "";

            var baseQuery = _db.templates.AsNoTracking()
                .Where(t => t.user_id == recruiterId
                            && t.template_status == "Active"
                            && t.template_name.StartsWith(JOB_PREFIX));

            var recentIds = GetRecentIds(recruiterId);
            var recentRows = recentIds.Count == 0
                ? new List<template>()
                : await baseQuery.Where(t => recentIds.Contains(t.template_id)).ToListAsync();

            var recents = recentRows
                .OrderBy(t => recentIds.IndexOf(t.template_id))
                .Select(t => new TemplateRowVM(
                    t.template_id,
                    t.template_name.Substring(JOB_PREFIX.Length),
                    BuildSnippet(t.template_body)))
                .ToList();

            var qTrim = (q ?? "").Trim();
            var mainQuery = baseQuery;
            if (!string.IsNullOrEmpty(qTrim))
            {
                mainQuery = mainQuery.Where(t =>
                    t.template_name.Contains(qTrim) ||
                    (t.template_body ?? "").Contains(qTrim));
            }
            if (recents.Any())
            {
                var exclude = recents.Select(r => r.Id).ToList();
                mainQuery = mainQuery.Where(t => !exclude.Contains(t.template_id));
            }

            var mainRows = await mainQuery
                .OrderByDescending(t => t.date_updated)
                .ThenByDescending(t => t.date_created)
                .Take(50)
                .Select(t => new { t.template_id, t.template_name, t.template_body })
                .ToListAsync();

            var mains = mainRows
                .Select(t => new TemplateRowVM(
                    t.template_id,
                    t.template_name.Substring(JOB_PREFIX.Length),
                    BuildSnippet(t.template_body)))
                .ToList();

            var vm = new TemplateModalVM(jobId, qTrim, recents, mains);
            return PartialView("Modal", vm);
        }

        private static string BuildSnippet(string? body)
        {
            try
            {
                var dto = JsonSerializer.Deserialize<JobTplDto>(body ?? "{}");
                var text = (dto?.description ?? dto?.must ?? "").Trim();
                if (text.Length == 0) return "";
                return text.Length > 120 ? text[..120] + "…" : text;
            }
            catch
            {
                var b = (body ?? "").Trim();
                return b.Length > 120 ? b[..120] + "…" : b;
            }
        }

        [HttpGet]
        public async Task<IActionResult> Apply(int id, int jobId)
        {
            if (!this.TryGetUserId(out var recruiterId, out var early)) return early!;
            var row = await _db.templates.AsNoTracking()
                .FirstOrDefaultAsync(t => t.template_id == id && t.user_id == recruiterId && t.template_name.StartsWith(JOB_PREFIX));
            if (row == null) return NotFound();

            PushRecentId(recruiterId, id);

            var dto = JsonSerializer.Deserialize<JobTplDto>(row.template_body ?? "{}") ?? new JobTplDto();

            TempData[TEMP_APPLY_KEY] = JsonSerializer.Serialize(dto);

            if (jobId <= 0)
                return RedirectToAction("Add", "Jobs", new { area = "Recruiter", fromTemplate = true });

            return RedirectToAction("Edit", "Jobs", new { area = "Recruiter", id = jobId, fromTemplate = true });
        }

        [HttpGet]
        public async Task<IActionResult> Preview(int id, int? jobId = null)
        {
            if (!this.TryGetUserId(out var recruiterId, out var early)) return early!;
            var row = await _db.templates.AsNoTracking()
                .FirstOrDefaultAsync(t => t.template_id == id && t.user_id == recruiterId && t.template_name.StartsWith(JOB_PREFIX));
            if (row == null) return NotFound();

            var dto = JsonSerializer.Deserialize<JobTplDto>(row.template_body ?? "{}") ?? new JobTplDto();

            ViewBag.TemplateId = id;
            ViewBag.Name = row.template_name.Substring(JOB_PREFIX.Length);
            ViewBag.Title = dto.title ?? "";
            ViewBag.Description = dto.description ?? "";
            ViewBag.Must = dto.must ?? "";
            ViewBag.Nice = dto.nice ?? "";

            // aligned fields
            ViewBag.JobType = dto.job_type ?? "Full Time";
            ViewBag.WorkMode = dto.work_mode ?? "On-site";
            ViewBag.JobCategory = dto.job_category ?? "Marketing";
            ViewBag.SalaryMin = dto.salary_min;
            ViewBag.SalaryMax = dto.salary_max;
            ViewBag.ExpiryDate = dto.expiry_date;

            if (jobId.HasValue && jobId.Value > 0)
            {
                var job = await _db.job_listings.AsNoTracking()
                    .FirstOrDefaultAsync(j => j.job_listing_id == jobId.Value && j.user_id == recruiterId);
                if (job != null)
                {
                    ViewBag.CurTitle = job.job_title ?? "";
                    ViewBag.CurDescription = job.job_description ?? "";
                    ViewBag.CurMust = job.job_requirements ?? "";
                    ViewBag.CurNice = job.job_requirements_nice ?? "";
                    ViewBag.CurJobType = job.job_type ?? "Full Time";
                    ViewBag.CurWorkMode = job.work_mode ?? "On-site";
                    ViewBag.CurJobCategory = job.job_category ?? "Marketing";
                    ViewBag.CurSalaryMin = job.salary_min;
                    ViewBag.CurSalaryMax = job.salary_max;
                    ViewBag.CurExpiryDate = job.expiry_date;
                    ViewBag.JobId = jobId.Value;
                }
                else
                {
                    ViewBag.JobId = null;
                }
            }
            else
            {
                ViewBag.JobId = null;
            }

            return View();
        }

        [HttpGet]
        public async Task<IActionResult> Index(string? q, int page = 1, int pageSize = DefaultPageSize, int? jobId = null)
        {
            if (!this.TryGetUserId(out var recruiterId, out var early)) return early!;
            ViewData["Title"] = "Job Post Templates";

            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 5, MaxPageSize);

            var baseQuery = _db.templates.AsNoTracking()
                .Where(t => t.user_id == recruiterId
                            && t.template_status == "Active"
                            && t.template_name.StartsWith(JOB_PREFIX));

            if (!string.IsNullOrWhiteSpace(q))
            {
                var qTrim = q.Trim();
                baseQuery = baseQuery.Where(t =>
                    t.template_name.Contains(qTrim) ||
                    (t.template_body != null && t.template_body.Contains(qTrim))
                );
            }

            var totalCount = await baseQuery.CountAsync();
            var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
            if (page > totalPages) page = totalPages;

            var skip = (page - 1) * pageSize;

            var rows = await baseQuery
                .OrderByDescending(t => t.date_updated)
                .ThenByDescending(t => t.date_created)
                .Skip(skip)
                .Take(pageSize)
                .ToListAsync();

            var items = rows.Select(t =>
            {
                string snippet;
                try
                {
                    var dto = JsonSerializer.Deserialize<JobTplDto>(t.template_body ?? "{}") ?? new JobTplDto();
                    snippet = (dto.description ?? dto.must ?? "").Trim();
                }
                catch { snippet = ""; }
                if (snippet.Length > 120) snippet = snippet.Substring(0, 120) + "…";
                return new TemplateItemVM(t.template_id, t.template_name.Substring(JOB_PREFIX.Length), null, snippet);
            }).ToList();

            var vm = new TemplatesIndexVM
            {
                Items = items,
                Page = page,
                PageSize = pageSize,
                TotalCount = totalCount,
                TotalPages = totalPages,
                Query = q ?? string.Empty,
                IsArchivedList = false,
                IsJobPost = true,
                ThreadId = jobId
            };

            return View(vm);
        }

        [HttpGet]
        public async Task<IActionResult> Archived(string? q, int page = 1, int pageSize = DefaultPageSize)
        {
            if (!this.TryGetUserId(out var recruiterId, out var early)) return early!;
            ViewData["Title"] = "Archived Job Post Templates";

            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 5, MaxPageSize);

            var baseQuery = _db.templates.AsNoTracking()
                .Where(t => t.user_id == recruiterId
                            && t.template_status == "Archived"
                            && t.template_name.StartsWith(JOB_PREFIX));

            if (!string.IsNullOrWhiteSpace(q))
            {
                var qTrim = q.Trim();
                baseQuery = baseQuery.Where(t =>
                    t.template_name.Contains(qTrim) ||
                    (t.template_body != null && t.template_body.Contains(qTrim))
                );
            }

            var totalCount = await baseQuery.CountAsync();
            var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
            if (page > totalPages) page = totalPages;

            var skip = (page - 1) * pageSize;

            var rows = await baseQuery
                .OrderByDescending(t => t.date_updated)
                .ThenByDescending(t => t.date_created)
                .Skip(skip)
                .Take(pageSize)
                .ToListAsync();

            var items = rows.Select(t =>
            {
                string snippet;
                try
                {
                    var dto = JsonSerializer.Deserialize<JobTplDto>(t.template_body ?? "{}") ?? new JobTplDto();
                    snippet = (dto.description ?? dto.must ?? "").Trim();
                }
                catch { snippet = ""; }
                if (snippet.Length > 120) snippet = snippet.Substring(0, 120) + "…";
                return new TemplateItemVM(t.template_id, t.template_name.Substring(JOB_PREFIX.Length), null, snippet);
            }).ToList();

            var vm = new TemplatesIndexVM
            {
                Items = items,
                Page = page,
                PageSize = pageSize,
                TotalCount = totalCount,
                TotalPages = totalPages,
                Query = q ?? string.Empty,
                IsArchivedList = true,
                IsJobPost = true,
                ThreadId = null
            };

            return View("Index", vm);
        }

        [HttpGet]
        public IActionResult Create()
        {
            if (!this.TryGetUserId(out _, out var early)) return early!;
            ViewData["Title"] = "New Job Template";
            return View(new JobTemplateFormVM
            {
                JobType = "Full Time",
                WorkMode = "On-site",
                JobCategory = "Marketing"
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(JobTemplateFormVM vm)
        {
            if (!this.TryGetUserId(out var recruiterId, out var early)) return early!;
            if (!ModelState.IsValid) { ViewData["Title"] = "New Job Template"; return View(vm); }

            var dto = new JobTplDto
            {
                title = vm.Title?.Trim(),
                description = vm.Description,
                must = vm.MustHaves,
                nice = vm.NiceToHaves,

                job_type = vm.JobType,
                work_mode = vm.WorkMode,
                job_category = vm.JobCategory,
                salary_min = vm.SalaryMin,
                salary_max = vm.SalaryMax,
                expiry_date = vm.ExpiryDate,

                status = null // hidden in UI
            };

            var row = new template
            {
                user_id = recruiterId,
                template_name = JOB_PREFIX + vm.Name.Trim(),
                template_subject = null,
                template_body = JsonSerializer.Serialize(dto),
                template_status = "Active",
                date_created = DateTime.Now,
                date_updated = DateTime.Now
            };
            _db.templates.Add(row);
            await _db.SaveChangesAsync();
            TempData["Message"] = "Job template created.";
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            if (!this.TryGetUserId(out var recruiterId, out var early)) return early!;
            var row = await _db.templates.FirstOrDefaultAsync(t => t.template_id == id && t.user_id == recruiterId && t.template_name.StartsWith(JOB_PREFIX));
            if (row == null) return NotFound();

            var dto = JsonSerializer.Deserialize<JobTplDto>(row.template_body ?? "{}") ?? new JobTplDto();

            var vm = new JobTemplateFormVM
            {
                TemplateId = row.template_id,
                Name = row.template_name.Substring(JOB_PREFIX.Length),

                Title = dto.title ?? "",
                Description = dto.description,
                MustHaves = dto.must,
                NiceToHaves = dto.nice,

                JobType = dto.job_type ?? "Full Time",
                WorkMode = dto.work_mode ?? "On-site",
                JobCategory = dto.job_category ?? "Marketing",
                SalaryMin = dto.salary_min,
                SalaryMax = dto.salary_max,
                ExpiryDate = dto.expiry_date
            };

            ViewData["Title"] = "Edit Job Template";
            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(JobTemplateFormVM vm)
        {
            if (!this.TryGetUserId(out var recruiterId, out var early)) return early!;
            if (!vm.TemplateId.HasValue) return NotFound();

            var row = await _db.templates.FirstOrDefaultAsync(t => t.template_id == vm.TemplateId && t.user_id == recruiterId && t.template_name.StartsWith(JOB_PREFIX));
            if (row == null) return NotFound();

            if (!ModelState.IsValid) { ViewData["Title"] = "Edit Job Template"; return View(vm); }

            var dto = new JobTplDto
            {
                title = vm.Title?.Trim(),
                description = vm.Description,
                must = vm.MustHaves,
                nice = vm.NiceToHaves,

                job_type = vm.JobType,
                work_mode = vm.WorkMode,
                job_category = vm.JobCategory,
                salary_min = vm.SalaryMin,
                salary_max = vm.SalaryMax,
                expiry_date = vm.ExpiryDate,

                status = null
            };

            row.template_name = JOB_PREFIX + vm.Name.Trim();
            row.template_body = JsonSerializer.Serialize(dto);
            row.date_updated = DateTime.Now;

            await _db.SaveChangesAsync();
            TempData["Message"] = "Job template updated.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Archive(int id)
        {
            if (!this.TryGetUserId(out var recruiterId, out var early)) return early!;
            var row = await _db.templates.FirstOrDefaultAsync(t => t.template_id == id && t.user_id == recruiterId && t.template_name.StartsWith(JOB_PREFIX));
            if (row == null) return NotFound();
            row.template_status = "Archived";
            row.date_updated = DateTime.Now;
            await _db.SaveChangesAsync();
            TempData["Message"] = "Template archived.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Unarchive(int id)
        {
            if (!this.TryGetUserId(out var recruiterId, out var early)) return early!;
            var row = await _db.templates.FirstOrDefaultAsync(t => t.template_id == id && t.user_id == recruiterId && t.template_name.StartsWith(JOB_PREFIX));
            if (row == null) return NotFound();
            row.template_status = "Active";
            row.date_updated = DateTime.Now;
            await _db.SaveChangesAsync();
            TempData["Message"] = "Template restored.";
            return RedirectToAction(nameof(Archived));
        }

        [HttpGet]
        public async Task<IActionResult> Fill(int id)
        {
            if (!this.TryGetUserId(out var recruiterId, out var early)) return early!;
            var row = await _db.templates.AsNoTracking()
                .FirstOrDefaultAsync(t => t.template_id == id && t.user_id == recruiterId && t.template_name.StartsWith(JOB_PREFIX));
            if (row == null) return NotFound();

            var dto = JsonSerializer.Deserialize<JobTplDto>(row.template_body ?? "{}") ?? new JobTplDto();
            return RedirectToAction("Add", "Jobs", new
            {
                area = "Recruiter",
                title = dto.title,
                description = dto.description,
                must = dto.must,
                nice = dto.nice,
                status = dto.status // ignored by UI; kept for compat
            });
        }
    }
}
