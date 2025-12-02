// File: Areas/Recruiter/Models/RecruiterViewModels.cs
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using JobPortal.Areas.Shared.Models;
using JobPortal.Areas.Shared.Extensions;

namespace JobPortal.Areas.Recruiter.Models
{
    public enum JobStatus { Draft, Open, Paused, Closed, PendingApproval }

    public static class JobCatalog
    {
        public static readonly string[] Categories = new[]
        {
            "Full Time", "Part Time", "Contract", "Temporary", "Internship"
        };

        // Domain categories for job_category
        public static readonly string[] JobCategories = new[]
        {
            "Marketing", "Customer Service", "Information Technology", "Accounting", "Finance",
            "Other" // why: allow UI toggle to show custom input
        };

        public static readonly string[] WorkModes = new[]
        {
            "On-site", "Hybrid", "Remote"
        };

        // ---- helpers for validation ----
        public static bool IsStandardJobCategory(string? value) =>
            !string.IsNullOrWhiteSpace(value) && JobCategories.Contains(value.Trim());

        public static bool IsValidCustomCategory(string? value)
        {
            // why: accept recruiter-entered custom categories; keep constraints sane
            var s = value?.Trim();
            if (string.IsNullOrWhiteSpace(s)) return false;
            return s.Length <= 120; // soft cap for DB/UI
        }
    }

    public class JobCreateVm : IValidatableObject
    {
        private string _jobTitle = string.Empty;

        [Required, Display(Name = "Job Title"), StringLength(160)]
        [RegularExpression(@".*\S.*", ErrorMessage = "Job Title cannot be empty or whitespace.")]
        public string job_title
        {
            get => _jobTitle;
            set => _jobTitle = (value ?? string.Empty).Trim();
        }

        private string? _jobDescription;

        [Required(ErrorMessage = "Job Description is required.")]
        [Display(Name = "Job Description"), StringLength(10000)]
        public string? job_description
        {
            get => _jobDescription;
            set => _jobDescription = value?.Trim();
        }

        private string? _jobRequirements;

        [Required(ErrorMessage = "Must-have requirements are required.")]
        [Display(Name = "Must-have Requirements"), StringLength(8000)]
        public string? job_requirements
        {
            get => _jobRequirements;
            set => _jobRequirements = value?.Trim();
        }

        private string? _jobRequirementsNice;

        [Display(Name = "Nice-to-have Requirements"), StringLength(8000)]
        public string? job_requirements_nice
        {
            get => _jobRequirementsNice;
            set => _jobRequirementsNice = value?.Trim();
        }

        [Range(typeof(decimal), "0.01", "79228162514264337593543950335",
            ErrorMessage = "Minimum salary must be > 0")]
        [Required, Display(Name = "Salary Min"), DataType(DataType.Currency)]
        public decimal? salary_min { get; set; }

        [Range(typeof(decimal), "0.01", "79228162514264337593543950335",
            ErrorMessage = "Maximum salary must be > 0")]
        [Required, Display(Name = "Salary Max"), DataType(DataType.Currency)]
        public decimal? salary_max { get; set; }

        private string _jobType = "Full Time";

        [Required, Display(Name = "Employment Type")]
        public string job_type
        {
            get => _jobType;
            set => _jobType = (value ?? string.Empty).Trim();
        }

        private string _workMode = "On-site";

        [Required, Display(Name = "Work Mode")]
        public string work_mode
        {
            get => _workMode;
            set => _workMode = (value ?? string.Empty).Trim();
        }

        private string _jobCategory = "Marketing";

        [Required, Display(Name = "Job Category")]
        public string job_category
        {
            get => _jobCategory;
            set => _jobCategory = (value ?? string.Empty).Trim();
        }

        [Required, DataType(DataType.Date), Display(Name = "Application Deadline")]
        public DateTime? expiry_date { get; set; }

        // prevent client from posting/overriding status during creation
        [Display(Name = "Status")]
        public JobStatus job_status { get; set; } = JobStatus.Open; // ignored by UI/controller

