// File: Areas/Admin/Controllers/UsersController.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using JobPortal.Areas.Admin.Models;
using JobPortal.Areas.Shared.Models;
using JobPortal.Areas.Shared.Extensions; // TryGetUserId()
using System;
using System.Linq;
using System.Text;
using Microsoft.AspNetCore.Http;
using System.Collections.Generic;

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
          int pageSize = 10,
          string role = "All")
        {
            ViewData["Title"] = "Users";
            ViewBag.Role = role;

            var baseQuery = _db.users.AsNoTracking()
                .Where(u => u.user_role == "Recruiter" || u.user_role == "JobSeeker");

            int all = baseQuery.Count();
            int active = baseQuery.Count(u => u.user_status == "Active");
            int suspended = baseQuery.Count(u => u.user_status == "Suspended");
            int pending = baseQuery.Count(u =>
                u.user_status == "Inactive" ||
                (u.company != null && u.company.company_status == "Pending"));

            var qset = baseQuery;

            switch ((role ?? "All").Trim())
            {
                case "Recruiter": qset = qset.Where(u => u.user_role == "Recruiter"); break;
                case "JobSeeker": qset = qset.Where(u => u.user_role == "JobSeeker"); break;
            }

            switch ((status ?? "All").Trim())
            {
                case "Active": qset = qset.Where(u => u.user_status == "Active"); break;
                case "Suspended": qset = qset.Where(u => u.user_status == "Suspended"); break;
                case "Pending":
                    qset = qset.Where(u => u.user_status == "Inactive" ||
                                           (u.company != null && u.company.company_status == "Pending"));
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

            // Effective company status rule
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
                    CompanyStatus = u.company != null
                        ? (u.company.job_listings.Any(j => j.job_status == "Open") ? "Active" : u.company.company_status)
                        : null,
                    CreatedAt = u.created_at
                });

            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 5, 100);
            var total = projected.Count();
            var items = projected.Skip((page - 1) * pageSize).Take(pageSize).ToList();

            var vm = new RecruitersIndexViewModel
            {
                Status = status,
                Query = q,
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

            ViewBag.From = from?.ToString("yyyy-MM-dd");
            ViewBag.To = to?.ToString("yyyy-MM-dd");
            return View(vm);
        }


        // IMPORT CSV: upsert users; for Recruiters also upsert company (validated)
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> ImportCsv(IFormFile? csv,
           string status = "All", string q = "", int page = 1, string role = "All")
        {
            if (csv == null || csv.Length == 0)
            {
                Flash("warning", "Please choose a CSV file.");
                return RedirectToAction(nameof(Index), new { status, q, page, role });
            }

            using var stream = csv.OpenReadStream();
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

            var headerLine = await reader.ReadLineAsync();
            if (string.IsNullOrWhiteSpace(headerLine))
            {
                Flash("danger", "Upload failed, incomplete file. Missing headers.");
                return RedirectToAction(nameof(Index), new { status, q, page, role });
            }

            var headers = ParseCsvLine(headerLine);
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < headers.Count; i++) map[headers[i]] = i;

            string[] baseReq = { "FirstName", "LastName", "Email", "Role", "Status" };
            string[] compReq = { "CompanyName", "CompanyIndustry", "CompanyLocation", "CompanyStatus" };
            var missing = baseReq.Concat(compReq).Where(h => !map.ContainsKey(h)).ToList();
            if (missing.Any())
            {
                Flash("danger", $"Upload failed, incomplete file. Missing headers: {string.Join(", ", missing)}");
                return RedirectToAction(nameof(Index), new { status, q, page, role });
            }

            string Get(IReadOnlyList<string> row, string name, string def = "") =>
                map.TryGetValue(name, out var ix) && ix < row.Count ? (row[ix]?.Trim() ?? def) : def;

            var rows = new List<IReadOnlyList<string>>();
            var errors = new List<string>();
            string? line;
            int lineNo = 1;

            while ((line = await reader.ReadLineAsync()) != null)
            {
                lineNo++;
                if (string.IsNullOrWhiteSpace(line)) continue;
                var r = ParseCsvLine(line);
                rows.Add(r);

                var first = Get(r, "FirstName");
                var last = Get(r, "LastName");
                var email = Get(r, "Email");
                var roleCsv = Get(r, "Role");
                var stat = Get(r, "Status");

                if (string.IsNullOrWhiteSpace(first) ||
                    string.IsNullOrWhiteSpace(last) ||
                    string.IsNullOrWhiteSpace(email) ||
                    string.IsNullOrWhiteSpace(roleCsv) ||
                    string.IsNullOrWhiteSpace(stat))
                { errors.Add($"Line {lineNo}: missing user fields."); continue; }

                if (roleCsv.Equals("Recruiter", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrWhiteSpace(Get(r, "CompanyName")) ||
                        string.IsNullOrWhiteSpace(Get(r, "CompanyIndustry")) ||
                        string.IsNullOrWhiteSpace(Get(r, "CompanyLocation")) ||
                        string.IsNullOrWhiteSpace(Get(r, "CompanyStatus")))
                        errors.Add($"Line {lineNo}: recruiter row requires CompanyName, CompanyIndustry, CompanyLocation, CompanyStatus.");
                }
                else
                {
                    bool hasCompany = !string.IsNullOrWhiteSpace(Get(r, "CompanyName")) ||
                                      !string.IsNullOrWhiteSpace(Get(r, "CompanyIndustry")) ||
                                      !string.IsNullOrWhiteSpace(Get(r, "CompanyLocation")) ||
                                      !string.IsNullOrWhiteSpace(Get(r, "CompanyStatus"));
                    if (hasCompany) errors.Add($"Line {lineNo}: job seeker must not include company columns.");
                }
            }

            if (errors.Any())
            {
                var preview = string.Join(" | ", errors.Take(8));
                var suffix = errors.Count > 8 ? $" (+{errors.Count - 8} more)" : "";
                Flash("danger", $"Upload failed, incomplete file. {preview}{suffix}");
                return RedirectToAction(nameof(Index), new { status, q, page, role });
            }

            int usersCreated = 0, usersUpdated = 0, companiesCreated = 0, companiesUpdated = 0;
            await using var tx = await _db.Database.BeginTransactionAsync();

            foreach (var r in rows)
            {
                var first = Get(r, "FirstName");
                var last = Get(r, "LastName");
                var email = Get(r, "Email");
                var roleCsv = Get(r, "Role");
                var stat = Get(r, "Status");

                var u = await _db.users.FirstOrDefaultAsync(x => x.email == email);
                if (u == null)
                {
                    u = new user
                    {
                        first_name = first,
                        last_name = last,
                        email = email,
                        password_hash = "AQAAAAIAAYagAAAAEIzV7hL4dlr2aojht4w5Og7ukkWnAEFMT6NdAs9y+b2hZfp7mpu3wcnkOL/1G0gAvw==",
                        user_role = roleCsv,
                        user_status = stat
                    };
                    _db.users.Add(u);
                    usersCreated++;
                    await _db.SaveChangesAsync();

                    var pref = new notification_preference
                    {
                        user_id = u.user_id,
                        allow_inApp = true,
                        allow_email = true,
                        notif_job_updates = true,
                        notif_messages = true,
                        notif_reminders = true
                    };
                    _db.notification_preferences.Add(pref);
                    await _db.SaveChangesAsync();
                }
                else
                {
                    u.first_name = first;
                    u.last_name = last;
                    u.user_role = roleCsv;
                    u.user_status = stat;
                    usersUpdated++;
                    await _db.SaveChangesAsync();

                    var pref = await _db.notification_preferences
                        .FirstOrDefaultAsync(p => p.user_id == u.user_id);

                    if (pref == null)
                    {
                        pref = new notification_preference
                        {
                            user_id = u.user_id,
                            allow_inApp = true,
                            allow_email = true,
                            notif_job_updates = true,
                            notif_messages = true,
                            notif_reminders = true
                        };
                        _db.notification_preferences.Add(pref);
                        await _db.SaveChangesAsync();
                    }
                }

                if (roleCsv.Equals("Recruiter", StringComparison.OrdinalIgnoreCase))
                {
                    var comp = await _db.companies.FirstOrDefaultAsync(c => c.user_id == u.user_id);
                    if (comp == null)
                    {
                        comp = new company
                        {
                            user_id = u.user_id,
                            company_name = Get(r, "CompanyName"),
                            company_industry = Get(r, "CompanyIndustry"),
                            company_location = Get(r, "CompanyLocation"),
                            company_status = Get(r, "CompanyStatus")
                        };
                        _db.companies.Add(comp);
                        companiesCreated++;
                    }
                    else
                    {
                        comp.company_name = Get(r, "CompanyName");
                        comp.company_industry = Get(r, "CompanyIndustry");
                        comp.company_location = Get(r, "CompanyLocation");
                        comp.company_status = Get(r, "CompanyStatus");
                        companiesUpdated++;
                    }
                    await _db.SaveChangesAsync();
                }
            }

            await tx.CommitAsync();
            Flash("success", $"Import complete. Users: +{usersCreated}/~{usersUpdated}. Companies: +{companiesCreated}/~{companiesUpdated}.");
            return RedirectToAction(nameof(Index), new { status, q, page, role });
        }


        // CSV export (respects role + filters)
        [HttpGet]
        public IActionResult ExportCsv(string status = "All", string q = "", DateTime? from = null, DateTime? to = null, string role = "All")
        {
            var qset = _db.users.AsNoTracking()
                .Where(u => u.user_role == "Recruiter" || u.user_role == "JobSeeker");

            switch ((role ?? "All").Trim())
            {
                case "Recruiter": qset = qset.Where(u => u.user_role == "Recruiter"); break;
                case "JobSeeker": qset = qset.Where(u => u.user_role == "JobSeeker"); break;
            }

            switch ((status ?? "All").Trim())
            {
                case "Active": qset = qset.Where(u => u.user_status == "Active"); break;
                case "Suspended": qset = qset.Where(u => u.user_status == "Suspended"); break;
                case "Pending":
                    qset = qset.Where(u => u.user_status == "Inactive" ||
                                           (u.company != null && u.company.company_status == "Pending"));
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

            var rows = qset.OrderBy(u => u.first_name).ThenBy(u => u.last_name)
                .Select(u => new
                {
                    Name = (u.first_name + " " + u.last_name).Trim(),
                    u.email,
                    Status = u.user_status,
                    Role = u.user_role,
                    Company = u.company != null ? u.company.company_name : null,
                    CompanyStatus = u.company != null
                        ? (u.company.job_listings.Any(j => j.job_status == "Open") ? "Active" : u.company.company_status)
                        : null,
                    Created = u.created_at
                }).ToList();

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
            return File(bytes, "text/csv; charset=utf-8", $"Users_{DateTime.UtcNow:yyyyMMdd_HHmm}.csv");
        }




        // BULK: Approve / Reject / Suspend / Reactivate (respects role)
        [HttpPost, ValidateAntiForgeryToken]
        public IActionResult Bulk(string actionType, int[] ids,
           string status = "All", string q = "", DateTime? from = null, DateTime? to = null, int page = 1, string role = "All")
        {
            if (ids == null || ids.Length == 0)
            {
                Flash("warning", "No users selected.");
                return RedirectToAction(nameof(Index), new { status, q, role, from = ToStr(from), to = ToStr(to), page });
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
                return RedirectToAction(nameof(Index), new { status, q, role, from = ToStr(from), to = ToStr(to), page });
            }

            var usersQ = _db.users.Where(u => ids.Contains(u.user_id));
            switch ((role ?? "All").Trim())
            {
                case "Recruiter": usersQ = usersQ.Where(u => u.user_role == "Recruiter"); break;
                case "JobSeeker": usersQ = usersQ.Where(u => u.user_role == "JobSeeker"); break;
            }

            var users = usersQ
                .Include(u => u.company) // needed to adjust company_status
                .ToList();
            if (!users.Any())
            {
                Flash("warning", "No matching users found for the selected role.");
                return RedirectToAction(nameof(Index), new { status, q, role, from = ToStr(from), to = ToStr(to), page });
            }

            foreach (var u in users)
            {
                u.user_status = setTo;

                if (u.user_role == "Recruiter" && u.company != null)
                {
                    if (actionType.Equals("approve", StringComparison.OrdinalIgnoreCase)
                        && string.Equals(u.company.company_status, "Pending", StringComparison.OrdinalIgnoreCase))
                        u.company.company_status = "Verified";

                    if (actionType.Equals("reject", StringComparison.OrdinalIgnoreCase)
                        && (string.Equals(u.company.company_status, "Pending", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(u.company.company_status, "Incomplete", StringComparison.OrdinalIgnoreCase)))
                        u.company.company_status = "Rejected";
                }

                // Only touch company_status for recruiters on approve/reject
                if (u.user_role == "Recruiter" && u.company != null)
                {
                    if (actionType.Equals("approve", StringComparison.OrdinalIgnoreCase)
                        && string.Equals(u.company.company_status, "Pending", StringComparison.OrdinalIgnoreCase))
                        u.company.company_status = "Verified";

                    if (actionType.Equals("reject", StringComparison.OrdinalIgnoreCase)
                        && (string.Equals(u.company.company_status, "Pending", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(u.company.company_status, "Incomplete", StringComparison.OrdinalIgnoreCase)))
                        u.company.company_status = "Rejected";
                }
            }
            _db.SaveChanges();

            if (this.TryGetUserId(out var adminId, out _))
            {
                var now = DateTime.UtcNow;
                foreach (var _ in users)
                    _db.admin_logs.Add(new admin_log { user_id = adminId, action_type = $"User-{setTo}", timestamp = now });
                _db.SaveChanges();
            }

            Flash("success", $"{users.Count} user{(users.Count == 1 ? "" : "s")} {actionType.ToLowerInvariant()}.");
            return RedirectToAction(nameof(Index), new { status, q, role, from = ToStr(from), to = ToStr(to), page });
        }


        // ---------- PREVIEW (supports both roles) ----------
        public IActionResult Preview(int id)
        {
            var u = _db.users
                .Where(x => x.user_id == id)
                .Select(x => new
                {
                    x.user_id,
                    x.first_name,
                    x.last_name,
                    x.email,
                    x.user_role,
                    x.user_status,
                    x.created_at,
                    CompanyId = x.company != null ? (int?)x.company.company_id : null,
                    CompanyName = x.company != null ? x.company.company_name : null,
                    CompanyStatusRaw = x.company != null ? x.company.company_status : null,
                    CompanyIndustry = x.company != null ? x.company.company_industry : null,
                    CompanyLocation = x.company != null ? x.company.company_location : null,
                    CompanyDescription = x.company != null ? x.company.company_description : null,
                    CompanyPhoto = x.company != null ? x.company.company_photo : null,  // <-- NEW: include path
                    OpenJobs = x.company != null ? x.company.job_listings.Count(j => j.job_status == "Open") : 0,
                    x.phone,
                    x.address
                })
                .FirstOrDefault();

            if (u == null) return NotFound();

            var effectiveCompanyStatus = u.CompanyId.HasValue
                ? (u.OpenJobs > 0 ? "Active" : u.CompanyStatusRaw)
                : null;

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
                CompanyStatus = effectiveCompanyStatus,
                Phone = u.phone,
                Address = u.address
            };

            // Provide rich company details for the view without changing the VM contract
            ViewBag.Company = u.CompanyId.HasValue ? new
            {
                Id = u.CompanyId.Value,
                Name = u.CompanyName,
                Status = effectiveCompanyStatus,
                Industry = u.CompanyIndustry,
                Location = u.CompanyLocation,
                Description = u.CompanyDescription,
                OpenJobs = u.OpenJobs,
                CompanyPhotoUrl = u.CompanyPhoto      // <-- NEW: expose photo to the view
            } : null;

            ViewData["Title"] = u.user_role;
            return View(vm);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public IActionResult Approve(int id, string status = "All", string q = "", DateTime? from = null, DateTime? to = null, int page = 1, string role = "All")
            => SetUserStatus(id, "Active", "User activated.", status, q, from, to, page, role);
        [HttpPost, ValidateAntiForgeryToken]
        public IActionResult Reject(int id, string status = "All", string q = "", DateTime? from = null, DateTime? to = null, int page = 1, string role = "All")
            => SetUserStatus(id, "Inactive", "User rejected.", status, q, from, to, page, role);
        [HttpPost, ValidateAntiForgeryToken]
        public IActionResult Suspend(int id, string status = "All", string q = "", DateTime? from = null, DateTime? to = null, int page = 1, string role = "All")
            => SetUserStatus(id, "Suspended", "User suspended.", status, q, from, to, page, role);
        [HttpPost, ValidateAntiForgeryToken]
        public IActionResult Reactivate(int id, string status = "All", string q = "", DateTime? from = null, DateTime? to = null, int page = 1, string role = "All")
            => SetUserStatus(id, "Active", "User reactivated.", status, q, from, to, page, role);

        // Redirect helper: from a user id, jump to the Companies/Preview page if company exists
        [HttpGet]
        public IActionResult CompanyProfile(int id, string status = "All", string q = "", int page = 1, string role = "All")
        {
            var comp = _db.companies.AsNoTracking().FirstOrDefault(c => c.user_id == id);
            if (comp == null)
            {
                Flash("warning", "Company profile not found for this recruiter.");
                return RedirectToAction(nameof(Index), new { status, q, page, role });
            }
            return RedirectToAction("Preview", "Companies", new { area = "Admin", id = comp.company_id });
        }

        private IActionResult SetUserStatus(int id, string newStatus, string okMessage,
            string status, string q, DateTime? from, DateTime? to, int page, string role)
        {
            var u = _db.users.Include(x => x.company).FirstOrDefault(x => x.user_id == id);
            if (u == null)
            {
                Flash("warning", "User not found.");
                return RedirectToAction(nameof(Index), new { status, q, role, from = ToStr(from), to = ToStr(to), page });
            }

            u.user_status = newStatus;

            if (u.user_role == "Recruiter" && u.company != null)
            {
                if (newStatus == "Active" &&
                    string.Equals(u.company.company_status, "Pending", StringComparison.OrdinalIgnoreCase))
                    u.company.company_status = "Verified";
                else if (newStatus == "Inactive" &&
                        (string.Equals(u.company.company_status, "Pending", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(u.company.company_status, "Incomplete", StringComparison.OrdinalIgnoreCase)))
                    u.company.company_status = "Rejected";
            }

            if (u.user_role == "Recruiter" && u.company != null)
            {
                if (newStatus == "Active" &&
                    string.Equals(u.company.company_status, "Pending", StringComparison.OrdinalIgnoreCase))
                {
                    u.company.company_status = "Verified";
                }
                else if (newStatus == "Inactive" &&
                         (string.Equals(u.company.company_status, "Pending", StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(u.company.company_status, "Incomplete", StringComparison.OrdinalIgnoreCase)))
                {
                    u.company.company_status = "Rejected";
                }
            }

            _db.SaveChanges();

            if (this.TryGetUserId(out var adminId, out _))
            {
                _db.admin_logs.Add(new admin_log { user_id = adminId, action_type = $"User-{newStatus}", timestamp = DateTime.UtcNow });
                _db.SaveChanges();
            }

            Flash("success", okMessage);
            return RedirectToAction(nameof(Index), new { status, q, role, from = ToStr(from), to = ToStr(to), page });
        }

        private static string? ToStr(DateTime? d) => d?.ToString("yyyy-MM-dd");

        private void Flash(string type, string message)
        {
            TempData["Flash.Type"] = type;
            TempData["Flash.Message"] = message;
        }

        private static List<string> ParseCsvLine(string line)
        {
            var res = new List<string>();
            if (string.IsNullOrEmpty(line)) { res.Add(""); return res; }

            var sb = new StringBuilder();
            bool inQuotes = false;
            for (int i = 0; i < line.Length; i++)
            {
                var c = line[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                        else { inQuotes = false; }
                    }
                    else sb.Append(c);
                }
                else
                {
                    if (c == ',') { res.Add(sb.ToString()); sb.Clear(); }
                    else if (c == '"') inQuotes = true;
                    else sb.Append(c);
                }
            }
            res.Add(sb.ToString());
            return res;
        }
    }
}
