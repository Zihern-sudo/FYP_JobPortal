using Microsoft.AspNetCore.Mvc;

namespace JobPortal.Areas.Recruiter.Controllers
{
    [Area("Recruiter")]
    public class CandidatesController : Controller
    {
        public IActionResult Detail(int id)
        {
            ViewData["Title"] = $"Candidate #{id}";
            ViewBag.Profile = RecruiterDummy.Profile(id);
            return View();
        }
    }
}
