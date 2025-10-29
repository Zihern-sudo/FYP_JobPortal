using System.IO.Compression;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using JobPortal.Areas.Shared.Models;
using JobPortal.Areas.Recruiter.Models;

namespace JobPortal.Areas.Recruiter.Controllers
{
    [Area("Recruiter")]
    public class BulkController : Controller
    {
        private readonly AppDbContext _db;
        private readonly IWebHostEnvironment _env;

        public BulkController(AppDbContext db, IWebHostEnvironment env)
        {
            _db = db;
            _env = env;
        }

        // GET: /Recruiter/Bulk
        public async Task<IActionResult> Index()
        {
            ViewData["Title"] = "Bulk Actions";

            // TODO: replace this hard-coded recruiter with the logged-in recruiter later
            var recruiterId = 3;

            var apps = await _db.job_applications
                .Include(a => a.user)
                .Include(a => a.job_listing)
                .Where(a => a.job_listing.user_id == recruiterId)
                .OrderByDescending(a => a.date_updated)
                .ToListAsync();

            var jobIds = apps.Select(a => a.job_listing_id).Distinct().ToList();

            var evals = await _db.ai_resume_evaluations
                .Where(ev => jobIds.Contains(ev.job_listing_id))
                .Join(_db.resumes,
                      ev => ev.resume_id,
                      r => r.resume_id,
                      (ev, r) => new { ev.job_listing_id, r.user_id, r.upload_date, ev.match_score })
                .ToListAsync();

            string MapStage(string raw)
            {
                var s = (raw ?? "").Trim();
                if (s.Equals("Submitted", StringComparison.OrdinalIgnoreCase)) return "New";
                if (s.Equals("AI-Screened", StringComparison.OrdinalIgnoreCase)) return "AI-Screened";
                if (s.Equals("Shortlisted", StringComparison.OrdinalIgnoreCase)) return "Shortlisted";
                if (s.Equals("Interview", StringComparison.OrdinalIgnoreCase)) return "Interview";
                if (s.Equals("Offer", StringComparison.OrdinalIgnoreCase)) return "Offer";
                if (s.Equals("Hired", StringComparison.OrdinalIgnoreCase)) return "Hired/Rejected";
                if (s.Equals("Rejected", StringComparison.OrdinalIgnoreCase)) return "Hired/Rejected";
                return "New";
            }

            var items = apps.Select(a =>
            {
                var score = evals
                    .Where(e => e.user_id == a.user_id && e.job_listing_id == a.job_listing_id)
                    .OrderByDescending(e => e.upload_date)
                    .Select(e => (int?)(e.match_score ?? 0))
                    .FirstOrDefault() ?? 0;

                var fullName = $"{a.user.first_name} {a.user.last_name}".Trim();
                return new CandidateItemVM(
                    Id: a.application_id,
                    Name: string.IsNullOrWhiteSpace(fullName) ? $"User #{a.user_id}" : fullName,
                    Stage: MapStage(a.application_status),
                    Score: score,
                    AppliedAt: a.date_updated.ToString("yyyy-MM-dd HH:mm"),
                    LowConfidence: false,
                    Override: false
                );
            }).ToList();

            ViewBag.Items = items;
            return View();
        }

        // POST: /Recruiter/Bulk/MoveToShortlisted
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MoveToShortlisted([FromForm] int[] selectedIds)
        {
            if (selectedIds == null || selectedIds.Length == 0)
            {
                TempData["Message"] = "No candidates selected.";
                return RedirectToAction(nameof(Index));
            }

            var apps = await _db.job_applications
                .Where(a => selectedIds.Contains(a.application_id))
                .ToListAsync();

            foreach (var a in apps)
            {
                a.application_status = "Shortlisted";
                a.date_updated = DateTime.Now;
            }

            await _db.SaveChangesAsync();

            TempData["Message"] = $"Moved {apps.Count} candidate(s) to Shortlisted.";
            return RedirectToAction(nameof(Index));
        }

        // POST: /Recruiter/Bulk/ExportCvsZip
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ExportCvsZip([FromForm] int[] selectedIds)
        {
            if (selectedIds == null || selectedIds.Length == 0)
            {
                TempData["Message"] = "No candidates selected.";
                return RedirectToAction(nameof(Index));
            }

            // Get the applications + users for the requested IDs
            var apps = await _db.job_applications
                .Include(a => a.user)
                .Where(a => selectedIds.Contains(a.application_id))
                .ToListAsync();

            // Which user_ids are we exporting?
            var userIds = apps.Select(a => a.user_id).Distinct().ToList();

            // Pull ALL resumes for those users (not just "latest")
            var allResumes = await _db.resumes
                .Where(r => userIds.Contains(r.user_id))
                .OrderByDescending(r => r.upload_date)
                .ToListAsync();

            // We'll choose the first existing file on disk for each user
            var root = _env.WebRootPath ?? AppContext.BaseDirectory;
            var chosenByUser = new Dictionary<int, (string absPath, string ext)>();

            foreach (var uid in userIds)
            {
                // all resumes for this user, newest first
                var candidatesForUser = allResumes
                    .Where(r => r.user_id == uid)
                    .OrderByDescending(r => r.upload_date)
                    .ToList();

                foreach (var r in candidatesForUser)
                {
                    // normalise DB path to relative under wwwroot
                    // e.g. "/uploads\resumes\Lee.pdf" -> "uploads/resumes/Lee.pdf"
                    var rel = (r.file_path ?? "")
                        .Replace('\\', '/')
                        .TrimStart('/');

                    // build absolute full path on disk
                    var abs = Path.Combine(root, rel);

                    if (System.IO.File.Exists(abs))
                    {
                        chosenByUser[uid] = (abs, Path.GetExtension(abs));
                        break; // stop at first valid physical file
                    }
                }
            }

            if (chosenByUser.Count == 0)
            {
                TempData["Message"] = "No valid resume files found on disk.";
                return RedirectToAction(nameof(Index));
            }

            using var ms = new MemoryStream();
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var kvp in chosenByUser)
                {
                    var uid = kvp.Key;
                    var absPath = kvp.Value.absPath;
                    var ext = kvp.Value.ext;

                    // Find the related application/user so we can name the file nicely
                    var appForUser = apps.FirstOrDefault(a => a.user_id == uid);
                    var first = appForUser?.user?.first_name ?? "";
                    var last = appForUser?.user?.last_name ?? "";

                    var safeBase = $"{first}_{last}".Trim();
                    if (string.IsNullOrWhiteSpace(safeBase))
                    {
                        safeBase = $"user_{uid}";
                    }

                    safeBase = safeBase.Replace(' ', '_');

                    var entryName = $"{safeBase}{ext}";

                    var entry = zip.CreateEntry(entryName, CompressionLevel.Fastest);
                    using var src = System.IO.File.OpenRead(absPath);
                    using var dest = entry.Open();
                    await src.CopyToAsync(dest);
                }
            }

            ms.Position = 0;
            var outName = $"CVs_{DateTime.Now:yyyyMMdd_HHmm}.zip";
            return File(ms.ToArray(), "application/zip", outName);
        }
    }
}
