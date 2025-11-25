using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using JobPortal.Areas.Shared.Models;
using System.Security.Cryptography;
using System.Text;
using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using JobPortal.Services;
using Microsoft.AspNetCore.Identity;
using JobPortal.Areas.JobSeeker.Models;
using System.Text.RegularExpressions;

namespace JobPortal.Areas.JobSeeker.Controllers
{
    [Area("JobSeeker")]
    public class AccountController : Controller
    {
        private readonly AppDbContext _db;
        private readonly EmailService _emailService;
        private readonly IConfiguration _config;

        public AccountController(AppDbContext db, EmailService emailService, IConfiguration config)
        {
            _db = db;
            _emailService = emailService;
            _config = config;
        }


        // GET: Login page
        [HttpGet]
        public IActionResult Login()
        {
            return View();
        }

        // POST: Handle login submission
        [HttpPost]
        public async Task<IActionResult> Login(string email, string password, string? twoFACode)
        {
            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
            {
                ViewBag.Error = "Please enter both email and password.";
                return View();
            }

            var user = await _db.users.FirstOrDefaultAsync(u => u.email == email);
            if (user == null)
            {
                TempData["Message"] = "Invalid email or password.";
                ViewBag.Email = email;
                return View("Login");
            }

            // Validate email format
            if (!Regex.IsMatch(email, @"^[^\s@]+@[^\s@]+\.[a-zA-Z]{2,}$"))
            {
                TempData["Message"] = "Please enter a valid email address.";
                ViewBag.Email = email;
                return View();
            }

            // ✅ Status checks first
            if (user.user_status == "Unverified")
            {
                TempData["Message"] = "Please verify your email before logging in.";
                return View("Login");
            }

            if (user.user_status == "Inactive")
            {
                TempData["Message"] = "Your account is inactive. Please contact support.";
                return View("Login");
            }
            if (user.user_status == "Suspended")
            {
                TempData["Message"] = "Your account has been suspended. Please contact support for assistance.";
                return View("Login");
            }


            // Restrict main login page to JobSeekers only
            if (user.user_role != "JobSeeker")
            {
                TempData["Message"] = "You must use Staff Login. You have been redirected.";
                return RedirectToAction("StaffLogin");
            }


            // -------------------------------
            // SAFE SESSION-BASED LOGIN LIMIT
            // -------------------------------
            // -------------------------------
            // DB-BACKED SUSPENSION (SAFE)
            // -------------------------------
            string lastLoginEmail = HttpContext.Session.GetString("LastLoginEmail");

            // Reset attempts when switching to a different email
            if (lastLoginEmail != email)
            {
                HttpContext.Session.SetInt32("FailedAttempts", 0);
                HttpContext.Session.SetString("LastLoginEmail", email);
            }

            // Load failed attempts from session
            int failedAttempts = HttpContext.Session.GetInt32("FailedAttempts") ?? 0;

            var hasher = new PasswordHasher<object>();
            var result = hasher.VerifyHashedPassword(null, user.password_hash, password);

            if (result == PasswordVerificationResult.Failed)
            {
                failedAttempts++;
                HttpContext.Session.SetInt32("FailedAttempts", failedAttempts);

                if (failedAttempts >= 5)
                {
                    // 🔥 Suspend properly in the database
                    user.user_status = "Suspended";
                    await _db.SaveChangesAsync();

                    TempData["Message"] = "Your account has been suspended due to too many failed attempts.";
                    return View("Login");
                }

                TempData["Message"] = $"Invalid email or password. Attempt {failedAttempts}/5.";
                ViewBag.Email = email;
                return View("Login");
            }

            // -------------------------------
            // SUCCESSFUL LOGIN → RESET COUNT
            // -------------------------------
            HttpContext.Session.SetInt32("FailedAttempts", 0);
            HttpContext.Session.SetString("LastLoginEmail", email);



            if (user.user_2FA)
            {
                if (string.IsNullOrEmpty(twoFACode))
                {
                    TempData["Require2FA"] = "true";
                    TempData["PendingEmail"] = user.email;
                    TempData["PendingPassword"] = password;
                    return RedirectToAction("Login");
                }

                if (string.IsNullOrEmpty(user.user_2FA_secret) ||
                    !TwoFactorService.VerifyCode(user.user_2FA_secret, twoFACode))
                {
                    TempData["Message"] = "Invalid 2FA code. Please try again.";
                    ViewBag.Email = email;
                    return View("Login");
                }
            }

            HttpContext.Session.SetString("UserId", user.user_id.ToString());
            HttpContext.Session.SetString("UserRole", user.user_role);
            HttpContext.Session.SetString("UserName", $"{user.first_name} {user.last_name}");
            HttpContext.Session.SetString("UserEmail", user.email);

            return RedirectToAction("Index", "Dashboard", new { area = "JobSeeker" });
        }

