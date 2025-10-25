using Microsoft.AspNetCore.Mvc;

namespace JobPortal.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class AuditController : Controller
    {
        public IActionResult Index()
        {
            ViewData["Title"] = "Audit Log";
            ViewBag.Items = AdminDummy.Audit;
            return View();
        }
    }
}
