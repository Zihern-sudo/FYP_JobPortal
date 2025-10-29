using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
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

    
}
