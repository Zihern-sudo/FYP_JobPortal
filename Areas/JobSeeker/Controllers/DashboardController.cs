using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Generic;
using JobPortal.Areas.Shared.Models;
using System; // for Uri.EscapeDataString
using JobPortal.Areas.JobSeeker.Models;
using QRCoder;
using JobPortal.Services;

namespace JobPortal.Areas.JobSeeker.Controllers
{
    [Area("JobSeeker")]
    public class DashboardController : Controller
    {
        private readonly AppDbContext _db;
        public DashboardController(AppDbContext db)
        {
            _db = db;
        }
        public IActionResult Index() => View();
        public IActionResult Feedback() => View();

        // ✅ Settings Page (GET)
        [HttpGet]
        public async Task<IActionResult> Settings()
        {
            var userIdStr = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrEmpty(userIdStr))
                return RedirectToAction("Login", "Account", new { area = "JobSeeker" });

            int userId = int.Parse(userIdStr);
            var user = await _db.users.FirstOrDefaultAsync(u => u.user_id == userId);

            if (user == null)
                return NotFound("User not found.");

            var vm = new ProfileViewModel
            {
                UserId = user.user_id,
                FirstName = user.first_name,
                LastName = user.last_name,
                Email = user.email,
                TwoFAEnabled = user.user_2FA, // assuming 1 = enabled
                Phone = "", // fill later when added to DB
                Address = "",
                Skills = "",
                Education = "",
                WorkExperience = ""
            };

            return View(vm);
        }

        // ✅ Save profile changes
        [HttpPost]
        public async Task<IActionResult> Settings(ProfileViewModel vm)
        {
            var user = await _db.users.FirstOrDefaultAsync(u => u.user_id == vm.UserId);
            if (user == null)
                return NotFound("User not found.");

            user.first_name = vm.FirstName;
            user.last_name = vm.LastName;
            user.email = vm.Email;
            user.user_2FA = vm.TwoFAEnabled;

            await _db.SaveChangesAsync();

            ViewBag.Message = "Profile updated successfully!";
            return View(vm);
        }

        // ==============================
        // ✅ Change Password (POST)
        // ==============================
        [HttpPost]
        public async Task<IActionResult> ChangePassword(string currentPassword, string newPassword, string confirmPassword)
        {
            var userId = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var user = await _db.users.FindAsync(int.Parse(userId));
            if (user == null)
                return NotFound();

            if (user.password_hash != currentPassword)
                return BadRequest("Current password is incorrect.");

            if (newPassword != confirmPassword)
                return BadRequest("New password and confirmation do not match.");

            user.password_hash = newPassword;
            await _db.SaveChangesAsync();

            return Ok("Password updated successfully!");
        }


        // ==============================
        // ❌ Delete Account (POST)
        // ==============================
        [HttpPost]
        public async Task<IActionResult> DeleteAccount()
        {
            var userId = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrEmpty(userId))
                return RedirectToAction("Login", "Account", new { area = "JobSeeker" });

            var user = await _db.users.FindAsync(int.Parse(userId));
            if (user == null)
                return NotFound();

            _db.users.Remove(user);
            await _db.SaveChangesAsync();

            HttpContext.Session.Clear();

