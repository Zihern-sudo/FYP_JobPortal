// ==========================================
// File: Areas/Recruiter/Controllers/AccountController.cs
// ==========================================
using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using JobPortal.Areas.Shared.Models;
using JobPortal.Areas.Recruiter.Models;
using System.Net.Http;
using System.Text.Json;
using System.IO; // <-- required for Path/Directory
using JobPortal.Services;               // <-- INotificationService

namespace JobPortal.Areas.Recruiter.Controllers
{
  [Area("Recruiter")]
  public class AccountController : Controller
  {
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;
    private readonly ILogger<AccountController> _log;
    private readonly INotificationService _notif;   // <-- inject service

    public AccountController(AppDbContext db, IConfiguration config, ILogger<AccountController> log, INotificationService notif)
    {
      _db = db;
      _config = config;
      _log = log;
      _notif = notif;                                // <-- assign
    }

    [HttpGet, AllowAnonymous]
    public IActionResult Register()
    {
      ViewBag.VerifiedEmail = HttpContext.Session.GetString("RecruiterRegisterVerifiedEmail");

      if (TempData["VerifyError"] is string e && !string.IsNullOrWhiteSpace(e))
        ViewBag.VerifyError = e;

      return View(new RegisterVm());
    }

    [HttpPost, ValidateAntiForgeryToken, AllowAnonymous]
    public async Task<IActionResult> Register(RegisterVm vm)
    {
      if (!ModelState.IsValid) return View(vm);

      // >>> ADDED: reCAPTCHA server-side verification <<<
      var captchaResponse = vm.RecaptchaToken ?? Request.Form["g-recaptcha-response"].ToString();
      var secret = _config["GoogleReCaptcha:SecretKey"];

      if (string.IsNullOrWhiteSpace(captchaResponse) || string.IsNullOrWhiteSpace(secret))
      {
        ModelState.AddModelError(nameof(vm.RecaptchaToken), "Please complete the reCAPTCHA challenge.");
        return View(vm);
      }

      bool captchaOk;
      try
      {
        captchaOk = await VerifyRecaptchaAsync(secret, captchaResponse, HttpContext.Connection.RemoteIpAddress?.ToString());
      }
      catch (Exception ex)
      {
        // Why: treat network/Google errors as validation failure for safety.
        _log.LogWarning(ex, "reCAPTCHA verification failed due to exception.");
        captchaOk = false;
      }

      if (!captchaOk)
      {
        ModelState.AddModelError(nameof(vm.RecaptchaToken), "reCAPTCHA validation failed. Please try again.");
        return View(vm);
      }
      // <<< END reCAPTCHA >>>

      var verifiedEmail = HttpContext.Session.GetString("RecruiterRegisterVerifiedEmail");
      if (!string.Equals(verifiedEmail, vm.email, StringComparison.OrdinalIgnoreCase))
      {
        ModelState.AddModelError(nameof(vm.email), "Please verify your email before creating an account.");
        return View(vm);
      }

      var emailExists = await _db.users.AnyAsync(u => u.email == vm.email);
      if (emailExists)
      {
        ModelState.AddModelError(nameof(vm.email), "Email already registered.");
        return View(vm);
      }

      var hasher = new Microsoft.AspNetCore.Identity.PasswordHasher<object>();
      var hashedPassword = hasher.HashPassword(null, vm.password);

      var u = new user
      {
        first_name = vm.first_name?.Trim(),
        last_name = vm.last_name?.Trim(),
        phone = vm.phone?.Trim(),
        email = vm.email?.Trim(),
        password_hash = hashedPassword,
        user_role = "Recruiter",
        user_2FA = false,
        user_status = "Active"
      };

      _db.users.Add(u);
      await _db.SaveChangesAsync();

      // ----- NEW: notify Admins that a recruiter registered -----
      try
      {
        var fullName = $"{u.first_name} {u.last_name}".Trim();
        await _notif.SendToAdminsAsync(
          "New recruiter registered",
          $"{fullName} ({u.email}) has registered as a recruiter. Their company profile will be submitted and is pending approval.",
          type: "System"
        );
      }
      catch (Exception ex)
      {
        // Do not block sign-up; just log.
        _log.LogWarning(ex, "Failed to send admin notification for recruiter registration (user_id {UserId})", u.user_id);
      }
      // ----------------------------------------------------------

      HttpContext.Session.Remove("RecruiterRegisterVerifiedEmail");
      HttpContext.Session.SetString("UserId", u.user_id.ToString());
      HttpContext.Session.SetString("UserRole", u.user_role);

      TempData["Message"] = "Welcome! Please complete your company profile.";
      return RedirectToAction("Setup", "Company", new { area = "Recruiter" });
    }


