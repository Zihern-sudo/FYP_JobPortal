// File: Areas/Shared/AI/LanguageService.cs
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

using JobPortal.Areas.Shared.Extensions;

namespace JobPortal.Areas.Shared.AI
{
    public interface ILanguageService
    {
        Task<string> NormaliseResumeJsonAsync(string compactJson, CancellationToken ct = default);
        Task<string> BuildExplanationAsync(string jobTitle, IEnumerable<string> topHits, IEnumerable<string> gaps, CancellationToken ct = default);
    }

    internal sealed class LanguageService : ILanguageService
    {
        private readonly IOpenAIClient _client;
        public LanguageService(IOpenAIClient client) => _client = client;

        // deterministic first; only fall back to LLM if structure is broken
        public async Task<string> NormaliseResumeJsonAsync(string compactJson, CancellationToken ct = default)
        {
            JsonNode? root;
            try { root = JsonNode.Parse(compactJson); }
            catch
            {
                var sys = "You clean and standardise a small resume JSON. Output ONLY JSON. Keep fields concise.";
                var user = $"JSON:\n{compactJson}\nRules: normalise dates to YYYY-MM, unify similar skills (NumPy/Pandas→Python data stack), fix obvious typos. Keep schema unchanged.";
                var fixedJson = await _client.CompleteAsync(sys, user, ct);
                root = JsonNode.Parse(fixedJson);
            }

            root ??= new JsonObject();
            var skillsNode = root["skills"] as JsonObject ?? new JsonObject();
            root["skills"] = skillsNode;

            var hard = ToStringArray(skillsNode["hard"]);
            var soft = ToStringArray(skillsNode["soft"]);

            // Canonicalise skills (token-safe; avoids substring traps)
            var canon = CanonicaliseSkills(hard);
            skillsNode["hard"] = new JsonArray(canon.Select(s => JsonValue.Create(s)!).ToArray());
            skillsNode["soft"] = new JsonArray(soft.Select(NormaliseToken).Distinct(StringComparer.OrdinalIgnoreCase).Select(s => JsonValue.Create(s)!).ToArray());

            // Experience: normalise dates & compute years
            var expArr = root["experience"] as JsonArray ?? new JsonArray();
            var outExp = new JsonArray();
            foreach (var item in expArr.OfType<JsonObject>())
            {
                var start = NormaliseDate(item["start"]?.GetValue<string>());
                var end = NormaliseDate(item["end"]?.GetValue<string>());
                var years = YearsBetween(start, end);
                var title = item["title"]?.GetValue<string>() ?? "";
                var company = item["company"]?.GetValue<string>() ?? "";
                var highlights = ToStringArray(item["highlights"]);
                if (years <= 0) years = InferYearsFromText(highlights);
                outExp.Add(new JsonObject
                {
                    ["title"] = title,
                    ["company"] = company,
                    ["start"] = start,
                    ["end"] = end,
                    ["years"] = years,
                    ["highlights"] = new JsonArray(highlights.Select(h => JsonValue.Create(h)!).ToArray())
                });
            }
            root["experience"] = outExp;

            // Education
            var edu = root["education"] as JsonArray ?? new JsonArray();
            var outEdu = new JsonArray();
            foreach (var item in edu.OfType<JsonObject>())
            {
                var degree = NormaliseToken(item["degree"]?.GetValue<string>() ?? item["title"]?.GetValue<string>() ?? "");
                var field = NormaliseToken(item["field"]?.GetValue<string>() ?? "");
                var school = item["school"]?.GetValue<string>() ?? item["institution"]?.GetValue<string>() ?? "";
                outEdu.Add(new JsonObject
                {
                    ["degree"] = degree,
                    ["field"] = field,
                    ["school"] = school
                });
            }
            root["education"] = outEdu;

            // Meta.detected
            var detected = new JsonArray(canon.Select(s => JsonValue.Create(s)!).ToArray());
            (root["meta"] as JsonObject ?? (JsonObject)(root["meta"] = new JsonObject()))["detected"] = detected;

            return root.ToJsonString(new JsonSerializerOptions { WriteIndented = false });

            // ---- helpers ----
            static string NormaliseToken(string s) => (s ?? "").Trim().Replace("\u00A0", " ").ToLowerInvariant();

            static string[] ToStringArray(JsonNode? node)
                => node is JsonArray arr ? arr.Select(x => x?.GetValue<string>() ?? "").Where(x => !string.IsNullOrWhiteSpace(x)).ToArray() : Array.Empty<string>();

            static string NormaliseDate(string? raw)
            {
                raw = (raw ?? "").Trim();
                if (string.IsNullOrEmpty(raw)) return "";
                if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dt))
                    return $"{dt:yyyy-MM}";
                var m = Regex.Match(raw, @"\b(\d{4})(?:[-/](\d{1,2}))?\b");
                if (m.Success)
                {
                    var y = int.Parse(m.Groups[1].Value);
                    var mm = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 1;
                    mm = Math.Clamp(mm, 1, 12);
                    return $"{y:D4}-{mm:D2}";
                }
                return "";
            }

