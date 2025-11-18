// File: Areas/Admin/Controllers/AIController.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using JobPortal.Areas.Shared.Models;
using JobPortal.Areas.Shared.AI;
using JobPortal.Areas.Shared.Options;
using JobPortal.Areas.Shared.Extensions; // TryGetUserId
using JobPortal.Areas.Admin.Models;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;

namespace JobPortal.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class AIController : Controller
    {
        private readonly AppDbContext _db;
        private readonly AiOrchestrator _ai;
        private readonly ILanguageService _lang;

        private readonly OpenAIOptions _opts;
        private readonly IOpenAIClient _openai;

        public AIController(
            AppDbContext db,
            AiOrchestrator ai,
            ILanguageService lang,

            IOptions<OpenAIOptions> opts,
            IOpenAIClient openai)
        {
            _db = db;
            _ai = ai;
            _lang = lang;

            _opts = opts.Value;
            _openai = openai;
        }

        // -------------------------------
        // Templates → pull real job rows
        // -------------------------------
        public async Task<IActionResult> Templates(CancellationToken ct)
        {
            ViewData["Title"] = "AI Criteria Templates";

            var list = await _db.job_listings
                .OrderByDescending(j => j.date_posted)            // FIX: date_posted
                .Take(25)
                .Select(j => new AiTemplate
                {
                    Name = j.job_title ?? $"Job #{j.job_listing_id}",
                    RoleExample = j.company.company_name ?? "—",   // navigate to company
                    Updated = j.date_posted,                       // date_posted
                    JobId = j.job_listing_id,
                    Requirements = j.job_requirements ?? ""
                })
                .ToListAsync(ct);

            ViewBag.Templates = list;
            return View();
        }

        // -------------------------------
        // Scoring → AI Dry-Run evaluator
        // -------------------------------
        [HttpGet]
        public IActionResult Scoring()
        {
            ViewData["Title"] = "AI Scoring & Dry-Run";
            return View(new AiDryRunVM
            {
                JobTitle = "Backend Developer",
                JobRequirements = "Python\nSQL\n3+ years backend\nBachelor's degree",
                ResumeJson =
                    "{\"basics\":{\"name\":\"Jane Doe\",\"email\":\"jane@example.com\"},\"experience\":[{\"title\":\"Software Engineer\",\"company\":\"Acme\",\"years\":3.5,\"highlights\":[\"Built REST APIs in Python\",\"Wrote SQL queries for reporting\"]}],\"skills\":{\"hard\":[\"Python\",\"SQL\",\"Django\"],\"soft\":[]},\"education\":[{\"degree\":\"BSc CS\",\"year\":2021}],\"certs\":[],\"languages\":[\"English\"],\"meta\":{\"source_file\":\"\",\"ocr_confidence\":0.0}}"
            });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Scoring(AiDryRunVM vm, CancellationToken ct)
        {
            ViewData["Title"] = "AI Scoring & Dry-Run";
            if (!ModelState.IsValid) return View(vm);

            try
            {
                var reqs = (vm.JobRequirements ?? string.Empty)
                    .Replace("\r", "\n")
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Take(20)
                    .ToArray();

                var result = await _ai.EvaluateAsync(vm.JobTitle ?? "Job", reqs, vm.ResumeJson ?? "{}", ct);
                vm.Score = result.MatchScore;
                vm.Explanation = result.Explanation;

                TempData["AiToast"] = $"Dry-run OK. Score {vm.Score}.";
            }
            catch (Exception ex)
            {
                vm.Error = ex.Message;
                TempData["AiToast"] = "Dry-run failed: " + ex.Message;
            }

            return View(vm);
        }

        // -------------------------------
        // Parsing rules → persist as audit
        // -------------------------------
        [HttpGet]
        public IActionResult ParsingRules()
        {
            ViewData["Title"] = "Parsing & Confidence Rules";
            return View();
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveParsingRules(bool highlight, bool manualReview, bool allowUploadPdf, CancellationToken ct)
        {
            // Capture admin user id (uses your existing helper)
            if (!this.TryGetUserId(out var adminId, out var early)) return early!;

            await _db.admin_logs.AddAsync(new admin_log
            {
                user_id = adminId,
                action_type = "ParsingRulesUpdated",
                timestamp = DateTime.Now
            }, ct);

            // Optional: notify
            await _db.notifications.AddAsync(new notification
            {
                user_id = adminId,
                notification_title = "Parsing rules updated",
                notification_msg = $"highlight={highlight}; manualReview={manualReview}; allowUploadPdf={allowUploadPdf}",
                notification_read_status = false,
                notification_date_created = DateTime.Now
            }, ct);

            await _db.SaveChangesAsync(ct);

            TempData["AiToast"] = "Parsing rules saved (logged).";
            return RedirectToAction(nameof(ParsingRules));
        }

        // -------------------------------
        // Fairness → persist as audit
        // -------------------------------
        [HttpGet]
        public IActionResult Fairness()
        {
            ViewData["Title"] = "Bias & Fairness Guardrails";
            return View();
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveFairness(bool blindMode, bool biasReport, CancellationToken ct)
        {
            if (!this.TryGetUserId(out var adminId, out var early)) return early!;

            await _db.admin_logs.AddAsync(new admin_log
            {
                user_id = adminId,
                action_type = "FairnessUpdated",
                timestamp = DateTime.Now
            }, ct);

            await _db.notifications.AddAsync(new notification
            {
                user_id = adminId,
                notification_title = "Fairness settings updated",
                notification_msg = $"blindMode={blindMode}; biasReport={biasReport}",
                notification_read_status = false,
                notification_date_created = DateTime.Now
            }, ct);

            await _db.SaveChangesAsync(ct);

            TempData["AiToast"] = "Fairness settings saved (logged).";
            return RedirectToAction(nameof(Fairness));
        }

        // -------------------------------
        // Health page: pings both models
        // -------------------------------
        [HttpGet]
        public async Task<IActionResult> Health(CancellationToken ct)
        {
            var vm = new AiHealthVM
            {
                ModelText = _opts.ModelText ?? "gpt-4o-mini",
                ModelEmbed = _opts.ModelEmbed ?? "text-embedding-3-small"
            };

            // Text model ping
            try
            {
                var t = await _openai.PingTextAsync(ct);
                vm.TextOk = t.ok;
                vm.TextLatencyMs = t.ms;
                vm.TextMessage = t.msg;
                vm.TextStatus = t.status;
                vm.TextRequestId = t.reqId;
            }
            catch (Exception ex)
            {
                vm.TextOk = false;
                vm.TextLatencyMs = 0;
                vm.TextMessage = ex.Message;
                vm.TextStatus = 0;
                vm.TextRequestId = null;
            }

            // Embedding model ping
            try
            {
                var e = await _openai.PingEmbedAsync(ct);
                vm.EmbedOk = e.ok;
                vm.EmbedLatencyMs = e.ms;
                vm.EmbedMessage = e.msg;
                vm.EmbedStatus = e.status;
                vm.EmbedRequestId = e.reqId;
            }
            catch (Exception ex)
            {
                vm.EmbedOk = false;
                vm.EmbedLatencyMs = 0;
                vm.EmbedMessage = ex.Message;
                vm.EmbedStatus = 0;
                vm.EmbedRequestId = null;
            }

            vm.OverallOk = vm.TextOk && vm.EmbedOk;
            ViewData["Title"] = "AI Health";
            return View(vm);
        }

    }
}
