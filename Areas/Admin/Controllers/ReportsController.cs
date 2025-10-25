using Microsoft.AspNetCore.Mvc;

namespace JobPortal.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class ReportsController : Controller
    {
        public IActionResult Index()
        {
            ViewData["Title"] = "Reports";
            ViewBag.Cards = AdminDummy.ReportCards;
            return View();
        }
    }
}
