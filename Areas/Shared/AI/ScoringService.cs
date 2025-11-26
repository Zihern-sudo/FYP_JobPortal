// File: Areas/Shared/AI/ScoringService.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace JobPortal.Areas.Shared.AI
{
    public interface IScoringService
    {
        Task<(byte score, IReadOnlyList<(string requirement, double sim)>)> ScoreAsync(
            string[] jobRequirements,
            string[] candidateSignals,
            CancellationToken ct = default);
    }

    internal sealed class ScoringService : IScoringService
    {
        private readonly IOpenAIClient _client;
        public ScoringService(IOpenAIClient client) => _client = client;

        public async Task<(byte score, IReadOnlyList<(string requirement, double sim)>)> ScoreAsync(
            string[] jobRequirements,
            string[] candidateSignals,
            CancellationToken ct = default)
        {
            // Minimal sanitisation only (no local scoring logic)
            var reqs = jobRequirements
                .Where(r => !string.IsNullOrWhiteSpace(r))
                .Select(r => r.Trim())
                .Distinct(StringComparer.Ordinal)
                .Take(40)
                .ToArray();

            var signals = candidateSignals
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim().ToLowerInvariant())
                .Distinct(StringComparer.Ordinal)
                .Take(300)
                .ToArray();

            if (reqs.Length == 0 || signals.Length == 0)
                return (0, Array.Empty<(string requirement, double sim)>());

            // System prompt: OpenAI performs all reasoning/calibration and must return strict JSON
            var sys = """
                You are a hiring evaluator.
                Use ONLY the provided candidate_signals and the requirement text.
                Consider common synonyms/abbreviations and minor formatting differences (e.g., "dotnet" ≈ ".NET", "ef core" ≈ "entity framework core").
                OUTPUT STRICT JSON ONLY (no Markdown, no comments).
                Schema:
                {
                  "score": integer,              // 0..100 overall fit
                  "per_requirement": [           // same order & exact text as inputs
                    { "requirement": string, "sim": number }  // sim in [0,1]
                  ]
                }
                Calibration:
                - 95–100: exceptional (all must-haves clearly evidenced, strong overall).
                - 85–94: strong (most must-haves and several nice-to-haves present).
                - 70–84: good (key must-haves present but some gaps).
                - 50–69: partial / junior-level fit.
                - <50: weak or missing key requirements.
                Rules:
                - Do not invent evidence; if unclear, assign a conservative similarity.
                - Preserve the exact requirement strings in per_requirement and keep the same order.
            """;

            // User payload: just the inputs; no local heuristics
            string user = JsonSerializer.Serialize(new
            {
                requirements = reqs,
                candidate_signals = signals
            }, new JsonSerializerOptions { WriteIndented = false });

            // Single retry on parse/API error (no local fallbacks)
            for (int attempt = 1; attempt <= 2; attempt++)
            {
                try
                {
                    var raw = await _client.CompleteAsync(sys, user, ct);
                    raw = StripCodeFence(raw).Trim();

                    using var doc = JsonDocument.Parse(raw);
                    var root = doc.RootElement;

                    // score
                    if (!root.TryGetProperty("score", out var s) || s.ValueKind != JsonValueKind.Number)
                        throw new InvalidOperationException("Missing 'score'");

                    var score = Math.Clamp(s.GetInt32(), 0, 100);

                    // per_requirement
                    if (!root.TryGetProperty("per_requirement", out var pr) || pr.ValueKind != JsonValueKind.Array)
                        throw new InvalidOperationException("Missing 'per_requirement'");

                    var parsed = new List<(string requirement, double sim)>(reqs.Length);
                    foreach (var item in pr.EnumerateArray())
                    {
                        var r = item.TryGetProperty("requirement", out var rr) ? rr.GetString() ?? "" : "";
                        var sim = item.TryGetProperty("sim", out var ss) && ss.ValueKind == JsonValueKind.Number
                            ? Clamp01(ss.GetDouble())
                            : 0.0;

                        if (!string.IsNullOrWhiteSpace(r))
                            parsed.Add((r.Trim(), sim));
                    }

                    // Enforce exact order & text as inputs; fill any missing with 0.0
                    IReadOnlyList<(string requirement, double sim)> sims;
                    if (parsed.Count == reqs.Length &&
                        parsed.Select(x => x.requirement).SequenceEqual(reqs, StringComparer.Ordinal))
                    {
                        sims = parsed;
                    }
                    else
                    {
                        var map = parsed.ToDictionary(x => x.requirement, x => x.sim, StringComparer.Ordinal);
                        var aligned = new List<(string requirement, double sim)>(reqs.Length);
                        foreach (var r in reqs)
                            aligned.Add(map.TryGetValue(r, out var v) ? (r, v) : (r, 0.0));
                        sims = aligned;
                    }

                    return ((byte)score, sims);
                }
                catch
                {
                    if (attempt == 2) break;
                    // Retry with an explicit repair instruction; still no local logic
                    user = JsonSerializer.Serialize(new
                    {
                        requirements = reqs,
                        candidate_signals = signals,
                        instruction = "Previous response was invalid. Return VALID JSON exactly matching the schema."
                    }, new JsonSerializerOptions { WriteIndented = false });
                }
            }

            // If the model still fails, return zeros (transparent failure; no heuristic fill-ins)
            return (0, reqs.Select(r => (r, 0.0)).ToList());
        }

        // ---- helpers (non-scoring) ----
        private static string StripCodeFence(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            s = TrimFence(s);
            return s;

            static string TrimFence(string s)
            {
                s = s.Trim();
                if (s.StartsWith("```"))
                {
                    var idx = s.IndexOf('\n');
                    if (idx >= 0) s = s[(idx + 1)..];
                    if (s.EndsWith("```")) s = s[..^3];
                }
                return s;
            }
        }

        private static double Clamp01(double v) => v < 0 ? 0 : (v > 1 ? 1 : v);
    }
}
