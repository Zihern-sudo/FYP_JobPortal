using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace JobPortal.Areas.JobSeeker.Controllers
{
    [Area("JobSeeker")]
    public class ResumeController : Controller
    {
        [HttpPost]
        public async Task<IActionResult> UploadResume(IFormFile resumeFile)
        {
            if (resumeFile == null || resumeFile.Length == 0)
                return Json(new { success = false, message = "Please upload a valid resume file." });

            string extractedText = "";

            // ✅ Extract text from PDF using PdfPig
            using (var stream = resumeFile.OpenReadStream())
            using (var pdf = PdfDocument.Open(stream))
            {
                StringBuilder sb = new StringBuilder();
                foreach (Page page in pdf.GetPages())
                    sb.AppendLine(page.Text);

                extractedText = sb.ToString();
            }

            // ✅ Send extracted text to TextRazor API
            string apiKey = "b426ca814983ef70ffdf990b592414657a82ac2cf2cade032c8f4dc7";
            string apiUrl = "https://api.textrazor.com/";

            using (var httpClient = new HttpClient())
            {
                httpClient.DefaultRequestHeaders.Add("x-textrazor-key", apiKey);

                var content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("extractors", "entities,topics,words"),
                    new KeyValuePair<string, string>("text", extractedText)
                });

                var response = await httpClient.PostAsync(apiUrl, content);
                var result = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                    return Json(new { success = false, message = "Error calling TextRazor API." });

                using var doc = JsonDocument.Parse(result);
                var keywords = new List<string>();

                if (doc.RootElement.TryGetProperty("response", out var resp) &&
                    resp.TryGetProperty("entities", out var entities))
                {
                    foreach (var entity in entities.EnumerateArray())
                    {
                        if (entity.TryGetProperty("entityId", out var keyword))
                            keywords.Add(keyword.GetString() ?? string.Empty);
                    }
                }

                // ✅ Define target job keywords (e.g., Software Engineer)
                var targetKeywords = new List<string>
                {
                    "C#", "ASP.NET", "SQL", "JavaScript", "Problem Solving",
                    "Communication", "Teamwork", "Software Development", "HTML", "CSS", "Java"
                };

                // ✅ Compare extracted keywords with target keywords
                var matched = keywords
                    .Intersect(targetKeywords, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                int matchCount = matched.Count;
                double matchScore = Math.Round((double)matchCount / targetKeywords.Count * 100, 2);

                return Json(new
                {
                    success = true,
                    totalExtracted = keywords.Count,
                    matchedKeywords = matched,
                    matchCount = matchCount,
                    matchScore = matchScore
                });
            }
        }

    }
}
