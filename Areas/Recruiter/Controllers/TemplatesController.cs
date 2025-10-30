using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using JobPortal.Areas.Recruiter.Models;   // TemplateItemVM, TemplateFormVM
using JobPortal.Areas.Shared.Models;      // AppDbContext, template entity
using JobPortal.Areas.Shared.Extensions;  // TryGetUserId extension

namespace JobPortal.Areas.Recruiter.Controllers
{
    [Area("Recruiter")]
    public class TemplatesController : Controller
    {
        private readonly AppDbContext _db;
        public TemplatesController(AppDbContext db) => _db = db;

        // GET: /Recruiter/Templates
        [HttpGet]
        public async Task<IActionResult> Index(int? threadId = null)
        {
            if (!this.TryGetUserId(out var recruiterId, out var early)) return early!;

            ViewData["Title"] = "Message Templates";
            ViewBag.ThreadId = threadId;

            var raw = await _db.templates.AsNoTracking()
                .Where(t => t.template_status == "Active"
                            && !t.template_name.StartsWith("[JOB]")
                            && t.user_id == recruiterId)
                .OrderByDescending(t => t.date_updated)
                .ThenByDescending(t => t.date_created)
                .Select(t => new { t.template_id, t.template_name, t.template_subject, t.template_body })
                .ToListAsync();

            var items = raw.Select(t =>
            {
                var body = t.template_body ?? string.Empty;
                var snippet = body.Length <= 120 ? body : body.Substring(0, 120) + "…";
                return new TemplateItemVM(t.template_id, t.template_name, t.template_subject, snippet);
            }).ToList();

            ViewBag.Items = items;
            return View();
        }

        // Archived list
        [HttpGet]
        public async Task<IActionResult> Archived()
        {
            if (!this.TryGetUserId(out var recruiterId, out var early)) return early!;

            ViewData["Title"] = "Archived Templates";
            ViewBag.IsArchivedList = true;

            var raw = await _db.templates.AsNoTracking()
                .Where(t => t.template_status == "Archived" && t.user_id == recruiterId)
                .OrderByDescending(t => t.date_updated)
                .Select(t => new { t.template_id, t.template_name, t.template_subject, t.template_body })
                .ToListAsync();

            var items = raw.Select(t =>
            {
                var body = t.template_body ?? string.Empty;
                var snippet = body.Length <= 120 ? body : body.Substring(0, 120) + "…";
                return new TemplateItemVM(t.template_id, t.template_name, t.template_subject, snippet);
            }).ToList();

            ViewBag.Items = items;
            return View("Index");
        }

        // Unarchive
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Unarchive(int id)
        {
            if (!this.TryGetUserId(out var recruiterId, out var early)) return early!;

            var row = await _db.templates.FirstOrDefaultAsync(t => t.template_id == id && t.user_id == recruiterId);
            if (row == null) return NotFound();

            row.template_status = "Active";
            row.date_updated = DateTime.Now;
            await _db.SaveChangesAsync();

            TempData["Message"] = "Template restored.";
            return RedirectToAction(nameof(Archived));
        }

        // Prefill and jump to Inbox thread with a merged draft
        [HttpGet]
        public async Task<IActionResult> Fill(int id, int threadId, string? firstName = null, string? jobTitle = null)
        {
            if (!this.TryGetUserId(out var recruiterId, out var early)) return early!;

            var tpl = await _db.templates
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.template_id == id
                                       && t.template_status == "Active"
                                       && t.user_id == recruiterId);
            if (tpl == null) return NotFound();

            var conv = await _db.conversations
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.conversation_id == threadId);

            var fn = !string.IsNullOrWhiteSpace(firstName)
                       ? firstName!
                       : (conv?.candidate_name?.Trim().Split(' ').FirstOrDefault() ?? "there");
            var jt = !string.IsNullOrWhiteSpace(jobTitle)
                       ? jobTitle!
                       : (conv?.job_title ?? string.Empty);
            var co = string.Empty;

            string draft = tpl.template_body ?? "";
            draft = draft.Replace("{{FirstName}}", fn)
                         .Replace("{{JobTitle}}", jt)
                         .Replace("{{Company}}", co);

