// File: Areas/Admin/Controllers/ApprovalsController.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using JobPortal.Areas.Shared.Models;
using JobPortal.Areas.Admin.Models;
using JobPortal.Areas.Shared.Extensions; // For TryGetUserId
using System;
using System.Linq;
using System.Collections.Generic;

namespace JobPortal.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class ApprovalsController : Controller
    {
        private readonly AppDbContext _db;
        public ApprovalsController(AppDbContext db) => _db = db;

        // GET: /Admin/Approvals?status=All&q=&page=1&pageSize=10
        public IActionResult Index(string? status = "All", string? q = null, int page = 1, int pageSize = 10)
        {
            ViewData["Title"] = "Job Approvals";

            // Counts for tabs
            var counts = _db.job_post_approvals
                .AsNoTracking()
                .GroupBy(a => a.approval_status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToList();

            int pending = counts.FirstOrDefault(x => x.Status == "Pending")?.Count ?? 0;
            int approved = counts.FirstOrDefault(x => x.Status == "Approved")?.Count ?? 0;
            int changes = counts.FirstOrDefault(x => x.Status == "ChangesRequested")?.Count ?? 0;
            int rejected = counts.FirstOrDefault(x => x.Status == "Rejected")?.Count ?? 0;

            var query = _db.job_post_approvals
                .AsNoTracking()
                .Include(a => a.job_listing.company)
                .OrderByDescending(a => a.approval_id)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(status) && status != "All")
                query = query.Where(a => a.approval_status == status);

            if (!string.IsNullOrWhiteSpace(q))
            {
                var like = $"%{q}%";
                query = query.Where(a =>
                    EF.Functions.Like(a.job_listing.job_title, like) ||
                    EF.Functions.Like(a.job_listing.company.company_name, like));
            }

            var total = query.Count();
            var items = query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(a => new ApprovalRow
                {
                    Id = a.approval_id,
                    JobId = a.job_listing_id,
                    JobTitle = a.job_listing.job_title,
                    Company = a.job_listing.company.company_name,
                    Status = a.approval_status, // Pending/Approved/ChangesRequested/Rejected
                    Date = a.job_listing.date_posted
                })
                .ToList();

            var vm = new ApprovalsIndexViewModel
            {
                Status = status ?? "All",
                Query = q ?? string.Empty,
                PendingCount = pending,
                ApprovedCount = approved,
                ChangesRequestedCount = changes,
                RejectedCount = rejected,
                Items = new PagedResult<ApprovalRow>
                {
                    Items = items,
                    Page = page,
                    PageSize = pageSize,
                    TotalItems = total
                }
            };

            return View(vm);
        }

        public IActionResult Preview(int id)
        {
            ViewData["Title"] = "Job Preview";

            var item = _db.job_post_approvals
                .AsNoTracking()
                .Include(a => a.job_listing.company)
                .Where(a => a.approval_id == id)
                .Select(a => new ApprovalPreviewViewModel
                {
                    Id = a.approval_id,
                    JobId = a.job_listing_id,
                    JobTitle = a.job_listing.job_title,
                    Company = a.job_listing.company.company_name,
                    Status = a.approval_status,
                    Date = a.job_listing.date_posted,
                    JobDescription = a.job_listing.job_description,
                    JobRequirements = a.job_listing.job_requirements,
                    Comments = a.comments
                })
                .FirstOrDefault();

            if (item == null) return NotFound();
            return View(item);
        }

        // --- Single-item Actions (unchanged) ----------------------------------------

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Approve(int id, string? comments)
        {
            if (!this.TryGetUserId(out var adminId, out var early)) return early!;

            var approval = _db.job_post_approvals
                .Include(a => a.job_listing)
                .FirstOrDefault(a => a.approval_id == id);
            if (approval == null) return NotFound();

            approval.approval_status = "Approved";
            approval.date_approved = DateTime.UtcNow;
            if (!string.IsNullOrWhiteSpace(comments))
                approval.comments = comments;

            if (approval.job_listing.job_status == "Draft")
                approval.job_listing.job_status = "Open";

            _db.admin_logs.Add(new admin_log
            {
                user_id = adminId,
                action_type = $"Approved job #{approval.job_listing_id}",
                timestamp = DateTime.UtcNow
            });

            _db.SaveChanges();

            TempData["Flash.Type"] = "success";
            TempData["Flash.Message"] = "Job approved.";
            return RedirectToAction(nameof(Preview), new { id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Reject(int id, string? comments)
        {
            if (!this.TryGetUserId(out var adminId, out var early)) return early!;

            var approval = _db.job_post_approvals
                .Include(a => a.job_listing)
                .FirstOrDefault(a => a.approval_id == id);
            if (approval == null) return NotFound();

            approval.approval_status = "Rejected";
            approval.date_approved = null;
            if (!string.IsNullOrWhiteSpace(comments))
                approval.comments = comments;

            if (approval.job_listing.job_status == "Open")
                approval.job_listing.job_status = "Paused";

            _db.admin_logs.Add(new admin_log
            {
                user_id = adminId,
                action_type = $"Rejected job #{approval.job_listing_id}",
                timestamp = DateTime.UtcNow
            });

            _db.SaveChanges();

            TempData["Flash.Type"] = "danger";
            TempData["Flash.Message"] = "Job rejected.";
            return RedirectToAction(nameof(Preview), new { id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult RequestChanges(int id, string? comments)
        {
            if (!this.TryGetUserId(out var adminId, out var early)) return early!;

            var approval = _db.job_post_approvals
                .Include(a => a.job_listing)
                .FirstOrDefault(a => a.approval_id == id);
            if (approval == null) return NotFound();

            approval.approval_status = "ChangesRequested";
            approval.date_approved = null;
            if (!string.IsNullOrWhiteSpace(comments))
                approval.comments = comments;

            if (approval.job_listing.job_status != "Closed")
                approval.job_listing.job_status = "Draft";

            _db.admin_logs.Add(new admin_log
            {
                user_id = adminId,
                action_type = $"Requested changes for job #{approval.job_listing_id}",
                timestamp = DateTime.UtcNow
            });

            _db.SaveChanges();

            TempData["Flash.Type"] = "warning";
            TempData["Flash.Message"] = "Changes requested from recruiter.";
            return RedirectToAction(nameof(Preview), new { id });
        }

        // --- BULK ACTIONS ------------------------------------------------------------

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Bulk(string actionType, int[] ids, string? comments, string? returnStatus, string? q, int page = 1, int pageSize = 10)
        {
            if (!this.TryGetUserId(out var adminId, out var early)) return early!;

            if (ids == null || ids.Length == 0)
            {
                TempData["Flash.Type"] = "warning";
                TempData["Flash.Message"] = "Select at least one row.";
                return RedirectToAction(nameof(Index), new { status = returnStatus, q, page, pageSize });
            }

            // Load approvals and related job
            var approvals = _db.job_post_approvals
                .Include(a => a.job_listing)
                .Where(a => ids.Contains(a.approval_id))
                .ToList();

            var now = DateTime.UtcNow;
            int changed = 0;

            foreach (var a in approvals)
            {
                switch (actionType)
                {
                    case "Approve":
                        a.approval_status = "Approved";
                        a.date_approved = now;
                        if (!string.IsNullOrWhiteSpace(comments))
                            a.comments = comments;
                        if (a.job_listing.job_status == "Draft")
                            a.job_listing.job_status = "Open";
                        _db.admin_logs.Add(new admin_log { user_id = adminId, action_type = $"Approved job #{a.job_listing_id}", timestamp = now });
                        changed++;
                        break;

                    case "Reject":
                        a.approval_status = "Rejected";
                        a.date_approved = null;
                        if (!string.IsNullOrWhiteSpace(comments))
                            a.comments = comments;
                        if (a.job_listing.job_status == "Open")
                            a.job_listing.job_status = "Paused";
                        _db.admin_logs.Add(new admin_log { user_id = adminId, action_type = $"Rejected job #{a.job_listing_id}", timestamp = now });
                        changed++;
                        break;

                    case "Changes":
                        a.approval_status = "ChangesRequested";
                        a.date_approved = null;
                        if (!string.IsNullOrWhiteSpace(comments))
                            a.comments = comments;
                        if (a.job_listing.job_status != "Closed")
                            a.job_listing.job_status = "Draft";
                        _db.admin_logs.Add(new admin_log { user_id = adminId, action_type = $"Requested changes for job #{a.job_listing_id}", timestamp = now });
                        changed++;
                        break;
                }
            }

            if (changed > 0) _db.SaveChanges();

            TempData["Flash.Type"] = "success";
            TempData["Flash.Message"] = $"{changed} item(s) updated ({actionType}).";
            return RedirectToAction(nameof(Index), new { status = returnStatus, q, page, pageSize });
        }
    }
}