        public IEnumerable<ValidationResult> Validate(ValidationContext context)
        {
            if (salary_min.HasValue && salary_max.HasValue && salary_min.Value > salary_max.Value)
                yield return new ValidationResult(
                    "Minimum salary cannot exceed maximum salary.",
                    new[] { nameof(salary_min), nameof(salary_max) });

            if (!JobCatalog.Categories.Contains(job_type))
                yield return new ValidationResult("Invalid Employment Type.", new[] { nameof(job_type) });

            if (!JobCatalog.WorkModes.Contains(work_mode))
                yield return new ValidationResult("Invalid Work Mode.", new[] { nameof(work_mode) });

            if (!(JobCatalog.IsStandardJobCategory(job_category) || JobCatalog.IsValidCustomCategory(job_category)))
                yield return new ValidationResult(
                    "Invalid Job Category. Choose one or enter a custom category.",
                    new[] { nameof(job_category) });

            if (expiry_date.HasValue && expiry_date.Value.Date < DateTime.Today)
                yield return new ValidationResult(
                    "Application deadline cannot be in the past.",
                    new[] { nameof(expiry_date) });
        }
    }

    public class JobEditVm : IValidatableObject
    {
        [HiddenInput]
        public int job_listing_id { get; set; }

        [Display(Name = "Status")]
        public JobStatus job_status { get; set; }

        private string _jobTitle = string.Empty;

        [Required, Display(Name = "Job Title"), StringLength(160)]
        [RegularExpression(@".*\S.*", ErrorMessage = "Job Title cannot be empty or whitespace.")]
        public string job_title
        {
            get => _jobTitle;
            set => _jobTitle = (value ?? string.Empty).Trim();
        }

        private string? _jobDescription;

        [Required(ErrorMessage = "Job Description is required.")]
        [Display(Name = "Job Description"), StringLength(10000)]
        public string? job_description
        {
            get => _jobDescription;
            set => _jobDescription = value?.Trim();
        }

        private string? _jobRequirements;

        [Required(ErrorMessage = "Must-have requirements are required.")]
        [Display(Name = "Must-have Requirements"), StringLength(8000)]
        public string? job_requirements
        {
            get => _jobRequirements;
            set => _jobRequirements = value?.Trim();
        }

        private string? _jobRequirementsNice;

        [Display(Name = "Nice-to-have Requirements"), StringLength(8000)]
        public string? job_requirements_nice
        {
            get => _jobRequirementsNice;
            set => _jobRequirementsNice = value?.Trim();
        }

        [Required, Display(Name = "Salary Min"), DataType(DataType.Currency)]
        [Range(typeof(decimal), "0.01", "79228162514264337593543950335",
            ErrorMessage = "Minimum salary must be > 0")]
        public decimal? salary_min { get; set; }

        [Required, Display(Name = "Salary Max"), DataType(DataType.Currency)]
        [Range(typeof(decimal), "0.01", "79228162514264337593543950335",
            ErrorMessage = "Maximum salary must be > 0")]
        public decimal? salary_max { get; set; }

        private string _jobType = "Full Time";

        [Required, Display(Name = "Employment Type")]
        public string job_type
        {
            get => _jobType;
            set => _jobType = (value ?? string.Empty).Trim();
        }

        private string _workMode = "On-site";

        [Required, Display(Name = "Work Mode")]
        public string work_mode
        {
            get => _workMode;
            set => _workMode = (value ?? string.Empty).Trim();
        }

        private string _jobCategory = "Marketing";

        [Required, Display(Name = "Job Category")]
        public string job_category
        {
            get => _jobCategory;
            set => _jobCategory = (value ?? string.Empty).Trim();
        }

        public DateTime? date_posted { get; set; }

