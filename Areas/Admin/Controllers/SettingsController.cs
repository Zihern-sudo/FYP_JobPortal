// ================================
// File: Areas/Admin/Controllers/SettingsController.cs
// ================================
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using JobPortal.Areas.Shared.Extensions; // MyTime


using JobPortal.Areas.Admin.Models;

namespace JobPortal.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class SettingsController : Controller
    {
        private readonly IWebHostEnvironment _env;

        public SettingsController(IWebHostEnvironment env)
        {
            _env = env;
        }

        // ---------- Small file helpers ----------
        private string AppDataPath
        {
            get
            {
                var p = Path.Combine(_env.WebRootPath ?? _env.ContentRootPath, "appsettings");
                if (!Directory.Exists(p)) Directory.CreateDirectory(p);
                return p;
            }
        }

        private static readonly JsonSerializerOptions _jsonOpts = new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private async Task<T> ReadJsonAsync<T>(string file, T @default)
        {
            if (!System.IO.File.Exists(file)) return @default;
            await using var fs = System.IO.File.OpenRead(file);
            try
            {
                var v = await JsonSerializer.DeserializeAsync<T>(fs, _jsonOpts);
                return v == null ? @default : v;
            }
            catch
            {
                return @default;
            }
        }

        private async Task WriteJsonAsync<T>(string file, T value)
        {
            await using var fs = System.IO.File.Create(file);
            await JsonSerializer.SerializeAsync(fs, value, _jsonOpts);
        }

        // =========================================================
        // Branding
        // =========================================================
        [HttpGet]
        public async Task<IActionResult> Branding()
        {
            var model = await ReadJsonAsync(
                Path.Combine(AppDataPath, "branding.json"),
                new BrandingSettingsViewModel { PrimaryColor = "#2563eb" }
            );
            ViewData["Title"] = "Branding";
            return View(model);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Branding(BrandingSettingsViewModel model, IFormFile? logo)
        {
            if (!ModelState.IsValid)
            {
                ViewData["Title"] = "Branding";
                return View(model);
            }

            // optional logo upload
            if (logo != null && logo.Length > 0)
            {
                var uploadDir = Path.Combine(_env.WebRootPath ?? _env.ContentRootPath, "uploads", "branding");
                if (!Directory.Exists(uploadDir)) Directory.CreateDirectory(uploadDir);

                var ext = Path.GetExtension(logo.FileName);
                var fileName = $"logo_{MyTime.NowMalaysia():yyyyMMddHHmmssfff}{ext}";
                var full = Path.Combine(uploadDir, fileName);

                await using (var fs = System.IO.File.Create(full))
                    await logo.CopyToAsync(fs);

                // store as web path
                model.LogoUrl = $"/uploads/branding/{fileName}";
            }

            await WriteJsonAsync(Path.Combine(AppDataPath, "branding.json"), model);

            TempData["Flash.Type"] = "success";
            TempData["Flash.Message"] = "Branding saved.";
            return RedirectToAction(nameof(Branding));
        }

        // =========================================================
        // Legal
        // =========================================================
        [HttpGet]
        public async Task<IActionResult> Legal()
        {
            var model = await ReadJsonAsync(
                Path.Combine(AppDataPath, "legal.json"),
                new LegalSettingsViewModel
                {
                    Terms = "Enter Terms & Conditions here…",
                    Privacy = "Enter Privacy Policy here…"
                }
            );
            ViewData["Title"] = "Legal";
            return View(model);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Legal(LegalSettingsViewModel model)
        {
            if (!ModelState.IsValid)
            {
                ViewData["Title"] = "Legal";
                return View(model);
            }

            await WriteJsonAsync(Path.Combine(AppDataPath, "legal.json"), model);

            TempData["Flash.Type"] = "success";
            TempData["Flash.Message"] = "Legal texts saved.";
            return RedirectToAction(nameof(Legal));
        }

        // =========================================================
        // Notifications (global admin-side switches)
        // =========================================================
        [HttpGet]
        public async Task<IActionResult> Notifications()
        {
            var model = await ReadJsonAsync(
                Path.Combine(AppDataPath, "notifications.json"),
                new AdminNotificationSettingsViewModel()
            );
            ViewData["Title"] = "Notifications";
            return View(model);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Notifications(AdminNotificationSettingsViewModel model)
        {
            if (!ModelState.IsValid)
            {
                ViewData["Title"] = "Notifications";
                return View(model);
            }

            await WriteJsonAsync(Path.Combine(AppDataPath, "notifications.json"), model);

            TempData["Flash.Type"] = "success";
            TempData["Flash.Message"] = "Notification preferences saved.";
            return RedirectToAction(nameof(Notifications));
        }
    }
}
