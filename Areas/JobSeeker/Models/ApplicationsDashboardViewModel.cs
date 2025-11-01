namespace JobPortal.Areas.JobSeeker.Models
{
    public class ApplicationsDashboardViewModel
    {
        // Summary cards
        public int TotalApplications { get; set; }
        public int ApplicationsInReview { get; set; }
        public int InterviewsScheduled { get; set; }

        // Recent activity (applied, in review, interviews)
        public List<RecentActivityViewModel> RecentActivities { get; set; } = new();

        // Recent notifications (new job listings or status updates)
        public List<RecentNotificationViewModel> RecentNotifications { get; set; } = new();
    }

    public class RecentActivityViewModel
    {
        public string Message { get; set; } = string.Empty;
        public DateTime Date { get; set; }
    }

    public class RecentNotificationViewModel
    {
        public string Message { get; set; } = string.Empty;
        public DateTime Date { get; set; }
    }
}
