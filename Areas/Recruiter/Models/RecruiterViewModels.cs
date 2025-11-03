// File: Areas/Recruiter/Models/RecruiterViewModels.cs
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using JobPortal.Areas.Shared.Models;

namespace JobPortal.Areas.Recruiter.Models
{
    public enum JobStatus { Draft, Open, Paused, Closed }

    public static class JobCatalog
    {
        public static readonly string[] Categories = new[]
        {
            "Full Time", "Part Time", "Contract", "Temporary", "Internship"
        };

        public static readonly string[] WorkModes = new[]
        {
            "On-site", "Hybrid", "Remote"
        };
    }

    public class JobCreateVm : IValidatableObject
    {
        [Required, Display(Name = "Job Title")]
        public string job_title { get; set; } = string.Empty;

        public string? job_description { get; set; }

        [Display(Name = "Must-have Requirements")]
        public string? job_requirements { get; set; }

        [Display(Name = "Nice-to-have Requirements")]
        public string? job_requirements_nice { get; set; }

        [Range(typeof(decimal), "0.01", "79228162514264337593543950335", ErrorMessage = "Minimum salary must be > 0")]
        [Display(Name = "Salary Min")]
        [DataType(DataType.Currency)]
        public decimal? salary_min { get; set; }

        [Range(typeof(decimal), "0.01", "79228162514264337593543950335", ErrorMessage = "Maximum salary must be > 0")]
        [Display(Name = "Salary Max")]
        [DataType(DataType.Currency)]
        public decimal? salary_max { get; set; }

        [Required, Display(Name = "Job Category")]
        public string job_category { get; set; } = "Full Time"; // DB default is 'Full Time' :contentReference[oaicite:2]{index=2}

        [Required, Display(Name = "Work Mode")]
        public string work_mode { get; set; } = "On-site"; // DB default is 'On-site' :contentReference[oaicite:3]{index=3}

        [DataType(DataType.Date), Display(Name = "Application Deadline")]
        public DateTime? expiry_date { get; set; }

        [Display(Name = "Status")]
        public JobStatus job_status { get; set; } = JobStatus.Open;

        // Cross-field validation
        public IEnumerable<ValidationResult> Validate(ValidationContext context)
        {
            if (salary_min.HasValue && salary_max.HasValue && salary_min.Value > salary_max.Value)
                yield return new ValidationResult("Minimum salary cannot exceed maximum salary.", new[] { nameof(salary_min), nameof(salary_max) });

            if (!JobCatalog.Categories.Contains(job_category))
                yield return new ValidationResult("Invalid Job Category.", new[] { nameof(job_category) });

            if (!JobCatalog.WorkModes.Contains(work_mode))
                yield return new ValidationResult("Invalid Work Mode.", new[] { nameof(work_mode) });
        }
    }

    public class JobEditVm : IValidatableObject
    {
        [HiddenInput]
        public int job_listing_id { get; set; }

        [Display(Name = "Status")]
        public JobStatus job_status { get; set; }

        public string job_title { get; set; } = string.Empty;
        public string? job_description { get; set; }

        [Display(Name = "Must-have Requirements")]
        public string? job_requirements { get; set; }

        [Display(Name = "Nice-to-have Requirements")]
        public string? job_requirements_nice { get; set; }

        [Display(Name = "Salary Min"), DataType(DataType.Currency)]
        public decimal? salary_min { get; set; }

        [Display(Name = "Salary Max"), DataType(DataType.Currency)]
        public decimal? salary_max { get; set; }

        [Required, Display(Name = "Job Category")]
        public string job_category { get; set; } = "Full Time";

        [Required, Display(Name = "Work Mode")]
        public string work_mode { get; set; } = "On-site";

        public DateTime? date_posted { get; set; }

        [DataType(DataType.Date), Display(Name = "Application Deadline")]
        public DateTime? expiry_date { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext context)
        {
            if (salary_min.HasValue && salary_max.HasValue && salary_min.Value > salary_max.Value)
                yield return new ValidationResult("Minimum salary cannot exceed maximum salary.", new[] { nameof(salary_min), nameof(salary_max) });

            if (!JobCatalog.Categories.Contains(job_category))
                yield return new ValidationResult("Invalid Job Category.", new[] { nameof(job_category) });

            if (!JobCatalog.WorkModes.Contains(work_mode))
                yield return new ValidationResult("Invalid Work Mode.", new[] { nameof(work_mode) });
        }
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

