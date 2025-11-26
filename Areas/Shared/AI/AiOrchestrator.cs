// File: Areas/Shared/AI/AiOrchestrator.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using JobPortal.Areas.Shared.Extensions;

namespace JobPortal.Areas.Shared.AI
{
    public sealed class AiOrchestrator
    {
        private readonly ILanguageService _lang;
        private readonly IOpenAIClient _client; // OPENAI entry

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
            var normalised = await _lang.NormaliseResumeJsonAsync(resumeCompactJson, ct);

            var (score, topHits, gaps, notes, llmBreakdown) =
                await ScoreWithOpenAIAsync(jobTitle, jobRequirements ?? Array.Empty<string>(), normalised, ct);

            string explanation = string.IsNullOrWhiteSpace(notes)
                ? await _lang.BuildExplanationAsync(jobTitle, topHits, gaps, ct)
                : notes.Trim();

            if (explanation.Length > 400) explanation = explanation[..400];

            var breakdown = BuildBreakdownFromModel(
                totalScore: (byte)Math.Clamp(score, 0, 100),
                llm: llmBreakdown,
                topHits: topHits,
                gaps: gaps,
                notes: explanation
            );

            return new AiResult
            {
                MatchScore = (byte)Math.Clamp(score, 0, 100),
                Explanation = explanation.Trim(),
                NormalisedResumeJson = normalised,
                Breakdown = breakdown
            };
        }

