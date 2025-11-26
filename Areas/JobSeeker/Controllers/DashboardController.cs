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
using Microsoft.AspNetCore.Identity;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using QuestPDF.Drawing;
using System.Text.RegularExpressions;



namespace JobPortal.Areas.JobSeeker.Controllers
{
    [Area("JobSeeker")]
    public class DashboardController : Controller
    {
        private readonly AppDbContext _db;
        private readonly IWebHostEnvironment _webHostEnvironment;
        private readonly INotificationService _notificationService;
        public DashboardController(AppDbContext db, IWebHostEnvironment webHostEnvironment, INotificationService notificationService)
        {
            _db = db;
            _webHostEnvironment = webHostEnvironment;
            _notificationService = notificationService;
        }
        public async Task<IActionResult> Index(string? search)
        {
            var userIdStr = HttpContext.Session.GetString("UserId");
            int? userId = string.IsNullOrEmpty(userIdStr) ? null : int.Parse(userIdStr);

            var recentJobsQuery = _db.job_listings
                .Include(j => j.company)
                .Where(j => j.job_status == "Open")
                .OrderByDescending(j => j.date_posted)
                .Take(4);

            if (!string.IsNullOrEmpty(search))
            {
                recentJobsQuery = recentJobsQuery.Where(j =>
                    j.job_title.Contains(search) ||
                    j.company.company_name.Contains(search) ||
                    j.company.company_industry.Contains(search) ||
                    j.company.company_location.Contains(search) ||
                    j.job_type.Contains(search)
                );
            }

            var recentJobs = await recentJobsQuery
                .Select(j => new RecentJobViewModel
                {
                    JobId = j.job_listing_id,
                    JobTitle = j.job_title,
                    CompanyName = j.company.company_name,
                    Industry = j.company.company_industry,
                    Location = j.company.company_location,
                    MinSalary = j.salary_min,
                    MaxSalary = j.salary_max,
                    JobType = j.job_type
                })
                .ToListAsync();

            // ✅ Get applied jobs for current user
            List<int> appliedJobIds = new List<int>();
            if (userId.HasValue)
            {
                appliedJobIds = await _db.job_applications
                    .Where(a => a.user_id == userId.Value)
                    .Select(a => a.job_listing_id)
                    .ToListAsync();
            }

            var model = new DashboardIndexViewModel
            {
                SearchKeyword = search,
                RecentJobs = recentJobs
            };

            ViewBag.AppliedJobs = appliedJobIds;

            return View(model);
        }


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


            var pref = await _db.notification_preferences.FirstOrDefaultAsync(p => p.user_id == userId);
            if (pref == null)
            {
                // if missing, create a default entry
                pref = new notification_preference
                {
                    user_id = userId,
                    allow_inApp = false
                };
                _db.notification_preferences.Add(pref);
                await _db.SaveChangesAsync();
            }

            // 🔹 Fetch resume history for this user
            var resumes = await _db.resumes
                .Where(r => r.user_id == userId)
                .OrderByDescending(r => r.upload_date)
                .ToListAsync();

            ViewBag.UserResumes = resumes;

            // 🔹 Fetch distinct job categories from job_listing
            var categories = await _db.job_listings
                .Where(j => j.job_category != null && j.job_category != "")
                .Select(j => j.job_category)
                .Distinct()
                .OrderBy(c => c)
                .ToListAsync();