    // >>> ADDED: helper for Google siteverify call <<<
    private static readonly HttpClient _http = new HttpClient();

    private sealed class RecaptchaResult
    {
      public bool success { get; set; }
      public decimal score { get; set; } // v3 only; harmless here
      public string action { get; set; } = "";
      public DateTime challenge_ts { get; set; }
      public string hostname { get; set; } = "";
      public string[]? error_codes { get; set; }
    }

    private async Task<bool> VerifyRecaptchaAsync(string secret, string response, string? remoteIp)
    {
      // v2 checkbox: success true/false.
      var content = new FormUrlEncodedContent(new[]
      {
                new KeyValuePair<string,string>("secret", secret),
                new KeyValuePair<string,string>("response", response),
                // remoteip is optional
                new KeyValuePair<string,string>("remoteip", remoteIp ?? string.Empty),
            });

      using var req = new HttpRequestMessage(HttpMethod.Post, "https://www.google.com/recaptcha/api/siteverify")
      {
        Content = content
      };

      using var res = await _http.SendAsync(req);
      if (!res.IsSuccessStatusCode) return false;

      var json = await res.Content.ReadAsStringAsync();
      var parsed = JsonSerializer.Deserialize<RecaptchaResult>(json, new JsonSerializerOptions
      {
        PropertyNameCaseInsensitive = true
      });

      return parsed?.success == true;
    }

    // ---- AJAX: issue verification email via Gmail SMTP (uses "Email" section) ----
    [HttpPost, ValidateAntiForgeryToken, AllowAnonymous]
    public async Task<IActionResult> SendVerificationEmail([FromForm] string email)
    {
      try
      {
        if (string.IsNullOrWhiteSpace(email) || !new EmailAddressAttribute().IsValid(email))
          return Json(new { ok = false, message = "Email not found." });

        // Prevent confusion if email is already taken
        if (await _db.users.AnyAsync(u => u.email == email))
          return Json(new { ok = false, message = "Email already registered." });

        var now = DateTime.UtcNow;

        // Invalidate any existing active tokens for this email/purpose
        await _db.email_verifications
            .Where(ev => ev.email == email && ev.purpose == "RecruiterRegister" && ev.used == false)
            .ExecuteUpdateAsync(s => s.SetProperty(ev => ev.used, true));

        // Create fresh token (5 minutes)
        var token = Guid.NewGuid();
        var evrow = new email_verification
        {
          email = email,
          token = token,
          purpose = "RecruiterRegister",
          expires_at = now.AddMinutes(5),
          used = false,
          created_at = now
        };
        _db.email_verifications.Add(evrow);
        await _db.SaveChangesAsync();

        // Callback URL
        var callbackUrl = Url.Action(
            "VerifyEmail",
            "Account",
            new { area = "Recruiter", token, email },
            protocol: Request.Scheme);

        var subject = "Verify your email for Recruiter registration";

        // Prepare inline logo (wwwroot/img/FYP_Logo.jpg). If found, embed as LinkedResource.
        string logoPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "img", "FYP_Logo.jpg");
        LinkedResource? inlineLogo = null;
        string? logoCid = null;

        if (System.IO.File.Exists(logoPath))
        {
          logoCid = Guid.NewGuid().ToString("N");
          var lr = new LinkedResource(logoPath, MediaTypeNames.Image.Jpeg)
          {
            ContentId = logoCid,
            TransferEncoding = System.Net.Mime.TransferEncoding.Base64
          };
          // Some clients need this:
          lr.ContentType.MediaType = MediaTypeNames.Image.Jpeg;
          lr.ContentType.Name = "logo.jpg";
          inlineLogo = lr;
        }

        // HTML & Text bodies (HTML will reference cid: if logoCid not null)
        var html = BuildVerifyEmailHtml(callbackUrl, logoCid);
        var text = BuildVerifyEmailText(callbackUrl);

        await SendViaSmtpAsync(email, subject, html, text, inlineLogo);

        return Json(new { ok = true, message = "Verification email sent. Please check your inbox." });
      }
      catch (Exception ex)
      {
        _log.LogError(ex, "Failed to send verification email to {Email}", email);
        return Json(new { ok = false, message = "Email not found." });
      }
    }