            // NOTE: we leave {{Date}} and {{Time}} in place so the Thread view can show pickers.
            return RedirectToAction("Thread", "Inbox", new { area = "Recruiter", id = threadId, draft });
        }

        private static string Merge(string text, string firstName, string jobTitle)
            => text.Replace("{{FirstName}}", firstName, StringComparison.OrdinalIgnoreCase)
                   .Replace("{{JobTitle}}", jobTitle, StringComparison.OrdinalIgnoreCase);


        // Create
        [HttpGet]
        public IActionResult Create()
        {
            if (!this.TryGetUserId(out _, out var early)) return early!;
            ViewData["Title"] = "New Template";
            return View(new TemplateFormVM());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(TemplateFormVM vm)
        {
            if (!this.TryGetUserId(out var recruiterId, out var early)) return early!;

            if (!ModelState.IsValid)
            {
                ViewData["Title"] = "New Template";
                return View(vm);
            }

            var row = new template
            {
                user_id = recruiterId,
                template_name = vm.Name.Trim(),
                template_subject = string.IsNullOrWhiteSpace(vm.Subject) ? null : vm.Subject!.Trim(),
                template_body = vm.Body ?? "",
                template_status = vm.Status ?? "Active",
                date_created = DateTime.Now,
                date_updated = DateTime.Now
            };

            _db.templates.Add(row);
            await _db.SaveChangesAsync();

            TempData["Message"] = "Template created.";
            return RedirectToAction(nameof(Index));
        }

        // Edit
        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            if (!this.TryGetUserId(out var recruiterId, out var early)) return early!;

            var row = await _db.templates.FirstOrDefaultAsync(t => t.template_id == id && t.user_id == recruiterId);
            if (row == null) return NotFound();

            var vm = new TemplateFormVM
            {
                TemplateId = row.template_id,
                Name = row.template_name,
                Subject = row.template_subject,
                Body = row.template_body,
                Status = row.template_status ?? "Active"
            };

            ViewData["Title"] = "Edit Template";
            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(TemplateFormVM vm)
        {
            if (!this.TryGetUserId(out var recruiterId, out var early)) return early!;

            if (!vm.TemplateId.HasValue) return NotFound();

            var row = await _db.templates.FirstOrDefaultAsync(t => t.template_id == vm.TemplateId && t.user_id == recruiterId);
            if (row == null) return NotFound();

            if (!ModelState.IsValid)
            {
                ViewData["Title"] = "Edit Template";
                return View(vm);
            }

            row.template_name = vm.Name.Trim();
            row.template_subject = string.IsNullOrWhiteSpace(vm.Subject) ? null : vm.Subject!.Trim();
            row.template_body = vm.Body ?? "";
            row.template_status = vm.Status ?? "Active";
            row.date_updated = DateTime.Now;

            await _db.SaveChangesAsync();

            TempData["Message"] = "Template updated.";
            return RedirectToAction(nameof(Index));
        }

        // Archive
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Archive(int id)
        {
            if (!this.TryGetUserId(out var recruiterId, out var early)) return early!;

            var row = await _db.templates.FirstOrDefaultAsync(t => t.template_id == id && t.user_id == recruiterId);
            if (row == null) return NotFound();

            row.template_status = "Archived";
            row.date_updated = DateTime.Now;

            await _db.SaveChangesAsync();

            TempData["Message"] = "Template archived.";
            return RedirectToAction(nameof(Index));
        }

        // NEW: QuickSave supports Save & Use
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> QuickSave(string name, string? subject, string body, int? threadId, bool useNow = false)
        {
            if (!this.TryGetUserId(out var recruiterId, out var early)) return early!;
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(body))
            {
                TempData["Message"] = "Template name and body are required.";
                return RedirectToAction("Thread", "Inbox", new { area = "Recruiter", id = threadId });
            }

            var row = new template
            {
                user_id = recruiterId,
                template_name = name.Trim(),
                template_subject = string.IsNullOrWhiteSpace(subject) ? null : subject!.Trim(),
                template_body = body,
                template_status = "Active",
                date_created = DateTime.Now,
                date_updated = DateTime.Now
            };

            _db.templates.Add(row);
            await _db.SaveChangesAsync();

            TempData["Message"] = useNow ? "Template saved and inserted." : "Template saved.";

            // Save & Use → return with composer prefilled
            if (useNow && threadId.HasValue)
                return RedirectToAction("Thread", "Inbox", new { area = "Recruiter", id = threadId.Value, draft = body });

            // Normal save → just return to thread (no draft)
            return RedirectToAction("Thread", "Inbox", new { area = "Recruiter", id = threadId });
        }
    }
}
