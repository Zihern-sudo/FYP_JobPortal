using Microsoft.AspNetCore.Mvc;
using JobPortal.Areas.Shared.Models; // Added
using JobPortal.Areas.Admin.Models; // Added
using System.Linq; // Added
using Microsoft.EntityFrameworkCore; // Added

namespace JobPortal.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class SettingsController : Controller
    {
        // --- Added DbContext ---
        private readonly AppDbContext _db;
        public SettingsController(AppDbContext db) => _db = db;
        // -------------------------

        public IActionResult Branding()
        {
            ViewData["Title"] = "Branding";
            return View(); // This view is a mock-up, no data to load
        }

        public IActionResult Notifications()
        {
            ViewData["Title"] = "Notifications";

            // --- Modified to use DbContext ---
            // Get the first admin user's notification settings as a demo
            var adminUser = _db.users
                .AsNoTracking()
                .FirstOrDefault(u => u.user_role == "Admin");

            var vm = new AdminNotificationSettingsViewModel();
            if (adminUser != null)
            {
                // Map DB properties to VM properties
                // These mappings are interpretive guesses
                vm.NotifyOnNewApplication = adminUser.notif_job_updates;
                vm.NotifyOnLowConfidenceParse = adminUser.notif_feedback;
                vm.NotifyOnFlaggedMessage = adminUser.notif_messages;
            }

            return View(vm); // Pass the model to the view
            // --- End Modification ---
        }

        // --- Added POST handler for saving (even if disabled) ---
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Notifications(AdminNotificationSettingsViewModel vm)
        {
            if (!ModelState.IsValid)
            {
                return View(vm);
            }

            // --- Logic to SAVE the settings would go here ---
            // 1. Get the current admin user (e.g., from HttpContext)
            // 2. Update their properties (e.g., user.notif_job_updates = vm.NotifyOnNewApplication)
            // 3. _db.SaveChanges();
            // 4. Add a success message (e.g., via TempData)
            // -------------------------------------------------

            TempData["SuccessMessage"] = "Notification settings saved (demo)!";
            return RedirectToAction(nameof(Notifications));
        }
        // -------------------------------------------------


        public IActionResult Legal()
        {
            ViewData["Title"] = "Legal & Consent";
            return View(); // This view is a mock-up, no data to load
        }
    }
}
