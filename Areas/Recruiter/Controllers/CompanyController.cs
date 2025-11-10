// File: Areas/Recruiter/Controllers/CompanyController.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using JobPortal.Areas.Shared.Models;
using JobPortal.Areas.Recruiter.Models;
using Microsoft.AspNetCore.Http;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System;
using System.Globalization;

// Image processing
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;

namespace JobPortal.Areas.Recruiter.Controllers
{
    [Area("Recruiter")]
    public class CompanyController : Controller
    {
        private readonly AppDbContext _db;
        public CompanyController(AppDbContext db) => _db = db;

        [HttpGet]
        public async Task<IActionResult> Setup()
        {
            if (!TryGetUserId(out var uid)) return Unauthorized();

            var c = await _db.companies.AsNoTracking().FirstOrDefaultAsync(x => x.user_id == uid);
            var vm = new CompanyProfileVm
            {
                company_name = c?.company_name ?? "",
                company_industry = c?.company_industry,
                company_location = c?.company_location,
                company_description = c?.company_description
            };

            ViewBag.CompanyStatus = c?.company_status;
            // Show nothing by default unless DB already has a path
            ViewBag.CompanyPhotoUrl = c?.company_photo;
            return View("Setup", vm);
        }

        [HttpGet]
        public async Task<IActionResult> Manage()
        {
            if (!TryGetUserId(out var uid)) return Unauthorized();

            var c = await _db.companies.AsNoTracking().FirstOrDefaultAsync(x => x.user_id == uid);
            var vm = new CompanyProfileVm
            {
                company_name = c?.company_name ?? "",
                company_industry = c?.company_industry,
                company_location = c?.company_location,
                company_description = c?.company_description
            };

            ViewBag.CompanyStatus = c?.company_status;

            // Prefer DB path; if empty, fall back to file on disk; then cache-bust for the view
            var path = !string.IsNullOrWhiteSpace(c?.company_photo) ? c!.company_photo : GetCompanyPhotoUrl(uid);
            ViewBag.CompanyPhotoUrl = AppendCacheBusting(path);

            return View(vm);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Manage(CompanyProfileVm vm, IFormFile? companyPhoto)
        {
            if (!TryGetUserId(out var uid)) return Unauthorized();

            if (!ValidatePhoto(companyPhoto, out var photoErr))
                ModelState.AddModelError(nameof(companyPhoto), photoErr!);

            if (!ModelState.IsValid)
            {
                ViewBag.CompanyStatus = await Status(uid);
                var c0 = await _db.companies.AsNoTracking().FirstOrDefaultAsync(x => x.user_id == uid);
                var path0 = !string.IsNullOrWhiteSpace(c0?.company_photo) ? c0!.company_photo : GetCompanyPhotoUrl(uid);
                ViewBag.CompanyPhotoUrl = AppendCacheBusting(path0);
                return View(vm); // stay on Manage
            }

            var c = await _db.companies.FirstOrDefaultAsync(x => x.user_id == uid);
            if (c == null)
            {
                c = new company { user_id = uid, company_status = "Incomplete" };
                _db.companies.Add(c);
            }

            c.company_name = vm.company_name?.Trim();
            c.company_industry = vm.company_industry?.Trim();
            c.company_location = vm.company_location?.Trim();
            c.company_description = vm.company_description?.Trim();

            if (companyPhoto != null && companyPhoto.Length > 0)
            {
                // new upload
                c.company_photo = await SaveCompanyPhotoAsync(uid, companyPhoto);
            }
            else if (string.IsNullOrWhiteSpace(c.company_photo))
            {
                // no upload this time but a file may already exist on disk → persist its path
                var existing = FindExistingCompanyPhotoPath(uid);
                if (!string.IsNullOrWhiteSpace(existing))
                    c.company_photo = existing!;
            }

            await _db.SaveChangesAsync();
            TempData["Message"] = "Changes saved.";
            return RedirectToAction(nameof(Manage));
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Submit(CompanyProfileVm vm, IFormFile? companyPhoto)
        {
            if (!TryGetUserId(out var uid)) return Unauthorized();

            if (!ValidatePhoto(companyPhoto, out var photoErr))
                ModelState.AddModelError(nameof(companyPhoto), photoErr!);

            if (!ModelState.IsValid)
            {
                ViewBag.CompanyStatus = await Status(uid);
                var c0 = await _db.companies.AsNoTracking().FirstOrDefaultAsync(x => x.user_id == uid);
                ViewBag.CompanyPhotoUrl = c0?.company_photo; // show only DB value on Setup
                return View("Setup", vm);
            }

            var c = await _db.companies.FirstOrDefaultAsync(x => x.user_id == uid);
            if (c == null)
            {
                c = new company { user_id = uid, company_status = "Incomplete" };
                _db.companies.Add(c);
            }

            c.company_name = vm.company_name?.Trim();
            c.company_industry = vm.company_industry?.Trim();
            c.company_location = vm.company_location?.Trim();
            c.company_description = vm.company_description?.Trim();
            c.company_status = "Pending";

            if (companyPhoto != null && companyPhoto.Length > 0)
            {
                c.company_photo = await SaveCompanyPhotoAsync(uid, companyPhoto);
            }
            else if (string.IsNullOrWhiteSpace(c.company_photo))
            {
                var existing = FindExistingCompanyPhotoPath(uid);
                if (!string.IsNullOrWhiteSpace(existing))
                    c.company_photo = existing!;
            }

            await _db.SaveChangesAsync();

            // Branch by source: Setup -> logout and go to JobSeeker/Account/StaffLogin
            var source = (Request.Form["source"].ToString() ?? "").ToLowerInvariant();
            if (source == "setup")
            {
                HttpContext.Session.Clear();
                TempData["Message"] = "Submitted for admin approval. Please log in as Recruiter to continue once approved.";
                return RedirectToAction("StaffLogin", "Account", new { area = "JobSeeker" });
            }

            TempData["Message"] = "Submitted for admin approval.";
            return RedirectToAction(nameof(Manage));
        }

        // --- helpers ---
        private async Task<string?> Status(int uid) =>
            await _db.companies.Where(x => x.user_id == uid).Select(x => x.company_status).FirstOrDefaultAsync();

        private bool TryGetUserId(out int userId)
        {
            userId = 0;
            var s = HttpContext.Session.GetString("UserId");
            return int.TryParse(s, out userId) && userId > 0;
        }

        private static bool ValidatePhoto(IFormFile? file, out string? error)
        {
            error = null;
            if (file == null || file.Length == 0) return true; // optional
            var max = 2 * 1024 * 1024; // 2MB
            if (file.Length > max) { error = "Image must be ≤ 2 MB."; return false; }

            var okExt = new[] { ".jpg", ".jpeg", ".png", ".webp" };
            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!okExt.Contains(ext)) { error = "Only JPG, JPEG, PNG, or WEBP are allowed."; return false; }

            var okMime = new[] { "image/jpeg", "image/png", "image/webp" };
            if (!okMime.Contains(file.ContentType)) { error = "Invalid image content type."; return false; }

            return true;
        }

        // Save with resize + compression (JPEG/PNG/WEBP)
        private async Task<string> SaveCompanyPhotoAsync(int uid, IFormFile file)
        {
            const int MAX_DIM = 800;
            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();

            var webRoot = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
            var dir = Path.Combine(webRoot, "uploads", "company");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            foreach (var f in Directory.GetFiles(dir, $"company_{uid}.*"))
                System.IO.File.Delete(f);

            var fileName = $"company_{uid}{ext}";
            var fullPath = Path.Combine(dir, fileName);

            await using var inputStream = file.OpenReadStream();
            using var image = await Image.LoadAsync(inputStream);

            if (image.Width > MAX_DIM || image.Height > MAX_DIM)
            {
                image.Mutate(x => x.Resize(new ResizeOptions
                {
                    Mode = ResizeMode.Max,
                    Size = new Size(MAX_DIM, MAX_DIM),
                    Sampler = KnownResamplers.Lanczos3
                }));
            }

            await using var outStream = System.IO.File.Create(fullPath);
            if (ext == ".jpg" || ext == ".jpeg")
            {
                await image.SaveAsync(outStream, new JpegEncoder { Quality = 80 });
            }
            else if (ext == ".png")
            {
                await image.SaveAsync(outStream, new PngEncoder
                {
                    CompressionLevel = PngCompressionLevel.Level6,
                    ColorType = PngColorType.Rgb
                });
            }
            else if (ext == ".webp")
            {
                await image.SaveAsync(outStream, new WebpEncoder { Quality = 80, FileFormat = WebpFileFormatType.Lossy });
            }
            else
            {
                await image.SaveAsync(outStream, new JpegEncoder { Quality = 80 });
                fileName = $"company_{uid}.jpg";
            }

            // clean web path (no query)
            return "/uploads/company/" + fileName;
        }

        // Fallback-to-disk lookup with cache-busting (for views only)
        private string? GetCompanyPhotoUrl(int uid)
        {
            var dir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "company");
            if (!Directory.Exists(dir)) return null;
            var f = Directory.GetFiles(dir, $"company_{uid}.*").FirstOrDefault();
            if (f == null) return null;

            var ticks = System.IO.File.GetLastWriteTimeUtc(f).Ticks.ToString(CultureInfo.InvariantCulture);
            var baseUrl = "/uploads/company/" + Path.GetFileName(f);
            return $"{baseUrl}?v={ticks}";
        }

        // Append cache-busting to a provided web path (if physical file exists)
        private string? AppendCacheBusting(string? webPath)
        {
            if (string.IsNullOrWhiteSpace(webPath)) return webPath;

            // strip any existing query before checking file
            var clean = CleanWebPath(webPath);
            var physical = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", clean.TrimStart('/'));
            if (!System.IO.File.Exists(physical)) return webPath;

            var ticks = System.IO.File.GetLastWriteTimeUtc(physical).Ticks;
            return $"{clean}?v={ticks}";
        }

        // NEW: find existing company_* file on disk and return clean web path (no ?v=)
        private string? FindExistingCompanyPhotoPath(int uid)
        {
            var dir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "company");
            if (!Directory.Exists(dir)) return null;
            var f = Directory.GetFiles(dir, $"company_{uid}.*").FirstOrDefault();
            return f == null ? null : "/uploads/company/" + Path.GetFileName(f);
        }

        // NEW: strip query string from a web path
        private static string CleanWebPath(string? webPath)
        {
            if (string.IsNullOrWhiteSpace(webPath)) return "";
            var q = webPath.IndexOf('?', StringComparison.Ordinal);
            return q >= 0 ? webPath.Substring(0, q) : webPath;
        }
    }
}
