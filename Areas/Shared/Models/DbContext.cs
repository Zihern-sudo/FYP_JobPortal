using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

namespace JobPortal.Areas.Shared.Models;

public partial class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<admin_log> admin_logs { get; set; }

    public virtual DbSet<ai_resume_analysis> ai_resume_analyses { get; set; }

    public virtual DbSet<ai_resume_evaluation> ai_resume_evaluations { get; set; }

    public virtual DbSet<company> companies { get; set; }

    public virtual DbSet<conversation> conversations { get; set; }

    public virtual DbSet<conversation_monitor> conversation_monitors { get; set; }

    public virtual DbSet<job_application> job_applications { get; set; }

    public virtual DbSet<job_listing> job_listings { get; set; }

    public virtual DbSet<job_offer> job_offers { get; set; }

    public virtual DbSet<job_post_approval> job_post_approvals { get; set; }

    public virtual DbSet<job_seeker_note> job_seeker_notes { get; set; }

    public virtual DbSet<message> messages { get; set; }

    public virtual DbSet<notification> notifications { get; set; }

    public virtual DbSet<notification_preference> notification_preferences { get; set; }

    public virtual DbSet<resume> resumes { get; set; }

    public virtual DbSet<template> templates { get; set; }

    public virtual DbSet<user> users { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder
            .UseCollation("utf8mb4_unicode_ci")
            .HasCharSet("utf8mb4");

        modelBuilder.Entity<admin_log>(entity =>
        {
            entity.HasKey(e => e.log_id).HasName("PRIMARY");

            entity.Property(e => e.timestamp).HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasOne(d => d.user).WithMany(p => p.admin_logs).HasConstraintName("fk_adminlog_user");
        });

        modelBuilder.Entity<ai_resume_analysis>(entity =>
        {
            entity.HasKey(e => e.analysis_id).HasName("PRIMARY");

            entity.HasOne(d => d.resume).WithMany(p => p.ai_resume_analyses).HasConstraintName("fk_analysis_resume");
        });

        modelBuilder.Entity<ai_resume_evaluation>(entity =>
        {
            entity.HasKey(e => e.evaluation_id).HasName("PRIMARY");

            entity.HasOne(d => d.job_listing).WithMany(p => p.ai_resume_evaluations).HasConstraintName("fk_eval_job");

            entity.HasOne(d => d.resume).WithMany(p => p.ai_resume_evaluations).HasConstraintName("fk_eval_resume");
        });

        modelBuilder.Entity<company>(entity =>
        {
            entity.HasKey(e => e.company_id).HasName("PRIMARY");

            entity.Property(e => e.company_status).HasDefaultValueSql("'Active'");

            entity.HasOne(d => d.user).WithMany(p => p.companies).HasConstraintName("fk_company_user");
        });

        modelBuilder.Entity<conversation>(entity =>
        {
            entity.HasKey(e => e.conversation_id).HasName("PRIMARY");

            entity.Property(e => e.created_at).HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasOne(d => d.job_listing).WithMany(p => p.conversations).HasConstraintName("fk_conversation_job");
        });

        modelBuilder.Entity<conversation_monitor>(entity =>
        {
            entity.HasKey(e => e.monitor_id).HasName("PRIMARY");

            entity.HasOne(d => d.conversation).WithMany(p => p.conversation_monitors).HasConstraintName("fk_monitor_conv");

            entity.HasOne(d => d.user).WithMany(p => p.conversation_monitors).HasConstraintName("fk_monitor_user");
        });

        modelBuilder.Entity<job_application>(entity =>
        {
            entity.HasKey(e => e.application_id).HasName("PRIMARY");

            entity.Property(e => e.application_status).HasDefaultValueSql("'Submitted'");
            entity.Property(e => e.date_updated)
                .ValueGeneratedOnAddOrUpdate()
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasOne(d => d.job_listing).WithMany(p => p.job_applications).HasConstraintName("fk_application_job");

            entity.HasOne(d => d.user).WithMany(p => p.job_applications).HasConstraintName("fk_application_user");
        });

        modelBuilder.Entity<job_listing>(entity =>
        {
            entity.HasKey(e => e.job_listing_id).HasName("PRIMARY");

            entity.Property(e => e.date_posted).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.job_status).HasDefaultValueSql("'Draft'");

            entity.HasOne(d => d.company).WithMany(p => p.job_listings)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_joblisting_company");

            entity.HasOne(d => d.user).WithMany(p => p.job_listings)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_joblisting_user");
        });

        modelBuilder.Entity<job_offer>(entity =>
        {
            entity.HasKey(e => e.offer_id).HasName("PRIMARY");

            entity.Property(e => e.date_updated)
                .ValueGeneratedOnAddOrUpdate()
                .HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.offer_status).HasDefaultValueSql("'Draft'");

            entity.HasOne(d => d.application).WithMany(p => p.job_offers).HasConstraintName("fk_offer_app");
        });

        modelBuilder.Entity<job_post_approval>(entity =>
        {
            entity.HasKey(e => e.approval_id).HasName("PRIMARY");

            entity.Property(e => e.approval_status).HasDefaultValueSql("'Pending'");

            entity.HasOne(d => d.job_listing).WithMany(p => p.job_post_approvals).HasConstraintName("fk_approval_job");

            entity.HasOne(d => d.user).WithMany(p => p.job_post_approvals)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_approval_admin");
        });

        modelBuilder.Entity<job_seeker_note>(entity =>
        {
            entity.HasKey(e => e.note_id).HasName("PRIMARY");

            entity.Property(e => e.created_at).HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasOne(d => d.application).WithMany(p => p.job_seeker_notes).HasConstraintName("fk_note_app");

            entity.HasOne(d => d.job_recruiter).WithMany(p => p.job_seeker_notejob_recruiters).HasConstraintName("fk_note_recruiter");

            entity.HasOne(d => d.job_seeker).WithMany(p => p.job_seeker_notejob_seekers).HasConstraintName("fk_note_seeker");
        });

        modelBuilder.Entity<message>(entity =>
        {
            entity.HasKey(e => e.message_id).HasName("PRIMARY");

            entity.Property(e => e.msg_timestamp).HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasOne(d => d.conversation).WithMany(p => p.messages).HasConstraintName("fk_message_conv");

            entity.HasOne(d => d.receiver).WithMany(p => p.messagereceivers).HasConstraintName("fk_message_receiver");

            entity.HasOne(d => d.sender).WithMany(p => p.messagesenders).HasConstraintName("fk_message_sender");
        });

        modelBuilder.Entity<notification>(entity =>
        {
            entity.HasKey(e => e.notification_id).HasName("PRIMARY");

            entity.Property(e => e.notification_date_created).HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasOne(d => d.user).WithMany(p => p.notifications).HasConstraintName("fk_notification_user");
        });

        modelBuilder.Entity<notification_preference>(entity =>
        {
            entity.HasKey(e => e.preference_id).HasName("PRIMARY");

            entity.Property(e => e.allow_email).HasDefaultValueSql("'1'");
            entity.Property(e => e.allow_inApp).HasDefaultValueSql("'1'");

            entity.HasOne(d => d.user).WithMany(p => p.notification_preferences).HasConstraintName("fk_pref_user");
        });

        modelBuilder.Entity<resume>(entity =>
        {
            entity.HasKey(e => e.resume_id).HasName("PRIMARY");

            entity.Property(e => e.upload_date).HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasOne(d => d.user).WithMany(p => p.resumes).HasConstraintName("fk_resume_user");
        });

        modelBuilder.Entity<template>(entity =>
        {
            entity.HasKey(e => e.template_id).HasName("PRIMARY");

            entity.Property(e => e.date_created).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.date_updated)
                .ValueGeneratedOnAddOrUpdate()
                .HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.template_status).HasDefaultValueSql("'Active'");

            entity.HasOne(d => d.user).WithMany(p => p.templates)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("fk_template_user");
        });

        modelBuilder.Entity<user>(entity =>
        {
            entity.HasKey(e => e.user_id).HasName("PRIMARY");

            entity.Property(e => e.created_at).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.user_status).HasDefaultValueSql("'Active'");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