            var vm = new ProfileViewModel
            {
                UserId = user.user_id,
                FirstName = user.first_name,
                LastName = user.last_name,
                Email = user.email,
                TwoFAEnabled = user.user_2FA,
                Phone = user.phone,
                Address = user.address,
                Skills = user.skills,
                Education = user.education,
                WorkExperience = user.work_experience,
                TargetIndustry = user.target_industry,

                // notification preferences
                notif_inapp = pref.allow_inApp,
                notif_job_updates = pref.notif_job_updates,
                notif_messages = pref.notif_messages,
                notif_reminders = pref.notif_reminders,

                ProfilePicturePath = string.IsNullOrEmpty(user.profile_picture)
                    ? "/wwwroot/uploads/profile_pictures/test.png"
                    : user.profile_picture,

                // 🔹 Add job categories to ViewModel
                JobCategories = categories
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

            // ---------------- VALIDATION ----------------

            // Validate First Name
            if (!Regex.IsMatch(vm.FirstName ?? "", @"^[A-Za-z\s]+$"))
            {
                ModelState.AddModelError("FirstName", "First name can only contain letters and spaces.");
            }

            // Validate Last Name
            if (!Regex.IsMatch(vm.LastName ?? "", @"^[A-Za-z\s]+$"))
            {
                ModelState.AddModelError("LastName", "Last name can only contain letters and spaces.");
            }

            // Validate Email format
            if (!Regex.IsMatch(vm.Email ?? "", @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
            {
                ModelState.AddModelError("Email", "Invalid email format.");
            }

            // Validate Phone (optional, + and digits only, length 7–15)
            if (!string.IsNullOrEmpty(vm.Phone) && !Regex.IsMatch(vm.Phone, @"^\+?\d{7,15}$"))
            {
                ModelState.AddModelError("Phone", "Phone must contain only digits and be 7 to 15 characters long.");
            }

            // If validation fails, return view 
            if (!ModelState.IsValid)
            {
                return View(vm);
            }

            // --- Handle profile picture upload ---
            if (vm.ProfileImage != null && vm.ProfileImage.Length > 0)
            {
                // Allowed MIME types
                var allowedTypes = new[] { "image/jpeg", "image/png", "image/gif", "image/webp" };

                // Allowed extensions
                var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" };

                var fileExtension = Path.GetExtension(vm.ProfileImage.FileName).ToLower();

                // Validate MIME/type
                if (!allowedTypes.Contains(vm.ProfileImage.ContentType.ToLower()) ||
                    !allowedExtensions.Contains(fileExtension))
                {
                    ModelState.AddModelError("ProfileImage", "Invalid file type. Only JPG, PNG, GIF, or WebP images are allowed.");
                    return View(vm);
                }

                // Max size: 2MB
                if (vm.ProfileImage.Length > 2 * 1024 * 1024)
                {
                    ModelState.AddModelError("ProfileImage", "File too large (max 2MB).");
                    return View(vm);
                }

                // Create folder if not exists
                var uploadDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "profile_pictures");
                if (!Directory.Exists(uploadDir))
                    Directory.CreateDirectory(uploadDir);

                // Define standard file name
                var fileName = $"{user.first_name}_{user.last_name}_Icon.png";
                var filePath = Path.Combine(uploadDir, fileName);

                // Delete old profile images
                var oldFiles = Directory.GetFiles(uploadDir, $"{user.first_name}_{user.last_name}_Icon.*");
                foreach (var old in oldFiles)
                {
                    System.IO.File.Delete(old);
                }

                // Convert uploaded image to PNG
                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    using (var image = System.Drawing.Image.FromStream(vm.ProfileImage.OpenReadStream()))
                    {
                        image.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                    }
                }

                // Save standardized path to DB
                user.profile_picture = $"/uploads/profile_pictures/{fileName}";
            }


            user.first_name = vm.FirstName;
            user.last_name = vm.LastName;
            user.email = vm.Email;
            user.user_2FA = vm.TwoFAEnabled;
            user.phone = vm.Phone;
            user.address = vm.Address;
            user.skills = vm.Skills;
            user.education = vm.Education;
            user.work_experience = vm.WorkExperience;
            user.target_industry = vm.TargetIndustry;


            // ✅ Fetch (or create) notification preference
            var pref = await _db.notification_preferences.FirstOrDefaultAsync(p => p.user_id == vm.UserId);
            if (pref == null)
            {
                pref = new notification_preference
                {
                    user_id = vm.UserId
                };
                _db.notification_preferences.Add(pref);
            }

            // ✅ Update preferences based on view model
            pref.allow_inApp = vm.notif_inapp;
            pref.notif_job_updates = vm.notif_job_updates;
            pref.notif_messages = vm.notif_messages;
            pref.notif_reminders = vm.notif_reminders;

            await _db.SaveChangesAsync();

            ViewBag.Message = "Profile updated successfully!";
            HttpContext.Session.SetString("FirstName", user.first_name ?? "");
            HttpContext.Session.SetString("LastName", user.last_name ?? "");
            HttpContext.Session.SetString("ProfilePicturePath", user.profile_picture ?? "");
            return RedirectToAction("Settings", new { refresh = Guid.NewGuid().ToString() });
        }

        // ==============================
        // ✅ Change Password (POST)
        // ==============================
        [HttpPost]
        public async Task<IActionResult> ChangePassword(string currentPassword, string newPassword, string confirmPassword)
        {
            var userId = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrEmpty(userId))
                return Unauthorized("User not logged in.");

            var user = await _db.users.FindAsync(int.Parse(userId));
            if (user == null)
                return NotFound("User not found.");

            // Verify current password
            var hasher = new PasswordHasher<object>();
            var verificationResult = hasher.VerifyHashedPassword(null, user.password_hash, currentPassword);
            if (verificationResult == PasswordVerificationResult.Failed)
                return BadRequest("Current password is incorrect.");

            // Confirm new password matches
            if (newPassword != confirmPassword)
                return BadRequest("New password and confirmation do not match.");

            // ✅ Apply your existing password strength rules
            var passwordErrors = new List<string>();
            if (newPassword.Length < 6 || newPassword.Length > 20)
                passwordErrors.Add("Password must be 6 to 20 characters long.");
            if (!Regex.IsMatch(newPassword, @"[A-Z]"))
                passwordErrors.Add("At least one uppercase letter required.");
            if (!Regex.IsMatch(newPassword, @"[a-z]"))
                passwordErrors.Add("At least one lowercase letter required.");
            if (!Regex.IsMatch(newPassword, @"\d"))
                passwordErrors.Add("At least one number required.");
            if (!Regex.IsMatch(newPassword, @"[@$!%*?&.,]"))
                passwordErrors.Add("At least one special character required.");

            if (passwordErrors.Any())
                return BadRequest(string.Join(" ", passwordErrors));

            // After verifying current password
            if (hasher.VerifyHashedPassword(null, user.password_hash, newPassword) == PasswordVerificationResult.Success)
                return BadRequest("New password cannot be the same as your current password.");

            // Hash and save
            user.password_hash = hasher.HashPassword(null, newPassword);
            await _db.SaveChangesAsync();

            return Ok("Password updated successfully!");
        }

        // ==============================
        // ❌ Delete Account (Soft Delete)
        // ==============================
        [HttpPost]
        public async Task<IActionResult> DeleteAccount()
        {
            var userIdStr = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrEmpty(userIdStr))
                return Unauthorized();

            int userId = int.Parse(userIdStr);
            var user = await _db.users.FirstOrDefaultAsync(u => u.user_id == userId);

            if (user == null)
                return NotFound();

            // 🟢 Soft delete: set account as Inactive instead of removing
            user.user_status = "Inactive";
            _db.users.Update(user);
            await _db.SaveChangesAsync();

            // 🧹 End session
            HttpContext.Session.Clear();

            return Ok(new { message = "Account deactivated successfully." });
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

        public async Task<IActionResult> ManageResume()
        {
            var userIdStr = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrEmpty(userIdStr))
                return RedirectToAction("Login", "Account", new { area = "JobSeeker" });

