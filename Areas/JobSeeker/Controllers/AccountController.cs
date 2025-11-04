using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using JobPortal.Areas.Shared.Models;
using System.Security.Cryptography;
using System.Text;
using JobPortal.Services;
using Microsoft.AspNetCore.Identity;

namespace JobPortal.Areas.JobSeeker.Controllers
{
    [Area("JobSeeker")]
    public class AccountController : Controller
    {
        private readonly AppDbContext _db;
        private readonly EmailService _emailService;

        public AccountController(AppDbContext db, EmailService emailService)
        {
            _db = db;
            _emailService = emailService;
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

            // ✅ Status checks first
            if (user.user_status == "Suspended")
            {
                TempData["Message"] = "Please verify your email before logging in.";
                return View("Login");
            }

            if (user.user_status == "Inactive")
            {
                TempData["Message"] = "Your account is inactive. Please contact support.";
                return View("Login");
            }

            // ✅ Restrict main login page to JobSeekers only
            if (user.user_role != "JobSeeker")
            {
                // Redirect to staff login page if admin/recruiter tries to use main login
                return RedirectToAction("StaffLogin");
            }

            // Existing password verification, 2FA, and session setup
            var hasher = new PasswordHasher<object>();
            var result = hasher.VerifyHashedPassword(null, user.password_hash, password);
            if (result == PasswordVerificationResult.Failed)
            {
                TempData["Message"] = "Invalid email or password.";
                ViewBag.Email = email;
                return View("Login");
            }

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
        // ✅ REGISTER (NEW)
        // ===============================
        [HttpPost]
        public async Task<IActionResult> Register(string name, string email, string password, string confirmPassword)
        {
            // Basic required field check
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(email) ||
                string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(confirmPassword))
            {
                TempData["Message"] = "All fields are required.";
                return RedirectToAction("Login", new { tab = "register" });
            }

            // ✅ Full name max 60 chars
            if (name.Length > 60)
            {
                TempData["Message"] = "Full Name cannot exceed 60 characters.";
                return RedirectToAction("Login", new { tab = "register" });
            }

            // ✅ Password max 20 chars and min 6 chars (recommended)
            if (password.Length > 20)
            {
                TempData["Message"] = "Password cannot exceed 20 characters.";
                return RedirectToAction("Login", new { tab = "register" });
            }

            if (password.Length < 6)
            {
                TempData["Message"] = "Password must be at least 6 characters long.";
                return RedirectToAction("Login", new { tab = "register" });
            }

            // ✅ Email format validation using System.ComponentModel.DataAnnotations
            var emailRegex = @"^[^@\s]+@[^@\s]+\.[^@\s]+$";
            if (!System.Text.RegularExpressions.Regex.IsMatch(email, emailRegex))
            {
                TempData["Message"] = "Please enter a valid email address.";
                return RedirectToAction("Login", new { tab = "register" });
            }

            if (password != confirmPassword)
            {
                TempData["Message"] = "Passwords do not match.";
                return RedirectToAction("Login");
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
                first_name = name,
                last_name = "",
                email = email,
                password_hash = hashedPassword, // store hash
                user_role = "JobSeeker",
                user_status = "Suspended", // unverified
                user_2FA = false,
                created_at = DateTime.Now
            };

            _db.users.Add(user);
            await _db.SaveChangesAsync();

            var verificationLink = Url.Action("VerifyEmail", "Account",
                new { area = "JobSeeker", email = user.email },
                Request.Scheme);

            string body = $@"
        <h2>Welcome to JobPortal!</h2>
        <p>Please verify your email by clicking the link below:</p>
        <a href='{verificationLink}' style='color:#007bff;'>Verify Email</a>";

            await _emailService.SendEmailAsync(user.email, "Verify Your JobPortal Account", body);

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
        public async Task<IActionResult> VerifyEmail(string email)
        {
            var user = await _db.users.FirstOrDefaultAsync(u => u.email == email);
            if (user == null)
            {
                TempData["Message"] = "Invalid verification link.";
                return RedirectToAction("Login");
            }

            user.user_status = "Active";
            await _db.SaveChangesAsync();

            TempData["Message"] = "Your email has been verified successfully!";
            return RedirectToAction("Login");
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
            if (string.IsNullOrEmpty(email))
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

            // Generate temporary reset token
            var token = Guid.NewGuid().ToString();
            HttpContext.Session.SetString("ResetToken", token);
            HttpContext.Session.SetString("ResetEmail", email);

            var resetLink = Url.Action("ResetPassword", "Account",
                new { area = "JobSeeker", email, token }, Request.Scheme);

            string body = $@"
        <h2>Password Reset Request</h2>
        <p>Click the link below to reset your password:</p>
        <a href='{resetLink}' style='color:#007bff;'>Reset My Password</a>";

            await _emailService.SendEmailAsync(email, "Reset Your JobPortal Password", body);

            TempData["Message"] = "A password reset link has been sent to your email.";
            return RedirectToAction("Login");
        }

        // ===============================
        // ✅ Reset Password (GET)
        // ===============================
        [HttpGet]
        public IActionResult ResetPassword(string email, string token)
        {
            var savedToken = HttpContext.Session.GetString("ResetToken");
            var savedEmail = HttpContext.Session.GetString("ResetEmail");

            if (token != savedToken || email != savedEmail)
            {
                TempData["Message"] = "Invalid or expired password reset link.";
                return RedirectToAction("Login");
            }

            ViewBag.Email = email;
            return View();
        }

        // ===============================
        // ✅ Reset Password (POST)
        // ===============================
        [HttpPost]
        public async Task<IActionResult> ResetPassword(string email, string newPassword, string confirmPassword)
        {
            if (newPassword != confirmPassword)
            {
                TempData["Message"] = "Passwords do not match.";
                ViewBag.Email = email;
                return View();
            }

            var user = await _db.users.FirstOrDefaultAsync(u => u.email == email);
            if (user == null)
            {
                TempData["Message"] = "User not found.";
                return RedirectToAction("Login");
            }

            // ✅ Hash the new password properly
            var hasher = new PasswordHasher<object>();
            user.password_hash = hasher.HashPassword(null, newPassword);

            await _db.SaveChangesAsync();

            TempData["Message"] = "Password reset successfully! You can now log in.";
            return RedirectToAction("Login");
        }


        [HttpGet]
        public IActionResult RecoverAccount()
        {
            return View("Recover");
        }

        [HttpPost]
        public async Task<IActionResult> RecoverAccount(string email)
        {
            var user = await _db.users.FirstOrDefaultAsync(u => u.email == email);
            if (user == null)
            {
                TempData["Message"] = "No account found with this email.";
                return RedirectToAction("Login");
            }

            if (user.user_status == "Inactive")
            {
                TempData["Message"] = "Your account is inactive. Please contact support.";
                return RedirectToAction("Login");
            }

            bool eligibleForRecovery =
                user.user_status == "Suspended" ||
                (user.user_status == "Active" && user.user_2FA);

            if (!eligibleForRecovery)
            {
                TempData["Message"] = "Your account is not locked. Try logging in.";
                return RedirectToAction("Login");
            }

            var token = Guid.NewGuid().ToString();
            HttpContext.Session.SetString("RecoverToken", token);
            HttpContext.Session.SetString("RecoverEmail", email);

            var link = Url.Action("UnlockAccount", "Account",
                new { area = "JobSeeker", email, token }, Request.Scheme);

            await _emailService.SendEmailAsync(email, "Recover Your JobPortal Account",
                $"Click here to recover your account: <a href='{link}'>Recover Account</a>");

            TempData["Message"] = "Recovery email sent. Please check your inbox.";
            return RedirectToAction("Login");
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
