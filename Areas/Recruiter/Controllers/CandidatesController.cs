using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using JobPortal.Areas.Shared.Models;      // AppDbContext, entities
using JobPortal.Areas.Recruiter.Models;   // CandidateItemVM, CandidateVM, NoteVM, OfferFormVM, CandidatesIndexVM
using JobPortal.Areas.Shared.Extensions;  // TryGetUserId extension
using System.Collections.Generic;

namespace JobPortal.Areas.Recruiter.Controllers
{
    [Area("Recruiter")]
    public class CandidatesController : Controller
    {
        private const int DefaultPageSize = 20;
        private const int MaxPageSize = 100;
        private const int CsvExportCap = 5000;

        private readonly AppDbContext _db;
        public CandidatesController(AppDbContext db) => _db = db;

        // GET /Recruiter/Candidates?q=&stage=&page=&pageSize=
        [HttpGet]
        public async Task<IActionResult> Index(string? q, string? stage, int page = 1, int pageSize = DefaultPageSize)
        {
            if (!this.TryGetUserId(out var recruiterId, out var early)) return early!;

            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 5, MaxPageSize);

            var baseQuery = BuildQuery(recruiterId, q, stage);

            var totalCount = await baseQuery.CountAsync();
            var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
            if (page > totalPages) page = totalPages;

            var skip = (page - 1) * pageSize;

            // two-step projection to avoid provider ToString translation issues
            var rows = await baseQuery
                .OrderByDescending(a => a.date_updated)
                .Skip(skip)
                .Take(pageSize)
                .Select(a => new
                {
                    a.application_id,
                    CandidateName = (a.user.first_name + " " + a.user.last_name).Trim(),
                    a.application_status,
                    JobTitle = a.job_listing.job_title,
                    a.date_updated
                })
                .AsNoTracking()
                .ToListAsync();

            var items = rows.Select(r => new CandidateItemVM(
                Id: r.application_id,
                Name: string.IsNullOrWhiteSpace(r.CandidateName) ? $"User" : r.CandidateName,
                Stage: r.application_status ?? "Submitted",
                Score: 0, // hook for AI
                AppliedAt: r.date_updated.ToString("yyyy-MM-dd"),
                LowConfidence: false,
                Override: false
            )).ToList();

            var vm = new CandidatesIndexVM
            {
                Items = items,
                Page = page,
                PageSize = pageSize,
                TotalCount = totalCount,
                TotalPages = totalPages,
                Query = q ?? string.Empty,
                Stage = stage ?? string.Empty
            };

            return View(vm);
        }

        // GET /Recruiter/Candidates/Export?q=&stage=
        [HttpGet]
        public async Task<IActionResult> Export(string? q, string? stage)
        {
            if (!this.TryGetUserId(out var recruiterId, out var early)) return early!;

            var baseQuery = BuildQuery(recruiterId, q, stage);

            var rows = await baseQuery
                .OrderByDescending(a => a.date_updated)
                .Take(CsvExportCap) // guardrail
                .Select(a => new
                {
                    a.application_id,
                    CandidateName = (a.user.first_name + " " + a.user.last_name).Trim(),
                    a.user.email,
                    a.application_status,
                    JobTitle = a.job_listing.job_title,
                    a.date_updated
                })
                .AsNoTracking()
                .ToListAsync();

            var sb = new StringBuilder();
            sb.AppendLine("application_id,candidate_name,email,stage,job_title,date_updated");
            foreach (var r in rows)
            {
                sb.AppendLine(string.Join(",", new[]
                {
                    Csv(r.application_id.ToString()),
                    Csv(r.CandidateName),
                    Csv(r.email),
                    Csv(r.application_status ?? "Submitted"),
                    Csv(r.JobTitle),
                    Csv(r.date_updated.ToString("yyyy-MM-dd HH:mm:ss"))
                }));
            }

            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
            var fileName = $"candidates_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv";
            return File(bytes, "text/csv", fileName);
        }

        // ---------------- existing actions below (unchanged except formatting) ----------------

