namespace JobPortal.Areas.JobSeeker.Models
{
    public class DashboardIndexViewModel
    {
        public string? SearchKeyword { get; set; } // Optional: pre-fill search
        public List<RecentJobViewModel> RecentJobs { get; set; } = new List<RecentJobViewModel>();
    }

    public class RecentJobViewModel
    {
        public int JobId { get; set; }
        public string JobTitle { get; set; } = string.Empty;
        public string CompanyName { get; set; } = string.Empty;
        public string Industry { get; set; } = string.Empty;
    }

        public class UserActivityViewModel
    {
        public string Description { get; set; } = string.Empty;
        public DateTime Date { get; set; }
    }

    public class UserNotificationViewModel
    {
        public string Message { get; set; } = string.Empty;
        public DateTime Date { get; set; }
    }
}
