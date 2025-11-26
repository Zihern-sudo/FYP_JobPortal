using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace JobPortal.Areas.JobSeeker.Models
{
    // Represents each conversation thread in the job seeker's inbox
    public record ThreadItemVM(
        int Id,
        string JobTitle,
        string RecruiterName,
        string LastMessageSnippet,
        string LastMessageAt,
        int UnreadCount
    );

    // Represents a single message inside a thread
    public record MessageItemVM(
        int Id,
        string SenderName,
        string MessageText,
        string SentAt,
        bool FromRecruiter
    );

    // The model used for listing all threads (Inbox Page)
    public class InboxIndexVM
    {
        public IList<ThreadItemVM> Threads { get; set; } = new List<ThreadItemVM>();
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalCount { get; set; }
        public int TotalPages { get; set; }
        public string Query { get; set; } = "";
        public string Filter { get; set; } = "";
    }

    // The model for viewing messages inside a specific thread
    public class InboxThreadVM
    {
        public int ThreadId { get; set; }
        public string JobTitle { get; set; } = "";
        public string RecruiterName { get; set; } = "";
        public IList<MessageItemVM> Messages { get; set; } = new List<MessageItemVM>();

        [Display(Name = "Type your reply")]
        public string? ReplyText { get; set; }
    }

    // Model for posting a reply
    public class MessagePostVM
    {
        [Required]
        public int ThreadId { get; set; }

        [Required, StringLength(1000, ErrorMessage = "Message cannot exceed 1000 characters.")]
        public string MessageText { get; set; } = "";
    }
}

// === AI moderation DTOs (jobseeker side) ===
public record MessageModerationCheckRequestVM(int ThreadId, string Text);

public record MessageModerationCheckResultVM
{
    public bool Allowed { get; init; }
    public string Reason { get; init; } = "";
    public string Category { get; init; } = "";
}

