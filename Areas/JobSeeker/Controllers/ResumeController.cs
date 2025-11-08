using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
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

                // ✅ Define keyword lists
                var educationKeywords = new List<string> { "SPM", "Diploma", "Degree", "Bachelor", "Masters", "PhD" };
                var experienceKeywords = new List<string> { "year", "experience", "internship", "developer", "engineer", "manager" };
                var skillKeywords = new List<string> { "C#", "ASP.NET", "SQL", "JavaScript", "Problem Solving", "Communication", "Teamwork", "Software Development", "HTML", "CSS", "Java", "Python", "Leadership" };
                var certKeywords = new List<string> { "certified", "certificate", "AWS", "Azure", "Google", "Microsoft", "Cisco", "Oracle" };

                // ✅ Education Scoring (based on highest qualification)
                int eduScore = 0;
                if (Regex.IsMatch(extractedText, @"\b(PHD|DOCTORATE)\b", RegexOptions.IgnoreCase))
                    eduScore = 10;
                else if (Regex.IsMatch(extractedText, @"\b(MASTER|MASTERS|MSC)\b", RegexOptions.IgnoreCase))
                    eduScore = 8;
                else if (Regex.IsMatch(extractedText, @"\b(DEGREE|BACHELOR)\b", RegexOptions.IgnoreCase))
                    eduScore = 6;
                else if (Regex.IsMatch(extractedText, @"\b(DIPLOMA)\b", RegexOptions.IgnoreCase))
                    eduScore = 4;
                else if (Regex.IsMatch(extractedText, @"\b(SPM|O LEVEL|STPM)\b", RegexOptions.IgnoreCase))
                    eduScore = 2;

                // ✅ Experience Scoring (based on years found)
                int expScore = 0;
                var expMatch = Regex.Match(extractedText, @"(\d+)\s*(year|years)", RegexOptions.IgnoreCase);
                if (expMatch.Success)
                {
                    int years = int.Parse(expMatch.Groups[1].Value);
                    if (years >= 10) expScore = 10;
                    else if (years >= 7) expScore = 8;
                    else if (years >= 4) expScore = 6;
                    else if (years >= 2) expScore = 4;
                    else expScore = 2;
                }
                else if (extractedText.Contains("internship", StringComparison.OrdinalIgnoreCase))
                {
                    expScore = 2;
                }

                // ✅ Skills Scoring (intersection of known list + text)
                int skillMatchCount = skillKeywords.Count(k =>
                    extractedText.Contains(k, StringComparison.OrdinalIgnoreCase));
                int skillScore = Math.Min(skillMatchCount, 10);

                // ✅ Certifications Scoring
                int certCount = certKeywords.Count(k =>
                    extractedText.Contains(k, StringComparison.OrdinalIgnoreCase));

                int certScore = certCount switch
                {
                    >= 3 => 10,
                    2 => 6,
                    1 => 4,
                    _ => 0
                };

                // ✅ Overall Score
                double overallScore = Math.Round((eduScore + expScore + skillScore + certScore) / 4.0, 2);

                // ✅ Return summarized result (hide matched keywords)
                return Json(new
                {
                    success = true,
                    education = $"{eduScore}/10",
                    experience = $"{expScore}/10",
                    skills = $"{skillScore}/10",
                    certifications = $"{certScore}/10",
                    overallScore = overallScore
                });
            }
        }
    }
}