using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using JobPortal.Areas.Shared.Models;
using JobPortal.Areas.JobSeeker.Models;

namespace JobPortal.Areas.JobSeeker.Controllers;

[Area("JobSeeker")]
public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;

    public HomeController(ILogger<HomeController> logger)
    {
        _logger = logger;
    }

    public IActionResult Index()
    {
        return View();
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