        // GET: /Recruiter/Candidates/Detail/{id}
        [HttpGet]
        public async Task<IActionResult> Detail(int id)
        {
            if (!this.TryGetUserId(out var recruiterId, out var early)) return early!;

            var app = await _db.job_applications
                .Include(a => a.user)
                .Include(a => a.job_listing)
                .FirstOrDefaultAsync(a => a.application_id == id);

            if (app == null) return NotFound();

            var fullName = $"{app.user.first_name} {app.user.last_name}".Trim();
            var vm = new CandidateVM(
                ApplicationId: app.application_id,
                UserId: app.user_id,
                Name: string.IsNullOrWhiteSpace(fullName) ? $"User #{app.user_id}" : fullName,
                Email: app.user.email,
                Phone: "000-0000000",
                Summary: "Generalist profile.",
                Status: (app.application_status ?? "Submitted").Trim()
            );

            var raw = await _db.job_seeker_notes
                .Where(n => n.application_id == id)
                .Include(n => n.job_recruiter)
                .Include(n => n.job_seeker)
                .OrderByDescending(n => n.created_at)
                .Select(n => new
                {
                    n.note_id,
                    n.note_text,
                    n.created_at,
                    n.job_recruiter_id,
                    RecruiterFirst = n.job_recruiter.first_name,
                    RecruiterLast = n.job_recruiter.last_name,
                    SeekerFirst = n.job_seeker.first_name,
                    SeekerLast = n.job_seeker.last_name
                })
                .AsNoTracking()
                .ToListAsync();

            var notes = raw.Select(n =>
            {
                var isFromRecruiter = n.job_recruiter_id == recruiterId;
                var author = (isFromRecruiter
                    ? $"{n.RecruiterFirst} {n.RecruiterLast}"
                    : $"{n.SeekerFirst} {n.SeekerLast}").Trim();

                return new NoteVM(
                    n.note_id,
                    author,
                    n.note_text,
                    n.created_at.ToString("yyyy-MM-dd HH:mm"),
                    isFromRecruiter
                );
            }).ToList();

            ViewData["Title"] = $"Candidate #{vm.ApplicationId}";
            ViewBag.Profile = vm;
            ViewBag.Messages = notes;

            ViewBag.OfferForm = new OfferFormVM
            {
                ApplicationId = vm.ApplicationId,
                ContractType = "Full-time"
            };

            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public Task<IActionResult> Shortlist(int id)
        {
            if (!this.TryGetUserId(out _, out var early)) return Task.FromResult(early!);
            return SetStatus(id, "Shortlisted");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public Task<IActionResult> Interview(int id)
        {
            if (!this.TryGetUserId(out _, out var early)) return Task.FromResult(early!);
            return SetStatus(id, "Interview");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public Task<IActionResult> Reject(int id)
        {
            if (!this.TryGetUserId(out _, out var early)) return Task.FromResult(early!);
            return SetStatus(id, "Rejected");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public Task<IActionResult> Hire(int id)
        {
            if (!this.TryGetUserId(out _, out var early)) return Task.FromResult(early!);
            return SetStatus(id, "Hired");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SendOffer(OfferFormVM vm)
        {
            if (!this.TryGetUserId(out var recruiterId, out var early)) return early!;

            if (!ModelState.IsValid)
            {
                TempData["Message"] = "Offer form has validation errors.";
                return RedirectToAction(nameof(Detail), new { id = vm.ApplicationId });
            }

            var app = await _db.job_applications
                .FirstOrDefaultAsync(a => a.application_id == vm.ApplicationId);

            if (app == null) return NotFound();

            var token = Guid.NewGuid();
            var now = DateTime.Now;

            DateOnly? startDate = vm.StartDate.HasValue
                ? DateOnly.FromDateTime(vm.StartDate.Value)
                : (DateOnly?)null;

            await using var tx = await _db.Database.BeginTransactionAsync();

            var offer = new job_offer
            {
                application_id = vm.ApplicationId,
                offer_status = "Sent",
                salary_offer = vm.SalaryOffer,
                start_date = startDate,
                contract_type = vm.ContractType,
                notes = vm.Notes,
                candidate_token = token,
                date_sent = now
            };
            _db.job_offers.Add(offer);

            app.application_status = "Offer";
            app.date_updated = now;

            var offerLines = new[]
            {
                "=== Offer Sent ===",
                vm.SalaryOffer.HasValue ? $"Salary: {vm.SalaryOffer.Value:N2}" : "Salary: (not specified)",
                vm.StartDate.HasValue ? $"Start Date: {vm.StartDate:yyyy-MM-dd}" : "Start Date: (not specified)",
                !string.IsNullOrWhiteSpace(vm.ContractType) ? $"Contract: {vm.ContractType}" : "Contract: (not specified)",
                string.IsNullOrWhiteSpace(vm.Notes) ? null : $"Notes: {vm.Notes}"
            }.Where(x => x != null);

            var note = new job_seeker_note
            {
                application_id = vm.ApplicationId,
                job_recruiter_id = recruiterId,
                job_seeker_id = app.user_id,
                note_text = string.Join("\n", offerLines),
                created_at = now
            };
            _db.job_seeker_notes.Add(note);

            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            TempData["Message"] = $"Offer sent for Application #{vm.ApplicationId}.";
            return RedirectToAction(nameof(Detail), new { id = vm.ApplicationId });
        }

        // ---------------- helpers ----------------

        private IQueryable<job_application> BuildQuery(int recruiterId, string? q, string? stage)
        {
            var myJobIds = _db.job_listings
                .Where(j => j.user_id == recruiterId)
                .Select(j => j.job_listing_id);

            var apps = _db.job_applications
                .Where(a => myJobIds.Contains(a.job_listing_id))
                .Include(a => a.user)
                .Include(a => a.job_listing)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(stage))
                apps = apps.Where(a => a.application_status == stage);

            if (!string.IsNullOrWhiteSpace(q))
            {
                var qTrim = q.Trim();
                apps = apps.Where(a =>
                    (a.user.first_name + " " + a.user.last_name).Contains(qTrim) ||
                    a.user.email.Contains(qTrim) ||
                    a.job_listing.job_title.Contains(qTrim));
            }

            return apps;
        }

        private static string Csv(string? s)
        {
            if (s == null) return "";
            // why: minimal RFC4180 escaping
            var needsQuote = s.Contains(',') || s.Contains('"') || s.Contains('\n') || s.Contains('\r');
            s = s.Replace("\"", "\"\"");
            return needsQuote ? $"\"{s}\"" : s;
        }

        private async Task<IActionResult> SetStatus(int applicationId, string nextStatus)
        {
            var app = await _db.job_applications
                .FirstOrDefaultAsync(a => a.application_id == applicationId);

            if (app == null) return NotFound();

            app.application_status = nextStatus;
            app.date_updated = DateTime.Now;

            await _db.SaveChangesAsync();

            TempData["Message"] = $"Application #{applicationId} moved to {nextStatus}.";
            return RedirectToAction(nameof(Detail), new { id = applicationId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SendMessage(int id, string text)
        {
            if (!this.TryGetUserId(out var recruiterId, out var early)) return early!;

            if (string.IsNullOrWhiteSpace(text))
            {
                TempData["Message"] = "Message is empty.";
                return RedirectToAction(nameof(Detail), new { id });
            }

            var app = await _db.job_applications
                .FirstOrDefaultAsync(a => a.application_id == id);
            if (app == null) return NotFound();

            var note = new job_seeker_note
            {
                application_id = id,
                job_recruiter_id = recruiterId,
                job_seeker_id = app.user_id,
                note_text = text,
                created_at = DateTime.Now
            };

            _db.job_seeker_notes.Add(note);
            await _db.SaveChangesAsync();

            TempData["Message"] = "Message sent.";
            return RedirectToAction(nameof(Detail), new { id });
        }
    }
}
