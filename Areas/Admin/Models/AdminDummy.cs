public record ApprovalItem(int Id, string JobTitle, string Organisation, string Risk, string SubmittedAt);
public record AiTemplate(int Id, string Name, string RoleExample, string Updated);
public record ConversationListItem(int Id, string Org, string Job, string LastMessageAt, bool Flagged);
public record ConversationMessage(string From, string Text, string At, bool IsFlagged = false);
public record AuditEvent(string When, string Actor, string Action, string Target, string Notes);
public record SimpleStat(string Label, string Value);
public record AttentionItem(string Type, string Title, string LinkText);

public static class AdminDummy
{
    public static List<ApprovalItem> Approvals => new()
    {
        new(101, "Junior .NET Developer", "Sunrise Tech", "Policy Check", "Today 10:12"),
        new(102, "Marketing Executive", "Lotus Media", "None", "Yesterday 16:40")
    };

    public static List<AttentionItem> NeedsAttention => new()
    {
        new("Message", "Flagged phrase in thread #342", "Open"),
        new("Parsing", "Low-confidence CV for Candidate #553", "Review")
    };

    public static List<AiTemplate> Templates => new()
    {
        new(1, "Junior .NET Developer", "Entry-level backend with C# and EF Core", "2 days ago"),
        new(2, "Digital Marketing Exec", "SEO + Meta Ads basic experience", "5 days ago"),
        new(3, "Data Analyst (Junior)", "SQL + basic Python, dashboards", "1 week ago"),
    };

    public static List<ConversationListItem> Threads => new()
    {
        new(342, "Sunrise Tech", "Junior .NET Developer", "Today 11:05", true),
        new(343, "Lotus Media", "Marketing Executive", "Today 09:48", false),
        new(344, "Kinta Soft", "QA Tester", "Yesterday 17:26", false),
    };

    public static List<ConversationMessage> ThreadMessages(int id) => id switch
    {
        342 => new()
        {
            new("Recruiter", "Thanks for your application, can you do Tue 2pm?", "Today 10:58"),
            new("Candidate", "Yes I can. Also, I used ASP.NET Core in uni.", "Today 11:02"),
            new("System", "[Flag] ‘informal phrase’ detected", "Today 11:03", true),
        },
        343 => new()
        {
            new("Recruiter", "Role details attached. Please confirm interest.", "Today 09:35"),
            new("Candidate", "Interested, I have 1 year in SEO.", "Today 09:48"),
        },
        _ => new()
        {
            new("Recruiter", "Thanks for applying!", "Yesterday 17:00"),
            new("Candidate", "Looking forward to hearing back.", "Yesterday 17:26"),
        }
    };

    public static List<AuditEvent> Audit => new()
    {
        new("Today 11:10", "Lee (Admin)", "Approved job", "Job #101", "No risky terms"),
        new("Today 10:45", "Aisyah (Recruiter)", "Override ranking", "Candidate #553", "Strong portfolio"),
        new("Yesterday 16:55", "Lee (Admin)", "Updated scoring weights", "Global", "More weight on must-haves")
    };

    public static List<SimpleStat> ReportCards => new()
    {
        new("Avg time to shortlist", "2.3 days"),
        new("% manual overrides", "8%"),
        new("Parsing low confidence", "12%"),
        new("Roles filled this month", "9")
    };
}