        // ===============================
        // REGISTER
        // ===============================
        [HttpPost]
        public async Task<IActionResult> Register(string firstName, string lastName, string email, string password, string confirmPassword, RegisterViewModel model)
        {
            if (!ModelState.IsValid)
            {
                // TempData message for SweetAlert
                TempData["Message"] = string.Join("\\n", ModelState.Values
                    .SelectMany(v => v.Errors)
                    .Select(e => e.ErrorMessage));
                ViewBag.ActiveTab = "register"; // activate the register tab
                return View("Login", model);    // render the Login view (which contains both tabs)
            }

            // Basic required field check
            if (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName) || string.IsNullOrWhiteSpace(email) ||
                string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(confirmPassword))
            {
                TempData["Message"] = "All fields are required.";
                return RedirectToAction("Login", new { tab = "register" });
            }

            // ✅ First & Last name max length validation
            if (firstName.Length > 40 || lastName.Length > 40)
            {
                TempData["Message"] = "First and Last Name cannot exceed 40 characters.";
                ModelState.AddModelError("", "Names may not exceed 40 characters");
                ViewBag.ActiveTab = "register";
                return View("Login", model);
            }

            // ✅ First & Last name content validation (letters and spaces only)
            var nameRegex = @"^[A-Za-z\s]+$";
            if (!System.Text.RegularExpressions.Regex.IsMatch(firstName, nameRegex))
            {
                TempData["Message"] = "First name can only contain letters and spaces.";
                ModelState.AddModelError("", "Invalid first name");
                ViewBag.ActiveTab = "register";
                return View("Login", model);
            }

            if (!System.Text.RegularExpressions.Regex.IsMatch(lastName, nameRegex))
            {
                TempData["Message"] = "Last name can only contain letters and spaces.";
                ModelState.AddModelError("", "Invalid last name");
                ViewBag.ActiveTab = "register";
                return View("Login", model);
            }

            // ✅ Email format validation using System.ComponentModel.DataAnnotations
            var emailRegex = @"^[^@\s]+@[^@\s]+\.[^@\s]+$";
            if (!System.Text.RegularExpressions.Regex.IsMatch(email, emailRegex))
            {
                TempData["Message"] = "Please enter a valid email address.";
                ModelState.AddModelError("", "Please enter a valid email address");
                ViewBag.ActiveTab = "register";
                return View("Login", model);
            }

            if (password != confirmPassword)
            {
                TempData["Message"] = "Passwords do not match.";
                ModelState.AddModelError("", "Passwords do not match");
                ViewBag.ActiveTab = "register";
                return View("Login", model);
            }
            var errors = new List<string>();

            // ✅ Detailed password validation
            var passwordErrors = new List<string>();

            if (password.Length < 6 || password.Length > 20)
                passwordErrors.Add("Password must be 6 to 20 characters long.");
            if (!System.Text.RegularExpressions.Regex.IsMatch(password, @"[A-Z]"))
                passwordErrors.Add("At least one uppercase letter required.");
            if (!System.Text.RegularExpressions.Regex.IsMatch(password, @"[a-z]"))
                passwordErrors.Add("At least one lowercase letter required.");
            if (!System.Text.RegularExpressions.Regex.IsMatch(password, @"\d"))
                passwordErrors.Add("At least one number required.");
            if (!System.Text.RegularExpressions.Regex.IsMatch(password, @"[@$!%*?&.,]"))
                passwordErrors.Add("At least one special character required.");