            static double YearsBetween(string start, string end)
            {
                if (!DateTime.TryParseExact(start, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var s)) return 0;
                var e = DateTime.TryParseExact(end, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var ee) ? ee : MyTime.NowMalaysia();
                var months = (e.Year - s.Year) * 12 + e.Month - s.Month;
                return Math.Round(Math.Max(0, months) / 12.0, 2);
            }

            static double InferYearsFromText(IEnumerable<string> lines)
            {
                foreach (var line in lines)
                {
                    var m = Regex.Match(line, @"\b(\d+(?:\.\d+)?)\s*\+?\s*years?\b", RegexOptions.IgnoreCase);
                    if (m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
                        return Math.Round(y, 2);
                }
                return 0;
            }

            static IEnumerable<string> CanonicaliseSkills(IEnumerable<string> src)
            {
                // Exact-match synonym sets (no substring replacement)
                // Keeps "dotnet" distinct from "asp.net core"; folds variants safely.
                var exactMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    // dotnet family (platform)
                    [".net"] = "dotnet",
                    ["dotnet"] = "dotnet",
                    [".net core"] = "dotnet",

                    // classic ASP.NET (web framework, non-core)
                    ["asp.net"] = "asp.net",
                    ["asp net"] = "asp.net",
                    ["aspnet"] = "asp.net",

                    // ASP.NET Core (modern web framework)
                    ["asp.net core"] = "asp.net core",
                    ["asp net core"] = "asp.net core",
                    ["aspnet core"] = "asp.net core",

                    // misc common normalisations
                    ["node.js"] = "nodejs",
                    ["node js"] = "nodejs",
                    ["react.js"] = "react",
                    ["reactjs"] = "react",
                    ["postgres"] = "postgresql",
                    ["postgre"] = "postgresql",
                    ["mysql server"] = "mysql",
                    ["c sharp"] = "c#",
                    ["c-sharp"] = "c#",
                    ["py"] = "python",
                    ["numpy"] = "python",
                    ["pandas"] = "python",
                    ["scikit-learn"] = "python",
                    ["powerbi"] = "power bi",
                    ["js"] = "javascript"
                };

                static string Clean(string s)
                {
                    s = (s ?? "").Trim().Replace("\u00A0", " ");
                    s = Regex.Replace(s, @"\s+", " ");
                    return s.ToLowerInvariant();
                }

                var outSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var raw in src)
                {
                    var t = Clean(raw);
                    if (string.IsNullOrWhiteSpace(t)) continue;

                    // exact-map only
                    if (exactMap.TryGetValue(t, out var mapped))
                        outSet.Add(mapped);
                    else
                        outSet.Add(t);
                }

                // Post rules:
                // - If "asp.net core" present, drop plain "asp.net" (core subsumes it).
                // - Keep "dotnet" alongside ASP.NET variants (platform vs framework).
                if (outSet.Contains("asp.net core"))
                    outSet.Remove("asp.net");

                // Keep order stable for output
                return outSet.OrderBy(x => x);
            }
        }

        public Task<string> BuildExplanationAsync(string jobTitle, IEnumerable<string> topHits, IEnumerable<string> gaps, CancellationToken ct = default)
        {
            var sys = "You write brief, neutral recruiter notes. 2 sentences max. No fluff.";
            var user =
                $"Role: {jobTitle}\nTop matches: {string.Join(", ", topHits)}\nMain gaps: {string.Join(", ", gaps)}\n" +
                "Write two short sentences: one positive reason for fit, one precise gap.";
            return _client.CompleteAsync(sys, user, ct);
        }
    }
}
