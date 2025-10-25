using Microsoft.AspNetCore.Mvc;

namespace JobPortal.Areas.Recruiter.Controllers
{
    [Area("Recruiter")]
    public class BulkController : Controller
    {
        public IActionResult Index()
        {
            ViewData["Title"] = "Bulk Actions";
            ViewBag.Items = RecruiterDummy.CandidatesForJob(201);
            return View();
        }
    }
}