            // If there are any errors, show them
            if (passwordErrors.Count > 0)
            {
                TempData["Message"] = string.Join(" ", passwordErrors); // Shows all issues
                ModelState.AddModelError("", "Password must be strong");
                ViewBag.ActiveTab = "register";
                return View("Login", model);
            }

            var existingUser = await _db.users.FirstOrDefaultAsync(u => u.email == email);
            if (existingUser != null)
            {
                TempData["Message"] = "This email is already registered. Please log in or use a different email.";
                return RedirectToAction("Login");
            }

            // ✅ Hash password
            var hasher = new PasswordHasher<object>();
            var hashedPassword = hasher.HashPassword(null, password);

            var user = new user
            {
                first_name = firstName,
                last_name = lastName,
                email = email,
                password_hash = hashedPassword, // store hash
                user_role = "JobSeeker",
                user_status = "Unverified", // unverified
                user_2FA = false,
                created_at = DateTime.Now
            };

            _db.users.Add(user);
            await _db.SaveChangesAsync();

            // ✅ Generate verification token (optional but recommended)
            var token = Guid.NewGuid();
            var now = DateTime.UtcNow;

            var ev = new email_verification
            {
                email = user.email,
                token = token,
                purpose = "SeekerRegister",
                expires_at = now.AddMinutes(5),
                used = false,
                created_at = now
            };

            _db.email_verifications.Add(ev);
            await _db.SaveChangesAsync();

            // ✅ Properly build verification URL
            var verificationLink = Url.Action(
                "VerifyEmail",               // Action name
                "Account",                   // Controller name
                new { area = "JobSeeker", email = user.email, token = token.ToString() }, // Pass both email + token
                Request.Scheme
            );

            // ✅ Build email body using your helper
            // Your BuildVerifyEmailHtml expects (verifyUrl, logoCid)
            var htmlBody = BuildVerifyEmailHtml(verificationLink, null);
            var textBody = BuildVerifyEmailText(verificationLink);

            // ✅ Send email via SMTP
            await SendViaSmtpAsync(user.email, "Verify Your Joboria Account", htmlBody, textBody);

            // ✅ Feedback for user
            TempData["Message"] = "Registration successful! Please check your email to verify your account.";
            return RedirectToAction("Login");
        }

        [HttpGet]
        public IActionResult StaffLogin()
        {
            return View(); // Create StaffLogin.cshtml
        }

        [HttpPost]
        public async Task<IActionResult> StaffLogin(string email, string password, string? twoFACode)
        {
            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
            {
                ViewBag.Error = "Please enter both email and password.";
                return View();
            }

            var user = await _db.users.FirstOrDefaultAsync(u => u.email == email);
            if (user == null || (user.user_role != "Admin" && user.user_role != "Recruiter"))
            {
                TempData["Message"] = "Invalid staff login credentials.";
                ViewBag.Email = email;
                return View();
            }

            // ✅ Status checks
            if (user.user_status == "Unverified")
            {
                TempData["Message"] = "Please verify your email before logging in.";
                return View("StaffLogin");
            }

            if (user.user_status == "Inactive")
            {
                TempData["Message"] = "Your account is inactive. Please contact support.";
                return View("StaffLogin");
            }

            if (user.user_status == "Suspended")
            {
                TempData["Message"] = "Your account has been suspended. Please contact support for assistance.";
                return View("StaffLogin");
            }


            var hasher = new PasswordHasher<object>();
            var result = hasher.VerifyHashedPassword(null, user.password_hash, password);
            if (result == PasswordVerificationResult.Failed)
            {
                TempData["Message"] = "Invalid staff login credentials.";
                ViewBag.Email = email;
                return View();
            }

            // Optional: 2FA check
            if (user.user_2FA && !string.IsNullOrEmpty(user.user_2FA_secret))
            {
                if (string.IsNullOrEmpty(twoFACode) || !TwoFactorService.VerifyCode(user.user_2FA_secret, twoFACode))
                {
                    TempData["Message"] = "Invalid 2FA code.";
                    ViewBag.Email = email;
                    return View();
                }
            }

            HttpContext.Session.SetString("UserId", user.user_id.ToString());
            HttpContext.Session.SetString("UserRole", user.user_role);
            HttpContext.Session.SetString("UserName", $"{user.first_name} {user.last_name}");
            HttpContext.Session.SetString("UserEmail", user.email);

            return user.user_role switch
            {
                "Admin" => RedirectToAction("Index", "Dashboard", new { area = "Admin" }),
                "Recruiter" => RedirectToAction("Index", "Home", new { area = "Recruiter" }),
                _ => View("StaffLogin")
            };
        }


