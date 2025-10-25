using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace JobPortal.Areas.Shared.Models;

[Table("admin_log")]
[Index("user_id", Name = "fk_adminlog_user")]
public partial class admin_log
{
    [Key]
    public int log_id { get; set; }

    public int user_id { get; set; }

    [StringLength(80)]
    public string action_type { get; set; } = null!;

    [Column(TypeName = "datetime")]
    public DateTime timestamp { get; set; }

    [ForeignKey("user_id")]
    [InverseProperty("admin_logs")]
    public virtual User user { get; set; } = null!;
}

[Table("ai_resume_analysis")]
[Index("resume_id", Name = "fk_analysis_resume")]
public partial class ai_resume_analysis
{
    [Key]
    public int analysis_id { get; set; }

    public int resume_id { get; set; }

    public byte? grammar_score { get; set; }

    public byte? formatting_score { get; set; }

    public byte? completeness_score { get; set; }

    [Column(TypeName = "text")]
    public string? suggestions { get; set; }

    [ForeignKey("resume_id")]
    [InverseProperty("ai_resume_analyses")]
    public virtual Resume resume { get; set; } = null!;
}

[Table("ai_resume_evaluation")]
[Index("job_listing_id", Name = "fk_eval_job")]
[Index("resume_id", Name = "fk_eval_resume")]
public partial class ai_resume_evaluation
{
    [Key]
    public int evaluation_id { get; set; }

    public int job_listing_id { get; set; }

    public int resume_id { get; set; }

    public byte? match_score { get; set; }

    [ForeignKey("job_listing_id")]
    [InverseProperty("ai_resume_evaluations")]
    public virtual job_listing job_listing { get; set; } = null!;

    [ForeignKey("resume_id")]
    [InverseProperty("ai_resume_evaluations")]
    public virtual Resume resume { get; set; } = null!;
}

[Table("company")]
[Index("user_id", Name = "fk_company_user")]
public partial class Company
{
    [Key]
    public int company_id { get; set; }

    public int user_id { get; set; }

    [StringLength(120)]
    public string? company_industry { get; set; }

    [StringLength(120)]
    public string? company_location { get; set; }

    [Column(TypeName = "text")]
    public string? company_description { get; set; }

    [Column(TypeName = "enum('Active','Pending','Suspended')")]
    public string? company_status { get; set; }

    [InverseProperty("company")]
    public virtual ICollection<job_listing> job_listings { get; set; } = new List<job_listing>();

    [ForeignKey("user_id")]
    [InverseProperty("companies")]
    public virtual User user { get; set; } = null!;
}

[Table("conversation_monitor")]
[Index("conversation_id", Name = "fk_monitor_conv")]
[Index("user_id", Name = "fk_monitor_user")]
public partial class ConversationMonitor
{
    [Key]
    public int monitor_id { get; set; }

    public int conversation_id { get; set; }

    public int user_id { get; set; }

    public bool flag { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? date_reviewed { get; set; }

    [ForeignKey("conversation_id")]
    [InverseProperty("conversation_monitors")]
    public virtual Conversation conversation { get; set; } = null!;

    [ForeignKey("user_id")]
    [InverseProperty("conversation_monitors")]
    public virtual User user { get; set; } = null!;
}

[Table("conversation")]
[Index("job_listing_id", Name = "fk_conversation_job")]
public partial class Conversation
{
    [Key]
    public int conversation_id { get; set; }

    public int job_listing_id { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime created_at { get; set; }

    [InverseProperty("conversation")]
    public virtual ICollection<ConversationMonitor> conversation_monitors { get; set; } = new List<ConversationMonitor>();

    [ForeignKey("job_listing_id")]
    [InverseProperty("conversations")]
    public virtual job_listing job_listing { get; set; } = null!;

    [InverseProperty("conversation")]
    public virtual ICollection<Message> messages { get; set; } = new List<Message>();
}

[Table("job_application")]
[Index("job_listing_id", Name = "ix_application_job")]
[Index("user_id", Name = "ix_application_user")]
public partial class job_application
{
    [Key]
    public int application_id { get; set; }

    public int user_id { get; set; }

    public int job_listing_id { get; set; }

    [Column(TypeName = "enum('Submitted','AI-Screened','Shortlisted','Interview','Offer','Hired','Rejected')")]
    public string application_status { get; set; } = null!;

    [Column(TypeName = "datetime")]
    public DateTime date_updated { get; set; }

    [ForeignKey("job_listing_id")]
    [InverseProperty("job_applications")]
    public virtual job_listing job_listing { get; set; } = null!;