        // Ask the model for both holistic score and a machine-readable breakdown.
        private async Task<(int score, List<string> topHits, List<string> gaps, string notes, LlmBreakdown? breakdown)>
            ScoreWithOpenAIAsync(string jobTitle, string[] reqs, string resumeJson, CancellationToken ct)
        {
            var sys = """
                You are a hiring evaluator. Compare a job (title + requirements) to a resume.
                Output STRICT JSON ONLY (no prose, no backticks).
                JSON schema:
                {
                  "score": integer 0-100,            // overall holistic fit
                  "top_hits": string[],               // strongest evidence items (≤5)
                  "gaps": string[],                   // key gaps (≤5)
                  "notes": string,                    // short recruiter-style note (≤220 chars)
                  "sections": {
                    "skills":     { "points": integer 0-50, "max": 50, "matched": string[], "missing": string[] },
                    "experience": { "points": integer 0-35, "max": 35, "matched": string[], "missing": string[] },
                    "education":  { "points": integer 0-15, "max": 15, "matched": string[], "missing": string[] }
                  },
                  "per_requirement": [
                    { "requirement": string, "category": "skills"|"experience"|"education", "similarity": number 0..1 }
                  ]
                }
                Rules:
                - Consider skills, years of experience, role relevance, education level/field and other verifiable resume signals.
                - Penalize missing must-haves and shallow evidence.
                - Section points must reflect the evidence you used; ensure they sum within their caps.
                - Keep arrays deduplicated, concise, truthful. No hallucinations.
            """;

            var payload = new
            {
                job = new { title = jobTitle, requirements = reqs },
                resume = TryParse(resumeJson)
            };
            var user = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = false });

            string raw;
            try
            {
                raw = await _client.CompleteAsync(sys, user, ct);
            }
            catch
            {
                return (0, new List<string>(), new List<string> { "API error while scoring" }, "Automatic scoring failed. No reliable evidence detected.", null);
            }

            raw = StripCodeFence(raw).Trim();

            try
            {
                using var doc = JsonDocument.Parse(raw);
                var root = doc.RootElement;

                int score = root.TryGetProperty("score", out var s) && s.ValueKind == JsonValueKind.Number
                    ? Math.Clamp(s.GetInt32(), 0, 100)
                    : 0;

                var top = ReadStringArray(root, "top_hits");
                var gaps = ReadStringArray(root, "gaps");
                var notes = root.TryGetProperty("notes", out var nt) ? (nt.GetString() ?? "") : "";

                // Parse sections & per_requirement if present
                LlmBreakdown? llm = null;
                if (root.TryGetProperty("sections", out var sec) && sec.ValueKind == JsonValueKind.Object)
                {
                    var skills = ReadSection(sec, "skills", 50);
                    var experience = ReadSection(sec, "experience", 35);
                    var education = ReadSection(sec, "education", 15);

                    var perReq = new List<PerRequirement>();
                    if (root.TryGetProperty("per_requirement", out var pr) && pr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var el in pr.EnumerateArray())
                        {
                            var req = el.TryGetProperty("requirement", out var rq) ? (rq.GetString() ?? "") : "";
                            var cat = el.TryGetProperty("category", out var cg) ? (cg.GetString() ?? "skills") : "skills";
                            double sim = el.TryGetProperty("similarity", out var sm) && sm.ValueKind == JsonValueKind.Number ? Math.Clamp(sm.GetDouble(), 0, 1) : 0.0;
                            if (!string.IsNullOrWhiteSpace(req))
                                perReq.Add(new PerRequirement { Requirement = req, Category = cat, Similarity = sim });
                        }
                    }

                    llm = new LlmBreakdown { Skills = skills, Experience = experience, Education = education, PerRequirement = perReq };
                }

                return (score, top, gaps, notes, llm);
            }
            catch
            {
                return (0, new List<string>(), new List<string> { "Unparseable model output" }, "Could not analyse this resume reliably.", null);
            }

            // --- local helpers for parsing the model output ---
            static JsonNode TryParse(string json)
            {
                try { return JsonNode.Parse(json) ?? new JsonObject(); }
                catch { return new JsonObject(); }
            }

            static string StripCodeFence(string s)
            {
                if (string.IsNullOrEmpty(s)) return s;
                s = s.Trim();
                if (s.StartsWith("```"))
                {
                    var idx = s.IndexOf('\n');
                    if (idx >= 0) s = s[(idx + 1)..];
                    if (s.EndsWith("```")) s = s[..^3];
                }
                return s;
            }

            static List<string> ReadStringArray(JsonElement obj, string name)
                => obj.TryGetProperty(name, out var arr) && arr.ValueKind == JsonValueKind.Array
                    ? arr.EnumerateArray()
                         .Select(x => x.GetString() ?? "")
                         .Where(x => !string.IsNullOrWhiteSpace(x))
                         .Distinct(StringComparer.OrdinalIgnoreCase)
                         .Take(10)
                         .ToList()
                    : new List<string>();

            static LlmSection ReadSection(JsonElement parent, string key, int @defaultMax)
            {
                var (points, max, matched, missing) = (0, @defaultMax, new List<string>(), new List<string>());
                if (parent.TryGetProperty(key, out var s) && s.ValueKind == JsonValueKind.Object)
                {
                    if (s.TryGetProperty("points", out var p) && p.ValueKind == JsonValueKind.Number) points = Math.Max(0, p.GetInt32());
                    if (s.TryGetProperty("max", out var m) && m.ValueKind == JsonValueKind.Number) max = Math.Max(0, m.GetInt32());
                    matched = ReadStringArray(s, "matched");
                    missing = ReadStringArray(s, "missing");
                }
                return new LlmSection(points, max, matched, missing);
            }
        }

        private static ScoreBreakdown BuildBreakdownFromModel(
            byte totalScore,
            LlmBreakdown? llm,
            IReadOnlyList<string> topHits,
            IReadOnlyList<string> gaps,
            string notes)
        {
            // If model provided detailed sections, map them 1:1; else return empty sections.
            var skills = llm?.Skills ?? new LlmSection(0, 50, new List<string>(), new List<string>());
            var exp = llm?.Experience ?? new LlmSection(0, 35, new List<string>(), new List<string>());
            var edu = llm?.Education ?? new LlmSection(0, 15, new List<string>(), new List<string>());

            return new ScoreBreakdown
            {
                Total = totalScore,
                Skills = new ScoreSection("Skills", skills.Points, skills.Max, new List<string>(), skills.Matched, skills.Missing),
                Experience = new ScoreSection("Experience", exp.Points, exp.Max, new List<string>(), exp.Matched, exp.Missing),
                Education = new ScoreSection("Education", edu.Points, edu.Max, new List<string>(), edu.Matched, edu.Missing),
                Extras = new ScoreSection("Extras", 0, 0,
                    highlights: (topHits ?? new List<string>()).ToList(),
                    matched: new List<string>(),
                    missing: (gaps ?? new List<string>()).ToList()),
                Notes = notes ?? "",
                PerRequirement = (llm?.PerRequirement ?? Array.Empty<PerRequirement>()).ToList(),
                EvaluatedAt = MyTime.NowMalaysia()
            };
        }

        // ------------- internal DTOs used only for AI -> UI mapping -------------
        private sealed record LlmSection(int Points, int Max, List<string> Matched, List<string> Missing);
        private sealed class LlmBreakdown
        {
            public LlmSection Skills { get; init; } = new(0, 50, new List<string>(), new List<string>());
            public LlmSection Experience { get; init; } = new(0, 35, new List<string>(), new List<string>());
            public LlmSection Education { get; init; } = new(0, 15, new List<string>(), new List<string>());
            public IReadOnlyList<PerRequirement> PerRequirement { get; init; } = Array.Empty<PerRequirement>();
        }
    }

    // ---------------- DTOs surfaced to UI (unchanged) ----------------
    public sealed class AiResult
    {
        public byte MatchScore { get; init; }
        public string Explanation { get; init; } = string.Empty;
        public string NormalisedResumeJson { get; init; } = string.Empty;
        public ScoreBreakdown Breakdown { get; init; } = new();
    }

    public sealed class ScoreBreakdown
    {
        public byte Total { get; init; }
        public ScoreSection Skills { get; init; } = new("Skills", 0, 50, new List<string>(), new List<string>(), new List<string>());
        public ScoreSection Experience { get; init; } = new("Experience", 0, 35, new List<string>(), new List<string>(), new List<string>());
        public ScoreSection Education { get; init; } = new("Education", 0, 15, new List<string>(), new List<string>(), new List<string>());
        public ScoreSection Extras { get; init; } = new("Extras", 0, 0, new List<string>(), new List<string>(), new List<string>());
        public string Notes { get; init; } = "";
        public DateTime? EvaluatedAt { get; init; }
        public IReadOnlyList<PerRequirement> PerRequirement { get; init; } = Array.Empty<PerRequirement>();
    }

    public sealed class ScoreSection
    {
        public string Name { get; }
        public int Points { get; }
        public int MaxPoints { get; }
        public IReadOnlyList<string> Highlights { get; }
        public IReadOnlyList<string> Matched { get; }
        public IReadOnlyList<string> Missing { get; }

        public ScoreSection(string name, int points, int maxPoints, IReadOnlyList<string> highlights, IReadOnlyList<string> matched, IReadOnlyList<string> missing)
        {
            Name = name;
            Points = points;
            MaxPoints = maxPoints;
            Highlights = highlights;
            Matched = matched;
            Missing = missing;
        }
    }

    public sealed class PerRequirement
    {
        public string Requirement { get; init; } = "";
        public double Similarity { get; init; }          // 0..1
        public string Category { get; init; } = "skills"; // skills|experience|education
    }
}
