using Microsoft.AspNetCore.Mvc;

namespace JobPortal.Areas.Recruiter.Controllers
{
    [Area("Recruiter")]
    public class ScreeningController : Controller
    {
        public IActionResult Queue()
        {
            ViewData["Title"] = "Screening Queue";
            ViewBag.Items = RecruiterDummy.CandidatesForJob(201);
            return View();
        }

        public IActionResult Parsing(int candidateId)
        {
            ViewData["Title"] = $"Parsing Review • Candidate #{candidateId}";
            ViewBag.Profile = RecruiterDummy.Profile(candidateId);
            return View();
        }

        public IActionResult Criteria(int jobId)
        {
            ViewData["Title"] = $"Criteria Tuner • Job #{jobId}";
            return View();
        }

        public IActionResult Overrides()
        {
            ViewData["Title"] = "Override Journal";
            ViewBag.Items = RecruiterDummy.CandidatesForJob(201).Where(c => c.Override).ToList();
            return View();
        }
    }
}