    [InverseProperty("application")]
    public virtual ICollection<job_seeker_note> job_seeker_notes { get; set; } = new List<job_seeker_note>();

    [ForeignKey("user_id")]
    [InverseProperty("job_applications")]
    public virtual User user { get; set; } = null!;
}

[Table("job_listing")]
[Index("company_id", Name = "ix_joblisting_company")]
[Index("user_id", Name = "ix_joblisting_user")]
public partial class job_listing
{
    [Key]
    public int job_listing_id { get; set; }

    public int user_id { get; set; }

    public int company_id { get; set; }

    [StringLength(160)]
    public string job_title { get; set; } = null!;

    [Column(TypeName = "text")]
    public string? job_description { get; set; }

    [Column(TypeName = "text")]
    public string? job_requirements { get; set; }

    [Precision(10, 2)]
    public decimal? salary_min { get; set; }

    [Precision(10, 2)]
    public decimal? salary_max { get; set; }

    [Column(TypeName = "enum('Draft','Open','Paused','Closed')")]
    public string job_status { get; set; } = null!;

    [Column(TypeName = "datetime")]
    public DateTime date_posted { get; set; }

    [InverseProperty("job_listing")]
    public virtual ICollection<ai_resume_evaluation> ai_resume_evaluations { get; set; } = new List<ai_resume_evaluation>();

    [ForeignKey("company_id")]
    [InverseProperty("job_listings")]
    [ValidateNever]
    public virtual Company? company { get; set; }

    [InverseProperty("job_listing")]
    public virtual ICollection<Conversation> conversations { get; set; } = new List<Conversation>();

    [InverseProperty("job_listing")]
    public virtual ICollection<job_application> job_applications { get; set; } = new List<job_application>();

    [InverseProperty("job_listing")]
    public virtual ICollection<job_post_approval> job_post_approvals { get; set; } = new List<job_post_approval>();

    [ForeignKey("user_id")]
    [InverseProperty("job_listings")]
    [ValidateNever]
    public virtual User? user { get; set; }
}

[Table("job_post_approval")]
[Index("user_id", Name = "fk_approval_admin")]
[Index("job_listing_id", Name = "fk_approval_job")]
public partial class job_post_approval
{
    [Key]
    public int approval_id { get; set; }

    public int user_id { get; set; }

    public int job_listing_id { get; set; }

    [Column(TypeName = "enum('Pending','Approved','ChangesRequested','Rejected')")]
    public string approval_status { get; set; } = null!;

    [Column(TypeName = "text")]
    public string? comments { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? date_approved { get; set; }

    [ForeignKey("job_listing_id")]
    [InverseProperty("job_post_approvals")]
    public virtual job_listing job_listing { get; set; } = null!;

    [ForeignKey("user_id")]
    [InverseProperty("job_post_approvals")]
    public virtual User user { get; set; } = null!;
}

[Table("job_seeker_note")]
[Index("application_id", Name = "fk_note_app")]
[Index("job_recruiter_id", Name = "fk_note_recruiter")]
[Index("job_seeker_id", Name = "fk_note_seeker")]
public partial class job_seeker_note
{
    [Key]
    public int note_id { get; set; }

    public int job_seeker_id { get; set; }

    public int job_recruiter_id { get; set; }

    public int application_id { get; set; }

    [Column(TypeName = "text")]
    public string note_text { get; set; } = null!;

    [Column(TypeName = "datetime")]
    public DateTime created_at { get; set; }

    [ForeignKey("application_id")]
    [InverseProperty("job_seeker_notes")]
    public virtual job_application application { get; set; } = null!;

    [ForeignKey("job_recruiter_id")]
    [InverseProperty("job_seeker_notejob_recruiters")]
    public virtual User job_recruiter { get; set; } = null!;

    [ForeignKey("job_seeker_id")]
    [InverseProperty("job_seeker_notejob_seekers")]
    public virtual User job_seeker { get; set; } = null!;
}

[Table("message")]
[Index("sender_id", Name = "fk_message_sender")]
[Index("conversation_id", Name = "ix_message_conv")]
[Index("receiver_id", Name = "ix_message_receiver")]
public partial class Message
{
    [Key]
    public int message_id { get; set; }

    public int conversation_id { get; set; }

    public int sender_id { get; set; }

    public int receiver_id { get; set; }

    [Column(TypeName = "text")]
    public string msg_content { get; set; } = null!;

    [Column(TypeName = "datetime")]
    public DateTime msg_timestamp { get; set; }

    public bool is_read { get; set; }

    [ForeignKey("conversation_id")]
    [InverseProperty("messages")]
    public virtual Conversation conversation { get; set; } = null!;

