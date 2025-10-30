using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using JobPortal.Areas.Shared.Models;
using System.Security.Cryptography;
using System.Text;
using JobPortal.Services;

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

            if (user.user_status == "Suspended")
            {
                TempData["Message"] = "Please verify your email before logging in.";
                ViewBag.Email = email;
                return View("Login");
            }

            // ⚠️ Ideally, hash passwords in production; for now plain match
            if (user.password_hash != password)
            {
                TempData["Message"] = "Invalid email or password.";
                ViewBag.Email = email;
                return View("Login");
            }

            // ✅ 2FA Verification (Database-based)
            if (user.user_2FA)
            {
                // Step 1: Request 2FA code if not provided
                if (string.IsNullOrEmpty(twoFACode))
                {
                    TempData["Require2FA"] = "true";
                    TempData["PendingEmail"] = user.email;
                    TempData["PendingPassword"] = password; // ✅ carry password for re-submission
                    return RedirectToAction("Login");
                }

                // Step 2: Validate 2FA secret and code
                if (string.IsNullOrEmpty(user.user_2FA_secret) ||
                    !TwoFactorService.VerifyCode(user.user_2FA_secret, twoFACode))
                {
                    TempData["Message"] = "Invalid 2FA code. Please try again.";
                    ViewBag.Email = email;
                    return View("Login");
                }
            }

            // ✅ Successful login — create session
            HttpContext.Session.SetString("UserId", user.user_id.ToString());
            HttpContext.Session.SetString("UserRole", user.user_role);
            HttpContext.Session.SetString("UserName", $"{user.first_name} {user.last_name}");
            HttpContext.Session.SetString("UserEmail", user.email);

            // Redirect by user role
            return user.user_role switch
            {
                "Admin" => RedirectToAction("Index", "Dashboard", new { area = "Admin" }),
                "Recruiter" => RedirectToAction("Index", "Home", new { area = "Recruiter" }),
                "JobSeeker" => RedirectToAction("Index", "Dashboard", new { area = "JobSeeker" }),
                _ => View("Login")
            };
        }


        // ===============================
        // ✅ REGISTER (NEW)
        // ===============================
        [HttpPost]
        public async Task<IActionResult> Register(string name, string email, string password, string confirmPassword)
        {
            if (password != confirmPassword)
            {
                TempData["Message"] = "Passwords do not match.";
                return RedirectToAction("Login");
            }

            // ✅ Check if email already exists
            var existingUser = await _db.users.FirstOrDefaultAsync(u => u.email == email);
            if (existingUser != null)
            {
                TempData["Message"] = "This email is already registered. Please log in or use a different email.";
                return RedirectToAction("Login");
            }

            // ✅ Create new user
            var user = new user
            {
                first_name = name,
                last_name = "",
                email = email,
                password_hash = password,
                user_role = "JobSeeker",
                user_status = "Suspended", // unverified
                user_2FA = false,
                created_at = DateTime.Now
            };

            _db.users.Add(user);
            await _db.SaveChangesAsync();

            // ✅ Generate verification link
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

            user.password_hash = newPassword; // (Hash later)
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

            if (user.user_status != "Suspended")
            {
                TempData["Message"] = "Your account is not locked. Try logging in.";
                return RedirectToAction("Login");
            }

            // ✅ Generate recovery link
            var token = Guid.NewGuid().ToString();
            HttpContext.Session.SetString("RecoverToken", token);
            HttpContext.Session.SetString("RecoverEmail", email);

            var link = Url.Action("UnlockAccount", "Account",
                new { area = "JobSeeker", email, token }, Request.Scheme);

            await _emailService.SendEmailAsync(email, "Recover Your JobPortal Account",
                $"Click here to unlock your account: <a href='{link}'>Recover Account</a>");

            TempData["Message"] = "Recovery email sent. Please check your inbox.";
            return RedirectToAction("Login");
        }

        [HttpGet]
        public async Task<IActionResult> UnlockAccount(string email, string token)
        {
            var savedToken = HttpContext.Session.GetString("RecoverToken");
            var savedEmail = HttpContext.Session.GetString("RecoverEmail");

            if (token != savedToken || email != savedEmail)
            {
                TempData["Message"] = "Invalid or expired recovery link.";
                return RedirectToAction("Login");
            }

            var user = await _db.users.FirstOrDefaultAsync(u => u.email == email);
            if (user != null)
            {
                user.user_status = "Active";
                await _db.SaveChangesAsync();
            }

            TempData["Message"] = "Your account has been unlocked. Please log in.";
            return RedirectToAction("Login");
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
