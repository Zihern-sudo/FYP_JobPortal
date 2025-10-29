using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;

namespace JobPortal.Areas.Recruiter.Models
{
    public enum JobStatus { Draft, Open, Paused, Closed }

    public class JobCreateVm
    {
        [Required, Display(Name = "Job Title")]
        public string job_title { get; set; } = string.Empty;

        public string? job_description { get; set; }
        public string? job_requirements { get; set; }

        [Display(Name = "Nice-to-have Requirements")]
        public string? job_requirements_nice { get; set; }

        public decimal? salary_min { get; set; }
        public decimal? salary_max { get; set; }

        [Display(Name = "Status")]
        public JobStatus job_status { get; set; } = JobStatus.Open;
    }

    public class JobEditVm
    {
        [HiddenInput]
        public int job_listing_id { get; set; }

        [Display(Name = "Status")]
        public JobStatus job_status { get; set; }

        public string job_title { get; set; } = string.Empty;
        public string? job_description { get; set; }
        public string? job_requirements { get; set; }

        [Display(Name = "Nice-to-have Requirements")]
        public string? job_requirements_nice { get; set; }

        public DateTime? date_posted { get; set; }
    }

    public record JobItemVM(
        int Id,
        string Title,
        string Location,
        JobStatus Status,
        string CreatedAt
    );

    public record JobRequirementVM(
        string MustHaves,
        string? NiceToHaves
    );

    public record CandidateItemVM(
        int Id,
        string Name,
        string Stage,
        int Score,
        string AppliedAt,
        bool LowConfidence,
        bool Override
    );

    public record CandidateVM(
        int ApplicationId,
        int UserId,
        string Name,
        string Email,
        string? Phone,
        string Summary,
        string Status
    );

    public record NoteVM(
        int Id,
        string Author,
        string Text,
        string CreatedAt,
        bool FromRecruiter
    );

    public record ThreadListItemVM(
        int Id,
        string JobTitle,
        string Participant,
        string LastSnippet,
        string LastAt,
        int UnreadCount
    );

    public record TemplateItemVM(
        int Id,
        string Name,
        string? Subject,
        string Snippet
    );

    public class TemplateFormVM
    {
        public int? TemplateId { get; set; }
        public string Name { get; set; } = "";
        public string? Subject { get; set; }
        public string Body { get; set; } = "";
        public string Status { get; set; } = "Active";
    }

    // Offer form for recruiters
    public class OfferFormVM
    {
        [HiddenInput]
        public int ApplicationId { get; set; }

        [Display(Name = "Salary Offer"), DataType(DataType.Currency)]
        public decimal? SalaryOffer { get; set; }

        [Display(Name = "Start Date"), DataType(DataType.Date)]
        public DateTime? StartDate { get; set; }

        [Display(Name = "Contract Type")]
        public string? ContractType { get; set; }

        [Display(Name = "Notes")]
        public string? Notes { get; set; }
    }
}
