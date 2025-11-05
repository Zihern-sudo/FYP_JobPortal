// File: Areas/Admin/Models/AdminViewModels.cs
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering; // Required for SelectList

namespace JobPortal.Areas.Admin.Models;

// Keep this type in place to avoid unrelated ripple effects
public class AdminViewModels { }

// ===== Shared =====
public sealed class PagedResult<T>
{
    public IReadOnlyList<T> Items { get; init; } = Array.Empty<T>();
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalItems { get; init; }
    public int TotalPages => PageSize <= 0 ? 1 : Math.Max(1, (int)Math.Ceiling(TotalItems / (double)PageSize));
}

// ===== Dashboard VMs =====
public sealed class DashboardViewModel
{
    [Display(Name = "Job Post Approvals")]
    public IReadOnlyList<ApprovalRow> Approvals { get; init; } = Array.Empty<ApprovalRow>();

    [Display(Name = "Needs Attention")]
    public IReadOnlyList<AttentionRow> Attention { get; init; } = Array.Empty<AttentionRow>();

    public int TotalUsers { get; init; }
    public int TotalJobs { get; init; }
    public int TotalLogs { get; init; }

    // --- Added real metrics + sparkline ---
    public int ActiveJobs { get; init; }              // job_listings where job_status == "Open"
    public int Candidates7d { get; init; }            // distinct applicants in last 7 days
    public DateTime SparkFromDate { get; init; }      // start date for sparkline (e.g., today - 13)
    public IReadOnlyList<int> ApplicationsSparkline { get; init; } = Array.Empty<int>(); // length 14
}

public sealed class ApprovalRow
{
    public int Id { get; init; }
    public int JobId { get; init; }
    [Required] public string JobTitle { get; init; } = string.Empty;
    [Required] public string Company { get; init; } = string.Empty;
    [Required] public string Status { get; init; } = string.Empty; // Pending/Approved/ChangesRequested/Rejected
    public DateTime? Date { get; init; }
}

public sealed class ApprovalsIndexViewModel
{
    public string Status { get; init; } = "All"; // All | Pending | Approved | ChangesRequested | Rejected
    public string Query { get; init; } = string.Empty; // search q
    public int PendingCount { get; init; }
    public int ApprovedCount { get; init; }
    public int ChangesRequestedCount { get; init; }
    public int RejectedCount { get; init; }
    public PagedResult<ApprovalRow> Items { get; init; } = new();
}

public sealed class AttentionRow
{
    public int Id { get; init; }
    public int ConversationId { get; init; }
    public int? JobId { get; init; }
    public string? JobTitle { get; init; }
    public string? Candidate { get; init; }
    public int UnreadForRecruiter { get; init; }
    public int UnreadForCandidate { get; init; }
    public DateTime? LastMessageAt { get; init; }
}

public sealed class ApprovalPreviewViewModel
{
    public int Id { get; init; }
    public int JobId { get; init; }
    [Required] public string JobTitle { get; init; } = string.Empty;
    [Required] public string Company { get; init; } = string.Empty;
    [Required] public string Status { get; init; } = string.Empty;
    public DateTime? Date { get; init; }
    public string? JobDescription { get; init; }
    public string? JobRequirements { get; init; }
    public string? Comments { get; init; }
}
public sealed class AuditLogViewModel
{
    public DateTime When { get; init; }
    [Required] public string Actor { get; init; } = string.Empty;
    [Required] public string Action { get; init; } = string.Empty;
    public string? Target { get; init; }
    public string? Notes { get; init; }
}

public sealed class MessagesFilterViewModel
{
    public string Q { get; init; } = string.Empty;
    public bool FlaggedOnly { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 10;
}

public sealed class MessagesIndexViewModel
{
    public MessagesFilterViewModel Filter { get; init; } = new();
    public PagedResult<ConversationListViewModel> Items { get; init; } = new();
}

public sealed class ConversationListViewModel
{
    public int ConversationId { get; init; }
    [Required] public string CompanyName { get; init; } = string.Empty;
    [Required] public string JobTitle { get; init; } = string.Empty;
    public DateTime? LastMessageAt { get; init; }
    public string? LastSnippet { get; init; }      // NEW
    public int MessageCount { get; init; }         // NEW
    public bool Flagged { get; init; }
    public int UnreadForRecruiter { get; init; }
    public int UnreadForCandidate { get; init; }
}

public sealed class Participant
{
    public int UserId { get; init; }
    [Required] public string Name { get; init; } = string.Empty;
    public string Role { get; init; } = "User";
    public string Status { get; init; } = "Active";
}

public sealed class ConversationThreadViewModel
{
    public int ConversationId { get; init; }
    public IReadOnlyList<MessageViewModel> Messages { get; init; } = Array.Empty<MessageViewModel>();
    public Participant? ParticipantA { get; init; }
    public Participant? ParticipantB { get; init; }
}

public sealed class MessageViewModel
{
    public int SenderId { get; init; }
    public string? SenderRole { get; init; }
    [Required] public string SenderName { get; init; } = string.Empty;
    [Required] public string Text { get; init; } = string.Empty;
    public DateTime Timestamp { get; init; }
}
public sealed class ReportStatCardViewModel
{
    [Required] public string Label { get; init; } = string.Empty;
    [Required] public string Value { get; init; } = string.Empty;
}

public sealed class ReportFilterViewModel
{
    [Display(Name = "From"), DataType(DataType.Date)]
    public DateTime? DateFrom { get; set; }

