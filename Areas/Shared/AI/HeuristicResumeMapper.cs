using System.Text.RegularExpressions;
using System.Text.Json.Nodes;

namespace JobPortal.Areas.Shared.AI
{
    internal static class HeuristicResumeMapper
    {
        public static string FromPlainText(string text)
        {
            text ??= string.Empty;

            // Tiny, safe heuristics: emails/phones/lines that look like skills.
            var email = Regex.Match(text, @"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}").Value;
            var phone = Regex.Match(text, @"\+?\d[\d\s\-()]{7,}").Value;

            var skills = Regex.Matches(text, @"\b[A-Za-z][A-Za-z0-9\.\+#\-\s]{1,20}\b")
                              .Select(m => m.Value.Trim())
                              .Where(s => s.Length is >= 2 and <= 30)
                              .Distinct(StringComparer.OrdinalIgnoreCase)
                              .Take(60)
                              .ToArray();

            var obj = new JsonObject
            {
                ["basics"] = new JsonObject
                {
                    ["fullName"] = "",
                    ["email"] = email,
                    ["phone"] = phone,
                    ["location"] = ""
                },
                ["summary"] = "",
                ["skills"] = new JsonObject
                {
                    ["hard"] = new JsonArray(skills.Select(s => JsonValue.Create(s)!).ToArray()),
                    ["soft"] = new JsonArray()
                },
                ["experience"] = new JsonArray(),
                ["education"] = new JsonArray(),
                ["certs"] = new JsonArray(),
                ["languages"] = new JsonArray(),
                ["meta"] = new JsonObject { ["language"] = "en", ["charCount"] = text.Length, ["truncated"] = false }
            };

            return obj.ToJsonString();
        }
    }
}