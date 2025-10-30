using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using JobPortal.Areas.Shared.Models;      // AppDbContext, entities
using JobPortal.Areas.Recruiter.Models;   // CandidateVM, NoteVM, OfferFormVM
using JobPortal.Areas.Shared.Extensions;  // TryGetUserId extension

namespace JobPortal.Areas.Recruiter.Controllers
{
    [Area("Recruiter")]
    public class CandidatesController : Controller
    {
        private readonly AppDbContext _db;
        public CandidatesController(AppDbContext db) => _db = db;

        // GET: /Recruiter/Candidates/Detail/{id}
        // id = job_application.application_id
        [HttpGet]
        public async Task<IActionResult> Detail(int id)
        {
            // Ensure logged-in and fetch recruiterId from session
            if (!this.TryGetUserId(out var recruiterId, out var early)) return early!;

            var app = await _db.job_applications
                .Include(a => a.user)
                .FirstOrDefaultAsync(a => a.application_id == id);

            if (app == null) return NotFound();

            var fullName = $"{app.user.first_name} {app.user.last_name}".Trim();
            var vm = new CandidateVM(
                ApplicationId: app.application_id,
                UserId: app.user_id,
                Name: string.IsNullOrWhiteSpace(fullName) ? $"User #{app.user_id}" : fullName,
                Email: app.user.email,
                Phone: "000-0000000",           // placeholder until phone is stored
                Summary: "Generalist profile.", // placeholder; wire to resume/notes later
                Status: (app.application_status ?? "Submitted").Trim()
            );

            // Load conversation/notes for this application (newest first)
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

            // Prefill an empty OfferFormVM for the modal
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

        // Mark as Hired (used after Offer is accepted, or recruiter confirms)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public Task<IActionResult> Hire(int id)
        {
            if (!this.TryGetUserId(out _, out var early)) return Task.FromResult(early!);
            return SetStatus(id, "Hired");
        }

        // ==========================
        // Send Offer (EF insert)
        //  - Creates job_offer (status 'Sent') with GUID token
        //  - Moves application_status to 'Offer'
        //  - Posts a short summary note
        // ==========================
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

            // FIX 1: use Guid, not string
            var token = Guid.NewGuid();
            var now = DateTime.Now;

            // FIX 2: convert DateTime? -> DateOnly? for start_date
            DateOnly? startDate = vm.StartDate.HasValue
                ? DateOnly.FromDateTime(vm.StartDate.Value)
                : (DateOnly?)null;

            await using var tx = await _db.Database.BeginTransactionAsync();

            // 1) Create the offer (EF entity)
            var offer = new job_offer
            {
                application_id = vm.ApplicationId,
                offer_status = "Sent",
                salary_offer = vm.SalaryOffer,
                start_date = startDate,        // DateOnly?
                contract_type = vm.ContractType,
                notes = vm.Notes,
                candidate_token = token,       // Guid
                date_sent = now
            };
            _db.job_offers.Add(offer);

            // 2) Flip application to Offer
            app.application_status = "Offer";
            app.date_updated = now;

            // 3) Log a short summary note for context
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

        // Central helper so status-change logic is in one place
        private async Task<IActionResult> SetStatus(int applicationId, string nextStatus)
        {
            var app = await _db.job_applications
                .FirstOrDefaultAsync(a => a.application_id == applicationId);

            if (app == null) return NotFound();

            // Allowed values: 'Submitted','AI-Screened','Shortlisted','Interview','Offer','Hired','Rejected'
            app.application_status = nextStatus;
            app.date_updated = DateTime.Now;

            await _db.SaveChangesAsync();

            TempData["Message"] = $"Application #{applicationId} moved to {nextStatus}.";
            return RedirectToAction(nameof(Detail), new { id = applicationId });
        }

        // ---- Quick message posts into job_seeker_note ----
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