            int userId = int.Parse(userIdStr);

            var resumes = await _db.resumes
                .Where(r => r.user_id == userId)
                .OrderByDescending(r => r.upload_date)
                .ToListAsync();

            ViewBag.UserResumes = resumes;
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> UploadResume(IFormFile resumeFile)
        {
            var userIdStr = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrEmpty(userIdStr))
                return RedirectToAction("Login", "Account", new { area = "JobSeeker" });

            int userId = int.Parse(userIdStr);

            if (resumeFile == null || resumeFile.Length == 0)
            {
                TempData["Error"] = "Please upload a valid resume file.";
                return RedirectToAction(nameof(ManageResume));
            }

            // 🔹 Get user's first name from DB
            var user = await _db.users.Where(u => u.user_id == userId).FirstOrDefaultAsync();
            string firstNameSafe = (user?.first_name ?? "User")
                .Replace(" ", "")
                .Replace(".", "")
                .Replace("-", "");

            // 📌 Extract extension only
            string extension = Path.GetExtension(resumeFile.FileName);

            // 📌 Build SAFE filename: FirstName + Timestamp
            string safeFileName = $"{firstNameSafe}_{DateTime.Now:yyyyMMddHHmmss}{extension}";
            safeFileName = string.Concat(safeFileName.Split(Path.GetInvalidFileNameChars()));

