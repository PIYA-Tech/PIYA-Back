using Microsoft.EntityFrameworkCore;
using PIYA_API.Model;

namespace PIYA_API.Data;

/// <summary>
/// Central model configuration for the native patient-health domains. Call
/// <c>modelBuilder.ConfigurePatientHealthDomain()</c> once from the application's
/// DbContext before creating the matching migration.
/// </summary>
public static class PatientHealthModelConfiguration
{
    public static ModelBuilder ConfigurePatientHealthDomain(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PatientMedication>(entity =>
        {
            entity.HasIndex(item => new { item.PatientId, item.Status });
            entity.HasIndex(item => new { item.PatientId, item.PrescriptionItemId }).IsUnique();
            entity.Property(item => item.DisplayName).HasMaxLength(200);
            entity.Property(item => item.GenericName).HasMaxLength(200);
            entity.Property(item => item.Strength).HasMaxLength(100);
            entity.Property(item => item.Form).HasMaxLength(100);
            entity.Property(item => item.Dosage).HasMaxLength(200);
            entity.Property(item => item.Instructions).HasMaxLength(1000);
            entity.Property(item => item.SupplyUnit).HasMaxLength(50);
            entity.Property(item => item.SupplyTotal).HasPrecision(12, 3);
            entity.Property(item => item.SupplyRemaining).HasPrecision(12, 3);
            entity.Property(item => item.LowSupplyThreshold).HasPrecision(12, 3);
            entity.HasOne(item => item.Patient).WithMany().HasForeignKey(item => item.PatientId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(item => item.Medication).WithMany().HasForeignKey(item => item.MedicationId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(item => item.PrescriptionItem).WithMany().HasForeignKey(item => item.PrescriptionItemId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<MedicationDoseSchedule>(entity =>
        {
            entity.HasIndex(item => new { item.RemindersEnabled, item.NextReminderAt });
            entity.Property(item => item.TimeZoneId).HasMaxLength(100);
            entity.Property(item => item.DoseAmount).HasPrecision(12, 3);
            entity.HasOne(item => item.PatientMedication).WithMany(item => item.Schedules)
                .HasForeignKey(item => item.PatientMedicationId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MedicationDoseOccurrence>(entity =>
        {
            entity.HasIndex(item => new { item.ScheduleId, item.ScheduledFor }).IsUnique();
            entity.HasIndex(item => new { item.PatientId, item.ScheduledFor });
            entity.HasIndex(item => new { item.PatientId, item.ClientEventId }).IsUnique();
            entity.Property(item => item.Note).HasMaxLength(500);
            entity.Property(item => item.SupplyDeducted).HasPrecision(12, 3);
            entity.HasOne(item => item.Schedule).WithMany(item => item.Occurrences)
                .HasForeignKey(item => item.ScheduleId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(item => item.PatientMedication).WithMany(item => item.DoseOccurrences)
                .HasForeignKey(item => item.PatientMedicationId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(item => item.Patient).WithMany().HasForeignKey(item => item.PatientId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PharmacyPickup>(entity =>
        {
            entity.HasIndex(item => item.RefillRequestId).IsUnique();
            entity.HasIndex(item => new { item.PatientId, item.CollectedAt });
            entity.Property(item => item.QuantityCollected).HasPrecision(12, 3);
            entity.HasOne(item => item.RefillRequest).WithMany().HasForeignKey(item => item.RefillRequestId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(item => item.Patient).WithMany().HasForeignKey(item => item.PatientId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(item => item.Pharmacy).WithMany().HasForeignKey(item => item.PharmacyId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(item => item.PrescriptionItem).WithMany().HasForeignKey(item => item.PrescriptionItemId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(item => item.CollectedByUser).WithMany().HasForeignKey(item => item.CollectedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PatientRefillStatusEvent>(entity =>
        {
            entity.HasIndex(item => new { item.RefillRequestId, item.OccurredAt });
            entity.Property(item => item.Note).HasMaxLength(1000);
            entity.HasOne(item => item.RefillRequest).WithMany().HasForeignKey(item => item.RefillRequestId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(item => item.ActorUser).WithMany().HasForeignKey(item => item.ActorUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<MedicalTestAnalyteResult>(entity =>
        {
            entity.HasIndex(item => new { item.MedicalTestId, item.Code }).IsUnique();
            entity.Property(item => item.Code).HasMaxLength(80);
            entity.Property(item => item.Name).HasMaxLength(200);
            entity.Property(item => item.NumericValue).HasPrecision(18, 6);
            entity.Property(item => item.ReferenceLow).HasPrecision(18, 6);
            entity.Property(item => item.ReferenceHigh).HasPrecision(18, 6);
            entity.Property(item => item.TextValue).HasMaxLength(500);
            entity.Property(item => item.Unit).HasMaxLength(80);
            entity.Property(item => item.ReferenceText).HasMaxLength(300);
            entity.HasOne(item => item.MedicalTest).WithMany().HasForeignKey(item => item.MedicalTestId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(item => item.EnteredByUser).WithMany().HasForeignKey(item => item.EnteredByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PatientInboxNotification>(entity =>
        {
            entity.HasIndex(item => new { item.UserId, item.CreatedAt });
            entity.HasIndex(item => new { item.UserId, item.ReadAt });
            entity.HasIndex(item => new { item.UserId, item.DedupeKey }).IsUnique();
            entity.Property(item => item.Title).HasMaxLength(200);
            entity.Property(item => item.Body).HasMaxLength(1000);
            entity.Property(item => item.ActionRoute).HasMaxLength(500);
            entity.Property(item => item.DataJson).HasColumnType("text");
            entity.Property(item => item.DedupeKey).HasMaxLength(300);
            entity.HasOne(item => item.User).WithMany().HasForeignKey(item => item.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        return modelBuilder;
    }
}
