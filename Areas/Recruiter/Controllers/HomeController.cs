using Microsoft.AspNetCore.Mvc;

namespace JobPortal.Areas.Recruiter.Controllers;

[Area("Recruiter")]
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