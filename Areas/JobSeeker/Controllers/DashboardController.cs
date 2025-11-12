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


namespace JobPortal.Areas.JobSeeker.Controllers
{
    [Area("JobSeeker")]
    public class DashboardController : Controller
    {
        private readonly AppDbContext _db;
        private readonly IWebHostEnvironment _webHostEnvironment;
        public DashboardController(AppDbContext db, IWebHostEnvironment webHostEnvironment)
        {
            _db = db;
            _webHostEnvironment = webHostEnvironment;
        }
        public async Task<IActionResult> Index(string? search)
        {
            // Fetch 4 most recent open jobs
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
                    j.company.company_industry.Contains(search));
            }

            var recentJobs = await recentJobsQuery
                .Select(j => new RecentJobViewModel
                {
                    JobId = j.job_listing_id,
                    JobTitle = j.job_title,
                    CompanyName = j.company.company_name,
                    Industry = j.company.company_industry
                })
                .ToListAsync();

            var model = new DashboardIndexViewModel
            {
                SearchKeyword = search,
                RecentJobs = recentJobs
            };

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
                    allow_email = false,
                    allow_inApp = false
                };
                _db.notification_preferences.Add(pref);
                await _db.SaveChangesAsync();
            }

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

                // now load from notification_preference table
                notif_email = pref.allow_email,
                notif_inapp = pref.allow_inApp,
                notif_job_updates = pref.notif_job_updates,
                notif_messages = pref.notif_messages,
                notif_reminders = pref.notif_reminders,

                ProfilePicturePath = string.IsNullOrEmpty(user.profile_picture)
        ? "/wwwroot/uploads/profile_pictures/test.png"
        : user.profile_picture
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

            // --- Handle profile picture upload ---
            if (vm.ProfileImage != null && vm.ProfileImage.Length > 0)
            {
                // Create folder if not exists
                var uploadDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "profile_pictures");
                if (!Directory.Exists(uploadDir))
                    Directory.CreateDirectory(uploadDir);

                // Validate size (2MB max)
                if (vm.ProfileImage.Length > 2 * 1024 * 1024)
                {
                    ModelState.AddModelError("ProfileImage", "File too large (max 2MB).");
                    return View(vm);
                }

                // Define standard file name
                var fileName = $"{user.first_name}_{user.last_name}_Icon.png";
                var filePath = Path.Combine(uploadDir, fileName);

                // Delete old profile image(s) for the user
                var oldFiles = Directory.GetFiles(uploadDir, $"{user.first_name}_{user.last_name}_Icon.*");
                foreach (var old in oldFiles)
                {
                    System.IO.File.Delete(old);
                }

                // Convert uploaded file to PNG and save
                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    using (var image = System.Drawing.Image.FromStream(vm.ProfileImage.OpenReadStream()))
                    {
                        image.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                    }
                }

                // Save standardized PNG path to DB
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
            pref.allow_email = vm.notif_email;
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
                return Unauthorized();

            var user = await _db.users.FindAsync(int.Parse(userId));
            if (user == null)
                return NotFound();

            // ✅ Use PasswordHasher to verify the current password
            var hasher = new PasswordHasher<object>();
            var verificationResult = hasher.VerifyHashedPassword(null, user.password_hash, currentPassword);

            if (verificationResult == PasswordVerificationResult.Failed)
                return BadRequest("Current password is incorrect.");

            if (newPassword != confirmPassword)
                return BadRequest("New password and confirmation do not match.");

            // ✅ Hash and update new password
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

        [HttpPost]
        public async Task<IActionResult> UploadResume(IFormFile resumeFile)
        {
            var userId = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrEmpty(userId))
                return RedirectToAction("Login", "Account", new { area = "JobSeeker" });

            if (resumeFile == null || resumeFile.Length == 0)
            {
                TempData["Message"] = "Please select a file to upload.";
                return RedirectToAction("Settings", "Dashboard", new { area = "JobSeeker" });
            }

            var uploadsDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "user_resumes");
            if (!Directory.Exists(uploadsDir))
                Directory.CreateDirectory(uploadsDir);

            var user = await _db.users.FindAsync(int.Parse(userId));
            string safeFileName = $"{user.first_name}_{DateTime.Now:yyyyMMdd_HHmm}{Path.GetExtension(resumeFile.FileName)}";
            safeFileName = string.Concat(safeFileName.Split(Path.GetInvalidFileNameChars()));

            string fullPath = Path.Combine(uploadsDir, safeFileName);
            using (var stream = new FileStream(fullPath, FileMode.Create))
            {
                await resumeFile.CopyToAsync(stream);
            }

            var relativePath = $"/uploads/user_resumes/{safeFileName}";

            var resume = new resume
            {
                user_id = int.Parse(userId),
                upload_date = DateTime.Now,
                file_path = relativePath
            };

            _db.resumes.Add(resume);
            await _db.SaveChangesAsync();

            TempData["Success"] = "Resume uploaded successfully!";
            return RedirectToAction("Settings", "Dashboard", new { area = "JobSeeker" });
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
            var userResumes = await _db.resumes
                .Where(r => r.user_id == int.Parse(userId))
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

        //Submit Application
