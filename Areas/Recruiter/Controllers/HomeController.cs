using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using JobPortal.Areas.Shared.Models;
using JobPortal.Areas.Shared.Extensions;
using JobPortal.Areas.Recruiter.Models;
using System.Linq;
using System.Threading.Tasks;

namespace JobPortal.Areas.Recruiter.Controllers;

[Area("Recruiter")]
public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;
    private readonly AppDbContext _db;

    public HomeController(ILogger<HomeController> logger, AppDbContext db)
    {
        _logger = logger;
        _db = db;
    }

    // Dashboard
    public async Task<IActionResult> Index()
    {
        if (!this.TryGetUserId(out var recruiterId, out var early)) return early!;

        // Jobs for this recruiter
        var jobsQ = _db.job_listings
                       .AsNoTracking()
                       .Where(j => j.user_id == recruiterId);

        var jobsCount = await jobsQ.CountAsync();
        var openJobs = await jobsQ.Where(j => j.job_status == "Open").CountAsync();

        // Applications for recruiter's jobs
        var appQ = _db.job_applications
                      .AsNoTracking()
                      .Where(a => _db.job_listings
                        .Where(j => j.user_id == recruiterId)
                        .Select(j => j.job_listing_id)
                        .Contains(a.job_listing_id));

        var appsCount = await appQ.CountAsync();

        // Unread conversations
        var unreadThreads = await _db.conversations
            .AsNoTracking()
            .Where(c => c.recruiter_id == recruiterId && c.unread_for_recruiter > 0)
            .CountAsync();

        // Latest 5 jobs
        var latestJobs = await jobsQ
            .OrderByDescending(j => j.date_posted)
            .Take(5)
            .Select(j => new DashJobItem(
                j.job_listing_id,
                j.job_title,
                j.job_status,
                j.date_posted.ToString("yyyy-MM-dd HH:mm")
            )).ToListAsync();

        // Latest 5 applications (with candidate + job)
        var latestApps = await appQ
            .OrderByDescending(a => a.date_updated)
            .Take(5)
            .Select(a => new DashAppItem(
                a.application_id,
                (a.user.first_name + " " + a.user.last_name).Trim(),
                a.job_listing.job_title,
                a.application_status,
                a.date_updated.ToString("yyyy-MM-dd HH:mm")
            )).ToListAsync();

        var vm = new DashboardVm
        {
            JobsCount = jobsCount,
            OpenJobs = openJobs,
            ApplicationsCount = appsCount,
            UnreadThreads = unreadThreads,
            LatestJobs = latestJobs,
            LatestApplications = latestApps
        };

        return View(vm);
    }
}