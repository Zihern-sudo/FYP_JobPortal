using Microsoft.AspNetCore.Mvc;

namespace JobPortal.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class ApprovalsController : Controller
    {
        public IActionResult Index()
        {
            ViewData["Title"] = "Job Approvals";
            ViewBag.Items = AdminDummy.Approvals;
            return View();
        }

        public IActionResult Preview(int id)
        {
            ViewData["Title"] = "Job Preview";
            var item = AdminDummy.Approvals.FirstOrDefault(x => x.Id == id) ?? AdminDummy.Approvals.First();
            return View(item);
        }
    }
}