            // 📁 Ensure upload folder exists
            var uploadsDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "user_resumes");
            Directory.CreateDirectory(uploadsDir);

            // 💾 Save file
            var fullPath = Path.Combine(uploadsDir, safeFileName);
            using (var stream = new FileStream(fullPath, FileMode.Create))
            {
                await resumeFile.CopyToAsync(stream);
            }

            // 🌐 Store relative path
            var relativePath = $"/uploads/user_resumes/{safeFileName}";

            // 🗄 Save record in DB
            var resume = new resume
            {
                user_id = userId,
                upload_date = DateTime.Now,
                file_path = relativePath
            };

            _db.resumes.Add(resume);
            await _db.SaveChangesAsync();

            TempData["Success"] = "Resume uploaded successfully!";
            return RedirectToAction(nameof(ManageResume));
        }


        [HttpPost]
        public async Task<IActionResult> DeleteResume(int id)
        {
            var resume = await _db.resumes.FindAsync(id);
            if (resume == null)
                return NotFound();

            var fullPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", resume.file_path.TrimStart('/'));
            if (System.IO.File.Exists(fullPath))
                System.IO.File.Delete(fullPath);

            _db.resumes.Remove(resume);
            await _db.SaveChangesAsync();

            TempData["Success"] = "Resume deleted successfully!";
            return RedirectToAction(nameof(ManageResume));
        }

        // Apply Page
        [HttpGet]
        public async Task<IActionResult> Apply(int id)
        {
            var userIdStr = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrEmpty(userIdStr))
                return RedirectToAction("Login", "Account", new { area = "JobSeeker" });

            int userId = int.Parse(userIdStr);

            var job = await _db.job_listings.FirstOrDefaultAsync(j => j.job_listing_id == id);
            if (job == null) return NotFound();

            // Check if user has already applied
            bool hasApplied = await _db.job_applications
                .AnyAsync(a => a.user_id == userId && a.job_listing_id == id);

            if (hasApplied)
            {
                TempData["Error"] = "You have already applied for this job.";
                return View("AlreadyApplied"); // or you can reuse Apply view with a message
            }

            var user = await _db.users.FirstOrDefaultAsync(u => u.user_id == userId);
            var userResumes = await _db.resumes
                .Where(r => r.user_id == userId)
                .OrderByDescending(r => r.upload_date)
                .ToListAsync();

            var model = new ApplyViewModel
            {
                JobId = job.job_listing_id,
                JobTitle = job.job_title,
                FullName = $"{user.first_name} {user.last_name}",
                Email = user.email,
                ExistingResumes = userResumes
            };

            return View(model);
        }



        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SubmitApplication(ApplyViewModel model)
        {
            if (!ModelState.IsValid)
            {
                var userIdStr = HttpContext.Session.GetString("UserId");
                model.ExistingResumes = await _db.resumes
                    .Where(r => r.user_id.ToString() == userIdStr)
                    .ToListAsync();
                return View("Apply", model);
            }

            var userIdString = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrEmpty(userIdString))
                return RedirectToAction("Login", "Account", new { area = "JobSeeker" });

            int userId = int.Parse(userIdString);

            // VALIDATION: Expected Salary must not be null or 0
            if (!model.ExpectedSalary.HasValue || model.ExpectedSalary <= 0)
            {
                TempData["SalaryError"] = "Please enter a positive expected salary.";

                model.ExistingResumes = await _db.resumes
                    .Where(r => r.user_id == userId)
                    .ToListAsync();

                return View("Apply", model);
            }

            // ---------------------------
            // VALIDATION: Must provide a resume
            // ---------------------------
            if (!model.SelectedResumeId.HasValue && (model.ResumeFile == null || model.ResumeFile.Length == 0))
            {
                TempData["SalaryError"] = "Please select an existing resume or upload a new one.";

                // Reload existing resumes for the form
                model.ExistingResumes = await _db.resumes
                    .Where(r => r.user_id == userId)
                    .ToListAsync();

                return View("Apply", model);
            }

            string? relativePath = null;

            // CASE 1: Existing resume selected
            if (model.SelectedResumeId.HasValue)
            {
                var existingResume = await _db.resumes.FindAsync(model.SelectedResumeId.Value);
                if (existingResume != null)
                    relativePath = existingResume.file_path;
            }

            // CASE 2: New file uploaded (always rename)
            if (model.ResumeFile != null && model.ResumeFile.Length > 0)
            {
                // 🔐 Validate file extension
                var allowedExtensions = new[] { ".pdf" };
                string extension = Path.GetExtension(model.ResumeFile.FileName).ToLower();

                if (!allowedExtensions.Contains(extension))
                {
                    TempData["ResumeError"] = "Only PDF files are allowed.";

                    model.ExistingResumes = await _db.resumes
                        .Where(r => r.user_id == userId)
                        .ToListAsync();

                    return View("Apply", model);
                }

                // 🔐 Validate file size (optional but recommended)
                if (model.ResumeFile.Length > 5 * 1024 * 1024) // 5 MB
                {
                    TempData["ResumeError"] = "File too large. Max 5MB allowed.";

                    model.ExistingResumes = await _db.resumes
                        .Where(r => r.user_id == userId)
                        .ToListAsync();

                    return View("Apply", model);
                }

                var uploadsDir = Path.Combine(Directory.GetCurrentDirectory(),
                                              "wwwroot", "uploads", "user_resumes");
                Directory.CreateDirectory(uploadsDir);

                var user = await _db.users.FindAsync(userId);
                string firstNameSafe = string.Concat(user.first_name.Where(c => !char.IsWhiteSpace(c)));

                string safeFileName = $"{firstNameSafe}_{DateTime.Now:yyyyMMddHHmmss}{extension}";

                string fullPath = Path.Combine(uploadsDir, safeFileName);
                using (var stream = new FileStream(fullPath, FileMode.Create))
                {
                    await model.ResumeFile.CopyToAsync(stream);
                }

                relativePath = $"/uploads/user_resumes/{safeFileName}";

                // Save to DB
                var newResume = new resume
                {
                    user_id = userId,
                    upload_date = DateTime.Now,
                    file_path = relativePath
                };
                _db.resumes.Add(newResume);
                await _db.SaveChangesAsync();
            }


            // Create job application
            var application = new job_application
            {
                user_id = userId,
                job_listing_id = model.JobId,
                application_status = "Submitted",
                date_updated = DateTime.Now,
                resume_path = relativePath,
                expected_salary = model.ExpectedSalary,
                description = model.Description
            };

            _db.job_applications.Add(application);
            await _db.SaveChangesAsync();

            // <-- Place the notification logic here
            var preferences = await _db.notification_preferences
                .FirstOrDefaultAsync(p => p.user_id == userId);

            if (preferences != null && preferences.allow_inApp && preferences.notif_job_updates)
            {
                var notification = new notification
                {
                    user_id = userId,
                    notification_title = "Application Update",
                    notification_msg = "Your application has been received.",
                    notification_type = "Application",
                    notification_date_created = DateTime.Now,
                    notification_read_status = false
                };
                _db.notifications.Add(notification);
                await _db.SaveChangesAsync();
            }

            TempData["Success"] = "Your application has been submitted successfully!";
            return RedirectToAction("Applications", "Dashboard", new { area = "JobSeeker" });
        }

        public async Task<IActionResult> Applications(string? status, string? sortBy, int page = 1)
        {
            var userId = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrEmpty(userId))
                return RedirectToAction("Login", "Account", new { area = "JobSeeker" });

            int id = int.Parse(userId);
            int pageSize = 10;

            var query = _db.job_applications
                .Include(a => a.job_listing)
                    .ThenInclude(j => j.company)
                .Where(a => a.user_id == id);


            // ✅ Filter by status
            if (!string.IsNullOrEmpty(status))
            {
                query = query.Where(a => a.application_status == status);
                ViewBag.SelectedStatus = status;
            }
            else
            {
                ViewBag.SelectedStatus = "";
            }

            // ✅ Sorting
            if (sortBy == "date_asc")
            {
                query = query.OrderBy(a => a.date_updated);
            }
            else
            {
                // Default newest first
                query = query.OrderByDescending(a => a.date_updated);
                sortBy = ""; // default (Newest)
            }

            ViewBag.SortBy = sortBy; // ✅ match the View's reference exactly



            int totalApplications = await query.CountAsync();
            var applications = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var dashboard = new ApplicationsDashboardViewModel
            {
                TotalApplications = totalApplications,
                ApplicationsInReview = await _db.job_applications.CountAsync(a => a.user_id == id && a.application_status == "AI-Screened"),
                InterviewsScheduled = await _db.job_applications.CountAsync(a => a.user_id == id && a.application_status == "Interview"),
                RecentActivities = applications.Take(5).Select(a => new RecentActivityViewModel
                {
                    Message = $"Updated application for {a.job_listing.job_title} ({a.application_status})",
                    Date = a.date_updated
                }).ToList(),
                RecentNotifications = await _db.notifications
    .Where(n => n.user_id == id)
    .OrderByDescending(n => n.notification_date_created)
    .Take(5)
    .Select(n => new RecentNotificationViewModel
    {
        Message = n.notification_msg,
        Date = n.notification_date_created
    })
    .ToListAsync()

            };

            var model = new MyApplicationsViewModel
            {
                Dashboard = dashboard,
                Applications = applications,
                CurrentPage = page,
                TotalPages = (int)Math.Ceiling(totalApplications / (double)pageSize)
            };

            return View(model);
        }

        [HttpPost]
        public IActionResult AcceptOffer(int applicationId)
        {
            var application = _db.job_applications
                .Include(a => a.job_listing)
                .FirstOrDefault(a => a.application_id == applicationId);

            if (application == null)
            {
                return NotFound();
            }

            var recruiterId = application.job_listing.user_id;
            var candidateId = application.user_id;

            // Try to find existing conversation between recruiter and candidate
            var convo = _db.conversations.FirstOrDefault(c =>
                c.recruiter_id == recruiterId && c.candidate_id == candidateId);

            if (convo == null)
            {
                // Create a new conversation entry
                convo = new conversation
                {
                    job_listing_id = application.job_listing_id,
                    created_at = DateTime.Now,
                    last_message_at = DateTime.Now,
                    last_snippet = "Offer accepted by candidate.",
                    unread_for_recruiter = 1,
                    unread_for_candidate = 0,
                    recruiter_id = recruiterId,
                    candidate_id = candidateId,
                    job_title = application.job_listing.job_title,
                    candidate_name = "Candidate"
                };

                _db.conversations.Add(convo);
                _db.SaveChanges();
            }

            // Update job application status
            application.application_status = "Offer";
            application.date_updated = DateTime.Now;
            _db.SaveChanges();

            // Redirect to the chat view (InboxController)
            return RedirectToAction("Thread", "Inbox", new { id = convo.conversation_id, prefill = "Hi, I have accepted the offer. Thank you for the opportunity!" });
        }

        // ✅ Dynamic Job Listings with Pagination
        public async Task<IActionResult> JobListings(int? minSalary, int? maxSalary, string? search, string? location, string? salaryRange, string? workMode, string? jobCategory, int page = 1,
    bool? favouritesOnly = null)
        {
            int pageSize = 10;

            var userIdStr = HttpContext.Session.GetString("UserId");
            int? userId = string.IsNullOrEmpty(userIdStr) ? null : int.Parse(userIdStr);

            // Get user's target industry
            string? targetIndustry = null;
            if (userId.HasValue)
            {
                targetIndustry = await _db.users
                    .Where(u => u.user_id == userId.Value)
                    .Select(u => u.target_industry)
                    .FirstOrDefaultAsync();
            }

            // Base query
            var jobsQuery = _db.job_listings
                .Include(j => j.company)
                .Where(j => j.job_status == "Open");

            // 🔍 Keyword search
            if (!string.IsNullOrEmpty(search))
            {
                jobsQuery = jobsQuery.Where(j =>
                    j.job_title.Contains(search) ||
                    j.job_description.Contains(search) ||
                    j.company.company_name.Contains(search) ||
                    j.company.company_industry.Contains(search));
            }

            // 📍 Location filter
            if (!string.IsNullOrEmpty(location))
            {
                jobsQuery = jobsQuery.Where(j => j.company.company_location == location);
            }

            // 🧭 Salary, work mode, category filters remain the same...

            // 🔹 Sort so that target industry jobs appear first
            if (!string.IsNullOrEmpty(targetIndustry))
            {
                jobsQuery = jobsQuery
                    .OrderByDescending(j =>
                        j.job_category.Trim().ToLower().Equals(targetIndustry.Trim().ToLower()))
                    .ThenByDescending(j => j.date_posted);
            }
            else
            {
                jobsQuery = jobsQuery.OrderByDescending(j => j.date_posted);
            }



            // 🧭 Filter by salary range
            if (minSalary.HasValue && maxSalary.HasValue)
            {
                jobsQuery = jobsQuery.Where(j =>
                    j.salary_max >= minSalary.Value && j.salary_min <= maxSalary.Value);
            }


            // 🏠 Work Mode
            if (!string.IsNullOrEmpty(workMode))
            {
                jobsQuery = jobsQuery.Where(j => j.work_mode == workMode);
            }
            ViewBag.MinSalary = minSalary ?? 0;
            ViewBag.MaxSalary = maxSalary ?? 5000;

            // 💼 Job Category
            if (!string.IsNullOrEmpty(jobCategory))
            {
                jobsQuery = jobsQuery.Where(j => j.job_type == jobCategory);
            }

            if (favouritesOnly == true && userId.HasValue)
            {
                jobsQuery = jobsQuery.Where(j =>
                    _db.job_favourites.Any(f => f.user_id == userId.Value && f.job_listing_id == j.job_listing_id));
            }


            // 📄 Pagination
            int totalJobs = await jobsQuery.CountAsync();
            var jobList = await jobsQuery
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();


            // ✅ Get applied job IDs for current user
            var appliedJobIds = new List<int>();
            if (userId.HasValue)
            {
                appliedJobIds = await _db.job_applications
                                         .Where(a => a.user_id == userId.Value)
                                         .Select(a => a.job_listing_id)
                                         .ToListAsync();
            }
            ViewBag.AppliedJobs = appliedJobIds;

            // 🪣 Pass filters to view
            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = (int)Math.Ceiling(totalJobs / (double)pageSize);
            ViewBag.Search = search;
            ViewBag.Location = location;
            ViewBag.SalaryRange = salaryRange;
            ViewBag.JobCategory = jobCategory;
            ViewBag.WorkMode = workMode;
            ViewBag.FavouritesOnly = favouritesOnly ?? false;

            var favouriteJobIds = new List<int>();
            if (userId.HasValue)
            {
                favouriteJobIds = await _db.job_favourites
                                           .Where(f => f.user_id == userId.Value)
                                           .Select(f => f.job_listing_id)
                                           .ToListAsync();
            }
            ViewBag.FavouriteJobs = favouriteJobIds;


            // 🗺️ Get all distinct locations for dropdown
            ViewBag.Locations = await _db.companies
                .Select(c => c.company_location)
                .Distinct()
                .OrderBy(c => c)
                .ToListAsync();

            return View(jobList);
        }

        [HttpGet]
        public async Task<IActionResult> FavouriteJobs()
        {
            var userIdStr = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrEmpty(userIdStr))
                return RedirectToAction("Login", "Account", new { area = "JobSeeker" });

            int userId = int.Parse(userIdStr);

            // Get job IDs the user has favourited
            var favJobIds = await _db.job_favourites
                                     .Where(f => f.user_id == userId)
                                     .Select(f => f.job_listing_id)
                                     .ToListAsync();

            // Get job listings for those IDs
            var jobs = await _db.job_listings
                                .Where(j => favJobIds.Contains(j.job_listing_id) && j.job_status == "Open")
                                .ToListAsync();


            return View(jobs); // pass to a view
        }

        [HttpPost]
        public async Task<IActionResult> ToggleFavourite([FromBody] FavouriteToggleModel model)
        {
            int jobId = model.jobId;
            var userIdStr = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrEmpty(userIdStr))
                return Json(new { success = false });

            int userId = int.Parse(userIdStr);

            var existing = await _db.job_favourites
                .FirstOrDefaultAsync(f => f.user_id == userId && f.job_listing_id == model.jobId);

            bool isFavourite = false;
            if (existing != null)
            {
                _db.job_favourites.Remove(existing);
            }
            else
            {
                _db.job_favourites.Add(new job_favourite
                {
                    user_id = userId,
                    job_listing_id = model.jobId,
                    created_at = DateTime.UtcNow
                });
                isFavourite = true;
            }

            await _db.SaveChangesAsync();

            return Json(new { success = true, isFavourite });
        }




        // ✅ Optional: Job Details page
        public async Task<IActionResult> JobDetails(int id)
        {
            var userIdStr = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrEmpty(userIdStr))
                return RedirectToAction("Login", "Account", new { area = "JobSeeker" });

            int userId = int.Parse(userIdStr);

            var job = await _db.job_listings
                .Include(j => j.company)
                .FirstOrDefaultAsync(j => j.job_listing_id == id);

            if (job == null) return NotFound();

            // Check if this job is favourited by the user
            bool isFavourite = await _db.job_favourites
                .AnyAsync(f => f.user_id == userId && f.job_listing_id == id);
            ViewBag.IsFavourite = isFavourite;

            // ✅ Check if the user has already applied to this job
            bool hasApplied = await _db.job_applications
                .AnyAsync(a => a.user_id == userId && a.job_listing_id == id);
            ViewBag.HasApplied = hasApplied;

            return View(job);
        }

        // GET: /JobSeeker/Dashboard/ResumeBuilder
        [HttpGet]
        public async Task<IActionResult> ResumeBuilder()
        {
            var userIdStr = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrEmpty(userIdStr))
                return RedirectToAction("Login", "Account", new { area = "JobSeeker" });

            int userId = int.Parse(userIdStr);
            var user = await _db.users.FirstOrDefaultAsync(u => u.user_id == userId);
            if (user == null)
                return NotFound("User not found.");

            var vm = new ResumeBuilderViewModel
            {
                FullName = $"{user.first_name} {user.last_name}",
                Email = user.email,
                Phone = user.phone,
                Address = user.address,
                Education = user.education,
                Experience = user.work_experience,
                Skills = user.skills
            };

            return View(vm);
        }


        private byte[] GenerateClassicResumePDF(byte[]? profileImage, string fullName, string email, string phone, string address,
            string summary, string education, string experience, string skills, string certifications, string projects)
        {
            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(2, Unit.Centimetre);
                    page.Background(Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(12));

                    page.Header().Column(column =>
                    {
                        column.Spacing(10);
                        //  Profile Picture (optional)
                        if (profileImage != null && profileImage.Length > 0)
                        {
                            column.Item().AlignCenter().Width(80).Height(80).Element(e =>
            {
                e.Image(profileImage, ImageScaling.FitArea);
            });
                        }

                        //  Full Name
                        column.Item().Text(fullName)
                            .Bold()
                            .FontSize(22)
                            .FontColor(Colors.Blue.Medium)
                            .AlignCenter();
                    });

                    page.Content().PaddingVertical(10).Column(column =>
                    {
                        column.Spacing(10);

                        // Contact Info
                        column.Item().Text($"Email: {email}");
                        if (!string.IsNullOrWhiteSpace(phone))
                            column.Item().Text($"Phone: {phone}");
                        if (!string.IsNullOrWhiteSpace(address))
                            column.Item().Text($"Address: {address}");

                        column.Item().PaddingVertical(5).LineHorizontal(1).LineColor(Colors.Grey.Medium);

                        // Summary
                        if (!string.IsNullOrWhiteSpace(summary))
                        {
                            column.Item().Text("Professional Summary:").Bold();
                            column.Item().Text(summary);
                        }

                        //  Education
                        if (!string.IsNullOrWhiteSpace(education))
                        {
                            column.Item().Text("Education:").Bold();
                            column.Item().Text(education);
                        }

                        //  Experience
                        if (!string.IsNullOrWhiteSpace(experience))
                        {
                            column.Item().Text("Experience:").Bold();

                            // Split experiences by newline and add each as a separate item
                            foreach (var exp in experience.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
                            {
                                column.Item().Text(exp.Trim());
                            }
                        }

                        // Projects
                        if (!string.IsNullOrWhiteSpace(projects))
                        {
                            column.Item().Text("Projects:").Bold();
                            foreach (var proj in projects.Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries))
                                column.Item().PaddingLeft(10).Text(proj.Trim());
                        }

                        // Skills
                        if (!string.IsNullOrWhiteSpace(skills))
                        {
                            column.Item().Text("Skills:").Bold();
                            column.Item().Text(skills);
                        }

                        // Certifications
                        if (!string.IsNullOrWhiteSpace(certifications))
                        {
                            column.Item().Text("Certifications:").Bold();
                            column.Item().Text(certifications);
                        }

                        // Footer Note
                        column.Item().PaddingVertical(10)
                            .LineHorizontal(1)
                            .LineColor(Colors.Grey.Medium);

                        // column.Item()
                        //     .Text("Generated by Job Seeker Portal")
                        //     .FontSize(10)
                        //     .Italic()
                        //     .FontColor(Colors.Grey.Darken2);
                    });

                    // page.Footer().AlignCenter().Text(x =>
                    // {
                    //     x.Span("Page ");
                    //     x.CurrentPageNumber();
                    //     x.Span(" of ");
                    //     x.TotalPages();
                    // });
                });
            });

            return document.GeneratePdf();
        }

        private byte[] GenerateModernResumePDF(byte[]? profileImage, string fullName, string email, string phone, string address,
      string summary, string education, string experience, string skills, string certifications, string projects)
        {
            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(1.5f, Unit.Centimetre);
                    page.Background(Colors.White);

                    page.Content().Row(row =>
                    {
                        // 🔹 Left Sidebar (Profile & Skills)
                        row.RelativeItem(1.2f).Background(Colors.Grey.Lighten3).Padding(15).Column(left =>
                        {
                            // Profile Image
                            if (profileImage != null && profileImage.Length > 0)
                            {
                                left.Item().AlignCenter().Width(120).Height(120).Element(e =>
                                {
                                    e.Image(profileImage, ImageScaling.FitArea);
                                });
                                left.Item().PaddingVertical(10);
                            }

                            // Name & Contact Info
                            left.Item().AlignCenter().Text(fullName)
                                .Bold().FontSize(18).FontColor(Colors.Blue.Medium);
                            if (!string.IsNullOrEmpty(email)) left.Item().AlignCenter().Text(email);
                            if (!string.IsNullOrEmpty(phone)) left.Item().AlignCenter().Text(phone);
                            if (!string.IsNullOrEmpty(address))
                                left.Item().AlignCenter().Text(address).WrapAnywhere();

                            // Divider
                            left.Item().PaddingVertical(10).LineHorizontal(1).LineColor(Colors.Grey.Medium);

                            //  Skills Section
                            if (!string.IsNullOrEmpty(skills))
                            {
                                left.Item().Text("Skills").Bold().FontSize(12).FontColor(Colors.Black);
                                foreach (var skill in skills.Split(new[] { '\n', ',' }, StringSplitOptions.RemoveEmptyEntries))
                                    left.Item().PaddingLeft(10).Text("• " + skill.Trim());
                            }

                            //  Certifications
                            if (!string.IsNullOrEmpty(certifications))
                            {
                                left.Item().PaddingTop(10).Text("Certifications").Bold().FontSize(12).FontColor(Colors.Black);
                                foreach (var cert in certifications.Split(new[] { '\n', ',' }, StringSplitOptions.RemoveEmptyEntries))
                                    left.Item().PaddingLeft(10).Text("• " + cert.Trim());
                            }

                        });

                        // 🔹 Right Main Content (Summary, Education, Experience)
                        row.RelativeItem(1.8f).PaddingLeft(25).Column(right =>
                        {
                            //  Professional Summary
                            if (!string.IsNullOrEmpty(summary))
                            {
                                right.Item().Text("Professional Summary").Bold().FontSize(14);
                                right.Item().Text(summary);
                                right.Item().PaddingVertical(6).LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten1);
                            }

                            // 🎓 Education
                            if (!string.IsNullOrEmpty(education))
                            {
                                right.Item().PaddingTop(10).Text("Education").Bold().FontSize(14);
                                right.Item().Text(education);
                                right.Item().PaddingVertical(6).LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten1);
                            }

                            // 💼 Experience
                            if (!string.IsNullOrEmpty(experience))
                            {
                                right.Item().PaddingTop(10).Text("Experience").Bold().FontSize(14);

                                foreach (var exp in experience.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
                                {
                                    right.Item().PaddingLeft(10).Text("• " + exp.Trim());
                                }

                                right.Item().PaddingVertical(6).LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten1);
                            }

                            // 🧩 Projects
                            if (!string.IsNullOrWhiteSpace(projects))
                            {
                                right.Item().PaddingTop(10).Text("Projects").Bold().FontSize(14);

                                // Split by double newlines — each project block
                                foreach (var projBlock in projects.Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries))
                                {
                                    var lines = projBlock.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);

                                    if (lines.Length > 0)
                                    {
                                        // First line is project title + tech stack
                                        right.Item().PaddingLeft(10).Text(lines[0].Trim());

                                        // Remaining lines are descriptions/achievements
                                        for (int i = 1; i < lines.Length; i++)
                                        {
                                            right.Item().PaddingLeft(10).Text(lines[i].Trim());
                                        }
                                    }
                                }

                                right.Item().PaddingVertical(6).LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten1);
                            }
                        });
                    });
                });
            });

            return document.GeneratePdf();
        }



        // POST: /JobSeeker/Dashboard/SaveResume
        [HttpPost]
        public async Task<IActionResult> SaveResumePDF(
            IFormFile? profilePic, string fullName, string email, string phone,
            string address, string summary, string education, string experience,
            string skills, string certifications, string projects, string template)
        {
            var userIdStr = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrEmpty(userIdStr))
                return RedirectToAction("Login", "Account", new { area = "JobSeeker" });

            int userId = int.Parse(userIdStr);
            if (!string.IsNullOrWhiteSpace(education))
            {
                // Look for year(s) inside parentheses, e.g., "(2020-2024)"
                var match = Regex.Match(education, @"\((\d{4})(-(\d{4}))?\)");
                if (match.Success)
                {
                    int startYear = int.Parse(match.Groups[1].Value);
                    if (startYear > DateTime.Now.Year)
                    {
                        TempData["Error"] = $"Education year cannot be later than {DateTime.Now.Year}.";
                        return RedirectToAction("ResumeBuilder");
                    }

                    if (match.Groups[3].Success)
                    {
                        int endYear = int.Parse(match.Groups[3].Value);
                        if (endYear > DateTime.Now.Year)
                        {
                            TempData["Error"] = $"Education year cannot be later than {DateTime.Now.Year}.";
                            return RedirectToAction("ResumeBuilder");
                        }
                    }
                }
                else
                {
                    TempData["Error"] = "Invalid education year format. Use YYYY or YYYY-YYYY.";
                    return RedirectToAction("ResumeBuilder");
                }
            }

            // Declare imageBytes first
            byte[]? imageBytes = null;

            if (profilePic != null && profilePic.Length > 0)
            {
                var allowedTypes = new[] { "image/jpeg", "image/png", "image/jpg", "image/webp" };

                if (!allowedTypes.Contains(profilePic.ContentType))
                {
                    TempData["Error"] = "Invalid file type. Please upload an image.";
                    return RedirectToAction("ResumeBuilder");
                }

                if (profilePic.Length > 2 * 1024 * 1024)
                {
                    TempData["Error"] = "Image too large. Maximum allowed size is 2MB.";
                    return RedirectToAction("ResumeBuilder");
                }

                // Convert to bytes only if valid
                using var ms = new MemoryStream();
                await profilePic.CopyToAsync(ms);
                imageBytes = ms.ToArray();
            }

            // Generate PDF
            var pdfBytes = template switch
            {
                "modern" => GenerateModernResumePDF(imageBytes, fullName, email, phone, address, summary, education, experience, skills, certifications, projects),
                "classic" => GenerateClassicResumePDF(imageBytes, fullName, email, phone, address, summary, education, experience, skills, certifications, projects),
                _ => throw new Exception("Invalid template selected")
            };


            // Ensure resumes folder exists
            var resumesDir = Path.Combine(_webHostEnvironment.WebRootPath, "resumes");
            Directory.CreateDirectory(resumesDir);

            // Safest Filename Format
            var user = await _db.users.FindAsync(userId);

            string firstNameSafe = (user?.first_name ?? "User")
                .Replace(" ", "")
                .Replace(".", "")
                .Replace("-", "");

            string safeFileName = $"{firstNameSafe}_{DateTime.Now:yyyyMMddHHmmss}.pdf";

            string filePath = Path.Combine(resumesDir, safeFileName);

            await System.IO.File.WriteAllBytesAsync(filePath, pdfBytes);

            // 3️⃣ SAVE IN DB
            var newResume = new resume
            {
                user_id = userId,
                upload_date = DateTime.Now,

                // ⚠ FIXED: Correct folder path
                file_path = $"/resumes/{safeFileName}"
            };

            _db.resumes.Add(newResume);
            await _db.SaveChangesAsync();

            // 4️⃣ RETURN PDF FILE TO USER
            return File(pdfBytes, "application/pdf", safeFileName);
        }

        public IActionResult GetRecentNotifications()
        {
            var userIdStr = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrEmpty(userIdStr))
                return Json(new { success = false, message = "User not logged in." });

            int userId = int.Parse(userIdStr);

            // Total unread count
            int unreadCount = _db.notifications
                .Count(n => n.user_id == userId && !n.notification_read_status);

            // Latest 3 unread notifications
            var notifications = _db.notifications
                .Where(n => n.user_id == userId && !n.notification_read_status)
                .OrderByDescending(n => n.notification_date_created)
                .Take(3)
                .Select(n => new
                {
                    title = n.notification_title,
                    message = n.notification_msg,
                    date = n.notification_date_created.ToString("yyyy-MM-dd HH:mm"),
                    read = n.notification_read_status
                })
                .ToList();

            return Json(new
            {
                success = true,
                data = notifications,
                totalUnread = unreadCount
            });
        }


        public IActionResult Notifications(int page = 1, int pageSize = 20)
        {
            var userId = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrEmpty(userId))
                return RedirectToAction("Login", "Account", new { area = "JobSeeker" });

            int parsedId = int.Parse(userId);

            var totalNotifications = _db.notifications
    .Where(n => n.user_id == parsedId)
    .Count();

            var notifications = _db.notifications
                .Where(n => n.user_id == parsedId)
                .OrderByDescending(n => n.notification_date_created)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            ViewBag.CurrentPage = page;
            ViewBag.PageSize = pageSize;
            ViewBag.TotalNotifications = totalNotifications;

            return View(notifications);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult MarkSelectedAsRead([FromBody] List<int> ids)
        {
            if (ids == null || ids.Count == 0)
                return Json(new { success = false, message = "No notifications selected." });

            var userId = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrEmpty(userId))
                return Json(new { success = false, message = "User not logged in." });

            int parsedId = int.Parse(userId);

            var userNotifs = _db.notifications
                .Where(n => n.user_id == parsedId && ids.Contains(n.notification_id))
                .ToList();

            foreach (var n in userNotifs)
            {
                n.notification_read_status = true;
            }

            _db.SaveChanges();

            return Json(new { success = true });
        }

        [HttpPost]
        public async Task<IActionResult> UpdateStatus(int applicationId, string newStatus)
        {
            // Include JobListing to get JobTitle
            var application = await _db.job_applications
                .Include(a => a.job_listing)
                .FirstOrDefaultAsync(a => a.application_id == applicationId);

            if (application == null)
                return NotFound();

            // Update status
            application.application_status = newStatus;
            application.date_updated = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            // Send notification using your service
            var message = $"Your application for '{application.job_listing.job_title}' has been {newStatus}.";
            await _notificationService.SendAsync(application.user_id, "Application Status Updated", message, "Application");

            return RedirectToAction("Details", new { id = applicationId });
        }








    }
}