        [Required, DataType(DataType.Date), Display(Name = "Application Deadline")]
        public DateTime? expiry_date { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext context)
        {
            if (salary_min.HasValue && salary_max.HasValue && salary_min.Value > salary_max.Value)
                yield return new ValidationResult(
                    "Minimum salary cannot exceed maximum salary.",
                    new[] { nameof(salary_min), nameof(salary_max) });

            if (!JobCatalog.Categories.Contains(job_type))
                yield return new ValidationResult("Invalid Employment Type.", new[] { nameof(job_type) });

            if (!JobCatalog.WorkModes.Contains(work_mode))
                yield return new ValidationResult("Invalid Work Mode.", new[] { nameof(work_mode) });

            if (!(JobCatalog.IsStandardJobCategory(job_category) || JobCatalog.IsValidCustomCategory(job_category)))
                yield return new ValidationResult(
                    "Invalid Job Category. Choose one or enter a custom category.",
                    new[] { nameof(job_category) });

            if (expiry_date.HasValue && expiry_date.Value.Date < DateTime.Today)
                yield return new ValidationResult(
                    "Application deadline cannot be in the past.",
                    new[] { nameof(expiry_date) });

            // Optional: enforce logical order if date_posted present
            if (date_posted.HasValue && expiry_date.HasValue &&
                expiry_date.Value.Date < date_posted.Value.Date)
            {
                yield return new ValidationResult(
                    "Application deadline must be on or after the posted date.",
                    new[] { nameof(expiry_date), nameof(date_posted) });
            }
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

    public class TemplateFormVM : IValidatableObject
    {
        public int? TemplateId { get; set; }

        private string _name = "";
        [Required, Display(Name = "Template Name"), StringLength(120)]
        [RegularExpression(@".*\S.*", ErrorMessage = "Template Name cannot be empty or whitespace.")]
        public string Name
        {
            get => _name;
            set => _name = (value ?? "").Trim();
        }

        private string? _subject;
        [Display(Name = "Subject"), StringLength(160)]
        // optional, but if provided cannot be whitespace-only
        [RegularExpression(@"^\s*$|.*\S.*", ErrorMessage = "Subject cannot be only whitespace.")]
        public string? Subject
        {
            get => _subject;
            set => _subject = value?.Trim();
        }

        private string _body = "";
        [Required, Display(Name = "Body"), StringLength(20000)]
        [RegularExpression(@".*\S.*", ErrorMessage = "Body cannot be empty or whitespace.")]
        [DataType(DataType.MultilineText)]
        public string Body
        {
            get => _body;
            set => _body = (value ?? "").Trim();
        }

        // Optional lifecycle flag; keep conservative allowed values
        public string Status { get; set; } = "Active";

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            // Guard status to a safe whitelist; adjust as needed (e.g., add "Draft")
            var allowed = new[] { "Active", "Inactive", "Archived" };
            if (!string.IsNullOrWhiteSpace(Status) && Array.IndexOf(allowed, Status.Trim()) < 0)
            {
                yield return new ValidationResult(
                    $"Invalid Status. Allowed: {string.Join(", ", allowed)}.",
                    new[] { nameof(Status) });
            }
        }
    }
   public class OfferFormVM : IValidatableObject
{
    [HiddenInput]
    public int ApplicationId { get; set; }

    [Required, Display(Name = "Salary Offer"), DataType(DataType.Currency)]
    [Range(typeof(decimal), "0.01", "79228162514264337593543950335",
        ErrorMessage = "Salary offer must be greater than 0.")]
    public decimal? SalaryOffer { get; set; }

    [Required, Display(Name = "Start Date"), DataType(DataType.Date)]
    public DateTime? StartDate { get; set; }

    [Required, Display(Name = "Contract Type")]
    [StringLength(60)]
    public string? ContractType { get; set; }

    [Display(Name = "Offer Expiry Date"), DataType(DataType.Date)]
    public DateTime? OfferExpiryDate { get; set; }

    [Display(Name = "Work Location"), StringLength(160)]
    public string? WorkLocation { get; set; }

    [Display(Name = "Probation Period (months)")]
    [Range(0, 24, ErrorMessage = "Probation period must be between 0 and 24 months.")]
    public int? ProbationMonths { get; set; }

    [Display(Name = "Benefits Summary")]
    [DataType(DataType.MultilineText)]
    [StringLength(2000)]
    public string? Benefits { get; set; }

    [Display(Name = "Internal Notes")]
    [DataType(DataType.MultilineText)]
    [StringLength(2000)]
    public string? Notes { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (StartDate.HasValue && StartDate.Value.Date < DateTime.Today)
        {
            yield return new ValidationResult(
                "Start date cannot be in the past.",
                new[] { nameof(StartDate) });
        }

        if (OfferExpiryDate.HasValue && OfferExpiryDate.Value.Date < DateTime.Today)
        {
            yield return new ValidationResult(
                "Offer expiry date cannot be in the past.",
                new[] { nameof(OfferExpiryDate) });
        }

        // Optional business rule: expiry should be on or before the start date (if both provided)
        if (OfferExpiryDate.HasValue && StartDate.HasValue &&
            OfferExpiryDate.Value.Date > StartDate.Value.Date)
        {
            yield return new ValidationResult(
                "Offer expiry should be on or before the start date.",
                new[] { nameof(OfferExpiryDate), nameof(StartDate) });
        }
    }
}


