using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using JobPortal.Areas.Shared.Models;
using JobPortal.Areas.JobSeeker.Models;
using Microsoft.EntityFrameworkCore;


namespace JobPortal.Areas.JobSeeker.Controllers;

[Area("JobSeeker")]
public class HomeController : Controller
{
    private readonly AppDbContext _db;
    private readonly ILogger<HomeController> _logger;

    public HomeController(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IActionResult> Index()
    {
        var recentJobs = await _db.job_listings
            .Include(j => j.company) // Include related company
            .Where(j => j.job_status == "Open")
            .OrderByDescending(j => j.date_posted)
            .Take(4)
            .Select(j => new
            {
                JobTitle = j.job_title,
                CompanyName = j.company.company_name,   // from related company
                Industry = j.company.company_industry  // from related company
            })
            .ToListAsync();


        return View(recentJobs);
    }



    public IActionResult Privacy()
    {
        return View();
    }


    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }

    public IActionResult About()
    {
        // You can pass data to the view through ViewData/ViewBag/Model if you like
        ViewData["Message"] = "This is a simple About page.";
        return View();
    }


}
