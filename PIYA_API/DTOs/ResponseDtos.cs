using PIYA_API.Model;

namespace PIYA_API.DTOs;

// ─────────────────────────────────────────────────────────────────────────────
// Prescription Response DTOs
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Prescription returned to clients — no EF navigation properties.</summary>
public class PrescriptionResponseDto
{
    public Guid Id { get; set; }
    public Guid PatientId { get; set; }
    public string? PatientName { get; set; }
    public Guid DoctorId { get; set; }
    public string? DoctorName { get; set; }
    public Guid? AppointmentId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Diagnosis { get; set; }
    public string? Instructions { get; set; }
    public DateTime IssuedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? FulfilledAt { get; set; }
    public Guid? FulfilledByPharmacyId { get; set; }
    public string? QrToken { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<PrescriptionItemResponseDto> Items { get; set; } = [];

    public static PrescriptionResponseDto FromEntity(Prescription p) => new()
    {
        Id = p.Id,
        PatientId = p.PatientId,
        PatientName = p.Patient != null ? $"{p.Patient.FirstName} {p.Patient.LastName}" : null,
        DoctorId = p.DoctorId,
        DoctorName = p.Doctor != null ? $"{p.Doctor.FirstName} {p.Doctor.LastName}" : null,
        AppointmentId = p.AppointmentId,
        Status = p.Status.ToString(),
        Diagnosis = p.Diagnosis,
        Instructions = p.Instructions,
        IssuedAt = p.IssuedAt,
        ExpiresAt = p.ExpiresAt,
        FulfilledAt = p.FulfilledAt,
        FulfilledByPharmacyId = p.FulfilledByPharmacyId,
        QrToken = p.QrToken,
        CreatedAt = p.CreatedAt,
        UpdatedAt = p.UpdatedAt,
        Items = p.Items?.Select(PrescriptionItemResponseDto.FromEntity).ToList() ?? []
    };
}

public class PrescriptionItemResponseDto
{
    public Guid Id { get; set; }
    public Guid MedicationId { get; set; }
    public string? MedicationName { get; set; }
    public string Dosage { get; set; } = string.Empty;
    public string Frequency { get; set; } = string.Empty;
    public string Duration { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public string? Instructions { get; set; }
    public bool IsFulfilled { get; set; }
    public DateTime? FulfilledAt { get; set; }

    public static PrescriptionItemResponseDto FromEntity(PrescriptionItem i) => new()
    {
        Id = i.Id,
        MedicationId = i.MedicationId,
        MedicationName = i.Medication?.BrandName,
        Dosage = i.Dosage,
        Frequency = i.Frequency,
        Duration = i.Duration,
        Quantity = i.Quantity,
        Instructions = i.Instructions,
        IsFulfilled = i.IsFulfilled,
        FulfilledAt = i.FulfilledAt
    };
}

// ─────────────────────────────────────────────────────────────────────────────
// Appointment Response DTOs
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Appointment returned to clients — no EF navigation properties.</summary>
public class AppointmentResponseDto
{
    public Guid Id { get; set; }
    public Guid PatientId { get; set; }
    public string? PatientName { get; set; }
    public Guid DoctorId { get; set; }
    public string? DoctorName { get; set; }
    public Guid HospitalId { get; set; }
    public string? HospitalName { get; set; }
    public DateTime ScheduledAt { get; set; }
    public int DurationMinutes { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public string? AppointmentNotes { get; set; }
    public DateTime? ActualStartTime { get; set; }
    public DateTime? ActualEndTime { get; set; }
    public string? CancellationReason { get; set; }
    public Guid? CancelledBy { get; set; }
    public DateTime? CancelledAt { get; set; }
    public Guid? ReferralId { get; set; }
    public Guid? RescheduledToId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public static AppointmentResponseDto FromEntity(Appointment a) => new()
    {
        Id = a.Id,
        PatientId = a.PatientId,
        PatientName = a.Patient != null ? $"{a.Patient.FirstName} {a.Patient.LastName}" : null,
        DoctorId = a.DoctorId,
        DoctorName = a.Doctor != null ? $"{a.Doctor.FirstName} {a.Doctor.LastName}" : null,
        HospitalId = a.HospitalId,
        HospitalName = a.Hospital?.Name,
        ScheduledAt = a.ScheduledAt,
        DurationMinutes = a.DurationMinutes,
        Status = a.Status.ToString(),
        Reason = a.Reason,
        AppointmentNotes = a.AppointmentNotes,
        ActualStartTime = a.ActualStartTime,
        ActualEndTime = a.ActualEndTime,
        CancellationReason = a.CancellationReason,
        CancelledBy = a.CancelledBy,
        CancelledAt = a.CancelledAt,
        ReferralId = a.ReferralId,
        RescheduledToId = a.RescheduledToId,
        CreatedAt = a.CreatedAt,
        UpdatedAt = a.UpdatedAt
    };
}

// ─────────────────────────────────────────────────────────────────────────────
// DoctorProfile Response DTO
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Doctor profile returned to clients — no User navigation property.</summary>
public class DoctorProfileResponseDto
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string? DoctorName { get; set; }
    public string LicenseNumber { get; set; } = string.Empty;
    public string? LicenseAuthority { get; set; }
    public DateTime? LicenseExpiryDate { get; set; }
    public string Specialization { get; set; } = string.Empty;
    public List<string> AdditionalSpecializations { get; set; } = [];
    public int YearsOfExperience { get; set; }
    public List<string> Certifications { get; set; } = [];
    public List<string> Education { get; set; } = [];
    public List<string> Languages { get; set; } = [];
    public string? Biography { get; set; }
    public decimal? ConsultationFee { get; set; }
    public bool AcceptingNewPatients { get; set; }
    public string CurrentStatus { get; set; } = string.Empty;
    public DateTime? LastOnlineAt { get; set; }
    public List<Guid> HospitalIds { get; set; } = [];
    public string? WorkingHours { get; set; }
    public int AverageAppointmentDuration { get; set; }
    public int TotalPatientsTreated { get; set; }
    public decimal? AverageRating { get; set; }
    public int TotalRatings { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public static DoctorProfileResponseDto FromEntity(DoctorProfile p) => new()
    {
        Id = p.Id,
        UserId = p.UserId,
        DoctorName = p.User != null ? $"{p.User.FirstName} {p.User.LastName}" : null,
        LicenseNumber = p.LicenseNumber,
        LicenseAuthority = p.LicenseAuthority,
        LicenseExpiryDate = p.LicenseExpiryDate,
        Specialization = p.Specialization.ToString(),
        AdditionalSpecializations = p.AdditionalSpecializations.Select(s => s.ToString()).ToList(),
        YearsOfExperience = p.YearsOfExperience,
        Certifications = p.Certifications,
        Education = p.Education,
        Languages = p.Languages,
        Biography = p.Biography,
        ConsultationFee = p.ConsultationFee,
        AcceptingNewPatients = p.AcceptingNewPatients,
        CurrentStatus = p.CurrentStatus.ToString(),
        LastOnlineAt = p.LastOnlineAt,
        HospitalIds = p.HospitalIds,
        WorkingHours = p.WorkingHours,
        AverageAppointmentDuration = p.AverageAppointmentDuration,
        TotalPatientsTreated = p.TotalPatientsTreated,
        AverageRating = p.AverageRating,
        TotalRatings = p.TotalRatings,
        CreatedAt = p.CreatedAt,
        UpdatedAt = p.UpdatedAt
    };
}

// ─────────────────────────────────────────────────────────────────────────────
// PharmacyInventory Response DTO
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Inventory item returned to clients — no EF navigation properties.</summary>
public class InventoryResponseDto
{
    public Guid Id { get; set; }
    public Guid PharmacyId { get; set; }
    public Guid MedicationId { get; set; }
    public string? MedicationName { get; set; }
    public int QuantityInStock { get; set; }
    public int MinimumStockLevel { get; set; }
    public int ReorderQuantity { get; set; }
    public decimal Price { get; set; }
    public string Currency { get; set; } = "AZN";
    public string? BatchNumber { get; set; }
    public DateTime? ExpirationDate { get; set; }
    public bool IsAvailable { get; set; }
    public bool LowStockAlertTriggered { get; set; }
    public DateTime LastRestockedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public static InventoryResponseDto FromEntity(PharmacyInventory i) => new()
    {
        Id = i.Id,
        PharmacyId = i.PharmacyId,
        MedicationId = i.MedicationId,
        MedicationName = i.Medication?.BrandName,
        QuantityInStock = i.QuantityInStock,
        MinimumStockLevel = i.MinimumStockLevel,
        ReorderQuantity = i.ReorderQuantity,
        Price = i.Price,
        Currency = i.Currency,
        BatchNumber = i.BatchNumber,
        ExpirationDate = i.ExpirationDate,
        IsAvailable = i.IsAvailable,
        LowStockAlertTriggered = i.LowStockAlertTriggered,
        LastRestockedAt = i.LastRestockedAt,
        CreatedAt = i.CreatedAt,
        UpdatedAt = i.UpdatedAt
    };
}

// ─────────────────────────────────────────────────────────────────────────────
// Referral Response DTO
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Referral returned to clients — no EF navigation properties.</summary>
public class ReferralResponseDto
{
    public Guid Id { get; set; }
    public Guid ReferringDoctorId { get; set; }
    public string? ReferringDoctorName { get; set; }
    public string Origin { get; set; } = string.Empty;
    public Guid? SourceAppointmentId { get; set; }
    public Guid? ParentReferralId { get; set; }
    public Guid PatientId { get; set; }
    public string? PatientName { get; set; }
    public string ReferredToSpecialty { get; set; } = string.Empty;
    public Guid? ReferredToDoctorId { get; set; }
    public string? ReferredToDoctorName { get; set; }
    public Guid? ResultAppointmentId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Urgency { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string? ClinicalNotes { get; set; }
    public string? ResultNotes { get; set; }
    public bool IsExternal { get; set; }
    public string? ExternalProviderName { get; set; }
    public string? ExternalProviderContact { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    public static ReferralResponseDto FromEntity(Referral r) => new()
    {
        Id = r.Id,
        ReferringDoctorId = r.ReferringDoctorId,
        ReferringDoctorName = r.ReferringDoctor != null ? $"{r.ReferringDoctor.FirstName} {r.ReferringDoctor.LastName}" : null,
        Origin = r.Origin.ToString(),
        SourceAppointmentId = r.SourceAppointmentId,
        ParentReferralId = r.ParentReferralId,
        PatientId = r.PatientId,
        PatientName = r.Patient != null ? $"{r.Patient.FirstName} {r.Patient.LastName}" : null,
        ReferredToSpecialty = r.ReferredToSpecialty.ToString(),
        ReferredToDoctorId = r.ReferredToDoctorId,
        ReferredToDoctorName = r.ReferredToDoctor != null ? $"{r.ReferredToDoctor.FirstName} {r.ReferredToDoctor.LastName}" : null,
        ResultAppointmentId = r.ResultAppointmentId,
        Status = r.Status.ToString(),
        Urgency = r.Urgency.ToString(),
        Reason = r.Reason,
        ClinicalNotes = r.ClinicalNotes,
        ResultNotes = r.ResultNotes,
        IsExternal = r.IsExternal,
        ExternalProviderName = r.ExternalProviderName,
        ExternalProviderContact = r.ExternalProviderContact,
        CreatedAt = r.CreatedAt,
        UpdatedAt = r.UpdatedAt,
        CompletedAt = r.CompletedAt
    };
}