    public class JobTemplateFormVM : IValidatableObject
    {
        public int? TemplateId { get; set; }

        private string _name = "";

        [Required, Display(Name = "Template Name"), StringLength(160)]
        [RegularExpression(@".*\S.*", ErrorMessage = "Template Name cannot be empty or whitespace.")]
        public string Name
        {
            get => _name;
            set => _name = (value ?? "").Trim();
        }

        private string _title = "";

        [Required, Display(Name = "Job Title"), StringLength(160)]
        [RegularExpression(@".*\S.*", ErrorMessage = "Job Title cannot be empty or whitespace.")]
        public string Title
        {
            get => _title;
            set => _title = (value ?? "").Trim();
        }

        private string? _description;

        [Required(ErrorMessage = "Job Description is required.")]
        [Display(Name = "Job Description"), StringLength(10000)]
        public string? Description
        {
            get => _description;
            set => _description = value?.Trim();
        }

        private string? _mustHaves;

        [Required(ErrorMessage = "Must-have requirements are required.")]
        [Display(Name = "Must-have Requirements"), StringLength(8000)]
        public string? MustHaves
        {
            get => _mustHaves;
            set => _mustHaves = value?.Trim();
        }

        private string? _niceToHaves;

        [Display(Name = "Nice-to-have Requirements"), StringLength(8000)]
        public string? NiceToHaves
        {
            get => _niceToHaves;
            set => _niceToHaves = value?.Trim();
        }

        private string _jobType = "Full Time";

        [Required, Display(Name = "Employment Type")]
        public string JobType
        {
            get => _jobType;
            set => _jobType = (value ?? "").Trim();
        }

        private string _workMode = "On-site";

        [Required, Display(Name = "Work Mode")]
        public string WorkMode
        {
            get => _workMode;
            set => _workMode = (value ?? "").Trim();
        }

        private string _jobCategory = "Marketing";

        [Required, Display(Name = "Job Category")]
        public string JobCategory
        {
            get => _jobCategory;
            set => _jobCategory = (value ?? "").Trim();
        }

        [Display(Name = "Salary Min"), DataType(DataType.Currency)]
        [Range(typeof(decimal), "0.01", "79228162514264337593543950335",
            ErrorMessage = "Minimum salary must be > 0")]
        public decimal? SalaryMin { get; set; }

        [Display(Name = "Salary Max"), DataType(DataType.Currency)]
        [Range(typeof(decimal), "0.01", "79228162514264337593543950335",
            ErrorMessage = "Maximum salary must be > 0")]
        public decimal? SalaryMax { get; set; }

        [Required, Display(Name = "Application Deadline"), DataType(DataType.Date)]
        public DateTime? ExpiryDate { get; set; }

        public string Status { get; set; } = "Active";

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (SalaryMin.HasValue && SalaryMax.HasValue && SalaryMin.Value > SalaryMax.Value)
            {
                yield return new ValidationResult(
                    "Minimum salary cannot exceed maximum salary.",
                    new[] { nameof(SalaryMin), nameof(SalaryMax) });
            }

            if (!JobCatalog.Categories.Contains(JobType))
                yield return new ValidationResult("Invalid Employment Type.", new[] { nameof(JobType) });

            if (!JobCatalog.WorkModes.Contains(WorkMode))
                yield return new ValidationResult("Invalid Work Mode.", new[] { nameof(WorkMode) });

            if (!(JobCatalog.IsStandardJobCategory(JobCategory) || JobCatalog.IsValidCustomCategory(JobCategory)))
            {
                yield return new ValidationResult(
                    "Invalid Job Category. Choose one or enter a custom category.",
                    new[] { nameof(JobCategory) });
            }

            if (ExpiryDate.HasValue && ExpiryDate.Value.Date < DateTime.Today)
            {
                yield return new ValidationResult(
                    "Application deadline cannot be in the past.",
                    new[] { nameof(ExpiryDate) });
            }
        }
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

