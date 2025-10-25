using Microsoft.AspNetCore.Mvc;

namespace JobPortal.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class SettingsController : Controller
    {
        public IActionResult Branding()
        {
            ViewData["Title"] = "Branding";
            return View();
        }

        public IActionResult Notifications()
        {
            ViewData["Title"] = "Notifications";
            return View();
        }

        public IActionResult Legal()
        {
            ViewData["Title"] = "Legal & Consent";
            return View();
        }
    }
}
