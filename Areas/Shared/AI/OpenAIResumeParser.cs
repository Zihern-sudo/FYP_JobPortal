// File: Areas/Shared/AI/OpenAIResumeParser.cs
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using UglyToad.PdfPig;

namespace JobPortal.Areas.Shared.AI
{
    public sealed class OpenAIResumeParser
    {
        private readonly IOpenAIClient _openai;

        public OpenAIResumeParser(IOpenAIClient openai) => _openai = openai;

        public async Task<string> ParsePdfAsync(string pdfPath, CancellationToken ct = default)
        {
            string text;
            try { text = ExtractPdfText(pdfPath); }
            catch (Exception ex) { return BuildFailureJson("PDF text extraction failed: " + ex.Message); }

            if (string.IsNullOrWhiteSpace(text) || text.Length < 200)
                return BuildFailureJson("PDF text too short or empty.");

            var chunks = Chunk(text, 7000);
            var sys = SchemaSystemPrompt();
            var merged = InitEmptyResume();

            foreach (var chunk in chunks)
            {
                var user = SchemaUserPrompt("From the RESUME_TEXT below, extract into the schema. Output JSON only.")
                         + "\nRESUME_TEXT:\n" + chunk;

                string raw;
                try
                {
                    raw = await _openai.ChatJsonAsync(sys, user, ct); // JSON mode
                }
                catch
                {
                    continue; // skip bad chunk
                }

                var cleaned = StripCodeFence(raw).Trim();
                var node = TryParseJson(cleaned);
                if (node is null) continue;

                MergeInto(merged, node);
            }

            var normalized = Normalise(merged);
            if (IsWeak(normalized, out var why))
                return BuildFailureJson("Insufficient parse: " + why);

            return normalized.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
        }

        // ---------- prompts -------------------------------------------------------

        private static string SchemaSystemPrompt() => """
            You are an expert resume parser.
            OUTPUT STRICT JSON ONLY. No markdown, no comments, no prose.
            Use the exact schema below and fill as many fields as possible:
            {
              "basics": { "fullName": string, "email": string, "phone": string, "location": string },
              "summary": string,
              "skills": { "hard": string[], "soft": string[] },
              "experience": [
                { "title": string, "company": string, "start": string, "end": string, "years": number, "highlights": string[] }
              ],
              "education": [
                { "degree": string, "field": string, "school": string, "year": string }
              ],
              "certs": string[],
              "languages": string[],
              "meta": { "language": string, "charCount": number, "truncated": boolean }
            }
            Notes:
            - Return FULL detail. Extract all skills and bullet points you can see.
            - If a value is unknown, use "" or [] (do NOT drop fields).
            """;

        private static string SchemaUserPrompt(string extra) => $"""
            {extra}
            Keep "meta.charCount" as the character count of ALL extracted textual content.
            """;

        // ---------- JSON helpers --------------------------------------------------

        private static JsonObject InitEmptyResume()
        {
            return new JsonObject
            {
                ["basics"] = new JsonObject { ["fullName"] = "", ["email"] = "", ["phone"] = "", ["location"] = "" },
                ["summary"] = "",
                ["skills"] = new JsonObject { ["hard"] = new JsonArray(), ["soft"] = new JsonArray() },
                ["experience"] = new JsonArray(),
                ["education"] = new JsonArray(),
                ["certs"] = new JsonArray(),
                ["languages"] = new JsonArray(),
                ["meta"] = new JsonObject { ["language"] = "en", ["charCount"] = 0d, ["truncated"] = false }
            };
        }

        private static JsonNode? TryParseJson(string json)
        {
            try { return JsonNode.Parse(json); }
            catch
            {
                int l = json.IndexOf('{'); int r = json.LastIndexOf('}');
                if (l >= 0 && r > l)
                {
                    var slice = json.Substring(l, r - l + 1);
                    try { return JsonNode.Parse(slice); } catch { }
                }
                return null;
            }
        }

        private static string StripCodeFence(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            s = s.Trim();
            if (s.StartsWith("```"))
            {
                var i = s.IndexOf('\n');
                if (i >= 0) s = s[(i + 1)..];
                if (s.EndsWith("```")) s = s[..^3];
            }
            return s.Trim();
        }

        private static double Num(JsonNode? n)
        {
            if (n is null) return 0d;
            try
            {
                if (n is JsonValue v)
                {
                    if (v.TryGetValue<double>(out var d)) return d;
                    if (v.TryGetValue<long>(out var l)) return l;
                    if (v.TryGetValue<int>(out var i)) return i;
                    if (v.TryGetValue<decimal>(out var m)) return (double)m;
                    if (v.TryGetValue<string>(out var s) && double.TryParse(s, out var ds)) return ds;
                }
            }
            catch { }
            return 0d;
        }

        private static bool IsWeak(JsonNode node, out string why)
        {
            why = "";
            try
            {
                var skills = node?["skills"]?["hard"] as JsonArray;
                var exp = node?["experience"] as JsonArray;
                var meta = node?["meta"]?.AsObject();

                var skillCount = skills?.Count ?? 0;
                var expCount = exp?.Count ?? 0;
                var charCount = meta is not null && meta.TryGetPropertyValue("charCount", out var cc) ? Num(cc) : 0d;

                if (skillCount + expCount < 3) { why = "too few skills/experience"; return true; }
                if (charCount < 200) { why = "meta.charCount too small"; return true; }
                return false;
            }
            catch { why = "validation error"; return true; }
        }

        private static JsonArray CloneArr(JsonNode? n)
        {
            var a = n as JsonArray;
            return a is null ? new JsonArray() : (JsonArray)a.DeepClone(); // <-- avoid parent collision
        }

