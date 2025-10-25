public record JobItem(int Id, string Title, string Location, string Status, string CreatedAt);
public record JobRequirement(int JobId, string MustHaves, string NiceToHaves);
public record CandidateItem(int Id, string Name, int Score, string Stage, bool LowConfidence, bool Override, string AppliedAt);
public record CandidateProfile(int Id, string Name, string Email, string Phone, string CvFile, string Summary);
public record MessageThread(int Id, string Candidate, string JobTitle, string LastAt, bool Unread);
public record MessageEntry(string From, string Text, string At, bool IsFlagged = false);
public record TemplateItem(int Id, string Name, string Subject, string Snippet);

public static class RecruiterDummy
{
    public static List<JobItem> Jobs => new()
    {
        new(201, "Junior .NET Developer", "Kuala Lumpur", "Open", "Today 09:10"),
        new(202, "QA Tester", "Penang", "Open", "Yesterday 14:22"),
        new(203, "Digital Marketing Exec", "Johor Bahru", "Draft", "2 days ago"),
        new(204, "Data Analyst (Junior)", "Remote", "Paused", "1 week ago")
    };

    public static JobRequirement JobReq(int jobId) => jobId switch
    {
        201 => new(201, "ASP.NET Core, SQL", "EF Core, Git"),
        202 => new(202, "Test cases, Bug tracking", "Selenium, Postman"),
        203 => new(203, "SEO basics, Meta Ads", "Google Analytics"),
        _   => new(jobId, "General comms, Teamwork", "Any BI tool")
    };

    public static List<CandidateItem> CandidatesForJob(int jobId) => new()
    {
        new(501, "Amira Lee",   88, "AI-Screened", false, false, "Today 10:05"),
        new(502, "Daniel Ong",  86, "AI-Screened", false, true,  "Today 09:54"),
        new(503, "Kumar Ravi",  75, "New",         true,  false, "Today 09:10"),
        new(504, "Nur Izzah",   72, "Shortlisted", false, false, "Yesterday 16:30"),
        new(505, "Chong Wei",   69, "New",         true,  false, "Yesterday 13:25")
    };

    public static CandidateProfile Profile(int id) => id switch
    {
        501 => new(501, "Amira Lee", "amira@example.com", "012-3456789", "AmiraLee_CV.pdf",
                   "Junior developer with internship in ASP.NET Core; comfortable with SQL and EF Core."),
        502 => new(502, "Daniel Ong", "daniel@example.com", "017-4567890", "DanielOng_CV.pdf",
                   "Fresh grad; strong portfolio projects in C#; understands Git workflows."),
        _   => new(id, "Candidate "+id, "user@example.com", "000-0000000", "Resume.pdf",
                   "Generalist profile.")
    };

    public static List<MessageThread> Threads => new()
    {
        new(801, "Amira Lee",  "Junior .NET Developer", "Today 11:20", true),
        new(802, "Daniel Ong", "Junior .NET Developer", "Today 10:45", false),
        new(803, "Kumar Ravi", "QA Tester", "Yesterday 17:10", false)
    };

    public static List<MessageEntry> Thread(int id) => id switch
    {
        801 => new()
        {
            new("Recruiter", "Thanks for applying! Are you free Wed 2pm for a quick call?", "Today 10:55"),
            new("Candidate", "Yes, Wed 2pm works for me.", "Today 11:20"),
        },
        802 => new()
        {
            new("Recruiter", "Could you share a repo link to your C# project?", "Today 10:30"),
            new("System",    "[Flag] Informal language detected", "Today 10:31", true),
            new("Candidate", "Sure, here’s my GitHub: github.com/daniel-ong", "Today 10:45"),
        },
        _ => new()
        {
            new("Recruiter", "Thanks for applying!", "Yesterday 16:50"),
            new("Candidate", "Looking forward to the next steps.", "Yesterday 17:10"),
        }
    };

    public static List<TemplateItem> Templates => new()
    {
        new(1, "Interview Invite", "Interview for your application",
            "We’d like to invite you to a 30-minute call this week…"),
        new(2, "Rejection", "Your application status",
            "Thank you for applying. After careful consideration…"),
        new(3, "Next Steps", "Next steps for your application",
            "Please complete the short coding task attached…")
    };
}
