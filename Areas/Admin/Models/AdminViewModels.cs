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
    public string Sort { get; set; } = "id_desc"; // id_asc | id_desc
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
    public bool IsBlocked { get; init; }
    public string? BlockedReason { get; init; }
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

public sealed class ReportFilterViewModel : IValidatableObject
{
    [Display(Name = "From"), DataType(DataType.Date)]
    public DateTime? DateFrom { get; set; }

    [Display(Name = "To"), DataType(DataType.Date)]
    public DateTime? DateTo { get; set; }

    [Display(Name = "Company")]
    public int? CompanyId { get; set; }

    // Why: prevent inverted date ranges that would produce empty/incorrect reports
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (DateFrom.HasValue && DateTo.HasValue && DateFrom > DateTo)
            yield return new ValidationResult("The 'From' date cannot be after the 'To' date.", new[] { nameof(DateFrom), nameof(DateTo) });
    }
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
    [Required, EmailAddress] public string Email { get; init; } = string.Empty; // added EmailAddress
    [Required] public string Status { get; init; } = "Active";     // user_status
    [Required] public string Role { get; init; } = "Recruiter";     // user_role
    public string? Company { get; init; }
    public string? CompanyStatus { get; init; }
    public int? CompanyId { get; init; }                      // company_status
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
    [Required, EmailAddress] public string Email { get; init; } = string.Empty; // added EmailAddress
    [Required] public string Role { get; init; } = "Recruiter";
    [Required] public string Status { get; init; } = "Active";
    public DateTime CreatedAt { get; init; }

    public string? CompanyName { get; init; }
    public string? CompanyStatus { get; init; }
    [Phone] public string? Phone { get; init; } // added Phone
    public string? Address { get; init; }

    public int? CompanyId { get; init; }
    public string? CompanyIndustry { get; init; }
    public string? CompanyLocation { get; init; }
    public string? CompanyDescription { get; init; }
    public int OpenJobs { get; init; }
    public bool HasCompany => CompanyId.HasValue;
}

// ---- Admin Settings VMs (NEW) ----
public sealed class BrandingSettingsViewModel
{
    [Display(Name = "Primary colour")]
    [RegularExpression("^#(?:[A-Fa-f0-9]{3}){1,2}$", ErrorMessage = "Enter a valid hex color like #2563eb or #fff.")] // why: ensure CSS-safe color
    public string PrimaryColor { get; set; } = "#2563eb";

    [Display(Name = "Logo URL"), Url(ErrorMessage = "Enter a valid URL.")] // why: prevent malformed asset URLs
    public string? LogoUrl { get; set; }
}

public sealed class LegalSettingsViewModel
{
    [Display(Name = "Terms & Conditions")]
    public string Terms { get; set; } = string.Empty;

    [Display(Name = "Privacy Policy")]
    public string Privacy { get; set; } = string.Empty;
}

// Compact item used in dropdown & list
public sealed class NotificationListItemViewModel
{
    public int Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? TextPreview { get; init; }
    public string? Type { get; init; }
    public DateTime CreatedAt { get; init; }
    public bool IsRead { get; init; }
}

// Paged index for Notification Centre
public sealed class NotificationsIndexViewModel
{
    public PagedResult<NotificationListItemViewModel> Items { get; init; } = new();
    public int UnreadCount { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 10;
    public string Filter { get; init; } = "all";      // "all" | "unread"
    public string? Type { get; init; }                // exact type match
    public List<string> AvailableTypes { get; init; } = new(); // for dropdown
}

/* ===========================
 * ======== AI VMs ===========
 * ===========================
 */

// Templates list item
public sealed class AiTemplate
{
    [Required] public string Name { get; set; } = string.Empty;
    public string RoleExample { get; set; } = "—";
    public DateTime Updated { get; set; } = DateTime.Now;
    public int JobId { get; set; }
    public string Requirements { get; set; } = string.Empty;
}

// Admin Dry-Run form/result
public sealed class AiDryRunVM
{
    [Required] public string JobTitle { get; set; } = "Backend Developer";
    [Required] public string JobRequirements { get; set; } = string.Empty; // newline-separated
    [Required] public string ResumeJson { get; set; } = "{}";
    public byte? Score { get; set; }
    public string? Explanation { get; set; }
    public string? Error { get; set; }
}

// Health page VM
public sealed class AiHealthVM
{
    // Config
    public string ModelText { get; set; } = string.Empty;
    public string ModelEmbed { get; set; } = string.Empty;

