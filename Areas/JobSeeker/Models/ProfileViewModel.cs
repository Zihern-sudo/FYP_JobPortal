namespace JobPortal.Areas.JobSeeker.Models
{
    public class ProfileViewModel
    {
        public int UserId { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Email { get; set; }
        public bool TwoFAEnabled { get; set; }
        public string? TwoFASecret { get; set; }
        public string? Phone { get; set; }
        public string? Address { get; set; }
        public string? Skills { get; set; }
        public string? Education { get; set; }
        public string? WorkExperience { get; set; }
        public string? TargetIndustry { get; set; }

        public IFormFile? ProfileImage { get; set; }
        public string? ProfilePicturePath { get; set; }
        public bool notif_inapp { get; set; }
        public bool notif_job_updates { get; set; }
        public bool notif_messages { get; set; }
        public bool notif_reminders { get; set; }
    }
}
