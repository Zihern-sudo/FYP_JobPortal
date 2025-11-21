using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using JobPortal.Areas.JobSeeker.Models;
using JobPortal.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using System.Net.Http;
using System.Threading.Tasks;
using System.Collections.Generic;
using JobPortal.Areas.Shared.Models;
using System; // for Uri.EscapeDataString
using Microsoft.AspNetCore.Identity;

namespace JobPortal.Areas.JobSeeker.Controllers
{
    [Area("JobSeeker")]
    public class ResumeController : Controller
    {
        private readonly AppDbContext _db;
        private readonly ChatbotService _chatbot;

        public ResumeController(ChatbotService chatbot, AppDbContext db)
        {
            _chatbot = chatbot;
            _db = db;
        }
        [HttpPost]
        public async Task<IActionResult> GenerateResumeFeedback(IFormFile resumeFile)
        {
            if (resumeFile == null || resumeFile.Length == 0)
                return Json(new { success = false, message = "Please upload a valid resume file." });

            string extractedText = "";

            // 📌 EXTRACT PDF TEXT
            using (var stream = resumeFile.OpenReadStream())
            using (var pdf = PdfDocument.Open(stream))
            {
                StringBuilder sb = new StringBuilder();
                foreach (Page page in pdf.GetPages())
                    sb.AppendLine(page.Text);

                extractedText = sb.ToString();
            }

            // ====== BASIC SAFETY CLEANUP ======
            extractedText = extractedText.Replace("\n", " ").Replace("\r", " ");

            // 📌 KEYWORD LISTS (Your system prompt will mention this simple scoring method)
            var educationKeywords = new List<string> { "SPM", "DIPLOMA", "DEGREE", "BACHELOR", "MASTER", "MASTERS", "PHD" };
            var experienceKeywords = new List<string> { "YEAR", "EXPERIENCE", "INTERN", "INTERNSHIP" };
            var skillKeywords = new List<string>
    {
        "C#", "ASP.NET", "SQL", "JAVASCRIPT", "JAVA", "PYTHON",
        "HTML", "CSS", "LEADERSHIP", "COMMUNICATION",
        "TEAMWORK", "PROBLEM", "SOFTWARE", "DEVELOPER", "MANAGE"
    };
            var certKeywords = new List<string> { "CERTIFIED", "CERTIFICATE", "AWS", "AZURE", "CISCO", "MICROSOFT", "GOOGLE" };

            // ============================
            // 📌 EDUCATION SCORING
            // ============================
            int eduScore = 0;
            string eduText = extractedText.ToUpper();

            if (Regex.IsMatch(eduText, @"\bPHD|DOCTORATE\b")) eduScore = 10;
            else if (Regex.IsMatch(eduText, @"\bMASTER|MASTERS|MSC\b")) eduScore = 8;
            else if (Regex.IsMatch(eduText, @"\bDEGREE|BACHELOR\b")) eduScore = 6;
            else if (Regex.IsMatch(eduText, @"\bDIPLOMA\b")) eduScore = 4;
            else if (Regex.IsMatch(eduText, @"\bSPM|STPM|O LEVEL\b")) eduScore = 2;

            // ============================
            // 📌 EXPERIENCE SCORING
            // ============================
            int expScore = 0;
            var expMatch = Regex.Match(eduText, @"(\d+)\s*(YEAR|YEARS)");

            if (expMatch.Success)
            {
                int years = int.Parse(expMatch.Groups[1].Value);

                if (years >= 10) expScore = 10;
                else if (years >= 7) expScore = 8;
                else if (years >= 4) expScore = 6;
                else if (years >= 2) expScore = 4;
                else expScore = 2;
            }
            else if (Regex.IsMatch(eduText, @"\bINTERN|INTERNSHIP\b"))
            {
                expScore = 2;
            }

            // ============================
            // 📌 SKILL MATCH SCORING
            // ============================
            int skillScore = skillKeywords.Count(s => eduText.Contains(s));
            if (skillScore > 10) skillScore = 10;

            // ============================
            // 📌 CERTIFICATION SCORING
            // ============================
            int certCount = certKeywords.Count(s => eduText.Contains(s));
            int certScore = certCount switch
            {
                >= 3 => 10,
                2 => 6,
                1 => 4,
                _ => 0
            };

            // ============================
            // 📌 FINAL OVERALL SCORE
            // ============================
            double overallScore = Math.Round((eduScore + expScore + skillScore + certScore) / 4.0, 2);

            // ✅ Get session once and parse
            var userIdStr = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrEmpty(userIdStr))
                return Json(new { success = false, message = "User session expired." });

            int userId = int.Parse(userIdStr); // use this for everything below

            string aiFeedback = "";

            try
            {
                // ======== AI FEEDBACK (Gemini) ========
                aiFeedback = await _chatbot.GenerateResumeFeedbackAI(extractedText, overallScore);

                // ===============================
                // 📌 Save uploaded resume to folder
                // ===============================
                var folderPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "feedback");
                Directory.CreateDirectory(folderPath);

                // Use a GUID to avoid file name conflicts
                var fileName = $"{Guid.NewGuid()}_resume.pdf"; // or get original file extension
                var filePath = Path.Combine(folderPath, fileName);

                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await resumeFile.CopyToAsync(stream);
                }

                // ===============================
                // 📌 Create new resume record
                // ===============================
                var newResume = new resume
                {
                    user_id = userId,
                    file_path = fileName,
                    upload_date = DateTime.Now
                };

                _db.resumes.Add(newResume);
                await _db.SaveChangesAsync();

                // ===============================
                // 📌 Save feedback history linked to new resume
                // ===============================
                var history = new resume_feedback_history
                {
                    resume_id = newResume.resume_id,
                    user_id = userId,
                    feedback_text = aiFeedback,
                    score = (decimal)overallScore,
                    created_at = DateTime.Now
                };

                _db.resume_feedback_histories.Add(history);
                await _db.SaveChangesAsync();
            }
            catch (Google.GenAI.ServerError)
            {
                return Json(new
                {
                    success = false,
                    message = "The server is currently busy. Please try again later."
                });
            }

            // RETURN BOTH SCORE + AI FEEDBACK
            return Json(new
            {
                success = true,
                education = $"{eduScore}/10",
                experience = $"{expScore}/10",
                skills = $"{skillScore}/10",
                certifications = $"{certScore}/10",
                overallScore = overallScore,
                aiFeedback = aiFeedback
            });

            {
                return Json(new
                {
                    success = false,
                    message = "The server is currently busy. Please try again later."
                });
            }

            return Json(new
            {
                success = true,
                education = $"{eduScore}/10",
                experience = $"{expScore}/10",
                skills = $"{skillScore}/10",
                certifications = $"{certScore}/10",
                overallScore = overallScore,
                aiFeedback = aiFeedback
            });
        }

        public async Task<IActionResult> FeedbackHistory(int page = 1)
        {
            var userIdStr = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrEmpty(userIdStr))
                return RedirectToAction("Login", "Account", new { area = "JobSeeker" });

            int userId = int.Parse(userIdStr);
            int pageSize = 5; // 5 records per page

            var query = _db.resume_feedback_histories
                .Where(f => f.user_id == userId)
                .OrderByDescending(f => f.created_at);

            int totalCount = await query.CountAsync();
            var feedbackList = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            ViewBag.FileName = "All Resumes";
            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

            return View(feedbackList);
        }

    }
}