    [Display(Name = "To"), DataType(DataType.Date)]
    public DateTime? DateTo { get; set; }

    [Display(Name = "Company")]
    public int? CompanyId { get; set; }
}

public sealed class ReportViewModel
{
    public ReportFilterViewModel Filters { get; set; } = new();
    public List<ReportStatCardViewModel> StatCards { get; set; } = new();
    public SelectList? CompanyList { get; set; }
}

public sealed class DailyReportRow
{
    public DateTime Date { get; init; }
    public int Jobs { get; init; }
    public int Applications { get; init; }
}

public sealed class TopCompanyRow
{
    public int CompanyId { get; init; }
    public string CompanyName { get; init; } = string.Empty;
    public int Applications { get; init; }
    public int JobListings { get; init; }
}

public sealed class PdfReportViewModel
{
    public ReportFilterViewModel Filters { get; init; } = new();
    public string CompanyName { get; init; } = "All Companies";
    public IReadOnlyList<ReportStatCardViewModel> StatCards { get; init; } = Array.Empty<ReportStatCardViewModel>();
    public IReadOnlyList<DailyReportRow> DailyRows { get; init; } = Array.Empty<DailyReportRow>();
    public IReadOnlyList<TopCompanyRow> TopCompanies { get; init; } = Array.Empty<TopCompanyRow>();
    public DateTime ReportDate { get; init; } = DateTime.UtcNow;
}




public sealed class AdminNotificationSettingsViewModel
{
    [Display(Name = "New application received")]
    public bool NotifyOnNewApplication { get; set; }

    [Display(Name = "Parsing low confidence")]
    public bool NotifyOnLowConfidenceParse { get; set; }

    [Display(Name = "Message with flagged terms")]
    public bool NotifyOnFlaggedMessage { get; set; }
}


// --- Companies (NEW) ---
public sealed class CompanyRow
{
    public int Id { get; init; }
    [Required] public string Name { get; init; } = string.Empty;
    public string? Industry { get; init; }
    public string? Location { get; init; }
    public string? Status { get; init; }
    public int Jobs { get; init; }
}

public sealed class CompaniesIndexViewModel
{
    public string Status { get; init; } = "All"; // All | Verified | Unverified | Incomplete | Rejected
    public string Query { get; init; } = string.Empty;

    public int AllCount { get; init; }
    public int VerifiedCount { get; init; }
    public int UnverifiedCount { get; init; }
    public int IncompleteCount { get; init; }
    public int RejectedCount { get; init; }

    public PagedResult<CompanyRow> Items { get; init; } = new();
}

public sealed class CompanyPreviewViewModel
{
    public int Id { get; init; }
    [Required] public string Name { get; init; } = string.Empty;
    public string? Industry { get; init; }
    public string? Location { get; init; }
    public string? Description { get; init; }
    public string? Status { get; init; }

    public IReadOnlyList<ApprovalRow> RecentJobs { get; init; } = Array.Empty<ApprovalRow>();
}

public sealed class RecruiterRow
{
    public int Id { get; init; }
    [Required] public string Name { get; init; } = string.Empty;
    [Required] public string Email { get; init; } = string.Empty;
    [Required] public string Status { get; init; } = "Active";     // user_status
    [Required] public string Role { get; init; } = "Recruiter";     // user_role
    public string? Company { get; init; }
    public string? CompanyStatus { get; init; }                     // company_status
    public DateTime CreatedAt { get; init; }
}

public sealed class RecruitersIndexViewModel
{
    public string Status { get; init; } = "All";  // All | Active | Pending | Suspended
    public string Query { get; init; } = string.Empty;

    public int AllCount { get; init; }
    public int ActiveCount { get; init; }
    public int PendingCount { get; init; }        // user Inactive or company Pending
    public int SuspendedCount { get; init; }

    public PagedResult<RecruiterRow> Items { get; init; } = new();
}

public sealed class RecruiterPreviewViewModel
{
    public int Id { get; init; }
    [Required] public string FirstName { get; init; } = string.Empty;
    [Required] public string LastName { get; init; } = string.Empty;
    [Required] public string Email { get; init; } = string.Empty;
    [Required] public string Role { get; init; } = "Recruiter";
    [Required] public string Status { get; init; } = "Active";
    public DateTime CreatedAt { get; init; }

    public string? CompanyName { get; init; }
    public string? CompanyStatus { get; init; }
    public string? Phone { get; init; }
    public string? Address { get; init; }
}


// ---- Admin Settings VMs (NEW) ----
public sealed class BrandingSettingsViewModel
{
    [Display(Name = "Primary colour")]
    public string PrimaryColor { get; set; } = "#2563eb";

    [Display(Name = "Logo URL")]
    public string? LogoUrl { get; set; }
}

public sealed class LegalSettingsViewModel
{
    [Display(Name = "Terms & Conditions")]
    public string Terms { get; set; } = string.Empty;

    [Display(Name = "Privacy Policy")]
    public string Privacy { get; set; } = string.Empty;
}
