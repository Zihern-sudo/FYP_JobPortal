using Microsoft.AspNetCore.Http;
using System.ComponentModel.DataAnnotations;

namespace JobPortal.Areas.JobSeeker.Models
{
    public class ApplyViewModel
    {
        // 🔹 The Job being applied for
        public int JobId { get; set; }

        [Display(Name = "Job Title")]
        public string JobTitle { get; set; } = string.Empty;

        // 🔹 The applicant's personal details
        [Display(Name = "Full Name")]
        public string FullName { get; set; } = string.Empty;

        [Display(Name = "Email Address")]
        public string Email { get; set; } = string.Empty;

        // 🔹 The applicant's information
        [Display(Name = "Skills")]
        public string Skills { get; set; } = string.Empty;

        [Display(Name = "Upload Resume")]
        public IFormFile? ResumeFile { get; set; }

        // 🔹 Optional feedback/result fields for viewing application later
        public string? ApplicationStatus { get; set; }
        public DateTime? DateSubmitted { get; set; }
    }
}