        [HttpGet]
        public async Task<IActionResult> VerifyEmail(string email, string token)
        {

            if (!Guid.TryParse(token, out var guidToken))
            {
                TempData["Error"] = "Invalid verification token format.";
                return RedirectToAction("Login");
            }

            var record = await _db.email_verifications
                .FirstOrDefaultAsync(v => v.email == email && v.token == guidToken && v.purpose == "SeekerRegister");

            if (record == null || record.used || record.expires_at < DateTime.UtcNow)
            {
                TempData["Error"] = "Invalid or expired verification link.";
                return RedirectToAction("Login");
            }

            // ✅ Mark as used
            record.used = true;

            // ✅ Activate the user
            var user = await _db.users.FirstOrDefaultAsync(u => u.email == email);
            if (user != null)
            {
                user.user_status = "Active";
                await _db.SaveChangesAsync();
            }

            TempData["Message"] = "Your email has been verified. You can now log in.";
            return RedirectToAction("Login");
        }



        // --- SMTP: sends HTML + plain text using appsettings.json -> "Email" section (Gmail) ---
        // UPDATED: optional inline LinkedResource (logo) support + from display renamed to Joboria.
        private async Task SendViaSmtpAsync(string toEmail, string subject, string htmlBody, string textBody, LinkedResource? inlineLogo = null)
        {
            var host = _config["Email:SmtpHost"];
            var portStr = _config["Email:SmtpPort"];
            var user = _config["Email:Username"];
            var pass = _config["Email:Password"];

            if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(pass))
                throw new InvalidOperationException("SMTP 'Email' settings are missing in configuration.");

            if (!int.TryParse(portStr, out var port)) port = 587;

            var fromDisplay = "Joboria Verification"; // branding
            var from = new MailAddress(user, fromDisplay, Encoding.UTF8);
            var to = new MailAddress(toEmail);

            using var msg = new MailMessage
            {
                From = from,
                Subject = subject,
                SubjectEncoding = Encoding.UTF8,
                BodyEncoding = Encoding.UTF8,
                HeadersEncoding = Encoding.UTF8
            };
            msg.To.Add(to);

            // Plain text (fallback for old clients)
            var plainView = AlternateView.CreateAlternateViewFromString(
                textBody, Encoding.UTF8, MediaTypeNames.Text.Plain);

            // HTML body (+ optional linked logo)
            var htmlView = AlternateView.CreateAlternateViewFromString(
                htmlBody, Encoding.UTF8, MediaTypeNames.Text.Html);

            if (inlineLogo != null)
            {
                htmlView.LinkedResources.Add(inlineLogo);
            }

            msg.AlternateViews.Add(plainView);
            msg.AlternateViews.Add(htmlView);
            msg.IsBodyHtml = true;

            using var client = new SmtpClient(host, port)
            {
                EnableSsl = true, // STARTTLS on 587
                Credentials = new NetworkCredential(user, pass),
                DeliveryMethod = SmtpDeliveryMethod.Network,
                Timeout = 20000
            };

