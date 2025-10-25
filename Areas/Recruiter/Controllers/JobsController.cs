using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using JobPortal.Areas.Shared.Models;   // <-- ensure this matches your DbContext namespace

namespace JobPortal.Areas.Recruiter.Controllers
{
    [Area("Recruiter")]
    public class JobsController : Controller
    {
        private readonly AppDbContext _db;
        public JobsController(AppDbContext db) => _db = db;

        // LIST
        public async Task<IActionResult> Index()
        {
            ViewData["Title"] = "Jobs";
            var recruiterId = 2; // TODO: replace with logged-in user later

            var jobs = await _db.job_listings
                .Where(j => j.user_id == recruiterId)
                .OrderByDescending(j => j.date_posted)
                .ToListAsync();

            return View(jobs);
        }

        // ADD (GET)
        [HttpGet]
        public IActionResult Add()
        {
            // default values so ModelState is valid even if you submit quickly
            var model = new job_listing
            {
                job_status = "Open"
            };
            return View(model);
        }

        // ADD (POST)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Add(
    [Bind("job_title,job_description,job_requirements,salary_min,salary_max,job_status")]
    job_listing model)
        {
            // supply required FKs
            model.user_id = 2;       // demo recruiter
            model.company_id = 1;    // ensure this exists

            if (!ModelState.IsValid)
            {
                var firstError = ModelState.Values.SelectMany(v => v.Errors).FirstOrDefault()?.ErrorMessage;
                ViewBag.DebugError = firstError ?? "Validation failed.";
                return View(model);
            }

            model.job_status ??= "Open";
            model.date_posted = DateTime.Now;

            _db.job_listings.Add(model);
            await _db.SaveChangesAsync();

            TempData["Message"] = "New job added successfully!";
            return RedirectToAction(nameof(Index));
        }




        // GET: /Recruiter/Jobs/Edit/5
        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            ViewData["Title"] = "Edit Job";
            var recruiterId = 2; // TODO: replace with logged-in user

            var job = await _db.job_listings
                .Where(j => j.user_id == recruiterId && j.job_listing_id == id)
                .FirstOrDefaultAsync();

            if (job == null) return NotFound();

            return View(job); // strongly-typed view
        }

        // POST: /Recruiter/Jobs/Edit
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(
            [Bind("job_listing_id,job_status")] job_listing form,
            string? setStatus) // comes from submit buttons (optional)
        {
            var recruiterId = 2;

            // Load the existing row (and verify ownership)
            var job = await _db.job_listings
                .Where(j => j.user_id == recruiterId && j.job_listing_id == form.job_listing_id)
                .FirstOrDefaultAsync();

            if (job == null) return NotFound();

            // If user clicked "Save Draft" or "Publish", override dropdown value
            if (!string.IsNullOrWhiteSpace(setStatus))
                form.job_status = setStatus;

            // Validate the incoming status (keep it simple)
            var allowed = new[] { "Draft", "Open", "Paused", "Closed" };
            if (!allowed.Contains(form.job_status))
            {
                ModelState.AddModelError(nameof(job.job_status), "Invalid status.");
                return View(job); // show original job with error
            }

            // Apply change
            job.job_status = form.job_status;

            await _db.SaveChangesAsync();

            TempData["Message"] = $"Job #{job.job_listing_id} status updated to {job.job_status}.";
            return RedirectToAction(nameof(Index));
        }

    }
}
