using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using JobPortal.Areas.Public.Models;
using System.Net;
using System.Net.Mail;
using System.Threading.Tasks;
using JobPortal.Areas.Shared.Models;
using Microsoft.EntityFrameworkCore;


namespace JobPortal.Areas.Public.Controllers
{
    [Area("Public")]
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _log;
        private readonly IConfiguration _config;
        private readonly AppDbContext _db;


        public HomeController(ILogger<HomeController> log, IConfiguration config, AppDbContext db)
        {
            _log = log;
            _config = config;
            _db = db;
        }

        // GET: /Public/Home/Index
        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var categoryCounts = await _db.job_listings
                .GroupBy(j => j.job_category)
                .Select(g => new
                {
                    Category = g.Key,
                    Count = g.Count()
                })
                .ToDictionaryAsync(g => g.Category, g => g.Count);

            ViewBag.CategoryCounts = categoryCounts;

            return View();
        }

        // GET: /Public/Home/About
        [HttpGet]
        public IActionResult About() => View();

        // GET: /Public/Home/Category
        [HttpGet]
        public IActionResult Category() => View();

        // GET: /Public/Home/Testimonial
        [HttpGet]
        public IActionResult Testimonial() => View();

        // GET: /Public/Home/Contact
        [HttpGet]
        public IActionResult Contact() => View(new ContactFormVm());

        // POST: /Public/Home/Contact
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Contact(ContactFormVm vm)
        {
            if (!ModelState.IsValid)
                return View(vm);

            // simple honeypot
            if (!string.IsNullOrWhiteSpace(vm.Hp))
            {
                TempData["ContactOk"] = "Thanks for your message — we’ll be in touch soon.";
                ModelState.Clear();
                return View(new ContactFormVm());
            }

            // Read SMTP settings from appsettings.json ("Email": { ... })
            var host = _config["Email:SmtpHost"];
            var port = _config.GetValue<int?>("Email:SmtpPort") ?? 587;
            var username = _config["Email:Username"];
            var password = _config["Email:Password"];

            // Use the configured username as the recipient (admin inbox)
            var adminTo = username;

            try
            {
                using var msg = new MailMessage
                {
                    From = new MailAddress(username!, "Joboria"),
                    Subject = $"[Contact] {vm.Subject}",
                    IsBodyHtml = true,
                    Body =
                        $"<div style='font-family:Segoe UI,Arial,sans-serif;font-size:14px'>" +
                        $"<p><strong>From:</strong> {WebUtility.HtmlEncode(vm.Name)} &lt;{WebUtility.HtmlEncode(vm.Email)}&gt;</p>" +
                        $"<p><strong>Subject:</strong> {WebUtility.HtmlEncode(vm.Subject)}</p>" +
                        $"<p><strong>Message:</strong></p>" +
                        $"<div style='white-space:pre-wrap'>{WebUtility.HtmlEncode(vm.Message)}</div>" +
                        $"<hr style='margin-top:20px'/>" +
                        $"<p style='color:#666'>Sent from JobPortal Public → Contact</p>" +
                        $"</div>"
                };

                msg.To.Add(new MailAddress(adminTo!));
                msg.ReplyToList.Add(new MailAddress(vm.Email, vm.Name));

                using var smtp = new SmtpClient(host, port)
                {
                    EnableSsl = true,
                    Credentials = new NetworkCredential(username, password)
                };

                await smtp.SendMailAsync(msg);

                TempData["ContactOk"] = "Thanks for your message — we’ll be in touch soon.";
                _log.LogInformation("Contact form sent successfully from {FromEmail}", vm.Email);

                ModelState.Clear();
                return View(new ContactFormVm());
            }
            catch (SmtpException ex)
            {
                // Do NOT log credentials or config values
                _log.LogError(ex, "Failed to send contact form email from {FromEmail}", vm.Email);
                ModelState.AddModelError(string.Empty, "We couldn't send your message right now. Please try again later.");
                return View(vm);
            }
        }

        // GET: /Public/Home/Error404
        [HttpGet]
        public IActionResult Error404()
        {
            Response.StatusCode = 404;
            return View("~/Areas/Public/Views/Shared/_404.cshtml");
        }
    }
}
