// File: Areas/Recruiter/Controllers/AiEvaluationController.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using JobPortal.Areas.Shared.AI;
using JobPortal.Areas.Shared.Extensions; // TryGetUserId
using JobPortal.Areas.Shared.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace JobPortal.Areas.Recruiter.Controllers
{
    [Area("Recruiter")]
    public class AiEvaluationController : Controller
    {
        private readonly AppDbContext _db;
        private readonly AiOrchestrator _ai;
        private readonly OpenAIResumeParser _parser;

        public AiEvaluationController(AppDbContext db, AiOrchestrator ai, OpenAIResumeParser parser)
        {
            _db = db;
            _ai = ai;
            _parser = parser;
        }

        // NOTE: kept for backward compatibility (no longer used by UI)
        [HttpGet]
        public async Task<IActionResult> ParsedJson(int applicationId, CancellationToken ct)
        {
            if (!this.TryGetUserId(out var recruiterId, out var early)) return early!;

            var app = await _db.job_applications
                .Include(a => a.job_listing)
                .FirstOrDefaultAsync(a => a.application_id == applicationId, ct);

            if (app == null) return NotFound();
            if (app.job_listing?.user_id != recruiterId) return Forbid();

            var json = await GetOrParseResumeJsonAsync(app, ct);
            return Content(json ?? "{}", "application/json");
        }

        // NEW: read-only Score Breakdown endpoint for Candidate Detail
        // GET: /Recruiter/AiEvaluation/Breakdown?applicationId=123
        [HttpGet]
        public async Task<IActionResult> Breakdown(int applicationId, CancellationToken ct)
        {
            if (!this.TryGetUserId(out var recruiterId, out var early)) return early!;

            var app = await _db.job_applications
                .Include(a => a.job_listing)
                .FirstOrDefaultAsync(a => a.application_id == applicationId, ct);

            if (app == null) return NotFound();
            if (app.job_listing?.user_id != recruiterId) return Forbid();

            var latestResumeId = await _db.resumes
                .Where(r => r.user_id == app.user_id)
                .OrderByDescending(r => r.upload_date)
                .Select(r => (int?)r.resume_id)
                .FirstOrDefaultAsync(ct);

            if (!latestResumeId.HasValue) return Json(new { ok = false });

            var eval = await _db.ai_resume_evaluations
                .Where(e => e.resume_id == latestResumeId.Value && e.job_listing_id == app.job_listing_id)
                .Select(e => new
                {
                    e.match_score,
                    e.date_evaluated,
                    e.explanation,
                    e.breakdown_json
                })
                .FirstOrDefaultAsync(ct);

            if (eval == null || string.IsNullOrWhiteSpace(eval.breakdown_json))
                return Json(new { ok = false });

            return Content(JsonSerializer.Serialize(new
            {
                ok = true,
                total = eval.match_score ?? 0,
                evaluatedAt = eval.date_evaluated,
                explanation = eval.explanation ?? "",
                breakdown = JsonDocument.Parse(eval.breakdown_json!).RootElement
            }), "application/json");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EvaluateOne(
            int applicationId,
            [FromForm] string? resumeJsonOverride,
            [FromForm] string[]? weight2,
            CancellationToken ct)
        {
            if (!this.TryGetUserId(out var recruiterId, out var early)) return early!;

            var app = await _db.job_applications
                .Include(a => a.job_listing)
                .FirstOrDefaultAsync(a => a.application_id == applicationId, ct);

            if (app == null) return NotFound();
            if (app.job_listing?.user_id != recruiterId) return Forbid();

            var (jobTitle, baseReqs) = ExtractJob(app.job_listing);
            var reqs = ApplyWeights(baseReqs, weight2);

            // Even though UI no longer allows editing, keep override param harmlessly
            var resumeJson = string.IsNullOrWhiteSpace(resumeJsonOverride)
                ? await GetOrParseResumeJsonAsync(app, ct)
                : resumeJsonOverride;

            var result = await _ai.EvaluateAsync(jobTitle, reqs, resumeJson ?? "{}", ct);
            await UpsertEvaluationAsync(app, resumeJson ?? "{}", result, ct);

            TempData["Message"] = $"AI score saved: {result.MatchScore}";
            return RedirectToAction("Pipeline", "Jobs", new { id = app.job_listing_id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EvaluateJob(
            int jobId,
            [FromForm] string[]? weight2,
            CancellationToken ct)
        {
            if (!this.TryGetUserId(out var recruiterId, out var early)) return early!;

            var job = await _db.job_listings.FirstOrDefaultAsync(j => j.job_listing_id == jobId, ct);
            if (job == null) return NotFound();
            if (job.user_id != recruiterId) return Forbid();

            var (jobTitle, baseReqs) = ExtractJob(job);
            var reqs = ApplyWeights(baseReqs, weight2);

            var apps = await _db.job_applications
                .Where(a => a.job_listing_id == jobId)
                .Select(a => a.application_id)
                .ToListAsync(ct);

            int ok = 0, fail = 0;
            foreach (var appId in apps)
            {
                try
                {
                    var app = await _db.job_applications
                        .Include(a => a.job_listing)
                        .FirstAsync(a => a.application_id == appId, ct);

                    var resumeJson = await GetOrParseResumeJsonAsync(app, ct);
                    var result = await _ai.EvaluateAsync(jobTitle, reqs, resumeJson ?? "{}", ct);

                    await UpsertEvaluationAsync(app, resumeJson ?? "{}", result, ct);
                    ok++;
                }
                catch
                {
                    fail++;
                }
            }

            TempData["Message"] = $"AI evaluated {ok} candidate(s){(fail > 0 ? $", {fail} failed" : "")}.";
            return RedirectToAction("Pipeline", "Jobs", new { id = jobId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RankNote(int applicationId, string direction, string reason, CancellationToken ct)
        {
            if (!this.TryGetUserId(out var recruiterId, out var early)) return early!;

            var app = await _db.job_applications
                .Include(a => a.job_listing)
                .FirstOrDefaultAsync(a => a.application_id == applicationId, ct);

            if (app == null) return NotFound();
            if (app.job_listing?.user_id != recruiterId) return Forbid();

            reason = (reason ?? "").Trim();
            var dir = (direction ?? "").Trim().ToLowerInvariant();
            if (dir != "up" && dir != "down") dir = "up";

            var note = new job_seeker_note
            {
                application_id = app.application_id,
                job_recruiter_id = recruiterId,
                job_seeker_id = app.user_id,
                note_text = $"[AI-Rank-{dir.ToUpper()}] {reason}",
                created_at = MyTime.NowMalaysia()
            };
            _db.job_seeker_notes.Add(note);
            await _db.SaveChangesAsync(ct);

            TempData["Message"] = "Ranking note recorded.";
            return RedirectToAction("Pipeline", "Jobs", new { id = app.job_listing_id });
        }

        // ---- helpers -------------------------------------------------------------

        private static (string title, string[] reqs) ExtractJob(job_listing job)
        {
            static string[] split(string? s) =>
                string.IsNullOrWhiteSpace(s)
                    ? Array.Empty<string>()
                    : s.Replace("\r", "\n")
                       .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var must = split(job.job_requirements);
            var nice = split(job.job_requirements_nice);
            var reqs = must.Concat(nice).Distinct(StringComparer.OrdinalIgnoreCase).Take(20).ToArray();

            return (job.job_title ?? "Job", reqs);
        }

        private static string[] ApplyWeights(string[] reqs, string[]? weight2)
        {
            if (weight2 == null || weight2.Length == 0) return reqs;
            var set = new HashSet<string>(weight2, StringComparer.OrdinalIgnoreCase);

            var boosted = new List<string>(reqs);
            foreach (var r in reqs)
            {
                if (set.Contains(r))
                    boosted.Add(r);
            }
            return boosted.ToArray();
        }

        private async Task UpsertEvaluationAsync(job_application app, string rawParsedJson, AiResult result, CancellationToken ct)
        {
            var resume = await _db.resumes
                .Where(r => r.user_id == app.user_id)
                .OrderByDescending(r => r.upload_date)
                .FirstOrDefaultAsync(ct);

            if (resume == null)
            {
                app.application_status = (result.MatchScore <= 0) ? "Rejected" : "AI-Screened";
                app.date_updated = MyTime.NowMalaysia();
                await _db.SaveChangesAsync(ct);
                return;
            }

            await UpsertParsedAsync(app, resume.resume_id, rawParsedJson, ct);

            var row = await _db.ai_resume_evaluations
                .FirstOrDefaultAsync(e => e.resume_id == resume.resume_id && e.job_listing_id == app.job_listing_id, ct);

            var now = MyTime.NowMalaysia();

            var breakdownJson = JsonSerializer.Serialize(result.Breakdown); // store full breakdown for UI
            if (row == null)
            {
                row = new ai_resume_evaluation
                {
                    resume_id = resume.resume_id,
                    job_listing_id = app.job_listing_id,
                    match_score = result.MatchScore,
                    date_evaluated = now,
                    // NEW
                    breakdown_json = breakdownJson,
                    explanation = string.IsNullOrWhiteSpace(result.Explanation) ? null : result.Explanation
                };
                _db.ai_resume_evaluations.Add(row);
            }
            else
            {
                row.match_score = result.MatchScore;
                row.date_evaluated = now;
                // NEW
                row.breakdown_json = breakdownJson;
                row.explanation = string.IsNullOrWhiteSpace(result.Explanation) ? row.explanation : result.Explanation;
            }

            if (!string.IsNullOrWhiteSpace(result.Explanation))
            {
                var note = new job_seeker_note
                {
                    application_id = app.application_id,
                    job_recruiter_id = app.job_listing?.user_id ?? 0,
                    job_seeker_id = app.user_id,
                    note_text = $"[AI] {result.Explanation}",
                    created_at = now
                };
                _db.job_seeker_notes.Add(note);
            }

            app.application_status = (result.MatchScore <= 0) ? "Rejected" : "AI-Screened";
            app.date_updated = now;

            await _db.SaveChangesAsync(ct);
        }

        private async Task UpsertParsedAsync(job_application app, int resumeId, string parsedJson, CancellationToken ct)
        {
            var row = await _db.ai_parsed_resumes
                .FirstOrDefaultAsync(p => p.resume_id == resumeId && p.job_listing_id == app.job_listing_id, ct);

            if (row == null)
            {
                row = new ai_parsed_resume
                {
                    resume_id = resumeId,
                    user_id = app.user_id,
                    job_listing_id = app.job_listing_id,
                    parsed_json = parsedJson ?? "{}",
                    updated_at = MyTime.NowMalaysia()
                };
                _db.ai_parsed_resumes.Add(row);
            }
            else
            {
                row.parsed_json = parsedJson ?? "{}";
                row.updated_at = MyTime.NowMalaysia();
            }
        }

        // Load saved JSON if exists; else parse PDF now and persist.
        private async Task<string?> GetOrParseResumeJsonAsync(job_application app, CancellationToken ct)
        {
            var latestResume = await _db.resumes
                .Where(r => r.user_id == app.user_id)
                .OrderByDescending(r => r.upload_date)
                .FirstOrDefaultAsync(ct);

            if (latestResume == null)
                return "{}";

            var saved = await _db.ai_parsed_resumes
                .Where(p => p.resume_id == latestResume.resume_id && p.job_listing_id == app.job_listing_id)
                .OrderByDescending(p => p.updated_at)
                .Select(p => p.parsed_json)
                .FirstOrDefaultAsync(ct);

            if (!string.IsNullOrWhiteSpace(saved) && LooksLikeValid(saved!))
                return saved;

            var fullPath = ResolveResumePath(latestResume.file_path);
            if (!System.IO.File.Exists(fullPath))
                return "{}";

            var json = await _parser.ParsePdfAsync(fullPath, ct);
            if (LooksLikeValid(json))
            {
                await UpsertParsedAsync(app, latestResume.resume_id, json ?? "{}", ct);
                await _db.SaveChangesAsync(ct);
            }

            return json;
        }

        private static bool LooksLikeValid(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return false;
            try
            {
                var node = JsonNode.Parse(json) as JsonObject;
                if (node is null) return false;

                bool Has(string k) => node.TryGetPropertyValue(k, out _);
                if (!(Has("basics") && Has("skills") && Has("experience") && Has("education") && Has("meta")))
                    return false;

                var skillsOk = node["skills"] is JsonObject s &&
                               s["hard"] is JsonArray &&
                               s["soft"] is JsonArray;

                var metaOk = node["meta"] is JsonObject m &&
                             m.TryGetPropertyValue("charCount", out _);

                return skillsOk && metaOk;
            }
            catch { return false; }
        }

        // In Areas/Recruiter/Controllers/AiEvaluationController.cs
        private static string ResolveResumePath(string storedPath)
        {
            if (string.IsNullOrWhiteSpace(storedPath)) return storedPath;

            var sp = storedPath.Trim();

            bool webRelative = sp.StartsWith("/") || sp.StartsWith("\\");
            bool hasDrive = sp.Length >= 2 && char.IsLetter(sp[0]) && sp[1] == ':';

            if (!webRelative && Path.IsPathRooted(sp) && global::System.IO.File.Exists(sp))
                return sp;

            var try1 = Path.Combine(
                Directory.GetCurrentDirectory(),
                sp.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                  .Replace('/', Path.DirectorySeparatorChar)
            );
            if (global::System.IO.File.Exists(try1)) return try1;

            var try2 = Path.Combine(
                Directory.GetCurrentDirectory(),
                "wwwroot",
                sp.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                  .Replace('/', Path.DirectorySeparatorChar)
            );
            if (global::System.IO.File.Exists(try2)) return try2;

            if (webRelative && !hasDrive)
            {
                var try3 = Path.Combine(
                    Directory.GetCurrentDirectory(),
                    sp.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                      .Replace('/', Path.DirectorySeparatorChar)
                );
                if (global::System.IO.File.Exists(try3)) return try3;
            }

            return sp;
        }

    }
}
