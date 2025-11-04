// File: Areas/Admin/Controllers/UsersController.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using JobPortal.Areas.Admin.Models;
using JobPortal.Areas.Shared.Models;
using JobPortal.Areas.Shared.Extensions; // TryGetUserId()
using System;
using System.Linq;
using System.Text;

namespace JobPortal.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class UsersController : Controller
    {
        private readonly AppDbContext _db;
        public UsersController(AppDbContext db) => _db = db;

        // GET: /Admin/Users
        public IActionResult Index(
            string status = "All",
            string q = "",
            DateTime? from = null,
            DateTime? to = null,
            int page = 1,
            int pageSize = 10)
        {
            ViewData["Title"] = "Recruiters";

            var baseQuery = _db.users
                .AsNoTracking()
                .Where(u => u.user_role == "Recruiter");

            // counts (unfiltered by search/date)
            int all = baseQuery.Count();
            int active = baseQuery.Count(u => u.user_status == "Active");
            int suspended = baseQuery.Count(u => u.user_status == "Suspended");
            int pending = baseQuery.Count(u =>
                u.user_status == "Inactive" ||
                (u.company != null && u.company.company_status == "Pending"));

            var qset = baseQuery;

            // status filter
            switch ((status ?? "All").Trim())
            {
                case "Active":
                    qset = qset.Where(u => u.user_status == "Active");
                    break;
                case "Suspended":
                    qset = qset.Where(u => u.user_status == "Suspended");
                    break;
                case "Pending":
                    qset = qset.Where(u =>
                        u.user_status == "Inactive" ||
                        (u.company != null && u.company.company_status == "Pending"));
                    break;
                default:
                    /* All */
                    break;
            }

            // text search
            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = q.Trim();
                qset = qset.Where(u =>
                    EF.Functions.Like(u.first_name, $"%{term}%") ||
                    EF.Functions.Like(u.last_name, $"%{term}%") ||
                    EF.Functions.Like(u.email, $"%{term}%") ||
                    (u.company != null && EF.Functions.Like(u.company.company_name, $"%{term}%")));
            }

            // created_at date range (inclusive)
            if (from.HasValue || to.HasValue)
            {
                var toExclusive = to?.Date.AddDays(1);
                qset = qset.Where(u =>
                    (!from.HasValue || u.created_at >= from.Value.Date) &&
                    (!toExclusive.HasValue || u.created_at < toExclusive.Value));
            }

            // projection
            var projected = qset
                .OrderBy(u => u.first_name).ThenBy(u => u.last_name)
                .Select(u => new RecruiterRow
                {
                    Id = u.user_id,
                    Name = (u.first_name + " " + u.last_name).Trim(),
                    Email = u.email,
                    Status = u.user_status,
                    Role = u.user_role,
                    Company = u.company != null ? u.company.company_name : null,
                    CompanyStatus = u.company != null ? u.company.company_status : null,
                    CreatedAt = u.created_at
                });

            // paging
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 5, 100);
            var total = projected.Count();
            var items = projected.Skip((page - 1) * pageSize).Take(pageSize).ToList();

            var vm = new RecruitersIndexViewModel
            {
                Status = status ?? "All",
                Query = q ?? "",
                AllCount = all,
                ActiveCount = active,
                PendingCount = pending,
                SuspendedCount = suspended,
                Items = new PagedResult<RecruiterRow>
                {
                    Items = items,
                    Page = page,
                    PageSize = pageSize,
                    TotalItems = total
                }
            };

            // keep date filters in view
            ViewBag.From = from?.ToString("yyyy-MM-dd");
            ViewBag.To = to?.ToString("yyyy-MM-dd");

            return View(vm);
        }

        // CSV export (respects status/search/date-range)
        [HttpGet]
        public IActionResult ExportCsv(string status = "All", string q = "", DateTime? from = null, DateTime? to = null)
        {
            var qset = _db.users
                .AsNoTracking()
                .Where(u => u.user_role == "Recruiter");

            switch ((status ?? "All").Trim())
            {
                case "Active":
                    qset = qset.Where(u => u.user_status == "Active");
                    break;
                case "Suspended":
                    qset = qset.Where(u => u.user_status == "Suspended");
                    break;
                case "Pending":
                    qset = qset.Where(u =>
                        u.user_status == "Inactive" ||
                        (u.company != null && u.company.company_status == "Pending"));
                    break;
                default:
                    /* All */
                    break;
            }

            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = q.Trim();
                qset = qset.Where(u =>
                    EF.Functions.Like(u.first_name, $"%{term}%") ||
                    EF.Functions.Like(u.last_name, $"%{term}%") ||
                    EF.Functions.Like(u.email, $"%{term}%") ||
                    (u.company != null && EF.Functions.Like(u.company.company_name, $"%{term}%")));
            }

            if (from.HasValue || to.HasValue)
            {
                var toExclusive = to?.Date.AddDays(1);
                qset = qset.Where(u =>
                    (!from.HasValue || u.created_at >= from.Value.Date) &&
                    (!toExclusive.HasValue || u.created_at < toExclusive.Value));
            }

            var rows = qset
                .OrderBy(u => u.first_name).ThenBy(u => u.last_name)
                .Select(u => new
                {
                    Name = (u.first_name + " " + u.last_name).Trim(),
                    u.email,
                    Status = u.user_status,
                    Role = u.user_role,
                    Company = u.company != null ? u.company.company_name : null,
                    CompanyStatus = u.company != null ? u.company.company_status : null,
                    Created = u.created_at
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
            sb.AppendLine("Name,Email,UserStatus,Role,Company,CompanyStatus,Created");
            foreach (var r in rows)
                sb.AppendLine($"{Esc(r.Name)},{Esc(r.email)},{Esc(r.Status)},{Esc(r.Role)},{Esc(r.Company)},{Esc(r.CompanyStatus)},{r.Created:yyyy-MM-dd HH:mm}");

            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
            var fileName = $"Recruiters_{DateTime.UtcNow:yyyyMMdd_HHmm}.csv";
            return File(bytes, "text/csv; charset=utf-8", fileName);
        }

        // BULK: Approve/Reject/Suspend/Reactivate (Active/Inactive/Suspended/Active)
        [HttpPost, ValidateAntiForgeryToken]
        public IActionResult Bulk(
            string actionType,
            int[] ids,
            string status = "All",
            string q = "",
            DateTime? from = null,
            DateTime? to = null,
            int page = 1)
        {
            if (ids == null || ids.Length == 0)
            {
                Flash("warning", "No users selected.");
                return RedirectToAction(nameof(Index), new { status, q, from = ToStr(from), to = ToStr(to), page });
            }

            string? setTo = actionType?.ToLowerInvariant() switch
            {
                "approve" => "Active",
                "reject" => "Inactive",
                "suspend" => "Suspended",
                "reactivate" => "Active",
                _ => null
            };

            if (setTo == null)
            {
                Flash("danger", "Unknown bulk action.");
                return RedirectToAction(nameof(Index), new { status, q, from = ToStr(from), to = ToStr(to), page });
            }

            var users = _db.users.Where(u => ids.Contains(u.user_id) && u.user_role == "Recruiter").ToList();
            if (!users.Any())
            {
                Flash("warning", "No matching recruiters found.");
                return RedirectToAction(nameof(Index), new { status, q, from = ToStr(from), to = ToStr(to), page });
            }

            foreach (var u in users) u.user_status = setTo;
            _db.SaveChanges();

            if (this.TryGetUserId(out var adminId, out _))
            {
                var now = DateTime.UtcNow;
                foreach (var _ in users)
                {
                    _db.admin_logs.Add(new admin_log
                    {
                        user_id = adminId,
                        action_type = $"User-{setTo}",
                        timestamp = now
                    });
                }
                _db.SaveChanges();
            }

            Flash("success", $"{users.Count} user{(users.Count == 1 ? "" : "s")} {actionType.ToLowerInvariant()}.");
            return RedirectToAction(nameof(Index), new { status, q, from = ToStr(from), to = ToStr(to), page });
        }

        // GET: /Admin/Users/Preview/5
        public IActionResult Preview(int id)
        {
            var u = _db.users
                .Where(x => x.user_id == id && x.user_role == "Recruiter")
                .Select(x => new
                {
                    x.user_id,
                    x.first_name,
                    x.last_name,
                    x.email,
                    x.user_role,
                    x.user_status,
                    x.created_at,
                    CompanyName = x.company != null ? x.company.company_name : null,
                    CompanyStatus = x.company != null ? x.company.company_status : null,
                    x.phone,
                    x.address
                })
                .FirstOrDefault();

            if (u == null) return NotFound();

            var vm = new RecruiterPreviewViewModel
            {
                Id = u.user_id,
                FirstName = u.first_name,
                LastName = u.last_name,
                Email = u.email,
                Role = u.user_role,
                Status = u.user_status,
                CreatedAt = u.created_at,
                CompanyName = u.CompanyName,
                CompanyStatus = u.CompanyStatus,
                Phone = u.phone,
                Address = u.address
            };

            ViewData["Title"] = "Recruiter";
            return View(vm);
        }

        [HttpPost, ValidateAntiForgeryToken] public IActionResult Approve(int id) => SetUserStatus(id, "Active", "Recruiter approved.");
        [HttpPost, ValidateAntiForgeryToken] public IActionResult Reject(int id) => SetUserStatus(id, "Inactive", "Recruiter registration rejected.");
        [HttpPost, ValidateAntiForgeryToken] public IActionResult Suspend(int id) => SetUserStatus(id, "Suspended", "Recruiter suspended.");
        [HttpPost, ValidateAntiForgeryToken] public IActionResult Reactivate(int id) => SetUserStatus(id, "Active", "Recruiter reactivated.");

        private IActionResult SetUserStatus(int id, string status, string msg)
        {
            var u = _db.users.FirstOrDefault(x => x.user_id == id && x.user_role == "Recruiter");
            if (u == null) return NotFound();

            u.user_status = status;
            _db.SaveChanges();

            if (this.TryGetUserId(out var adminId, out _))
            {
                _db.admin_logs.Add(new admin_log
                {
                    user_id = adminId,
                    action_type = $"User-{status}",
                    timestamp = DateTime.UtcNow
                });
                _db.SaveChanges();
            }

            Flash("success", msg);
            return RedirectToAction(nameof(Preview), new { id });
        }

        private static string? ToStr(DateTime? d) => d?.ToString("yyyy-MM-dd");

        private void Flash(string type, string message)
        {
            TempData["Flash.Type"] = type;
            TempData["Flash.Message"] = message;
        }
    }
}