            TempData["Message"] = "Your account has been deleted successfully.";
            return RedirectToAction("Login", "Account", new { area = "JobSeeker" });
        }


        public IActionResult Enable2FA()
        {
            var secret = TwoFactorService.GenerateSecret();
            HttpContext.Session.SetString("2FA_Secret", secret);

            var userEmail = HttpContext.Session.GetString("UserEmail") ?? "demo@jobportal.com";
            var qrUrl = TwoFactorService.GenerateQrCodeUrl(userEmail, secret);

            // Generate QR image (base64)
            using var qrGen = new QRCodeGenerator();
            using var qrData = qrGen.CreateQrCode(qrUrl, QRCodeGenerator.ECCLevel.Q);
            using var qrCode = new PngByteQRCode(qrData);
            var qrImage = Convert.ToBase64String(qrCode.GetGraphic(20));

            ViewBag.QrImage = $"data:image/png;base64,{qrImage}";
            ViewBag.Secret = secret;

            return View();
        }

        // ===============================
        // ✅ Two-Factor Authentication (DB-based)
        // ===============================

        // Generate a new 2FA secret and QR code
        [HttpPost]
        public async Task<IActionResult> Generate2FA()
        {
            var userId = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrEmpty(userId))
                return Json(new { success = false, message = "Not logged in." });

            var user = await _db.users.FindAsync(int.Parse(userId));
            if (user == null)
                return Json(new { success = false, message = "User not found." });

            var secret = TwoFactorService.GenerateSecret();
            var qrUrl = TwoFactorService.GenerateQrCodeUrl(user.email, secret);

            using var qrGen = new QRCodeGenerator();
            using var qrData = qrGen.CreateQrCode(qrUrl, QRCodeGenerator.ECCLevel.Q);
            using var qrCode = new PngByteQRCode(qrData);
            var qrImage = Convert.ToBase64String(qrCode.GetGraphic(20));

            // Store the secret in DB temporarily until verification
            user.user_2FA_secret = secret;
            await _db.SaveChangesAsync();

            return Json(new
            {
                success = true,
                qrImage = $"data:image/png;base64,{qrImage}",
                secret
            });
        }

        [HttpPost]
        public async Task<IActionResult> Verify2FA(string code)
        {
            var userId = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrEmpty(userId))
                return Json(new { success = false, message = "Not logged in." });

            var user = await _db.users.FindAsync(int.Parse(userId));
            if (user == null || string.IsNullOrEmpty(user.user_2FA_secret))
                return Json(new { success = false, message = "2FA not initialized." });

            bool valid = TwoFactorService.VerifyCode(user.user_2FA_secret, code);

            if (valid)
            {
                user.user_2FA = true;
                await _db.SaveChangesAsync();
                return Json(new { success = true });
            }

            return Json(new { success = false, message = "Invalid code." });
        }

        // Toggle 2FA ON/OFF
        [HttpPost]
        public async Task<IActionResult> Toggle2FA(bool enabled)
        {
            var userId = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrEmpty(userId))
                return Json(new { success = false, message = "Not logged in." });

            var user = await _db.users.FindAsync(int.Parse(userId));
            if (user == null)
                return Json(new { success = false, message = "User not found." });

            user.user_2FA = enabled;

            if (!enabled)
                user.user_2FA_secret = null; // clear secret when disabled

            await _db.SaveChangesAsync();

            return Json(new { success = true });
        }



        // Apply Page
        [HttpGet]
        public async Task<IActionResult> Apply(int id)
        {
            var userId = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrEmpty(userId))
                return RedirectToAction("Login", "Account", new { area = "JobSeeker" });

            var job = await _db.job_listings.FirstOrDefaultAsync(j => j.job_listing_id == id);
            if (job == null) return NotFound();

            var user = await _db.users.FirstOrDefaultAsync(u => u.user_id == int.Parse(userId));

            var model = new ApplyViewModel
            {
                JobId = job.job_listing_id,
                JobTitle = job.job_title,
                FullName = $"{user.first_name} {user.last_name}",
                Email = user.email
            };

            return View(model);
        }

        //Submit Application
        [HttpPost]
        public async Task<IActionResult> SubmitApplication(ApplyViewModel model, IFormFile resumeFile)
        {
            if (!ModelState.IsValid)
                return View("Apply", model);

            var userId = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrEmpty(userId))
                return RedirectToAction("Login", "Account", new { area = "JobSeeker" });

            // 🗂️ Save uploaded resume (optional)
            string? filePath = null;
            if (resumeFile != null && resumeFile.Length > 0)
            {
                var uploadsDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "resumes");
                if (!Directory.Exists(uploadsDir))
                    Directory.CreateDirectory(uploadsDir);

                filePath = Path.Combine(uploadsDir, $"{Guid.NewGuid()}_{resumeFile.FileName}");
                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await resumeFile.CopyToAsync(stream);
                }
            }

            //Create new application record
            var application = new job_application
            {
                user_id = int.Parse(userId),
                job_listing_id = model.JobId,
                application_status = "Submitted",
                date_updated = DateTime.Now
            };

            _db.job_applications.Add(application);
            await _db.SaveChangesAsync();

            TempData["Success"] = "Your application has been submitted successfully!";
            return RedirectToAction("Applications", "Dashboard", new { area = "JobSeeker" });
        }

        public async Task<IActionResult> Applications()
        {
            var userId = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrEmpty(userId))
            {
                return RedirectToAction("Login", "Account", new { area = "JobSeeker" });
            }

            int id = int.Parse(userId);

            var applications = await _db.job_applications
                .Include(a => a.job_listing)
                .Where(a => a.user_id == id)
                .OrderByDescending(a => a.date_updated)
                .ToListAsync();

            return View(applications);
        }

        // ✅ Dynamic Job Listings
        public async Task<IActionResult> JobListings(string? search)
        {
            var jobs = _db.job_listings
                .Where(j => j.job_status == "Open");

            // Optional search by job title or company
            if (!string.IsNullOrEmpty(search))
            {
                jobs = jobs.Where(j =>
                    j.job_title.Contains(search) ||
                    j.job_description.Contains(search));
            }

            var jobList = await jobs
                .OrderByDescending(j => j.date_posted)
                .ToListAsync();

            return View(jobList);
        }

        // ✅ Optional: Job Details page
        public async Task<IActionResult> JobDetails(int id)
        {
            var job = await _db.job_listings
                .FirstOrDefaultAsync(j => j.job_listing_id == id);

            if (job == null) return NotFound();

            return View(job);
        }

        [HttpPost]
        public async Task<IActionResult> AnalyzeResume(IFormFile resumeFile)
        {
            if (resumeFile == null || resumeFile.Length == 0)
            {
                ViewBag.Error = "Please upload a valid resume file.";
                return View("Feedback");
            }

            // Mock resume text for now
            string extractedText = "Experienced skilled in C#, ASP.NET, SQL, and Azure.";

            // ✅ Send text to TextRazor API
            var client = new HttpClient();
            client.DefaultRequestHeaders.Add("x-textrazor-key", "b426ca814983ef70ffdf990b592414657a82ac2cf2cade032c8f4dc7");

            // Correctly create StringContent (UTF8 + media type)
            var formData = $"extractors=entities&text={Uri.EscapeDataString(extractedText)}";
            var data = new StringContent(formData, Encoding.UTF8, "application/x-www-form-urlencoded");

            var response = await client.PostAsync("https://api.textrazor.com/", data);
            var jsonResponse = await response.Content.ReadAsStringAsync();

            // ✅ Parse response safely
            var extractedKeywords = new List<string>();
            using (JsonDocument doc = JsonDocument.Parse(jsonResponse))
            {
                if (doc.RootElement.TryGetProperty("response", out var responseObj) &&
                    responseObj.TryGetProperty("entities", out var entities))
                {
                    foreach (var entity in entities.EnumerateArray())
                    {
                        if (entity.TryGetProperty("entityId", out var keyword))
                        {
                            extractedKeywords.Add(keyword.GetString() ?? "");
                        }
                    }
                }
            }

            // ✅ Compare with target keywords
            var targetKeywords = new List<string> { "C#", "ASP.NET", "SQL", "JavaScript", "Azure" };
            int matchCount = 0;
            foreach (var word in extractedKeywords)
            {
                if (targetKeywords.Contains(word))
                    matchCount++;
            }

            double matchPercent = (double)matchCount / targetKeywords.Count * 100;

            // ✅ Send results to view
            ViewBag.Keywords = extractedKeywords;
            ViewBag.MatchScore = Math.Round(matchPercent, 2);

            return View("Feedback");
        }
    }
}
