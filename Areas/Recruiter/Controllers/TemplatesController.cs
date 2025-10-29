using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using JobPortal.Areas.Recruiter.Models;   // TemplateItemVM, TemplateFormVM
using JobPortal.Areas.Shared.Models;      // AppDbContext, template entity

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
            ViewData["Title"] = "Message Templates";
            ViewBag.ThreadId = threadId;

            int? userId = null; // TODO: set when auth is wired

            var query = _db.templates.AsNoTracking()
                        .Where(t => t.template_status == "Active" &&
                                    !t.template_name.StartsWith("[JOB]")); // message-only

            if (userId.HasValue) query = query.Where(t => t.user_id == userId.Value);

            var raw = await query
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

        // === New: Archived list
        [HttpGet]
        public async Task<IActionResult> Archived()
        {
            ViewData["Title"] = "Archived Templates";
            ViewBag.IsArchivedList = true;   // <-- add this line

            var raw = await _db.templates.AsNoTracking()
                .Where(t => t.template_status == "Archived")
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
            return View("Index"); // reuse
        }

        // === New: Unarchive
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Unarchive(int id)
        {
            var row = await _db.templates.FirstOrDefaultAsync(t => t.template_id == id);
            if (row == null) return NotFound();
            row.template_status = "Active";
            row.date_updated = DateTime.Now;
            await _db.SaveChangesAsync();
            TempData["Message"] = "Template restored.";
            return RedirectToAction(nameof(Archived));
        }

        [HttpGet]
        public async Task<IActionResult> Fill(int id, int threadId, string? firstName = null, string? jobTitle = null)
        {
            // 1) Load the active Message template
            var tpl = await _db.templates
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.template_id == id
                                       && t.template_status == "Active");
                                       //&& t.template_type == "Message"); --> Haven't add to Database
            if (tpl == null) return NotFound();

            // 2) Best-effort context: prefer DB values from conversations, fallback to query
            var conv = await _db.conversations
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.conversation_id == threadId);

            var fn = !string.IsNullOrWhiteSpace(firstName)
                       ? firstName!
                       : (conv?.candidate_name?.Trim().Split(' ').FirstOrDefault() ?? "there");
            var jt = !string.IsNullOrWhiteSpace(jobTitle)
                       ? jobTitle!
                       : (conv?.job_title ?? string.Empty);
            var co = string.Empty; // fill from your model later if you add it

            // 3) Simple token merge
            string draft = tpl.template_body ?? "";
            draft = draft.Replace("{{FirstName}}", fn)
                         .Replace("{{JobTitle}}", jt)
                         .Replace("{{Company}}", co);

            // 4) Redirect back with the draft prefilled
            return RedirectToAction("Thread", "Inbox", new { area = "Recruiter", id = threadId, draft });
        }


        private static string Merge(string text, string firstName, string jobTitle)
            => text.Replace("{{FirstName}}", firstName, StringComparison.OrdinalIgnoreCase)
                   .Replace("{{JobTitle}}", jobTitle, StringComparison.OrdinalIgnoreCase);

        // ===== existing create/edit/archive kept the same =====

        [HttpGet]
        public IActionResult Create()
        {
            ViewData["Title"] = "New Template";
            return View(new TemplateFormVM());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(TemplateFormVM vm)
        {
            if (!ModelState.IsValid)
            {
                ViewData["Title"] = "New Template";
                return View(vm);
            }

            var recruiterId = 3; // TODO
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

        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var recruiterId = 3; // TODO
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
            if (!vm.TemplateId.HasValue) return NotFound();

            var recruiterId = 3; // TODO
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






        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Archive(int id)
        {
            var recruiterId = 3; // TODO
            var row = await _db.templates.FirstOrDefaultAsync(t => t.template_id == id && t.user_id == recruiterId);
            if (row == null) return NotFound();

            row.template_status = "Archived";
            row.date_updated = DateTime.Now;
            await _db.SaveChangesAsync();
            TempData["Message"] = "Template archived.";
            return RedirectToAction(nameof(Index));
        }







    }
}