    [ForeignKey("receiver_id")]
    [InverseProperty("messagereceivers")]
    public virtual User receiver { get; set; } = null!;

    [ForeignKey("sender_id")]
    [InverseProperty("messagesenders")]
    public virtual User sender { get; set; } = null!;
}

[Table("notification_preference")]
[Index("user_id", Name = "fk_pref_user")]
public partial class NotificationPreference
{
    [Key]
    public int preference_id { get; set; }

    public int user_id { get; set; }

    [Required]
    public bool? allow_email { get; set; }

    [Required]
    public bool? allow_inApp { get; set; }

    public bool allow_SMS { get; set; }

    [ForeignKey("user_id")]
    [InverseProperty("notification_preferences")]
    public virtual User user { get; set; } = null!;
}

[Table("notification")]
[Index("user_id", Name = "ix_notification_user")]
public partial class Notification
{
    [Key]
    public int notification_id { get; set; }

    public int user_id { get; set; }

    [StringLength(160)]
    public string notification_title { get; set; } = null!;

    [Column(TypeName = "text")]
    public string notification_msg { get; set; } = null!;

    [StringLength(50)]
    public string? notification_type { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime notification_date_created { get; set; }

    public bool notification_read_status { get; set; }

    [ForeignKey("user_id")]
    [InverseProperty("notifications")]
    public virtual User user { get; set; } = null!;
}

[Table("resume")]
[Index("user_id", Name = "ix_resume_user")]
public partial class Resume
{
    [Key]
    public int resume_id { get; set; }

    public int user_id { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime upload_date { get; set; }

    [StringLength(255)]
    public string file_path { get; set; } = null!;

    [InverseProperty("resume")]
    public virtual ICollection<ai_resume_analysis> ai_resume_analyses { get; set; } = new List<ai_resume_analysis>();

    [InverseProperty("resume")]
    public virtual ICollection<ai_resume_evaluation> ai_resume_evaluations { get; set; } = new List<ai_resume_evaluation>();

    [ForeignKey("user_id")]
    [InverseProperty("resumes")]
    public virtual User user { get; set; } = null!;
}

[Table("user")]
[Index("email", Name = "email", IsUnique = true)]
public partial class User
{
    [Key]
    public int user_id { get; set; }

    [StringLength(60)]
    public string first_name { get; set; } = null!;

    [StringLength(60)]
    public string last_name { get; set; } = null!;

    [StringLength(190)]
    public string email { get; set; } = null!;

    [StringLength(255)]
    public string password_hash { get; set; } = null!;

    [Column(TypeName = "enum('Admin','Recruiter','JobSeeker')")]
    public string user_role { get; set; } = null!;

    public bool user_2FA { get; set; }

    [Column(TypeName = "enum('Active','Suspended')")]
    public string user_status { get; set; } = null!;

    [Column(TypeName = "datetime")]
    public DateTime created_at { get; set; }

    [InverseProperty("user")]
    public virtual ICollection<admin_log> admin_logs { get; set; } = new List<admin_log>();

    [InverseProperty("user")]
    public virtual ICollection<Company> companies { get; set; } = new List<Company>();

    [InverseProperty("user")]
    public virtual ICollection<ConversationMonitor> conversation_monitors { get; set; } = new List<ConversationMonitor>();

    [InverseProperty("user")]
    public virtual ICollection<job_application> job_applications { get; set; } = new List<job_application>();

    [InverseProperty("user")]
    public virtual ICollection<job_listing> job_listings { get; set; } = new List<job_listing>();

    [InverseProperty("user")]
    public virtual ICollection<job_post_approval> job_post_approvals { get; set; } = new List<job_post_approval>();

    [InverseProperty("job_recruiter")]
    public virtual ICollection<job_seeker_note> job_seeker_notejob_recruiters { get; set; } = new List<job_seeker_note>();

    [InverseProperty("job_seeker")]
    public virtual ICollection<job_seeker_note> job_seeker_notejob_seekers { get; set; } = new List<job_seeker_note>();

    [InverseProperty("receiver")]
    public virtual ICollection<Message> messagereceivers { get; set; } = new List<Message>();

    [InverseProperty("sender")]
    public virtual ICollection<Message> messagesenders { get; set; } = new List<Message>();

    [InverseProperty("user")]
    public virtual ICollection<NotificationPreference> notification_preferences { get; set; } = new List<NotificationPreference>();

    [InverseProperty("user")]
    public virtual ICollection<Notification> notifications { get; set; } = new List<Notification>();

    [InverseProperty("user")]
    public virtual ICollection<Resume> resumes { get; set; } = new List<Resume>();
}
