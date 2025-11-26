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
using System.Text.Json;
using JobPortal.Areas.Shared.Extensions;                 // Session helpers (TryGetUserId), MyTime
using JobPortal.Areas.Shared.Models.Extensions;         // TryNotifyAsync(), AreaUrl()
using Microsoft.Extensions.Logging;                     // ILogger<T>
using JobPortal.Services;                               // INotificationService

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
        private readonly INotificationService _notif;
        private readonly ILogger<CompanyController> _logger;

        public CompanyController(AppDbContext db,
                                 INotificationService notif,
                                 ILogger<CompanyController> logger)
        {
            _db = db;
            _notif = notif;
            _logger = logger;
        }

        // ===== Setup (registration) =====
        [HttpGet]
        public async Task<IActionResult> Setup()
        {
            if (!this.TryGetUserId("Recruiter", out var uid, out var early))
                return early ?? Unauthorized(); // why: consistent login redirect

            var c = await _db.companies.AsNoTracking().FirstOrDefaultAsync(x => x.user_id == uid);
            var vm = new CompanyProfileVm
            {
                company_name = c?.company_name ?? "",
                company_industry = c?.company_industry,
                company_location = c?.company_location,
                company_description = c?.company_description
            };

            ViewBag.CompanyStatus = c?.company_status;
            ViewBag.CompanyPhotoUrl = c?.company_photo; // live only on setup
            return View("Setup", vm);
        }

        // ===== Manage (edit profile) =====
        [HttpGet]
        public async Task<IActionResult> Manage()
        {
            if (!this.TryGetUserId("Recruiter", out var uid, out var early))
                return early ?? Unauthorized();

            var c = await _db.companies.AsNoTracking().FirstOrDefaultAsync(x => x.user_id == uid);
            var live = c ?? new company { user_id = uid, company_status = "Inactive" };

            var draft = LoadDraft(uid);
            var vm = new CompanyProfileManageVm
            {
                company_name = draft?.company_name ?? live.company_name ?? "",
                company_industry = draft?.company_industry ?? live.company_industry,
                company_location = draft?.company_location ?? live.company_location,
                company_description = draft?.company_description ?? live.company_description,
                Status = live.company_status ?? "Inactive",
                LivePhotoUrl = AppendCacheBusting(PreferLivePhoto(uid, live.company_photo)),
                DraftPhotoUrl = AppendCacheBusting(GetDraftPhotoWebPath(uid)),
                HasDraft = draft != null || GetDraftPhotoWebPath(uid) != null
            };

            ViewBag.CompanyStatus = vm.Status;
            ViewBag.CompanyPhotoUrl = vm.DraftPhotoUrl ?? vm.LivePhotoUrl; // prefer draft preview

            return View(vm);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Manage(CompanyProfileManageVm vm, IFormFile? companyPhoto)
        {
            if (!this.TryGetUserId("Recruiter", out var uid, out var early))
                return early ?? Unauthorized();

            if (!ValidatePhoto(companyPhoto, out var photoErr))
                ModelState.AddModelError(nameof(companyPhoto), photoErr!);

            // STRICT: require all fields for Save Draft too
            var strict = new CompanyProfileSubmitVm
            {
                company_name = vm.company_name,
                company_industry = vm.company_industry,
                company_location = vm.company_location,
                company_description = vm.company_description
            };
            if (!TryValidateModel(strict))   // adds errors to ModelState under the same field keys
            {
                var c0 = await _db.companies.AsNoTracking().FirstOrDefaultAsync(x => x.user_id == uid);
                ViewBag.CompanyStatus = c0?.company_status;

                var draftPhoto = GetDraftPhotoWebPath(uid);
                var livePhoto = PreferLivePhoto(uid, c0?.company_photo);
                ViewBag.CompanyPhotoUrl = AppendCacheBusting(draftPhoto ?? livePhoto);
                return View(vm);
            }

            // 1) Persist recruiter edits to draft (JSON + optional photo)
            SaveDraft(uid, vm);
            if (companyPhoto != null && companyPhoto.Length > 0)
                await SaveCompanyPhotoDraftAsync(uid, companyPhoto);

            // 2) Ensure a row exists; keep status at Draft (don't overwrite live if already Pending)
            var c = await _db.companies.FirstOrDefaultAsync(x => x.user_id == uid);
            if (c == null)
            {
                c = new company
                {
                    user_id = uid,
                    company_status = "Draft",
                    company_name = strict.company_name!.Trim(),
                    company_industry = strict.company_industry!.Trim(),
                    company_location = strict.company_location!.Trim(),
                    company_description = strict.company_description!.Trim()
                };
                _db.companies.Add(c);
            }
            else if (!string.Equals(c.company_status, "Pending", StringComparison.OrdinalIgnoreCase))
            {
                c.company_status = "Draft";
                c.company_name = strict.company_name!.Trim();
                c.company_industry = strict.company_industry!.Trim();
                c.company_location = strict.company_location!.Trim();
                c.company_description = strict.company_description!.Trim();
            }

            await _db.SaveChangesAsync();

            TempData["Message"] = "Draft saved. Submit for approval when ready.";
            return RedirectToAction(nameof(Manage));
        }



        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Submit(CompanyProfileManageVm vm, IFormFile? companyPhoto)
        {
            if (!this.TryGetUserId("Recruiter", out var uid, out var early))
                return early ?? Unauthorized();

            // Validate photo (adds to ModelState if bad)
            if (!ValidatePhoto(companyPhoto, out var photoErr))
                ModelState.AddModelError(nameof(companyPhoto), photoErr!);

            var c = await _db.companies.FirstOrDefaultAsync(x => x.user_id == uid);

            if (c != null && string.Equals(c.company_status, "Pending", StringComparison.OrdinalIgnoreCase))
            {
                TempData["Message"] = "Already submitted for approval. Please wait for admin review.";
                return RedirectToAction(nameof(Manage));
            }

            // --- STRICT: validate using CompanyProfileSubmitVm (all fields required) ---
            var strict = new CompanyProfileSubmitVm
            {
                company_name = vm.company_name,
                company_industry = vm.company_industry,
                company_location = vm.company_location,
                company_description = vm.company_description
            };
            // TryValidateModel adds any errors to ModelState for the same field names
            if (!TryValidateModel(strict))
            {
                ViewBag.CompanyStatus = c?.company_status;
                var draftPhoto = GetDraftPhotoWebPath(uid);
                var livePhoto = PreferLivePhoto(uid, c?.company_photo);
                ViewBag.CompanyPhotoUrl = AppendCacheBusting(draftPhoto ?? livePhoto);
                return View("Manage", vm);
            }
            // -------------------------------------------------------------------------

            // 1) Save latest draft (text + optional photo)
            SaveDraft(uid, vm);
            if (companyPhoto != null && companyPhoto.Length > 0)
                await SaveCompanyPhotoDraftAsync(uid, companyPhoto);

            // 2) Ensure entity exists and COPY TRIMMED FIELDS before first save
            if (c == null)
            {
                c = new company { user_id = uid };
                _db.companies.Add(c);
            }
            c.company_name = strict.company_name!.Trim();
            c.company_industry = strict.company_industry!.Trim();
            c.company_location = strict.company_location!.Trim();
            c.company_description = strict.company_description!.Trim();

            // 3) Mark as Pending and save
            c.company_status = "Pending";
            await _db.SaveChangesAsync();

            // Notify Admins (non-blocking)
            await this.TryNotifyAsync(_notif, _logger, () =>
                _notif.SendToAdminsAsync(
                    "Company profile submitted",
                    $"“{(strict.company_name ?? "Company")}” was submitted and awaits review.",
                    type: "Review"
                )
            );

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

        // ======== Live photo handling (unchanged for drafts) ========
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

            if (image.Width > 800 || image.Height > 800)
            {
                image.Mutate(x => x.Resize(new ResizeOptions
                {
                    Mode = ResizeMode.Max,
                    Size = new Size(800, 800),
                    Sampler = KnownResamplers.Lanczos3
                }));
            }

            await using var outStream = System.IO.File.Create(fullPath);
            if (ext == ".jpg" || ext == ".jpeg")
                await image.SaveAsync(outStream, new JpegEncoder { Quality = 80 });
            else if (ext == ".png")
                await image.SaveAsync(outStream, new PngEncoder { CompressionLevel = PngCompressionLevel.Level6, ColorType = PngColorType.Rgb });
            else if (ext == ".webp")
                await image.SaveAsync(outStream, new WebpEncoder { Quality = 80, FileFormat = WebpFileFormatType.Lossy });
            else
            {
                await image.SaveAsync(outStream, new JpegEncoder { Quality = 80 });
                fileName = $"company_{uid}.jpg";
            }

            return "/uploads/company/" + fileName;
        }

        private string? PreferLivePhoto(int uid, string? dbWebPath)
        {
            var path = !string.IsNullOrWhiteSpace(dbWebPath) ? dbWebPath : GetCompanyPhotoUrl(uid);
            return path;
        }

        private string? GetCompanyPhotoUrl(int uid)
        {
            var dir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "company");
            if (!Directory.Exists(dir)) return null;

            var f = Directory.GetFiles(dir, $"company_{uid}.*").FirstOrDefault();
            if (f == null) return null;

            var lastWriteUtc = System.IO.File.GetLastWriteTimeUtc(f);
            var lastWriteMy = MyTime.ToMalaysiaTime(lastWriteUtc);
            var ticks = lastWriteMy.Ticks.ToString(CultureInfo.InvariantCulture);

            var baseUrl = "/uploads/company/" + Path.GetFileName(f);
            return $"{baseUrl}?v={ticks}";
        }

        private string? AppendCacheBusting(string? webPath)
        {
            if (string.IsNullOrWhiteSpace(webPath)) return webPath;

            var clean = CleanWebPath(webPath);
            var physical = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", clean.TrimStart('/'));
            if (!System.IO.File.Exists(physical)) return webPath;

            var lastWriteUtc = System.IO.File.GetLastWriteTimeUtc(physical);
            var lastWriteMy = MyTime.ToMalaysiaTime(lastWriteUtc);
            var ticks = lastWriteMy.Ticks.ToString(CultureInfo.InvariantCulture);

            return $"{clean}?v={ticks}";
        }

        private static string CleanWebPath(string? webPath)
        {
            if (string.IsNullOrWhiteSpace(webPath)) return "";
            var q = webPath.IndexOf('?', StringComparison.Ordinal);
            return q >= 0 ? webPath.Substring(0, q) : webPath;
        }

        // ======== Draft persistence (photo + JSON) ========
        private string GetDraftDir(int uid)
        {
            var dir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "company", "_drafts", uid.ToString(CultureInfo.InvariantCulture));
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            return dir;
        }

        private string GetDraftJsonPath(int uid) => Path.Combine(GetDraftDir(uid), $"company_{uid}.json");

        private string? GetDraftPhotoPath(int uid)
        {
            var dir = GetDraftDir(uid);
            var f = Directory.GetFiles(dir, $"company_{uid}.*")
                             .FirstOrDefault(x => !x.EndsWith(".json", StringComparison.OrdinalIgnoreCase));
            return f;
        }

        private string? GetDraftPhotoWebPath(int uid)
        {
            var p = GetDraftPhotoPath(uid);
            return p == null ? null : "/uploads/company/_drafts/" + uid + "/" + Path.GetFileName(p);
        }

        private CompanyDraftPayload? LoadDraft(int uid)
        {
            var jsonPath = GetDraftJsonPath(uid);
            if (!System.IO.File.Exists(jsonPath)) return null;

            var json = System.IO.File.ReadAllText(jsonPath);
            try
            {
                return JsonSerializer.Deserialize<CompanyDraftPayload>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            catch
            {
                return null; // why: corrupted draft shouldn't block page
            }
        }

        private void SaveDraft(int uid, CompanyProfileVm vm)
        {
            var payload = new CompanyDraftPayload
            {
                company_name = vm.company_name?.Trim() ?? "",
                company_industry = vm.company_industry?.Trim(),
                company_location = vm.company_location?.Trim(),
                company_description = vm.company_description?.Trim(),
                saved_at = DateTime.UtcNow
            };
            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = false });
            System.IO.File.WriteAllText(GetDraftJsonPath(uid), json);
        }

        private async Task SaveCompanyPhotoDraftAsync(int uid, IFormFile file)
        {
            const int MAX_DIM = 800;
            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(ext)) ext = ".jpg";

            var dir = GetDraftDir(uid);

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
                await image.SaveAsync(outStream, new JpegEncoder { Quality = 80 });
            else if (ext == ".png")
                await image.SaveAsync(outStream, new PngEncoder { CompressionLevel = PngCompressionLevel.Level6, ColorType = PngColorType.Rgb });
            else if (ext == ".webp")
                await image.SaveAsync(outStream, new WebpEncoder { Quality = 80, FileFormat = WebpFileFormatType.Lossy });
            else
            {
                await image.SaveAsync(outStream, new JpegEncoder { Quality = 80 });
            }
        }

        // ======== Legacy helpers (kept for compatibility) ========
        private string? FindExistingCompanyPhotoPath(int uid)
        {
            var dir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "company");
            if (!Directory.Exists(dir)) return null;
            var f = Directory.GetFiles(dir, $"company_{uid}.*").FirstOrDefault();
            return f == null ? null : "/uploads/company/" + Path.GetFileName(f);
        }
    }
}
