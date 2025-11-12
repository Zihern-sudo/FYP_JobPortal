// File: Areas/Admin/Controllers/DashboardController.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using JobPortal.Areas.Admin.Models;
using JobPortal.Areas.Shared.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace JobPortal.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class DashboardController : Controller
    {
        private readonly AppDbContext _db;
        public DashboardController(AppDbContext db) => _db = db;

        public IActionResult Index()
        {
            ViewData["Title"] = "Admin Dashboard";

            // --- Pending approvals (for quick bulk actions)
            var approvals = _db.job_post_approvals
                .AsNoTracking()
                .Include(a => a.job_listing)
                    .ThenInclude(j => j.company)
                .Where(a => a.approval_status == "Pending")
                .OrderByDescending(a => a.approval_id)
                .Take(12)
                .Select(a => new ApprovalRow
                {
                    Id = a.approval_id,
                    JobId = a.job_listing_id,
                    JobTitle = a.job_listing.job_title,
                    Company = a.job_listing.company.company_name,
                    Status = a.approval_status,
                    Date = a.job_listing.date_posted
                })
                .ToList();

            // --- Needs Attention (flagged conversations)
            var attention = _db.conversation_monitors
                .AsNoTracking()
                .Where(m => m.flag)
                .Include(m => m.conversation)
                .OrderByDescending(m => m.conversation.last_message_at ?? m.conversation.created_at)
                .Take(12)
                .Select(m => new AttentionRow
                {
                    Id = m.monitor_id,
                    ConversationId = m.conversation_id,
                    JobId = m.conversation.job_listing_id,
                    JobTitle = m.conversation.job_title,
                    Candidate = m.conversation.candidate_name,
                    UnreadForRecruiter = m.conversation.unread_for_recruiter,
                    UnreadForCandidate = m.conversation.unread_for_candidate,
                    LastMessageAt = m.conversation.last_message_at
                })
                .ToList();

            // --- Real metrics ---
            // Active jobs = Open
            int activeJobs = _db.job_listings.Count(j => j.job_status == "Open");

            // Distinct candidates who applied in last 7 days
            var today = DateTime.UtcNow.Date;
            var sevenDaysAgo = today.AddDays(-6); // inclusive 7-day window
            int candidates7d = _db.job_applications
                .Where(a => a.date_updated >= sevenDaysAgo && a.date_updated < today.AddDays(1))
                .Select(a => a.user_id) // applicant user_id
                .Distinct()
                .Count();

            // Applications per day (last 14 days)
            const int days = 14;
            var from = today.AddDays(-(days - 1));
            var perDay = _db.job_applications
                .Where(a => a.date_updated >= from && a.date_updated < today.AddDays(1))
                .GroupBy(a => a.date_updated.Date)
                .Select(g => new { Day = g.Key, Cnt = g.Count() })
                .ToDictionary(x => x.Day, x => x.Cnt);

            var spark = new List<int>(days);
            for (var d = from; d <= today; d = d.AddDays(1))
                spark.Add(perDay.TryGetValue(d, out var c) ? c : 0);

            var vm = new DashboardViewModel
            {
                Approvals = approvals,
                Attention = attention,
                TotalUsers = _db.users.Count(),
                TotalJobs = _db.job_listings.Count(),
                TotalLogs = _db.admin_logs.Count(),

                ActiveJobs = activeJobs,
                Candidates7d = candidates7d,
                SparkFromDate = from,
                ApplicationsSparkline = spark
            };

            return View(vm);
        }

        // --- Logout (Recruiter) ---
        [HttpGet]
        public IActionResult Logout()
        {
            // Why: ensure full sign-out for session-based auth
            HttpContext.Session.Clear();

            // Return to Recruiter login
            return RedirectToAction("Login", "Account", new { area = "JobSeeker" });
        }
    }
}