            await client.SendMailAsync(msg);
        }

        private string BuildVerifyEmailHtml(string verifyUrl, string? logoCid)
        {
            var year = DateTime.UtcNow.Year;
            var safeUrl = System.Net.WebUtility.HtmlEncode(verifyUrl);
            var logoImg = string.IsNullOrWhiteSpace(logoCid)
                ? ""
                : $@"<img src=""cid:{logoCid}"" alt=""Joboria Logo"" width=""36"" height=""36"" 
              style=""display:block; border:0; outline:none; text-decoration:none; border-radius:6px;"" />";

            return $@"
<!DOCTYPE html>
<html lang=""en"">
<head>
  <meta charset=""utf-8"">
  <meta name=""viewport"" content=""width=device-width, initial-scale=1"">
  <title>Verify your Joboria Account</title>
  <style>
    body, table, td, a {{ -webkit-text-size-adjust:100%; -ms-text-size-adjust:100%; }}
    table, td {{ mso-table-lspace:0pt; mso-table-rspace:0pt; }}
    img {{ -ms-interpolation-mode:bicubic; }}
    body {{ margin:0; padding:0; width:100%!important; height:100%!important; background:#f6f8fb; }}
    a {{ color:#0b5fff; text-decoration:none; }}
    .btn {{ display:inline-block; padding:14px 22px; background:#0b5fff; color:#ffffff!important; border-radius:8px; font-weight:600; }}
    .text-muted {{ color:#7b8694; }}
  </style>
</head>
<body>
  <table role=""presentation"" width=""100%"" cellspacing=""0"" cellpadding=""0"" bgcolor=""#f6f8fb"">
    <tr>
      <td align=""center"" style=""padding:24px;"">
        <table role=""presentation"" width=""560"" cellspacing=""0"" cellpadding=""0"" bgcolor=""#ffffff""
               style=""border-radius:12px; box-shadow:0 2px 8px rgba(16,24,40,.06);"">
          <tr>
            <td style=""padding:18px 28px; background:#0b5fff; color:#ffffff; font-family:Segoe UI, Roboto, Arial, sans-serif;"">
              <table width=""100%"" cellspacing=""0"" cellpadding=""0"">
                <tr>
                  <td style=""width:44px;"">{logoImg}</td>
                  <td>
                    <div style=""font-size:18px; font-weight:700;"">Joboria</div>
                    <div style=""font-size:12px; opacity:.9;"">Job Seeker Email Verification</div>
                  </td>
                </tr>
              </table>
            </td>
          </tr>
          <tr>
            <td style=""padding:28px; font-family:Segoe UI, Roboto, Arial, sans-serif; color:#1f2937;"">
              <h1>Verify your email</h1>
              <p>Thanks for registering as a Job Seeker. Please confirm your email address by clicking the button below.</p>
              <p><a href=""{safeUrl}"" class=""btn"">Verify Email</a></p>
              <p class=""text-muted"">If the button doesn't work, paste this link into your browser:</p>
              <p><a href=""{safeUrl}"">{safeUrl}</a></p>
              <hr style=""border:none; border-top:1px solid #eceff3; margin:20px 0;""/>
              <p class=""text-muted"">If you didn’t register for Joboria, you can safely ignore this email.</p>
            </td>
          </tr>
          <tr>
            <td style=""padding:18px 28px; background:#f8fafc; color:#6b7280; font-size:12px;"">
              © {year} Joboria • This is an automated message, please do not reply.
            </td>
          </tr>
        </table>
        <div style=""font-family:Segoe UI, Roboto, Arial, sans-serif; font-size:11px; color:#8c96a5; padding-top:12px;"">
          Sent by Joboria • Kuala Lumpur, MY
        </div>
      </td>
    </tr>
  </table>
</body>
</html>";
        }

        private string BuildVerifyEmailText(string verifyUrl)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Joboria - Email Verification");
            sb.AppendLine();
            sb.AppendLine("Verify your email:");
            sb.AppendLine("Thanks for registering as a Job Seeker. Click the link below to confirm your email.");
            sb.AppendLine("This link expires in 5 minutes.");
            sb.AppendLine();
            sb.AppendLine(verifyUrl);
            sb.AppendLine();
            sb.AppendLine("If you didn’t request this, you can ignore this email.");
            return sb.ToString();
        }



        // ===============================
        // ✅ Forgot Password (GET)
        // ===============================
        [HttpGet]
        public IActionResult ForgotPassword()
        {
            return View();
        }

        // ===============================
        // ✅ Forgot Password (POST)
        // ===============================
        [HttpPost]
        public async Task<IActionResult> ForgotPassword(string email)
        {
            if (string.IsNullOrWhiteSpace(email))
            {
                TempData["Message"] = "Please enter your registered email.";
                return View();
            }

            var user = await _db.users.FirstOrDefaultAsync(u => u.email == email);
            if (user == null)
            {
                TempData["Message"] = "No account found with this email.";
                return View();
            }

            // ✅ Generate GUID token and store in email_verifications table
            var token = Guid.NewGuid();
            var now = DateTime.UtcNow;

            var record = new email_verification
            {
                email = user.email,
                token = token,
                purpose = "PasswordReset",
                expires_at = now.AddMinutes(5), // reset link valid for 5 mins
                used = false,
                created_at = now
            };

            _db.email_verifications.Add(record);
            await _db.SaveChangesAsync();

            // ✅ Build password reset URL
            var resetLink = Url.Action(
                "ResetPassword",         // action to show reset form
                "Account",                   // controller
                new { area = "JobSeeker", email = user.email, token = token.ToString() },
                Request.Scheme
            );

            // ✅ Build email body (HTML + plain text)
            var htmlBody = $@"
<p>Hello,</p>
<p>You requested to reset your Joboria password. Click the button below:</p>
<p><a href='{resetLink}' style='padding:10px 16px; background:#0b5fff; color:#fff; border-radius:6px;'>Reset Password</a></p>
<p>If the button doesn't work, paste this link into your browser: {resetLink}</p>
<p>If you didn't request a password reset, ignore this email.</p>";

            var textBody = $"Reset your Joboria password: {resetLink}\n\nIf you didn't request this, ignore this email.";

            // ✅ Send via your existing SMTP helper
            await SendViaSmtpAsync(user.email, "Reset Your Joboria Password", htmlBody, textBody);

            TempData["Message"] = "Password reset link sent! Please check your email.";
            return RedirectToAction("Login");
        }


        // ===============================
        // ✅ Reset Password (GET)
        // ===============================
        [HttpGet]
        public async Task<IActionResult> ResetPassword(string email, string token)
        {
            // 1️⃣ Validate token format
            if (!Guid.TryParse(token, out var guidToken))
            {
                TempData["Message"] = "Invalid password reset token format.";
                return RedirectToAction("Login");
            }

            // 2️⃣ Get the record for this token
            var record = await _db.email_verifications
                .FirstOrDefaultAsync(v =>
                    v.email == email &&
                    v.token == guidToken &&
                    v.purpose == "PasswordReset" &&
                    !v.used &&
                    v.expires_at > DateTime.UtcNow);

            // 3️⃣ If no matching valid record → invalid link
            if (record == null)
            {
                TempData["Message"] = "Invalid or expired password reset link.";
                return RedirectToAction("Login");
            }

            // 4️⃣ Ensure this is the NEWEST token generated
            var latestRecord = await _db.email_verifications
                .Where(v => v.email == email && v.purpose == "PasswordReset")
                .OrderByDescending(v => v.created_at)
                .FirstOrDefaultAsync();

            if (latestRecord == null || latestRecord.token != guidToken)
            {
                TempData["Message"] = "A newer password reset link has been issued. Please check your latest email.";
                return RedirectToAction("Login");
            }

            // 5️⃣ Valid & newest → allow reset
            ViewBag.Email = email;
            ViewBag.Token = token;
            return View();
        }



        // ===============================
        // ✅ Reset Password (POST)
        // ===============================
        [HttpPost]
        public async Task<IActionResult> ResetPassword(string email, string token, string newPassword, string confirmPassword)
        {
            // 1️⃣ Check required fields
            if (string.IsNullOrWhiteSpace(newPassword) || string.IsNullOrWhiteSpace(confirmPassword))
            {
                TempData["Message"] = "Both password fields are required.";
                ViewBag.Email = email;
                ViewBag.Token = token;
                return View();
            }

            // 2️⃣ Check if passwords match
            if (newPassword != confirmPassword)
            {
                TempData["Message"] = "Passwords do not match.";
                ViewBag.Email = email;
                ViewBag.Token = token;
                return View();
            }

            // 3️⃣ Password complexity checks (like registration)
            var passwordErrors = new List<string>();
            if (newPassword.Length < 6 || newPassword.Length > 20)
                passwordErrors.Add("Password must be 6 to 20 characters long.");
            if (!System.Text.RegularExpressions.Regex.IsMatch(newPassword, @"[A-Z]"))
                passwordErrors.Add("At least one uppercase letter required.");
            if (!System.Text.RegularExpressions.Regex.IsMatch(newPassword, @"[a-z]"))
                passwordErrors.Add("At least one lowercase letter required.");
            if (!System.Text.RegularExpressions.Regex.IsMatch(newPassword, @"\d"))
                passwordErrors.Add("At least one number required.");
            if (!System.Text.RegularExpressions.Regex.IsMatch(newPassword, @"[@$!%*?&.,]"))
                passwordErrors.Add("At least one special character required.");

            if (passwordErrors.Count > 0)
            {
                TempData["Message"] = string.Join(" ", passwordErrors);
                ViewBag.Email = email;
                ViewBag.Token = token;
                return View();
            }

            // 4️⃣ Validate token
            if (!Guid.TryParse(token, out var guidToken))
            {
                TempData["Message"] = "Invalid password reset token format.";
                return RedirectToAction("Login");
            }

            var record = await _db.email_verifications
                .FirstOrDefaultAsync(v =>
                    v.email == email &&
                    v.token == guidToken &&
                    v.purpose == "PasswordReset" &&
                    !v.used &&
                    v.expires_at > DateTime.UtcNow);

            if (record == null)
            {
                TempData["Message"] = "Invalid or expired password reset link.";
                return RedirectToAction("Login");
            }

            // 5️⃣ Find the user
            var user = await _db.users.FirstOrDefaultAsync(u => u.email == email);
            if (user == null)
            {
                TempData["Message"] = "User not found.";
                return RedirectToAction("Login");
            }

            // 6️⃣ Hash new password
            var hasher = new PasswordHasher<object>();
            if (hasher.VerifyHashedPassword(null, user.password_hash, newPassword) == PasswordVerificationResult.Success)
            {
                TempData["Message"] = "New password cannot be the same as your current password.";
                ViewBag.Email = email;
                ViewBag.Token = token;
                return View();
            }

            user.password_hash = hasher.HashPassword(null, newPassword);

            // ✅ Set user_status to Active
            user.user_status = "Active";

            // 7️⃣ Mark token as used
            record.used = true;
            await _db.SaveChangesAsync();

            TempData["Message"] = "Password reset successfully! You can now log in.";
            return RedirectToAction("Login");
        }

        [HttpGet]
        public IActionResult RecoverAccount()
        {
            return View("Recover"); // Make sure Recover.cshtml has a simple email input form
        }

        [HttpPost]
        public async Task<IActionResult> RecoverAccount(string email)
        {
            if (string.IsNullOrWhiteSpace(email))
            {
                TempData["Message"] = "Please enter your registered email.";
                return View("Recover");
            }

            var user = await _db.users.FirstOrDefaultAsync(u => u.email == email);
            if (user == null)
            {
                TempData["Message"] = "No account found with this email.";
                return View("Recover");
            }

            if (user.user_status == "Inactive")
            {
                TempData["Message"] = "Your account is inactive. Please contact support.";
                return RedirectToAction("Login");
            }

            // Eligible for recovery if suspended or 2FA-enabled
            bool eligibleForRecovery =
                user.user_status == "Suspended" ||
                (user.user_status == "Active" && user.user_2FA);

            if (!eligibleForRecovery)
            {
                TempData["Message"] = "Your account is not locked. Try logging in.";
                return RedirectToAction("Login");
            }

            // ✅ Create token in DB for recovery (same as password reset flow)
            var token = Guid.NewGuid();
            var expiry = DateTime.UtcNow.AddMinutes(5);

            var record = new email_verification
            {
                email = email,
                token = token,
                purpose = "PasswordReset",
                expires_at = expiry,
                used = false,
                created_at = DateTime.UtcNow
            };

            _db.email_verifications.Add(record);
            await _db.SaveChangesAsync();

            // ✅ Generate password reset link using ResetPasswordForm (GET)
            var resetLink = Url.Action("ResetPassword", "Account",
                new { area = "JobSeeker", email, token }, Request.Scheme);

            // ✅ Build HTML + Text email using the same helper as ForgotPassword
            var htmlBody = BuildPasswordResetHtml(resetLink, null);
            var textBody = BuildPasswordResetText(resetLink);

            await SendViaSmtpAsync(email, "Joboria Password Reset", htmlBody, textBody);

            TempData["Message"] = "A password reset email has been sent. Please check your inbox.";
            return RedirectToAction("Login");
        }

        private string BuildPasswordResetHtml(string resetUrl, string? logoCid)
        {
            var safeUrl = System.Net.WebUtility.HtmlEncode(resetUrl);
            var logoImg = string.IsNullOrWhiteSpace(logoCid)
                ? ""
                : $@"<img src=""cid:{logoCid}"" alt=""Joboria Logo"" width=""36"" height=""36"" />";
            return $@"
<p>Hello,</p>
<p>You requested to reset your Joboria password. Click the button below:</p>
<p><a href='{safeUrl}' style='padding:10px 16px; background:#0b5fff; color:#fff; border-radius:6px;'>Reset Password</a></p>
<p>If the button doesn't work, paste this link into your browser: {safeUrl}</p>
<p>If you didn't request a password reset, ignore this email.</p>";
        }

        private string BuildPasswordResetText(string resetUrl)
        {
            return $"Reset your Joboria password: {resetUrl}\n\nIf you didn't request this, ignore this email.";
        }


        [HttpGet]
        public async Task<IActionResult> UnlockAccount(string email, string token)
        {
            var storedToken = HttpContext.Session.GetString("RecoverToken");
            var storedEmail = HttpContext.Session.GetString("RecoverEmail");

            if (storedToken == null || storedEmail == null || storedToken != token || storedEmail != email)
            {
                TempData["Message"] = "Invalid or expired recovery link.";
                return RedirectToAction("Login");
            }

            var user = await _db.users.FirstOrDefaultAsync(u => u.email == email);
            if (user == null)
            {
                TempData["Message"] = "Account not found.";
                return RedirectToAction("Login");
            }

            // ✅ Generate a temporary password
            var tempPassword = GenerateTemporaryPassword();

            // ✅ Hash it securely using ASP.NET's built-in PasswordHasher
            var hasher = new PasswordHasher<object>();
            user.password_hash = hasher.HashPassword(null, tempPassword);

            // ✅ Disable 2FA and clear secret
            user.user_2FA = false;
            user.user_2FA_secret = null;

            // ✅ Reactivate the account if suspended
            if (user.user_status == "Suspended")
                user.user_status = "Active";

            await _db.SaveChangesAsync();

            await _emailService.SendEmailAsync(user.email,
                "Your Account Has Been Recovered",
                $"Your account has been recovered successfully.<br>" +
                $"Here is your temporary password: <b>{tempPassword}</b><br>" +
                $"Please log in and change it immediately.");

            // ✅ Clear session tokens
            HttpContext.Session.Remove("RecoverToken");
            HttpContext.Session.Remove("RecoverEmail");

            TempData["Message"] = "Your account has been recovered. Check your email for a temporary password.";
            return RedirectToAction("Login");
        }

        // 🔹 Utility: random secure temporary password generator
        private string GenerateTemporaryPassword(int length = 10)
        {
            const string validChars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789@#$_";
            var random = new Random();
            return new string(Enumerable.Repeat(validChars, length)
                .Select(s => s[random.Next(s.Length)]).ToArray());
        }

        // ✅ Logout
        [HttpGet]
        public IActionResult Logout()
        {
            HttpContext.Session.Clear();
            return RedirectToAction("Login");
        }
    }
}