        [Required, Display(Name = "Phone Number"), StringLength(16)]
        [RegularExpression(@"^\+[1-9]\d{7,14}$", ErrorMessage = "Use international format, e.g., +60123456789.")]
        public string phone { get; set; } = "";


        [Required, EmailAddress, Display(Name = "Email"), StringLength(190)]
        public string email { get; set; } = "";

        [Required, DataType(DataType.Password), StringLength(20, MinimumLength = 6)]
        [Display(Name = "Password")]
        [RegularExpression(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^A-Za-z0-9]).+$",
            ErrorMessage = "Password must include at least 1 uppercase, 1 lowercase, 1 number, and 1 special character (e.g., Abc123@).")] // why: enforce strength
        public string password { get; set; } = "";

        [Required, DataType(DataType.Password), Display(Name = "Confirm Password")]
        [Compare(nameof(password), ErrorMessage = "Passwords do not match.")]
        public string confirm { get; set; } = "";

        [FromForm(Name = "g-recaptcha-response")]
        [Display(Name = "I'm not a robot")]
        public string? RecaptchaToken { get; set; }
    }

    public class CompanyProfileVm
    {
        [Required, Display(Name = "Company Name"), StringLength(160, MinimumLength = 2)]
        [RegularExpression(@"^(?=.*\S).{2,160}$", ErrorMessage = "Company name can't be empty or whitespace.")]
        public string company_name { get; set; } = "";

        [Display(Name = "Industry"), StringLength(120)]
        [RegularExpression(@"^\s*$|.*\S.*", ErrorMessage = "Industry cannot be only whitespace.")]
        public string? company_industry { get; set; }

        [Display(Name = "Location"), StringLength(120)]
        [RegularExpression(@"^\s*$|.*\S.*", ErrorMessage = "Location cannot be only whitespace.")]
        public string? company_location { get; set; }

        [Display(Name = "Description"), StringLength(1000)]
        [RegularExpression(@"^\s*$|.*\S.*", ErrorMessage = "Description cannot be only whitespace.")]
        public string? company_description { get; set; }
    }

    public class CompanyProfileSubmitVm
    {
        [Required, StringLength(160, MinimumLength = 2)]
        [RegularExpression(@"^(?=.*\S).{2,160}$")]
        public string company_name { get; set; } = "";

        [Required, StringLength(120)]
        [RegularExpression(@".*\S.*", ErrorMessage = "Industry cannot be only whitespace.")]
        public string? company_industry { get; set; }

        [Required, StringLength(120)]
        [RegularExpression(@".*\S.*", ErrorMessage = "Location cannot be only whitespace.")]
        public string? company_location { get; set; }

        [Required, StringLength(1000)]
        [RegularExpression(@".*\S.*", ErrorMessage = "Description cannot be only whitespace.")]
        public string? company_description { get; set; }
    }



    public enum CompanyProfileStatus
    {
        Pending,   // awaiting admin review
        Draft,     // local draft saved by recruiter
        Verified,  // approved & live
        Inactive   // rejected or incomplete
    }

    /// <summary>
    /// Manage page VM: shows either live or draft values, plus status flags.
    /// </summary>
    public class CompanyProfileManageVm : CompanyProfileVm
    {
        public string Status { get; set; } = "Inactive";
        public string? LivePhotoUrl { get; set; }
        public string? DraftPhotoUrl { get; set; }
        public bool HasDraft { get; set; }
        public bool IsPending => string.Equals(Status, "Pending", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// On-disk JSON payload for company drafts (text-only). Photo is stored as file beside this JSON.
    /// </summary>
    public class CompanyDraftPayload
    {
        public string company_name { get; set; } = "";
        public string? company_industry { get; set; }
        public string? company_location { get; set; }
        public string? company_description { get; set; }
        public DateTime saved_at { get; set; } = MyTime.NowMalaysia();
    }

    public class DashboardVm
    {
        public int JobsCount { get; set; }
        public int OpenJobs { get; set; }
        public int ApplicationsCount { get; set; }
        public int UnreadThreads { get; set; }

        public IList<DashJobItem> LatestJobs { get; set; } = new List<DashJobItem>();
        public IList<DashAppItem> LatestApplications { get; set; } = new List<DashAppItem>();
        public string SortJobs { get; set; } = "id_desc";
        public string SortApps { get; set; } = "id_desc";
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
        public string Sort { get; set; } = "id_desc";
    }

    public class BulkMessagePostVM : IValidatableObject
    {
        [Required]
        public int[] SelectedIds { get; set; } = Array.Empty<int>();
        public string? Text { get; set; }
        public int? TemplateId { get; set; }
        public string? Date { get; set; }
        public string? Time { get; set; }

        // Why: prevent empty bulk message and enforce valid scheduling input
        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if ((TemplateId is null || TemplateId == 0) && string.IsNullOrWhiteSpace(Text))
                yield return new ValidationResult("Provide a message body or choose a template.", new[] { nameof(Text), nameof(TemplateId) });

            var hasDate = !string.IsNullOrWhiteSpace(Date);
            var hasTime = !string.IsNullOrWhiteSpace(Time);
            if (hasDate ^ hasTime)
                yield return new ValidationResult("Scheduling requires both date and time.", new[] { nameof(Date), nameof(Time) });
        }
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
        public string Sort { get; set; } = "id_desc";
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
        public string Sort { get; set; } = "id_desc";
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
        public string Sort { get; set; } = "id_desc";
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
        public Dictionary<int, string> LatestApprovalStatuses { get; set; } = new();
    }

    // Generic pager
    public class PagedResult<T>
    {
        public IList<T> Items { get; set; } = new List<T>();
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalItems { get; set; }
        public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling((double)TotalItems / PageSize);
    }

    // A single notification row
    public class NotificationListItemVM
    {
        public int Id { get; set; }
        public string Title { get; set; } = "";
        public string TextPreview { get; set; } = "";
        public string Text { get; set; } = "";
        public string? Type { get; set; }
        public DateTime CreatedAt { get; set; }
        public bool IsRead { get; set; }
    }

    // Page view model for Notification Centre
    public class RecruiterNotificationsIndexVM
    {
        public PagedResult<NotificationListItemVM> Items { get; set; } = new();
        public int UnreadCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }

        // Filters
        public string Filter { get; set; } = "all";   // "all" | "unread"
        public string? Type { get; set; }
        public IList<string> AvailableTypes { get; set; } = new List<string>();
    }

    public class MessageModerationCheckRequestVM
    {
        public int ThreadId { get; set; }
        [Required, StringLength(8000)]
        public string Text { get; set; } = "";
    }

    public class MessageModerationCheckResultVM
    {
        public bool Allowed { get; set; }
        public string Reason { get; set; } = "";
        public string? Category { get; set; }
    }

    // ===================== NEW: Score Breakdown VMs (read-only) =====================
    // Why: present transparent scoring to recruiters (no editing of resume JSON)

    public record ScoreSectionVM(
        string Name,
        int Points,                // awarded points for this section
        int MaxPoints,             // section cap
        IReadOnlyList<string> Matched,  // human-readable hits (e.g., skill names)
        IReadOnlyList<string> Missing   // human-readable gaps
    );

    public record PerRequirementVM(
        string Requirement,        // exact requirement text
        double Similarity,         // 0..1 similarity estimate
        string Category            // "skills" | "experience" | "education" | "extras"
    );

    public class ScoreBreakdownVM
    {
        public byte Total { get; init; }                       // 0..100
        public ScoreSectionVM Skills { get; init; } = new("Skills", 0, 50, Array.Empty<string>(), Array.Empty<string>());
        public ScoreSectionVM Experience { get; init; } = new("Experience", 0, 35, Array.Empty<string>(), Array.Empty<string>());
        public ScoreSectionVM Education { get; init; } = new("Education", 0, 15, Array.Empty<string>(), Array.Empty<string>());
        public ScoreSectionVM Extras { get; init; } = new("Extras", 0, 0, Array.Empty<string>(), Array.Empty<string>());
        public string Notes { get; init; } = "";               // short explanation (model notes)
        public DateTime? EvaluatedAt { get; init; }            // when the score was computed
        public IReadOnlyList<PerRequirementVM> PerRequirement { get; init; } = Array.Empty<PerRequirementVM>();
    }
}

public class NewConversationPostVM
{
    [Required]
    public int ApplicationId { get; set; }
}
