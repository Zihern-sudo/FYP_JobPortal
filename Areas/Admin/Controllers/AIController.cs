using Microsoft.AspNetCore.Mvc;

namespace JobPortal.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class AIController : Controller
    {
        public IActionResult Templates()
        {
            ViewData["Title"] = "AI Criteria Templates";
            ViewBag.Templates = AdminDummy.Templates;
            return View();
        }

        public IActionResult Scoring()
        {
            ViewData["Title"] = "AI Scoring Weights";
            return View();
        }

        public IActionResult ParsingRules()
        {
            ViewData["Title"] = "Parsing & Confidence Rules";
            return View();
        }

        public IActionResult Fairness()
        {
            ViewData["Title"] = "Bias & Fairness Guardrails";
            return View();
        }
    }
}
