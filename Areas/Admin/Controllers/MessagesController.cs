using Microsoft.AspNetCore.Mvc;

namespace JobPortal.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class MessagesController : Controller
    {
        public IActionResult Index()
        {
            ViewData["Title"] = "Conversation Monitor";
            ViewBag.Threads = AdminDummy.Threads;
            return View();
        }

        public IActionResult Thread(int id)
        {
            ViewData["Title"] = $"Thread #{id}";
            ViewBag.Messages = AdminDummy.ThreadMessages(id);
            ViewBag.Id = id;
            return View();
        }
    }
}
