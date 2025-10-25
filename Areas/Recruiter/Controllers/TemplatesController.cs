using Microsoft.AspNetCore.Mvc;

namespace JobPortal.Areas.Recruiter.Controllers
{
    [Area("Recruiter")]
    public class TemplatesController : Controller
    {
        public IActionResult Index()
        {
            ViewData["Title"] = "Message Templates";
            ViewBag.Items = RecruiterDummy.Templates;
            return View();
        }
    }
}
