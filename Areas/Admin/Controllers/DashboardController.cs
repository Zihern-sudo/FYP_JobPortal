using Microsoft.AspNetCore.Mvc;

namespace JobPortal.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class DashboardController : Controller
    {
        public IActionResult Index()
        {
            ViewData["Title"] = "Admin Dashboard";
            ViewBag.Approvals = AdminDummy.Approvals;
            ViewBag.Attention = AdminDummy.NeedsAttention;
            return View();
        }
    }
}
