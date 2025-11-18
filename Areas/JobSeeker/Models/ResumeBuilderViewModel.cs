using Microsoft.AspNetCore.Http;
using System.ComponentModel.DataAnnotations;

namespace JobPortal.Areas.JobSeeker.Models
{
    public class ResumeBuilderViewModel
    {
        [Required]
        [Display(Name = "Full Name")]
        public string FullName { get; set; } = "";

        [Required]
        [EmailAddress]
        public string Email { get; set; } = "";

        [Display(Name = "Phone Number")]
        public string? Phone { get; set; }

        [Display(Name = "Address")]
        public string? Address { get; set; }

        [Display(Name = "Professional Summary")]
        public string? Summary { get; set; }

        [Display(Name = "Education")]
        public string? Education { get; set; }

        [Display(Name = "Experience")]
        public string? Experience { get; set; }

        [Display(Name = "Skills")]
        public string? Skills { get; set; }

        [Display(Name = "Certifications")]
        public string? Certifications { get; set; }
        public string? Projects { get; set; }


        [Display(Name = "Profile Picture")]
        public IFormFile? ProfilePic { get; set; }

        [Required]
        [Display(Name = "Resume Template")]
        public string Template { get; set; } = "classic";

        // Optional: Preloaded profile image path for display in the builder
        public string? ProfileImagePath { get; set; }
    }
}