        private static JsonObject Normalise(JsonNode node)
        {
            var o = InitEmptyResume();

            static string Str(JsonNode? n) => n?.GetValue<string?>() ?? "";

            var basics = node?["basics"] as JsonObject;
            if (basics is not null)
            {
                o["basics"]!["fullName"] = Str(basics["fullName"]);
                o["basics"]!["email"] = Str(basics["email"]);
                o["basics"]!["phone"] = Str(basics["phone"]);
                o["basics"]!["location"] = Str(basics["location"]);
            }

            o["summary"] = Str(node?["summary"]);

            var skills = node?["skills"] as JsonObject;
            if (skills is not null)
            {
                o["skills"]!["hard"] = CloneArr(skills["hard"]);
                o["skills"]!["soft"] = CloneArr(skills["soft"]);
            }

            o["experience"] = CloneArr(node?["experience"]);
            o["education"] = CloneArr(node?["education"]);
            o["certs"] = CloneArr(node?["certs"]);
            o["languages"] = CloneArr(node?["languages"]);

            var meta = node?["meta"] as JsonObject;
            if (meta is not null)
            {
                o["meta"]!["language"] = Str(meta["language"]);
                o["meta"]!["charCount"] = Num(meta["charCount"]);
                o["meta"]!["truncated"] = Bool(meta["truncated"]);
            }

            static JsonArray DedupeStrings(JsonArray arr)
            {
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var outArr = new JsonArray();
                foreach (var v in arr)
                {
                    var s = v?.GetValue<string?>()?.Trim() ?? "";
                    if (s.Length == 0) continue;
                    if (seen.Add(s)) outArr.Add(s);
                }
                return outArr;
            }
            o["skills"]!["hard"] = DedupeStrings((JsonArray)o["skills"]!["hard"]!);
            o["skills"]!["soft"] = DedupeStrings((JsonArray)o["skills"]!["soft"]!);

            return o;
        }

        private static void MergeInto(JsonObject dst, JsonNode src)
        {
            static void MergeArray(JsonArray dstArr, JsonArray? srcArr)
            {
                if (srcArr is null) return;
                var set = new HashSet<string>(dstArr.Select(x => x?.GetValue<string?>() ?? ""), StringComparer.OrdinalIgnoreCase);
                foreach (var v in srcArr)
                {
                    var s = v?.GetValue<string?>() ?? "";
                    if (!string.IsNullOrWhiteSpace(s) && set.Add(s))
                        dstArr.Add(s);
                }
            }

            // basics: prefer longest non-empty
            foreach (var key in new[] { "fullName", "email", "phone", "location" })
            {
                var curr = dst["basics"]![key]?.GetValue<string?>() ?? "";
                var incoming = src?["basics"]?[key]?.GetValue<string?>() ?? "";
                if (incoming.Length > curr.Length) dst["basics"]![key] = incoming;
            }

            // summary
            var currSum = dst["summary"]?.GetValue<string?>() ?? "";
            var incSum = src?["summary"]?.GetValue<string?>() ?? "";
            if (incSum.Length > currSum.Length) dst["summary"] = incSum;

            // skills
            MergeArray((JsonArray)dst["skills"]!["hard"]!, src?["skills"]?["hard"] as JsonArray);
            MergeArray((JsonArray)dst["skills"]!["soft"]!, src?["skills"]?["soft"] as JsonArray);

            // experience / education / certs / languages
            static void MergeList(JsonArray dstList, JsonArray? srcList)
            {
                if (srcList is null) return;
                var seen = new HashSet<string>(dstList.Select(x => x?.ToJsonString() ?? ""), StringComparer.Ordinal);
                foreach (var e in srcList)
                {
                    var sig = e?.ToJsonString() ?? "";
                    if (!string.IsNullOrWhiteSpace(sig) && seen.Add(sig))
                        dstList.Add(e!.DeepClone());
                }
            }
            MergeList((JsonArray)dst["experience"]!, src?["experience"] as JsonArray);
            MergeList((JsonArray)dst["education"]!, src?["education"] as JsonArray);
            MergeArray((JsonArray)dst["certs"]!, src?["certs"] as JsonArray);
            MergeArray((JsonArray)dst["languages"]!, src?["languages"] as JsonArray);

            // meta.charCount: take max
            var currCC = Num(dst["meta"]!["charCount"]);
            var incCC = Num(src?["meta"]?["charCount"]);
            if (incCC > currCC) dst["meta"]!["charCount"] = incCC;
        }

        // ---------- PDF utils -----------------------------------------------------

        private static string ExtractPdfText(string path)
        {
            var sb = new StringBuilder(64_000);
            using var doc = PdfDocument.Open(path);
            foreach (var page in doc.GetPages())
            {
                foreach (var word in page.GetWords())
                    sb.Append(word.Text).Append(' ');
                sb.AppendLine();
            }
            return sb.ToString();
        }

        private static IEnumerable<string> Chunk(string text, int max)
        {
            if (text.Length <= max) { yield return text; yield break; }
            for (int i = 0; i < text.Length; i += max)
                yield return text.Substring(i, Math.Min(max, text.Length - i));
        }

        private static string BuildFailureJson(string reason)
        {
            var o = InitEmptyResume();
            o["meta"]!["charCount"] = 0d;
            o["meta"]!["truncated"] = true;
            o["meta"]!.AsObject().Add("reason", reason);
            return o.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
        }

        private static bool Bool(JsonNode? n)
        {
            if (n is null) return false;
            try
            {
                if (n is JsonValue v)
                {
                    if (v.TryGetValue<bool>(out var b)) return b;
                    if (v.TryGetValue<string>(out var s) && bool.TryParse(s, out var bs)) return bs;
                    if (v.TryGetValue<int>(out var i)) return i != 0;
                }
            }
            catch { }
            return false;
        }

    }
}