    public class JobTemplateFormVM
    {
        public int? TemplateId { get; set; }

        [Required, Display(Name = "Template Name")]
        public string Name { get; set; } = "";

        [Required, Display(Name = "Job Title")]
        public string Title { get; set; } = "";

        [Display(Name = "Job Description")]
        public string? Description { get; set; }

        [Display(Name = "Must-have Requirements")]
        public string? MustHaves { get; set; }

        [Display(Name = "Nice-to-have Requirements")]
        public string? NiceToHaves { get; set; }

        [Display(Name = "Status")]
        public JobStatus Status { get; set; } = JobStatus.Open;
    }

    public record TemplateRowVM(int Id, string Name, string Snippet);

    public record TemplateModalVM(
        int JobId,
        string Query,
        IList<TemplateRowVM> Recents,
        IList<TemplateRowVM> Items
    );

    public class RegisterVm
    {
        [Required, Display(Name = "First Name"), StringLength(60)]
        public string first_name { get; set; } = "";

        [Required, Display(Name = "Last Name"), StringLength(60)]
        public string last_name { get; set; } = "";

        [Required, EmailAddress, Display(Name = "Email"), StringLength(190)]
        public string email { get; set; } = "";

        [Required, DataType(DataType.Password), StringLength(100, MinimumLength = 6)]
        public string password { get; set; } = "";

        [Required, DataType(DataType.Password), Display(Name = "Confirm Password"), Compare(nameof(password))]
        public string confirm { get; set; } = "";
    }

    public class CompanyProfileVm
    {
        [Required, Display(Name = "Company Name"), StringLength(160)]
        public string company_name { get; set; } = "";

        [Display(Name = "Industry"), StringLength(120)]
        public string? company_industry { get; set; }

        [Display(Name = "Location"), StringLength(120)]
        public string? company_location { get; set; }

        [Display(Name = "Description")]
        public string? company_description { get; set; }
    }

    public class DashboardVm
    {
        public int JobsCount { get; set; }
        public int OpenJobs { get; set; }
        public int ApplicationsCount { get; set; }
        public int UnreadThreads { get; set; }

        public IList<DashJobItem> LatestJobs { get; set; } = new List<DashJobItem>();
        public IList<DashAppItem> LatestApplications { get; set; } = new List<DashAppItem>();
    }

    public record DashJobItem(int Id, string Title, string Status, string CreatedAt);
    public record DashAppItem(int Id, string Candidate, string JobTitle, string Status, string UpdatedAt);

    public class CandidatesIndexVM
    {
        public IList<CandidateItemVM> Items { get; set; } = new List<CandidateItemVM>();
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalCount { get; set; }
        public int TotalPages { get; set; }
        public string Query { get; set; } = "";
        public string Stage { get; set; } = "";
    }

    public class BulkMessagePostVM
    {
        [Required]
        public int[] SelectedIds { get; set; } = Array.Empty<int>();
        public string? Text { get; set; }
        public int? TemplateId { get; set; }
        public string? Date { get; set; }
        public string? Time { get; set; }
    }

    public class InboxIndexVM
    {
        public IList<ThreadListItemVM> Items { get; set; } = new List<ThreadListItemVM>();
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalCount { get; set; }
        public int TotalPages { get; set; }
        public string Query { get; set; } = "";
        public string Filter { get; set; } = "";
    }

    public class TemplatesIndexVM
    {
        public IList<TemplateItemVM> Items { get; set; } = new List<TemplateItemVM>();
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalCount { get; set; }
        public int TotalPages { get; set; }
        public string Query { get; set; } = "";
        public bool IsArchivedList { get; set; }
        public bool IsJobPost { get; set; }
        public int? ThreadId { get; set; }
        
    }

    public class BulkIndexVM
    {
        public IList<CandidateItemVM> Items { get; set; } = new List<CandidateItemVM>();
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalCount { get; set; }
        public int TotalPages { get; set; }
        public string Query { get; set; } = "";
        public string Stage { get; set; } = "";
    }

    public class JobsIndexVM
    {
        public IList<job_listing> Items { get; set; } = new List<job_listing>();
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalCount { get; set; }
        public int TotalPages { get; set; }
        public string Query { get; set; } = "";
        public string Status { get; set; } = "";
        public string Order { get; set; } = "";
    }
}
