using System;
using System.ComponentModel.DataAnnotations;

namespace JobPortal.Areas.Public.Models
{
    /// <summary>
    /// View model for the Contact page form.
    /// Mirrors the markup in Areas/Public/Views/Home/Contact.cshtml.
    /// </summary>
    public class ContactFormVm
    {
        [Required, StringLength(80)]
        [Display(Name = "Your Name")]
        public string Name { get; set; } = string.Empty;

        [Required, EmailAddress, StringLength(190)]
        [Display(Name = "Your Email")]
        public string Email { get; set; } = string.Empty;

        [Required, StringLength(120)]
        public string Subject { get; set; } = string.Empty;

        [Required, StringLength(4000)]
        public string Message { get; set; } = string.Empty;

        // Optional: light anti-bot honeypot (left empty by real users).
        [Display(AutoGenerateField = false)]
        public string? Hp { get; set; }
    }

    /// <summary>
    /// Minimal projection for job list cards on the public site.
    /// Useful if you later bind the Job List page directly from public controllers.
    /// </summary>
    public class JobListItemVm
    {
        public int Id { get; set; }

        [Required, StringLength(160)]
        public string Title { get; set; } = string.Empty;

        [StringLength(120)]
        public string? Location { get; set; }

        /// <summary>
        /// e.g., "Full Time", "Part Time", "Contract"
        /// </summary>
        [StringLength(40)]
        public string? EmploymentType { get; set; }

        /// <summary>
        /// e.g., "RM4,000–RM6,000"
        /// </summary>
        [StringLength(60)]
        public string? SalaryRange { get; set; }

        [DataType(DataType.Date)]
        public DateTime Deadline { get; set; } = DateTime.UtcNow.AddDays(14);
    }
}
