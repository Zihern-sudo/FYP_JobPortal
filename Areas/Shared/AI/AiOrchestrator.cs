using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace JobPortal.Areas.Shared.AI
{
    public sealed class AiOrchestrator
    {
        private readonly ILanguageService _lang;
        private readonly IOpenAIClient _client; // OPENAI entry
        // IScoringService is no longer required for the match score; kept only if used elsewhere.
        // Remove it from DI if unused across the solution.

        public AiOrchestrator(ILanguageService lang, IOpenAIClient client)
        {
            _lang = lang;
            _client = client;
        }

        public async Task<AiResult> EvaluateAsync(
            string jobTitle,
            string[] jobRequirements,
            string resumeCompactJson,
            CancellationToken ct = default)
        {
            // 1) Normalise resume JSON (robust to malformed input)
            var normalised = await _lang.NormaliseResumeJsonAsync(resumeCompactJson, ct);

            // 2) Extract concise signals to help the LLM ground its judgement
            var signals = ExtractSignals(normalised);

            // 3) Ask OpenAI for an end-to-end score + rationale
            var (score, topHits, gaps, notes) =
                await ScoreWithOpenAIAsync(jobTitle, jobRequirements, normalised, signals, ct);

            // 4) Explanation (prefer model notes; fallback to short builder)
            string explanation = !string.IsNullOrWhiteSpace(notes)
                ? notes.Trim()
                : await _lang.BuildExplanationAsync(jobTitle, topHits, gaps, ct);

            // UI guard: keep explanations short
            if (explanation.Length > 400) explanation = explanation[..400];

            // 5) Return
            return new AiResult
            {
                MatchScore = (byte)Math.Clamp(score, 0, 100),
                Explanation = explanation.Trim(),
                NormalisedResumeJson = normalised
            };
        }

        private async Task<(int score, List<string> topHits, List<string> gaps, string notes)>
            ScoreWithOpenAIAsync(string jobTitle, string[] reqs, string resumeJson, string[] signals, CancellationToken ct)
        {
            // System prompt keeps output deterministic and machine-readable.
            var sys = """
                You are a hiring evaluator. Score resume-vs-job strictly and fairly.
                Output STRICT JSON ONLY. No prose, no Markdown.
                JSON schema:
                {
                  "score": integer 0-100,  // holistic fit for this job
                  "top_hits": string[],    // up to 5 best-matched requirements (verifiable from resume)
                  "gaps": string[],        // up to 5 clear gaps or weak evidence
                  "notes": string          // 1-2 short sentences, neutral recruiter tone
                }
                Rules:
                - Calibrate: 50 = partial fit; 70 = good; 85 = strong; 95+ = exceptional.
                - Penalise missing must-haves and shallow evidence.
                - Do not hallucinate. If unclear, treat as a gap.
                - Keep "notes" concise (<= 220 chars).
            """;

            // User payload: job + normalised resume + extracted signals to help grounding.
            var payload = new
            {
                job = new { title = jobTitle, requirements = reqs },
                resume = TryParse(resumeJson),
                signals = signals
            };
            var user = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = false });

            string raw;
            try
            {
                raw = await _client.CompleteAsync(sys, user, ct);
            }
            catch
            {
                // Network/API issue: conservative default
                return (0, new List<string>(), new List<string> { "API error while scoring" }, "Automatic scoring failed. No reliable evidence detected.");
            }

            // Some models wrap JSON in ```json fences; strip safely.
            raw = StripCodeFence(raw).Trim();

            try
            {
                using var doc = JsonDocument.Parse(raw);
                var root = doc.RootElement;

                int score = root.TryGetProperty("score", out var s) && s.ValueKind == JsonValueKind.Number
                    ? Math.Clamp(s.GetInt32(), 0, 100)
                    : 0;

                List<string> top = root.TryGetProperty("top_hits", out var th) && th.ValueKind == JsonValueKind.Array
                    ? th.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).Take(5).ToList()
                    : new();

                List<string> gaps = root.TryGetProperty("gaps", out var gp) && gp.ValueKind == JsonValueKind.Array
                    ? gp.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).Take(5).ToList()
                    : new();

                string notes = root.TryGetProperty("notes", out var nt) ? (nt.GetString() ?? "") : "";

                return (score, top, gaps, notes);
            }
            catch
            {
                // Unparseable model output → be safe and predictable
                return (0, new List<string>(), new List<string> { "Unparseable model output" }, "Could not analyse this resume reliably.");
            }

            static JsonNode TryParse(string json)
            {
                try { return JsonNode.Parse(json) ?? new JsonObject(); }
                catch { return new JsonObject(); } // keep schema stable if upstream text was malformed
            }

            static string StripCodeFence(string s)
            {
                // Why: some responses are wrapped like ```json ... ```
                if (string.IsNullOrEmpty(s)) return s;
                s = s.Trim();
                if (s.StartsWith("```"))
                {
                    var idx = s.IndexOf('\n');
                    if (idx >= 0) s = s[(idx + 1)..];
                    if (s.EndsWith("```"))
                    {
                        s = s[..^3];
                    }
                }
                return s;
            }
        }

        private static string[] ExtractSignals(string json)
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            IEnumerable<string> Arr(JsonElement? el)
                => el is { ValueKind: JsonValueKind.Array }
                    ? el.Value.EnumerateArray().Select(x => x.GetString() ?? string.Empty)
                    : Enumerable.Empty<string>();

            var hard = root.TryGetProperty("skills", out var skills) && skills.TryGetProperty("hard", out var h)
                ? Arr(h)
                : Enumerable.Empty<string>();

            var soft = root.TryGetProperty("skills", out skills) && skills.TryGetProperty("soft", out var sft)
                ? Arr(sft)
                : Enumerable.Empty<string>();

            var highlights = root.TryGetProperty("experience", out var exp)
                ? exp.EnumerateArray().SelectMany(e =>
                    (e.TryGetProperty("highlights", out var hls) ? Arr(hls) : Enumerable.Empty<string>())
                    .Concat(e.TryGetProperty("title", out var t) ? new[] { t.GetString() ?? "" } : Array.Empty<string>())
                    .Concat(e.TryGetProperty("company", out var c) ? new[] { c.GetString() ?? "" } : Array.Empty<string>())
                )
                : Enumerable.Empty<string>();

            var yearsHints = root.TryGetProperty("experience", out exp)
                ? exp.EnumerateArray().SelectMany(e =>
                {
                    var years = e.TryGetProperty("years", out var y) && y.ValueKind is JsonValueKind.Number ? y.GetDouble() : 0.0;
                    var title = e.TryGetProperty("title", out var t) ? (t.GetString() ?? "") : "";
                    return years > 0 && !string.IsNullOrWhiteSpace(title)
                        ? new[] { $"{title} {years:0.#} years" }
                        : Array.Empty<string>();
                })
                : Enumerable.Empty<string>();

            var edu = root.TryGetProperty("education", out var ed)
                ? ed.EnumerateArray().SelectMany(e =>
                    (e.TryGetProperty("degree", out var d) ? new[] { d.GetString() ?? "" } : Array.Empty<string>())
                    .Concat(e.TryGetProperty("field", out var f) ? new[] { f.GetString() ?? "" } : Array.Empty<string>())
                    .Concat(e.TryGetProperty("school", out var sc) ? new[] { sc.GetString() ?? "" } : Array.Empty<string>()))
                : Enumerable.Empty<string>();

            var certs = root.TryGetProperty("certs", out var certsEl) ? Arr(certsEl) : Enumerable.Empty<string>();
            var langs = root.TryGetProperty("languages", out var lngEl) ? Arr(lngEl) : Enumerable.Empty<string>();

            var signals = hard
                .Concat(soft)
                .Concat(highlights)
                .Concat(yearsHints)
                .Concat(edu)
                .Concat(certs)
                .Concat(langs)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(300)
                .ToArray();

            return signals;
        }
    }

    public sealed class AiResult
    {
        public byte MatchScore { get; init; }
        public string Explanation { get; init; } = string.Empty;
        public string NormalisedResumeJson { get; init; } = string.Empty;
    }
}