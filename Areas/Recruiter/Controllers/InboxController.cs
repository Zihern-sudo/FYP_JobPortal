using Microsoft.AspNetCore.Mvc;

namespace JobPortal.Areas.Recruiter.Controllers
{
    [Area("Recruiter")]
    public class InboxController : Controller
    {
        public IActionResult Index()
        {
            ViewData["Title"] = "Inbox";
            ViewBag.Threads = RecruiterDummy.Threads;
            return View();
        }

        public IActionResult Thread(int id)
        {
            ViewData["Title"] = $"Thread #{id}";
            ViewBag.Messages = RecruiterDummy.Thread(id);
            ViewBag.Id = id;
            return View();
        }
    }
}
