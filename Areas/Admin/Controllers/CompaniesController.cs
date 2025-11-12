// File: Areas/Admin/Controllers/CompaniesController.cs  (ASYNC UPDATED - FIXED CALLS)
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using JobPortal.Areas.Admin.Models;
using JobPortal.Areas.Shared.Models;
using JobPortal.Areas.Shared.Extensions; // TryGetUserId()
using JobPortal.Services;                // INotificationService
using System;
using System.Linq;
using System.Text;

namespace JobPortal.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class CompaniesController : Controller
    {
        private readonly AppDbContext _db;
        private readonly INotificationService _notif;

        public CompaniesController(AppDbContext db, INotificationService notif)
        {
            _db = db;
            _notif = notif;
        }

        // GET: /Admin/Companies
        public IActionResult Index(
            string status = "All",
            string q = "",
            DateTime? from = null,
            DateTime? to = null,
            int page = 1,
            int pageSize = 10)
        {
            ViewData["Title"] = "Companies";

            // Auto-flag Incomplete (missing required fields)
            AutoFlagIncompleteCompanies();

            var baseQuery = _db.companies.AsNoTracking();

            int all = baseQuery.Count();
            int verified = baseQuery.Count(c => c.company_status == "Verified");
            int unverified = baseQuery.Count(c => c.company_status == null || c.company_status == "" || c.company_status == "Pending");
            int incomplete = baseQuery.Count(c => c.company_status == "Incomplete");
            int rejected = baseQuery.Count(c => c.company_status == "Rejected");

            var qset = baseQuery;

            switch ((status ?? "All").Trim())
            {
                case "Verified": qset = qset.Where(c => c.company_status == "Verified"); break;
                case "Unverified": qset = qset.Where(c => c.company_status == null || c.company_status == "" || c.company_status == "Pending"); break;
                case "Incomplete": qset = qset.Where(c => c.company_status == "Incomplete"); break;
                case "Rejected": qset = qset.Where(c => c.company_status == "Rejected"); break;
                case "Active": qset = qset.Where(c => c.company_status == "Active"); break;
                default: break;
            }

            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = q.Trim();
                qset = qset.Where(c =>
                    EF.Functions.Like(c.company_name, $"%{term}%") ||
                    EF.Functions.Like(c.company_industry ?? "", $"%{term}%") ||
                    EF.Functions.Like(c.company_location ?? "", $"%{term}%"));
            }

            if (from.HasValue || to.HasValue)
            {
                var toExclusive = to?.Date.AddDays(1);
                qset = qset.Where(c => c.job_listings.Any(j =>
                    (!from.HasValue || j.date_posted >= from.Value.Date) &&
                    (!toExclusive.HasValue || j.date_posted < toExclusive.Value)));
            }

            var projected = qset
                .OrderBy(c => c.company_name)
                .Select(c => new CompanyRow
                {
                    Id = c.company_id,
                    Name = c.company_name,
                    Industry = c.company_industry,
                    Location = c.company_location,
                    Status = c.job_listings.Any(j => j.job_status == "Open")
                                ? "Active"
                                : (c.company_status ?? "Pending"),
                    Jobs = c.job_listings.Count()
                });

            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 5, 100);
            int total = projected.Count();
            var items = projected.Skip((page - 1) * pageSize).Take(pageSize).ToList();

            var vm = new CompaniesIndexViewModel
            {
                Status = status ?? "All",
                Query = q ?? "",
                AllCount = all,
                VerifiedCount = verified,
                UnverifiedCount = unverified,
                IncompleteCount = incomplete,
                RejectedCount = rejected,
                Items = new PagedResult<CompanyRow>
                {
                    Items = items,
                    Page = page,
                    PageSize = pageSize,
                    TotalItems = total
                }
            };

            ViewBag.From = from?.ToString("yyyy-MM-dd");
            ViewBag.To = to?.ToString("yyyy-MM-dd");

            return View(vm);
        }

        // POST: bulk verify/reject (ASYNC)
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Bulk(string actionType, int[] ids, string? comments, string status = "All", string q = "", int page = 1)
        {
            if (ids == null || ids.Length == 0)
            {
                Flash("warning", "No companies selected.");
                return RedirectToAction(nameof(Index), new { status, q, page });
            }

            var setTo = actionType?.Equals("Verify", StringComparison.OrdinalIgnoreCase) == true
                ? "Verified"
                : actionType?.Equals("Reject", StringComparison.OrdinalIgnoreCase) == true
                    ? "Rejected"
                    : null;

            if (setTo == null)
            {
                Flash("danger", "Unknown bulk action.");
                return RedirectToAction(nameof(Index), new { status, q, page });
            }

            var companies = _db.companies.Where(c => ids.Contains(c.company_id)).ToList();
            if (!companies.Any())
            {
                Flash("warning", "No matching companies found.");
                return RedirectToAction(nameof(Index), new { status, q, page });
            }

            foreach (var c in companies) c.company_status = setTo;
            await _db.SaveChangesAsync();

            if (this.TryGetUserId(out var adminId, out _))
            {
                var now = DateTime.UtcNow;
                foreach (var _ in companies)
                {
                    _db.admin_logs.Add(new admin_log
                    {
                        user_id = adminId,
                        action_type = $"Company-{setTo}",
                        timestamp = now
                    });
                }
                await _db.SaveChangesAsync();
            }

            // notify owners (Verify/Reject handled in bulk)
            var notifyTasks = companies
                .Where(c => c.user_id > 0)
                .Select(c =>
                {
                    var title = setTo == "Verified"
                        ? "Company profile approved"
                        : "Company profile rejected";

                    var msg = setTo == "Verified"
                        ? $"Your company profile \"{c.company_name}\" has been approved. You can now post jobs."
                        : $"Your company profile \"{c.company_name}\" has been rejected.{(string.IsNullOrWhiteSpace(comments) ? "" : $" Reason: {comments.Trim()}")}";

                    var type = setTo == "Verified" ? "Approval" : "Review";
                    return _notif.SendAsync(c.user_id, title, msg, type: type);  // ← fixed
                });

            try { await Task.WhenAll(notifyTasks); }
            catch { /* best effort */ }

            Flash("success", $"{companies.Count} compan{(companies.Count == 1 ? "y" : "ies")} {setTo.ToLowerInvariant()}.");
            return RedirectToAction(nameof(Index), new { status, q, page });
        }

        // CSV export (respects status/search/date-range)
        [HttpGet]
        public IActionResult ExportCsv(string status = "All", string q = "", DateTime? from = null, DateTime? to = null)
        {
            var qset = _db.companies.AsNoTracking();

            switch ((status ?? "All").Trim())
            {
                case "Verified": qset = qset.Where(c => c.company_status == "Verified"); break;
                case "Unverified": qset = qset.Where(c => c.company_status == null || c.company_status == "" || c.company_status == "Pending"); break;
                case "Incomplete": qset = qset.Where(c => c.company_status == "Incomplete"); break;
                case "Rejected": qset = qset.Where(c => c.company_status == "Rejected"); break;
                case "Active": qset = qset.Where(c => c.company_status == "Active"); break;
                default: break;
            }

            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = q.Trim();
                qset = qset.Where(c =>
                    EF.Functions.Like(c.company_name, $"%{term}%") ||
                    EF.Functions.Like(c.company_industry ?? "", $"%{term}%") ||
                    EF.Functions.Like(c.company_location ?? "", $"%{term}%"));
            }

            if (from.HasValue || to.HasValue)
            {
                var toExclusive = to?.Date.AddDays(1);
                qset = qset.Where(c => c.job_listings.Any(j =>
                    (!from.HasValue || j.date_posted >= from.Value.Date) &&
                    (!toExclusive.HasValue || j.date_posted < toExclusive.Value)));
            }

            var rows = qset
                .OrderBy(c => c.company_name)
                .Select(c => new
                {
                    c.company_name,
                    c.company_industry,
                    c.company_location,
                    Status = c.job_listings.Any(j => j.job_status == "Open") ? "Active" : (c.company_status ?? "Unverified"),
                    Jobs = c.job_listings.Count()
                })
                .ToList();

            static string Esc(string? s)
            {
                if (string.IsNullOrEmpty(s)) return "";
                var needs = s.Contains(',') || s.Contains('"') || s.Contains('\n') || s.Contains('\r');
                var t = s.Replace("\"", "\"\"");
                return needs ? $"\"{t}\"" : t;
            }

            var sb = new StringBuilder();
            sb.AppendLine("Name,Industry,Location,Status,Jobs");
            foreach (var r in rows)
                sb.AppendLine($"{Esc(r.company_name)},{Esc(r.company_industry)},{Esc(r.company_location)},{Esc(r.Status)},{r.Jobs}");

            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
            var fileName = $"Companies_{DateTime.UtcNow:yyyyMMdd_HHmm}.csv";
            return File(bytes, "text/csv; charset=utf-8", fileName);
        }

        // GET: /Admin/Companies/Preview/5
        public IActionResult Preview(int id)
        {
            var c = _db.companies
                .Include(x => x.job_listings)
                .FirstOrDefault(x => x.company_id == id);

            if (c == null) return NotFound();

            var jobs = c.job_listings
                .OrderByDescending(j => j.date_posted)
                .Take(10)
                .Select(j => new ApprovalRow
                {
                    Id = j.job_listing_id,
                    JobId = j.job_listing_id,
                    JobTitle = j.job_title,
                    Company = c.company_name,
                    Status = j.job_status,
                    Date = j.date_posted
                })
                .ToList();

            var vm = new CompanyPreviewViewModel
            {
                Id = c.company_id,
                Name = c.company_name,
                Industry = c.company_industry,
                Location = c.company_location,
                Description = c.company_description,
                Status = c.job_listings.Any(j => j.job_status == "Open") ? "Active" : (c.company_status ?? "Pending"),
                RecentJobs = jobs
            };

            // expose the photo path for the view
            ViewBag.CompanyPhotoUrl = c.company_photo;

            ViewData["Title"] = "Company Preview";
            return View(vm);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Verify(int id)
            => await SetStatus(id, "Verified", "Company verified.");

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> MarkIncomplete(int id)
            => await SetStatus(id, "Incomplete", "Company marked as incomplete.");

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Reject(int id, string? comments)
            => await SetStatus(id, "Rejected", string.IsNullOrWhiteSpace(comments) ? "Company rejected." : $"Company rejected: {comments}", comments);

        private void AutoFlagIncompleteCompanies()
        {
            var toFlag = _db.companies
                .Where(c =>
                    c.company_status != "Incomplete" &&
                    (string.IsNullOrWhiteSpace(c.company_name) ||
                     string.IsNullOrWhiteSpace(c.company_location)))
                .ToList();

            if (toFlag.Count == 0) return;

            foreach (var c in toFlag)
                c.company_status = "Incomplete";

            _db.SaveChanges();
        }

        // NOTE: recruiterComments used when status == "Rejected"
        private async Task<IActionResult> SetStatus(int id, string status, string logMsg, string? recruiterComments = null)
        {
            var c = _db.companies.FirstOrDefault(x => x.company_id == id);
            if (c == null) return NotFound();

            c.company_status = status;
            await _db.SaveChangesAsync();

            if (this.TryGetUserId(out var adminId, out _))
            {
                _db.admin_logs.Add(new admin_log
                {
                    user_id = adminId,
                    action_type = $"Company-{status}",
                    timestamp = DateTime.UtcNow
                });
                await _db.SaveChangesAsync();
            }

            // ---- Notify the company owner (recruiter) for Verified / Rejected / Incomplete ----
            if (c.user_id > 0 && (status == "Verified" || status == "Rejected" || status == "Incomplete"))
            {
                string title, message, type;

                if (status == "Verified")
                {
                    title = "Company profile approved";
                    message = $"Your company profile \"{c.company_name}\" has been approved. You can now post jobs.";
                    type = "Approval";
                }
                else if (status == "Rejected")
                {
                    title = "Company profile rejected";
                    message = $"Your company profile \"{c.company_name}\" has been rejected.{(string.IsNullOrWhiteSpace(recruiterComments) ? "" : $" Reason: {recruiterComments.Trim()}")}";
                    type = "Review";
                }
                else // Incomplete
                {
                    title = "Company profile incomplete";
                    message = $"Your company profile \"{c.company_name}\" is marked as incomplete. Please fill in all required details (e.g., name and location) and resubmit for approval.";
                    type = "Review";
                }

                try { await _notif.SendAsync(c.user_id, title, message, type: type); }  // ← fixed
                catch { /* ignore notification failure */ }
            }
            // ---------------------------------------------------------------------

            Flash("success", logMsg);
            return RedirectToAction(nameof(Preview), new { id });
        }

        private void Flash(string type, string message)
        {
            TempData["Flash.Type"] = type;
            TempData["Flash.Message"] = message;
        }
    }
}