    // Text model
    public bool TextOk { get; set; }
    public long TextLatencyMs { get; set; }
    public string TextMessage { get; set; } = string.Empty;

    // NEW: raw HTTP/status/debug for text model
    public int? TextStatus { get; set; }           // e.g., 200
    public string? TextRequestId { get; set; }     // e.g., req_abc123

    // Embedding model
    public bool EmbedOk { get; set; }
    public long EmbedLatencyMs { get; set; }
    public string EmbedMessage { get; set; } = string.Empty;

    // NEW: raw HTTP/status/debug for embedding model
    public int? EmbedStatus { get; set; }          // e.g., 200
    public string? EmbedRequestId { get; set; }    // e.g., req_def456

    public bool OverallOk { get; set; }
}

public sealed class FlagConversationViewModel : IValidatableObject
{
    [Required] public int ConversationId { get; set; }

    // Selected preset reason key
    public string? ReasonKey { get; set; } // e.g., "inappropriate", "spam", "harassment", "other"

    // Custom free-text reason (used when ReasonKey == "other" or when none selected)
    [StringLength(1000)]
    public string? CustomReason { get; set; }

    // For dropdown
    public List<SelectListItem> PresetReasons { get; set; } = new();

    public string ResolveReason()
    {
        // Why: Ensure non-empty reason for audit and user-facing banner
        if (!string.IsNullOrWhiteSpace(CustomReason)) return CustomReason!.Trim();

        return ReasonKey switch
        {
            "inappropriate" => "Inappropriate text",
            "spam" => "Spam/scam content",
            "harassment" => "Harassment or abusive behaviour",
            "personal_info" => "Sharing personal/sensitive information",
            "other" => "Other (unspecified)",
            _ => "Flagged by admin"
        };
    }

    // Why: enforce that some reason exists for moderation audit trail
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(ReasonKey) && string.IsNullOrWhiteSpace(CustomReason))
            yield return new ValidationResult("Please select a reason or provide a custom reason.", new[] { nameof(ReasonKey), nameof(CustomReason) });
    }


}

public class AiJobPolicyCheckRequestVM
{
    [Required]
    public int Id { get; set; }
}

public class AiJobPolicyCheckItemVM
{
    [Required]
    public string Issue { get; set; } = "";
    // Advice | Warning | Violation
    [Required]
    public string Severity { get; set; } = "Advice";
}

public class AiJobPolicyCheckResultVM
{
    public bool Pass { get; set; } = true;
    public string Summary { get; set; } = "Looks good.";
    public List<AiJobPolicyCheckItemVM> Items { get; set; } = new();
    // Optional debugging note (not shown to end users)
    public string? RawNote { get; set; }
}

/* ===============================
 * === Company AI Check VMs ======
 * ===============================
 */

// Minimal, controller-agnostic VMs (do NOT move into controllers)
public sealed class AiCompanyProfileCheckItemVM
{
    [Required] public string Issue { get; set; } = string.Empty;            // human-readable
    [Required] public string Severity { get; set; } = "Advice";             // Advice | Warning | Violation
}

public sealed class AiCompanyProfileCheckResultVM
{
    public bool Pass { get; set; } = true;
    public string Summary { get; set; } = "Looks good.";
    public List<AiCompanyProfileCheckItemVM> Items { get; set; } = new();

    // Optional metadata for the UI (not required to persist)
    public bool FromCache { get; set; } = false;
    public DateTime CachedAt { get; set; } = DateTime.UtcNow;
}
