using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using JobPortal.Areas.Shared.Models;
using JobPortal.Areas.Recruiter.Models;

namespace JobPortal.Areas.Recruiter.Controllers
{
    [Area("Recruiter")]
    public class AccountController : Controller
    {
        private readonly AppDbContext _db;
        public AccountController(AppDbContext db) => _db = db;

        [HttpGet]
        public IActionResult Register()
        {
            return View(new RegisterVm());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(RegisterVm vm)
        {
            if (!ModelState.IsValid) return View(vm);

            var emailExists = await _db.users.AnyAsync(u => u.email == vm.email);
            if (emailExists)
            {
                ModelState.AddModelError(nameof(vm.email), "Email already registered.");
                return View(vm);
            }

            var row = new user
            {
                first_name = vm.first_name.Trim(),
                last_name = vm.last_name.Trim(),
                email = vm.email.Trim(),
                password_hash = Sha256(vm.password),
                user_role = "Recruiter",
                user_2FA = false,
                user_status = "Active"
            };

            _db.users.Add(row);
            await _db.SaveChangesAsync();

            // Minimal sign-in: store user id in session (matches TryGetUserId)
            HttpContext.Session.SetString("UserId", row.user_id.ToString());

            TempData["Message"] = "Welcome! Please complete your company profile.";
            return RedirectToAction("Manage", "Company", new { area = "Recruiter" });
        }

        // Why SHA-256: avoid storing plain text; simple, no external dep.
        private static string Sha256(string input)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }
}