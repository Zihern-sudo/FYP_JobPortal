using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using JobPortal.Areas.Shared.Models;      // AppDbContext, entities (job_application, job_offer, job_seeker_note)
using JobPortal.Areas.Recruiter.Models;   // CandidateVM, NoteVM, OfferFormVM
using System;

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
            var recruiterId = 3; // TODO: replace with logged-in recruiter id

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
        public Task<IActionResult> Shortlist(int id) => SetStatus(id, "Shortlisted");

        [HttpPost]
        [ValidateAntiForgeryToken]
        public Task<IActionResult> Interview(int id) => SetStatus(id, "Interview");

        [HttpPost]
        [ValidateAntiForgeryToken]
        public Task<IActionResult> Reject(int id) => SetStatus(id, "Rejected");

        // Mark as Hired (used after Offer is accepted, or recruiter confirms)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public Task<IActionResult> Hire(int id) => SetStatus(id, "Hired");

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
            var recruiterId = 3; // TODO: replace with logged-in recruiter id

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
                start_date = startDate,        // <-- DateOnly?
                contract_type = vm.ContractType,
                notes = vm.Notes,
                candidate_token = token,       // <-- Guid
                date_sent = now
            };
            _db.job_offers.Add(offer);

            // 2) Flip application to Offer
            app.application_status = "Offer";
            app.date_updated = now;

            // 3) Log a short summary note for context (optional but helpful)
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

        // Central helper so logic is in one place
        private async Task<IActionResult> SetStatus(int applicationId, string nextStatus)
        {
            var app = await _db.job_applications
                .FirstOrDefaultAsync(a => a.application_id == applicationId);

            if (app == null) return NotFound();

            // DB enum values expected: 'Submitted','AI-Screened','Shortlisted','Interview','Offer','Hired','Rejected'
            app.application_status = nextStatus;
            app.date_updated = System.DateTime.Now;

            await _db.SaveChangesAsync();

            TempData["Message"] = $"Application #{applicationId} moved to {nextStatus}.";
            return RedirectToAction(nameof(Detail), new { id = applicationId });
        }

        // ---- Quick message posts into job_seeker_note ----
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SendMessage(int id, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                TempData["Message"] = "Message is empty.";
                return RedirectToAction(nameof(Detail), new { id });
            }

            var app = await _db.job_applications
                .FirstOrDefaultAsync(a => a.application_id == id);
            if (app == null) return NotFound();

            var recruiterId = 3; // TODO: replace with logged-in recruiter id

            var note = new job_seeker_note
            {
                application_id = id,
                job_recruiter_id = recruiterId,
                job_seeker_id = app.user_id,
                note_text = text,
                created_at = System.DateTime.Now
            };

            _db.job_seeker_notes.Add(note);
            await _db.SaveChangesAsync();

            TempData["Message"] = "Message sent.";
            return RedirectToAction(nameof(Detail), new { id });
        }
    }
}
