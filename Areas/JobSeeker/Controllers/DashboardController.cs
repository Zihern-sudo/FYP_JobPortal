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

            var vm = new ProfileViewModel
            {
                UserId = user.user_id,
                FirstName = user.first_name,
                LastName = user.last_name,
                Email = user.email,
                TwoFAEnabled = user.user_2FA, // assuming 1 = enabled
                Phone = user.phone,
                Address = user.address,
                Skills = user.skills,
                Education = user.education,
                WorkExperience = user.work_experience,
                notif_inapp = user.notif_inapp,
                notif_email = user.notif_email,
                notif_sms = user.notif_sms,
                notif_job_updates = user.notif_job_updates,
                notif_feedback = user.notif_feedback,
                notif_messages = user.notif_messages,
                notif_system = user.notif_system,
                notif_reminders = user.notif_reminders
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
            user.phone = vm.Phone;
            user.address = vm.Address;
            user.skills = vm.Skills;
            user.education = vm.Education;
            user.work_experience = vm.WorkExperience;
            user.notif_inapp = vm.notif_inapp;
            user.notif_email = vm.notif_email;
            user.notif_sms = vm.notif_sms;
            user.notif_job_updates = vm.notif_job_updates;
            user.notif_feedback = vm.notif_feedback;
            user.notif_messages = vm.notif_messages;
            user.notif_system = vm.notif_system;
            user.notif_reminders = vm.notif_reminders;

            await _db.SaveChangesAsync();

            ViewBag.Message = "Profile updated successfully!";
            return RedirectToAction("Settings");
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

            string? relativePath = null;

            // ✅ Retrieve user and job info
            var user = await _db.users.FindAsync(int.Parse(userId));
            var job = await _db.job_listings
                .Include(j => j.company)
                .FirstOrDefaultAsync(j => j.job_listing_id == model.JobId);

            if (user == null || job == null)
                return NotFound();

            // ✅ Upload folder path
            var uploadsDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "job_applications");
            if (!Directory.Exists(uploadsDir))
                Directory.CreateDirectory(uploadsDir);

            // ✅ Save resume if uploaded
            if (resumeFile != null && resumeFile.Length > 0)
            {
                // Example: EasonLowCheeGuan_TechCorp_UIUXDesigner_20251101_1916_Resume.pdf
                string safeFileName = $"{user.first_name}{user.last_name}_{job.company.company_name}_{job.job_title}_{DateTime.Now:yyyyMMdd_HHmm}_{Path.GetFileName(resumeFile.FileName)}";
                safeFileName = string.Concat(safeFileName.Split(Path.GetInvalidFileNameChars()));

                string fullPath = Path.Combine(uploadsDir, safeFileName);
                using (var stream = new FileStream(fullPath, FileMode.Create))
                {
                    await resumeFile.CopyToAsync(stream);
                }

                relativePath = $"/uploads/job_applications/{safeFileName}";
            }

            // ✅ Check if existing application exists (edit mode)
            var existingApp = await _db.job_applications
                .FirstOrDefaultAsync(a => a.application_id == model.ApplicationId && a.user_id == int.Parse(userId));

            if (existingApp != null)
            {
                // 🔹 Update existing application
                existingApp.resume_path = relativePath ?? existingApp.resume_path;
                existingApp.date_updated = DateTime.Now;
                existingApp.application_status = "Updated";

                _db.job_applications.Update(existingApp);
                TempData["Success"] = "Your application has been updated successfully!";
            }
            else
            {
                // 🆕 Create new application
                var application = new job_application
                {
                    user_id = int.Parse(userId),
                    job_listing_id = model.JobId,
                    application_status = "Submitted",
                    date_updated = DateTime.Now,
                    resume_path = relativePath
                };

                _db.job_applications.Add(application);
                TempData["Success"] = "Your application has been submitted successfully!";
            }

            await _db.SaveChangesAsync();
            return RedirectToAction("Applications", "Dashboard", new { area = "JobSeeker" });
        }

        public async Task<IActionResult> Applications(int page = 1)
        {
            var userId = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrEmpty(userId))
                return RedirectToAction("Login", "Account", new { area = "JobSeeker" });

            int id = int.Parse(userId);
            int pageSize = 10;

            var query = _db.job_applications
                .Include(a => a.job_listing)
                    .ThenInclude(j => j.company)
                .Where(a => a.user_id == id)
                .OrderByDescending(a => a.date_updated);

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
            return RedirectToAction("Thread", "Inbox", new { id = convo.conversation_id });
        }

        // ✅ Dynamic Job Listings with Pagination
        public async Task<IActionResult> JobListings(string? search, string? location, string? salaryRange, string? workMode, string? jobCategory, int page = 1)
        {
            int pageSize = 10;

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

            // 💰 Filter by Salary Range
            if (!string.IsNullOrEmpty(salaryRange))
            {
                var parts = salaryRange.Split('-');
                if (parts.Length == 2)
                {
                    int min = int.Parse(parts[0]);
                    int max = int.Parse(parts[1]);

                    // 🧭 Filter using only the minimum salary
                    jobsQuery = jobsQuery.Where(j => j.salary_min >= min && j.salary_min <= max);
                }
            }

            // 🏠 Work Mode
            if (!string.IsNullOrEmpty(workMode))
            {
                jobsQuery = jobsQuery.Where(j => j.work_mode == workMode);
            }

            // 💼 Job Category
            if (!string.IsNullOrEmpty(jobCategory))
            {
                jobsQuery = jobsQuery.Where(j => j.job_category == jobCategory);
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

            // 🗺️ Get all distinct locations for dropdown
            ViewBag.Locations = await _db.companies
                .Select(c => c.company_location)
                .Distinct()
                .OrderBy(c => c)
                .ToListAsync();

            return View(jobList);
        }




        // ✅ Optional: Job Details page
        public async Task<IActionResult> JobDetails(int id)
        {
            var job = await _db.job_listings
                .Include(j => j.company) // ✅ include company details
                .FirstOrDefaultAsync(j => j.job_listing_id == id);

            if (job == null)
                return NotFound();

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
        // GET: /JobSeeker/Dashboard/ResumeBuilder
        [HttpGet]
        public IActionResult ResumeBuilder()
        {
            // Optionally check if user is logged in
            var userIdStr = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrEmpty(userIdStr))
                return RedirectToAction("Login", "Account", new { area = "JobSeeker" });

            return View(); // Looks for Views/Dashboard/ResumeBuilder.cshtml
        }

        private byte[] GenerateResumePDF(string fullName, string email, string education, string experience, string skills)
        {
            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    // Page setup
                    page.Size(PageSizes.A4);
                    page.Margin(2, Unit.Centimetre);
                    page.Background(Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(12));

                    // Header
                    page.Header()
                        .Text("Resume")
                        .SemiBold()
                        .FontSize(24)
                        .FontColor(Colors.Blue.Medium)
                        .AlignCenter();

                    // Content
                    page.Content().PaddingVertical(10).Column(column =>
                    {
                        column.Spacing(10);

                        column.Item().Text($"Name: {fullName}").Bold();
                        column.Item().Text($"Email: {email}");
                        column.Item().Text($"Education: {education}");
                        column.Item().Text($"Experience: {experience}");
                        column.Item().Text($"Skills: {skills}");

                        // A simple line separator
                        column.Item().PaddingVertical(10)
                            .LineHorizontal(1)
                            .LineColor(Colors.Grey.Medium);

                        column.Item()
                            .Text("Generated by Job Seeker Portal")
                            .FontSize(10)
                            .Italic()
                            .FontColor(Colors.Grey.Darken2);
                    });

                    // Footer
                    page.Footer().AlignCenter().Text(x =>
                    {
                        x.Span("Page ");
                        x.CurrentPageNumber();
                        x.Span(" of ");
                        x.TotalPages();
                    });
                });
            });

            return document.GeneratePdf();
        }



        // POST: /JobSeeker/Dashboard/SaveResume
        [HttpPost]
        public async Task<IActionResult> SaveResume(string fullName, string email, string education, string experience, string skills)
        {
            var userIdStr = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrEmpty(userIdStr))
                return RedirectToAction("Login", "Account", new { area = "JobSeeker" });

            int userId = int.Parse(userIdStr);

            // 1️⃣ Generate PDF
            var pdfBytes = GenerateResumePDF(fullName, email, education, experience, skills);

            // 2️⃣ Save PDF to wwwroot/resumes
            var resumesDir = Path.Combine(_webHostEnvironment.WebRootPath, "resumes");
            if (!Directory.Exists(resumesDir))
                Directory.CreateDirectory(resumesDir);

            var fileName = $"resume_{Guid.NewGuid()}.pdf";
            var filePath = Path.Combine(resumesDir, fileName);
            await System.IO.File.WriteAllBytesAsync(filePath, pdfBytes);

            // 3️⃣ Save record in DB (optional — you can keep this if you want)
            var newResume = new resume
            {
                user_id = userId,
                upload_date = DateTime.Now,
                file_path = "/resumes/" + fileName
            };
            _db.resumes.Add(newResume);
            await _db.SaveChangesAsync();

            // 4️⃣ Return file directly for download
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
