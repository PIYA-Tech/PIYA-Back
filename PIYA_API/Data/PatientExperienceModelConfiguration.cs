using Microsoft.EntityFrameworkCore;
using PIYA_API.Model;

namespace PIYA_API.Data;

/// <summary>
/// Schema configuration for verification, Care Circle, and continuity-of-care
/// domains. Kept separate so the main DbContext and migration can integrate it
/// as one deliberate schema change.
/// </summary>
public static class PatientExperienceModelConfiguration
{
    public static ModelBuilder ConfigurePatientExperienceModels(this ModelBuilder modelBuilder)
    {
        ConfigureVerification(modelBuilder);
        ConfigureCareCircle(modelBuilder);
        ConfigureContinuity(modelBuilder);
        return modelBuilder;
    }

    private static void ConfigureVerification(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<PatientVerification>();
        entity.HasKey(item => item.Id);
        entity.Property(item => item.ProviderName).HasMaxLength(120);
        entity.Property(item => item.ProviderReference).HasMaxLength(300);
        entity.Property(item => item.ActionUrl).HasMaxLength(2000);
        entity.Property(item => item.CountryCode).HasMaxLength(3);
        entity.Property(item => item.DocumentType).HasMaxLength(64);
        entity.Property(item => item.InsuranceIssuer).HasMaxLength(160);
        entity.Property(item => item.PolicyReferenceLastFour).HasMaxLength(4);
        entity.Property(item => item.StatusReasonCode).HasMaxLength(120);
        entity.HasIndex(item => new { item.PatientId, item.Kind, item.CreatedAt });
        entity.HasIndex(item => new { item.ProviderName, item.ProviderReference }).IsUnique();
        entity.HasOne<User>().WithMany().HasForeignKey(item => item.PatientId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureCareCircle(ModelBuilder modelBuilder)
    {
        var invitation = modelBuilder.Entity<CareCircleInvitation>();
        invitation.HasKey(item => item.Id);
        invitation.Property(item => item.InviteeEmailNormalized).HasMaxLength(254);
        invitation.Property(item => item.TokenHash).HasMaxLength(64);
        invitation.HasIndex(item => item.TokenHash).IsUnique();
        invitation.HasIndex(item => new { item.PatientId, item.InviteeEmailNormalized, item.Status });
        invitation.HasOne<User>().WithMany().HasForeignKey(item => item.PatientId)
            .OnDelete(DeleteBehavior.Restrict);

        var member = modelBuilder.Entity<CareCircleMember>();
        member.HasKey(item => item.Id);
        member.HasIndex(item => new { item.PatientId, item.MemberUserId })
            .IsUnique().HasFilter("\"Status\" = 1");
        member.HasIndex(item => new { item.MemberUserId, item.Status, item.ExpiresAt });
        member.HasOne<User>().WithMany().HasForeignKey(item => item.PatientId)
            .OnDelete(DeleteBehavior.Restrict);
        member.HasOne<User>().WithMany().HasForeignKey(item => item.MemberUserId)
            .OnDelete(DeleteBehavior.Restrict);
        member.HasOne<CareCircleInvitation>().WithMany().HasForeignKey(item => item.InvitationId)
            .OnDelete(DeleteBehavior.Restrict);

        var consent = modelBuilder.Entity<CareCircleConsent>();
        consent.HasKey(item => item.Id);
        consent.Property(item => item.RevocationReason).HasMaxLength(500);
        consent.HasIndex(item => item.MemberId).IsUnique().HasFilter("\"Status\" = 1");
        consent.HasIndex(item => new { item.PatientId, item.Status, item.ExpiresAt });
        consent.HasOne<CareCircleMember>().WithMany().HasForeignKey(item => item.MemberId)
            .OnDelete(DeleteBehavior.Cascade);
        consent.HasOne<User>().WithMany().HasForeignKey(item => item.PatientId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureContinuity(ModelBuilder modelBuilder)
    {
        var summary = modelBuilder.Entity<ConsultationSummary>();
        summary.HasKey(item => item.Id);
        summary.Property(item => item.Summary).HasMaxLength(8000);
        summary.Property(item => item.Diagnosis).HasMaxLength(4000);
        summary.Property(item => item.CareInstructions).HasMaxLength(8000);
        summary.Property(item => item.WarningSigns).HasMaxLength(4000);
        summary.Property(item => item.PatientMessage).HasMaxLength(4000);
        summary.Property(item => item.LastAmendmentReason).HasMaxLength(500);
        summary.HasIndex(item => item.AppointmentId).IsUnique();
        summary.HasIndex(item => new { item.PatientId, item.Status, item.PublishedAt });
        summary.HasOne<Appointment>().WithOne().HasForeignKey<ConsultationSummary>(item => item.AppointmentId)
            .OnDelete(DeleteBehavior.Restrict);
        summary.HasOne<User>().WithMany().HasForeignKey(item => item.PatientId)
            .OnDelete(DeleteBehavior.Restrict);
        summary.HasOne<User>().WithMany().HasForeignKey(item => item.DoctorId)
            .OnDelete(DeleteBehavior.Restrict);

        var followUp = modelBuilder.Entity<FollowUpPlan>();
        followUp.HasKey(item => item.Id);
        followUp.Property(item => item.Reason).HasMaxLength(1000);
        followUp.Property(item => item.DeclineReason).HasMaxLength(500);
        followUp.HasIndex(item => new { item.ConsultationSummaryId, item.Status });
        followUp.HasIndex(item => new { item.PatientId, item.Status, item.EarliestAt });
        followUp.HasOne<ConsultationSummary>().WithMany()
            .HasForeignKey(item => item.ConsultationSummaryId).OnDelete(DeleteBehavior.Restrict);
        followUp.HasOne<Appointment>().WithMany().HasForeignKey(item => item.SourceAppointmentId)
            .OnDelete(DeleteBehavior.Restrict);
        followUp.HasOne<Appointment>().WithMany().HasForeignKey(item => item.ScheduledAppointmentId)
            .OnDelete(DeleteBehavior.Restrict);
        followUp.HasOne<User>().WithMany().HasForeignKey(item => item.PatientId)
            .OnDelete(DeleteBehavior.Restrict);
        followUp.HasOne<User>().WithMany().HasForeignKey(item => item.DoctorId)
            .OnDelete(DeleteBehavior.Restrict);
        followUp.HasOne<Hospital>().WithMany().HasForeignKey(item => item.HospitalId)
            .OnDelete(DeleteBehavior.Restrict);

        var workflow = modelBuilder.Entity<CareLoopWorkflow>();
        workflow.HasKey(item => item.Id);
        workflow.Property(item => item.Title).HasMaxLength(200);
        workflow.Property(item => item.Description).HasMaxLength(2000);
        workflow.Property(item => item.Source).HasMaxLength(64);
        workflow.HasIndex(item => new { item.PatientId, item.Status, item.UpdatedAt });
        workflow.HasOne<User>().WithMany().HasForeignKey(item => item.PatientId)
            .OnDelete(DeleteBehavior.Restrict);
        workflow.HasOne<User>().WithMany().HasForeignKey(item => item.DoctorId)
            .OnDelete(DeleteBehavior.Restrict);
        workflow.HasOne<Appointment>().WithMany().HasForeignKey(item => item.AppointmentId)
            .OnDelete(DeleteBehavior.Restrict);
        workflow.HasOne<ConsultationSummary>().WithMany().HasForeignKey(item => item.ConsultationSummaryId)
            .OnDelete(DeleteBehavior.Restrict);

        var task = modelBuilder.Entity<CareLoopTask>();
        task.HasKey(item => item.Id);
        task.Property(item => item.Title).HasMaxLength(200);
        task.Property(item => item.Instructions).HasMaxLength(2000);
        task.Property(item => item.PatientResponse).HasMaxLength(2000);
        task.Property(item => item.SkipReason).HasMaxLength(500);
        task.HasIndex(item => new { item.WorkflowId, item.DueAt });
        task.HasIndex(item => new { item.Status, item.DueAt });
        task.HasOne<CareLoopWorkflow>().WithMany().HasForeignKey(item => item.WorkflowId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
