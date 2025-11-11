using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using JobPortal.Areas.Public.Models;
using JobPortal.Areas.Shared.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace JobPortal.Areas.Public.Controllers
{
    [Area("Public")]
    public class JobsController : Controller
    {
        private readonly AppDbContext _db;

        public JobsController(AppDbContext db)
        {
            _db = db;
        }

        // GET: /Public/Jobs/List?type=Full%20Time&q=developer&location=Kuala%20Lumpur
        [HttpGet]
        public IActionResult List(string? type, string? q, string? location)
        {
            // Supported employment categories for the tabs
            var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Full Time", "Part Time", "Contract", "Freelance", "Internship"
            };

            // Normalise incoming values
            type = string.IsNullOrWhiteSpace(type) || !allowed.Contains(type) ? null : type?.Trim();
            q = string.IsNullOrWhiteSpace(q) ? null : q!.Trim();
            location = string.IsNullOrWhiteSpace(location) ? null : location!.Trim();

            // Base query: only OPEN jobs
            var query = _db.job_listings
                .AsNoTracking()
                .Include(j => j.company)
                .Where(j => j.job_status == "Open");

            // Category (employment type) filter
            if (type is not null)
                query = query.Where(j => j.job_type == type);

            // Keyword search (against job_title)
            if (q is not null)
                query = query.Where(j => EF.Functions.Like(j.job_title, $"%{q}%"));

            // Location filter (against company.company_location)
            if (location is not null)
                query = query.Where(j => j.company != null &&
                                         EF.Functions.Like(j.company.company_location, $"%{location}%"));

            var model = query
                .OrderByDescending(j => j.date_posted)
                .Select(j => new JobListItemVm
                {
                    Id = j.job_listing_id,
                    Title = j.job_title,
                    Location = j.company != null ? j.company.company_location : null,
                    EmploymentType = j.job_type,
                    SalaryRange = (j.salary_min.HasValue || j.salary_max.HasValue)
                        ? $"RM{(j.salary_min ?? 0):0,#}–RM{(j.salary_max ?? 0):0,#}"
                        : null,
                    Deadline = j.expiry_date ?? j.date_posted.AddDays(30)
                })
                .ToList();

            // View state for tabs + sticky form values
            ViewData["ActiveType"] = type ?? "All";
            ViewData["Q"] = q ?? "";
            ViewData["Location"] = location ?? "";

            return View(model);
        }

        // (Optional placeholder for future detail page)
        [HttpGet]
        public IActionResult Detail(int id) => NotFound();
    }
}
