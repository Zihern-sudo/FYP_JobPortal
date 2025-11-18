using Microsoft.AspNetCore.Http;
using System.ComponentModel.DataAnnotations;
using JobPortal.Areas.Shared.Models;

namespace JobPortal.Areas.JobSeeker.Models
{
    public class ApplyViewModel
    {
        // 🔹 Unique ID for the application (for edit/update operations)
        public int ApplicationId { get; set; }
        // 🔹 The Job being applied for
        public int JobId { get; set; }

        [Display(Name = "Job Title")]
        public string JobTitle { get; set; } = string.Empty;

        // 🔹 The applicant's personal details
        [Display(Name = "Full Name")]
        public string FullName { get; set; } = string.Empty;

        [Display(Name = "Email Address")]
        public string Email { get; set; } = string.Empty;

        [Display(Name = "Expected Salary")]
        [Range(0, int.MaxValue, ErrorMessage = "Expected salary must be positive.")]
        public int? ExpectedSalary { get; set; }
        [Display(Name = "Describe Yourself")]
        [StringLength(2000, ErrorMessage = "Description is too long.")]
        public string? Description { get; set; }
        // 🆕 Resume selection
        public int? SelectedResumeId { get; set; } // selected from dropdown
        [Display(Name = "Upload Resume")]
        public IFormFile? ResumeFile { get; set; }
        public List<resume>? ExistingResumes { get; set; } // populate from DB
        public bool HasApplied { get; set; }

        // 🔹 Optional feedback/result fields for viewing application later
        public string? ApplicationStatus { get; set; }
        public DateTime? DateSubmitted { get; set; }
    }
}