[HttpPost]
[ValidateAntiForgeryToken]
public async Task<IActionResult> SubmitApplication(ApplyViewModel model)
{
    if (!ModelState.IsValid)
    {
        // Re-load resumes in case of validation errors
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

    string? relativePath = null;

    // ✅ CASE 1: Existing resume selected
    if (model.SelectedResumeId.HasValue)
    {
        var existingResume = await _db.resumes.FindAsync(model.SelectedResumeId.Value);
        if (existingResume != null)
            relativePath = existingResume.file_path;
    }

    // ✅ CASE 2: New file uploaded
    if (model.ResumeFile != null && model.ResumeFile.Length > 0)
    {
        var uploadsDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "user_resumes");
        Directory.CreateDirectory(uploadsDir);

        string safeFileName = $"{userId}_{DateTime.Now:yyyyMMdd_HHmm}_{Path.GetFileName(model.ResumeFile.FileName)}";
        safeFileName = string.Concat(safeFileName.Split(Path.GetInvalidFileNameChars()));

        string fullPath = Path.Combine(uploadsDir, safeFileName);
        using (var stream = new FileStream(fullPath, FileMode.Create))
        {
            await model.ResumeFile.CopyToAsync(stream);
        }

        relativePath = $"/uploads/user_resumes/{safeFileName}";

        // save new resume
        var newResume = new resume
        {
            user_id = userId,
            upload_date = DateTime.Now,
            file_path = relativePath
        };
        _db.resumes.Add(newResume);
        await _db.SaveChangesAsync();
    }

    // ✅ Create new job application
    var application = new job_application
    {
        user_id = userId,
        job_listing_id = model.JobId,
        application_status = "Submitted",
        date_updated = DateTime.Now,
        resume_path = relativePath
    };

    _db.job_applications.Add(application);
    await _db.SaveChangesAsync();

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
                ApplicationsInReview = await _db.job_applications.CountAsync(a => a.user_id == id && a.application_status == "In Review"),
                InterviewsScheduled = await _db.job_applications.CountAsync(a => a.user_id == id && a.application_status == "Interview"),
                RecentActivities = applications.Take(5).Select(a => new RecentActivityViewModel
                {
                    Message = $"Updated application for {a.job_listing.job_title} ({a.application_status})",
                    Date = a.date_updated
                }).ToList(),
                RecentNotifications = new List<RecentNotificationViewModel>
        {
            new RecentNotificationViewModel { Message = "Keep track of your applications here!", Date = DateTime.Now }
        }
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

            // 📍 Filter by Location
            if (!string.IsNullOrEmpty(location))
            {
                jobsQuery = jobsQuery.Where(j => j.company.company_location == location);
            }

            // 🧭 Filter by salary range
            if (minSalary.HasValue && maxSalary.HasValue)
            {
                jobsQuery = jobsQuery.Where(j => j.salary_min >= minSalary.Value && j.salary_min <= maxSalary.Value);
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
                .OrderByDescending(j => j.date_posted)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

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
                                .Where(j => favJobIds.Contains(j.job_listing_id))
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
            string summary, string education, string experience, string skills, string certifications)
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
                        // 👤 Profile Picture (optional)
                        if (profileImage != null && profileImage.Length > 0)
                        {
                            column.Item().AlignCenter().Width(80).Height(80).Element(e =>
            {
                e.Image(profileImage, ImageScaling.FitArea);
            });
                        }

                        // 🧑 Full Name
                        column.Item().Text(fullName)
                            .Bold()
                            .FontSize(22)
                            .FontColor(Colors.Blue.Medium)
                            .AlignCenter();
                    });

                    page.Content().PaddingVertical(10).Column(column =>
                    {
                        column.Spacing(10);

                        // 📧 Contact Info
                        column.Item().Text($"Email: {email}");
                        if (!string.IsNullOrWhiteSpace(phone))
                            column.Item().Text($"Phone: {phone}");
                        if (!string.IsNullOrWhiteSpace(address))
                            column.Item().Text($"Address: {address}");

                        column.Item().PaddingVertical(5).LineHorizontal(1).LineColor(Colors.Grey.Medium);

                        // 🧾 Summary
                        if (!string.IsNullOrWhiteSpace(summary))
                        {
                            column.Item().Text("Professional Summary:").Bold();
                            column.Item().Text(summary);
                        }

                        // 🎓 Education
                        if (!string.IsNullOrWhiteSpace(education))
                        {
                            column.Item().Text("Education:").Bold();
                            column.Item().Text(education);
                        }

                        // 💼 Experience
                        if (!string.IsNullOrWhiteSpace(experience))
                        {
                            column.Item().Text("Experience:").Bold();

                            // Split experiences by newline and add each as a separate item
                            foreach (var exp in experience.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
                            {
                                column.Item().Text(exp.Trim());
                            }
                        }

                        // 🧠 Skills
                        if (!string.IsNullOrWhiteSpace(skills))
                        {
                            column.Item().Text("Skills:").Bold();
                            column.Item().Text(skills);
                        }

                        // 🏅 Certifications
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
      string summary, string education, string experience, string skills, string certifications)
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
                            // 📸 Profile Image
                            if (profileImage != null && profileImage.Length > 0)
                            {
                                left.Item().AlignCenter().Width(120).Height(120).Element(e =>
                                {
                                    e.Image(profileImage, ImageScaling.FitArea);
                                });
                                left.Item().PaddingVertical(10);
                            }

                            // 🧑 Name & Contact Info
                            left.Item().AlignCenter().Text(fullName)
                                .Bold().FontSize(18).FontColor(Colors.Blue.Medium);
                            if (!string.IsNullOrEmpty(email)) left.Item().AlignCenter().Text(email);
                            if (!string.IsNullOrEmpty(phone)) left.Item().AlignCenter().Text(phone);
                            if (!string.IsNullOrEmpty(address))
                                left.Item().AlignCenter().Text(address).WrapAnywhere();

                            // Divider
                            left.Item().PaddingVertical(10).LineHorizontal(1).LineColor(Colors.Grey.Medium);

                            // 🧠 Skills Section
                            if (!string.IsNullOrEmpty(skills))
                            {
                                left.Item().Text("💡 Skills").Bold().FontSize(12).FontColor(Colors.Black);
                                foreach (var skill in skills.Split(new[] { '\n', ',' }, StringSplitOptions.RemoveEmptyEntries))
                                    left.Item().PaddingLeft(10).Text("• " + skill.Trim());
                            }

                            // 🏅 Certifications
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
                            // 🎯 Professional Summary
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
                        });
                    });
                });
            });

            return document.GeneratePdf();
        }



        // POST: /JobSeeker/Dashboard/SaveResume
        [HttpPost]
        public async Task<IActionResult> SaveResume(IFormFile? profilePic, string fullName, string email, string phone, string address,
            string summary, string education, string experience, string skills, string certifications, string template)
        {
            var userIdStr = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrEmpty(userIdStr))
                return RedirectToAction("Login", "Account", new { area = "JobSeeker" });

            int userId = int.Parse(userIdStr);

            // 🖼 Convert image to byte array (if uploaded)
            byte[]? imageBytes = null;
            if (profilePic != null && profilePic.Length > 0)
            {
                using var ms = new MemoryStream();
                await profilePic.CopyToAsync(ms);
                imageBytes = ms.ToArray();
            }

            // 1️⃣ Generate PDF with the image
            var pdfBytes = template switch
            {
                "modern" => GenerateModernResumePDF(imageBytes, fullName, email, phone, address, summary, education, experience, skills, certifications),
                "classic" => GenerateClassicResumePDF(imageBytes, fullName, email, phone, address, summary, education, experience, skills, certifications),
            };
            // 2️⃣ Save PDF to wwwroot/resumes
            var resumesDir = Path.Combine(_webHostEnvironment.WebRootPath, "resumes");
            if (!Directory.Exists(resumesDir))
                Directory.CreateDirectory(resumesDir);

            // ✅ New file naming: FirstName + timestamp
            var user = await _db.users.FindAsync(userId);
            string fileName = $"{user.first_name}_{DateTime.Now:yyyyMMdd_HHmm}.pdf";
            string filePath = Path.Combine(resumesDir, fileName);

            await System.IO.File.WriteAllBytesAsync(filePath, pdfBytes);

            // 3️⃣ Save DB record
            var newResume = new resume
            {
                user_id = userId,
                upload_date = DateTime.Now,
                file_path = "/resumes/" + fileName
            };
            _db.resumes.Add(newResume);
            await _db.SaveChangesAsync();

            // 4️⃣ Return file for download
            return File(pdfBytes, "application/pdf", fileName);
        }

        public IActionResult GetRecentNotifications()
        {
            var userId = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrEmpty(userId))
                return Json(new { success = false, message = "User not logged in." });

            int parsedId = int.Parse(userId);

            var notifications = _db.notifications
                .Where(n => n.user_id == parsedId && !n.notification_read_status)
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

            return Json(new { success = true, data = notifications });
        }

        public IActionResult Notifications()
        {
            var userId = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrEmpty(userId))
                return RedirectToAction("Login", "Account", new { area = "JobSeeker" });

            int parsedId = int.Parse(userId);

            var notifications = _db.notifications
                .Where(n => n.user_id == parsedId)
                .OrderByDescending(n => n.notification_date_created)
                .ToList();

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







    }
}