    [HttpGet, AllowAnonymous]
    public async Task<IActionResult> VerifyEmail([FromQuery] Guid token, [FromQuery] string email)
    {
      var now = DateTime.UtcNow;

      if (await _db.users.AnyAsync(u => u.email == email))
      {
        TempData["VerifyError"] = "Email already registered.";
        return RedirectToAction(nameof(Register));
      }

      var row = await _db.email_verifications
          .FirstOrDefaultAsync(ev =>
              ev.email == email &&
              ev.purpose == "RecruiterRegister" &&
              ev.token == token &&
              ev.used == false &&
              ev.expires_at > now);

      if (row == null)
      {
        TempData["VerifyError"] = "Verification link is invalid or expired.";
        return RedirectToAction(nameof(Register));
      }

      row.used = true;
      await _db.SaveChangesAsync();

      HttpContext.Session.SetString("RecruiterRegisterVerifiedEmail", email);
      TempData["VerifiedEmail"] = email;

      return RedirectToAction(nameof(Register));
    }

    // --- SMTP (unchanged) ---
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

      var plainView = AlternateView.CreateAlternateViewFromString(
          textBody, Encoding.UTF8, MediaTypeNames.Text.Plain);

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
        EnableSsl = true,
        Credentials = new NetworkCredential(user, pass),
        DeliveryMethod = SmtpDeliveryMethod.Network,
        Timeout = 20000
      };

      await client.SendMailAsync(msg);
    }

    // --- Branded HTML email (unchanged) ---
    private string BuildVerifyEmailHtml(string verifyUrl, string? logoCid)
    {
      var year = DateTime.UtcNow.Year;
      var safeUrl = System.Net.WebUtility.HtmlEncode(verifyUrl);

      var logoImg = string.IsNullOrWhiteSpace(logoCid)
        ? ""
        : $@"<img src=""cid:{logoCid}"" alt=""Joboria Logo"" width=""36"" height=""36"" style=""display:block; border:0; outline:none; text-decoration:none; border-radius:6px;"" />";

      return $@"
<!DOCTYPE html>
<html lang=""en"">
<head>
  <meta charset=""utf-8"">
  <meta name=""viewport"" content=""width=device-width, initial-scale=1"">
  <title>Verify your email</title>
  <style>
    body, table, td, a {{ -webkit-text-size-adjust:100%; -ms-text-size-adjust:100%; }}
    table, td {{ mso-table-lspace:0pt; mso-table-rspace:0pt; }}
    img {{ -ms-interpolation-mode:bicubic; }}
    body {{ margin:0; padding:0; width:100%!important; height:100%!important; background:#f6f8fb; }}
    a {{ color:#0b5fff; text-decoration:none; }}
    .btn {{ display:inline-block; padding:14px 22px; background:#0b5fff; color:#ffffff!important; border-radius:8px; font-weight:600; }}
    .text-muted {{ color:#7b8694; }}
    @media (max-width:600px) {{
      .container {{ width:100%!important; }}
      .px {{ padding-left:16px!important; padding-right:16px!important; }}
    }}
  </style>
</head>
<body>
  <table role=""presentation"" width=""100%"" cellspacing=""0"" cellpadding=""0"" bgcolor=""#f6f8fb"">
    <tr>
      <td align=""center"" style=""padding:24px;"">
        <table role=""presentation"" width=""560"" class=""container"" cellspacing=""0"" cellpadding=""0"" bgcolor=""#ffffff"" style=""width:560px; border-radius:12px; box-shadow:0 2px 8px rgba(16,24,40,.06); overflow:hidden;"">
          <tr>
            <td class=""px"" style=""padding:18px 28px; background:#0b5fff; color:#ffffff; font-family:Segoe UI, Roboto, Arial, sans-serif;"">
              <table role=""presentation"" width=""100%"" cellspacing=""0"" cellpadding=""0"">
                <tr>
                  <td style=""vertical-align:middle; width:44px;"">
                    {logoImg}
                  </td>
                  <td style=""vertical-align:middle;"">
                    <div style=""font-size:18px; font-weight:700;"">Joboria</div>
                    <div style=""font-size:12px; opacity:.9;"">Recruiter Email Verification</div>
                  </td>
                </tr>
              </table>
            </td>
          </tr>
          <tr>
            <td class=""px"" style=""padding:28px; font-family:Segoe UI, Roboto, Arial, sans-serif; color:#1f2937;"">
              <h1 style=""margin:0 0 12px; font-size:20px; line-height:28px;"">Verify your email</h1>
              <p style=""margin:0 0 16px; font-size:14px; line-height:22px;"">
                Thanks for registering as a recruiter. For security, please confirm your email address by clicking the button below.
              </p>
              <p style=""margin:0 0 24px; font-size:14px; line-height:22px; color:#111827;"">
                This link expires in <strong>5 minutes</strong>.
              </p>
              <p style=""margin:0 0 28px;"">
                <a href=""{safeUrl}"" class=""btn"">Confirm email</a>
              </p>
              <p class=""text-muted"" style=""margin:0 0 8px; font-size:12px;"">
                Trouble with the button? Paste this link into your browser:
              </p>
              <p style=""margin:0 0 24px; font-size:12px; line-height:18px; word-break:break-all;"">
                <a href=""{safeUrl}"">{safeUrl}</a>
              </p>
              <hr style=""border:none; border-top:1px solid #eceff3; margin:20px 0;""/>
              <p class=""text-muted"" style=""margin:0; font-size:11px; line-height:18px;"">
                If you didn’t request this, you can safely ignore this email.
              </p>
            </td>
          </tr>
          <tr>
            <td class=""px"" style=""padding:18px 28px; background:#f8fafc; font-family:Segoe UI, Roboto, Arial, sans-serif; color:#6b7280; font-size:12px;"">
              © {year} Joboria • This is an automated message, please do not reply.
            </td>
          </tr>
        </table>
        <div class=""text-muted"" style=""font-family:Segoe UI, Roboto, Arial, sans-serif; font-size:11px; color:#8c96a5; padding-top:12px;"">
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
      sb.AppendLine("Joboria - Recruiter Email Verification");
      sb.AppendLine();
      sb.AppendLine("Verify your email");
      sb.AppendLine("Thanks for registering as a recruiter. Click the link below to confirm your email.");
      sb.AppendLine("This link expires in 5 minutes.");
      sb.AppendLine();
      sb.AppendLine(verifyUrl);
      sb.AppendLine();
      sb.AppendLine("If you didn’t request this, you can ignore this email.");
      return sb.ToString();
    }

    [HttpGet]
    public IActionResult Logout()
    {
      HttpContext.Session.Clear();
      return RedirectToAction("Login", "Account", new { area = "JobSeeker" });
    }

  }
}
