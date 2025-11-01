namespace JobPortal.Areas.JobSeeker.Models
{
    public class MyApplicationsViewModel
    {
        // Dashboard summary + activity + notifications
        public ApplicationsDashboardViewModel Dashboard { get; set; } = new();

        // Applications list (paginated)
        public List<JobPortal.Areas.Shared.Models.job_application> Applications { get; set; } = new();

        // Pagination
        public int CurrentPage { get; set; }
        public int TotalPages { get; set; }
    }
}
