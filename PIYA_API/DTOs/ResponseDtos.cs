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

/// <summary>
/// Public doctor directory record. <see cref="Id"/> and <see cref="UserId"/> are
/// retained for existing clients; the explicit aliases remove the historical
/// ambiguity between a DoctorProfile primary key and the User id used by booking,
/// referrals and prescriptions. No license identifiers or User entity are exposed.
/// </summary>
public sealed class PublicDoctorProfileResponseDto
{
    /// <summary>Legacy alias for <see cref="ProfileId"/>.</summary>
    public Guid Id { get; init; }
    /// <summary>DoctorProfile primary key; use only for profile administration.</summary>
    public Guid ProfileId { get; init; }
    /// <summary>Legacy alias for <see cref="DoctorUserId"/>.</summary>
    public Guid UserId { get; init; }
    /// <summary>User id used as DoctorId in appointments, referrals and prescriptions.</summary>
    public Guid DoctorUserId { get; init; }
    public PublicDoctorIdentityDto? User { get; init; }
    public string? DoctorName { get; init; }
    public string Specialization { get; init; } = string.Empty;
    public List<string> AdditionalSpecializations { get; init; } = [];
    public int YearsOfExperience { get; init; }
    public List<string> Certifications { get; init; } = [];
    public List<string> Education { get; init; } = [];
    public List<string> Languages { get; init; } = [];
    public string? Biography { get; init; }
    /// <summary>Compatibility alias for clients that previously used "bio".</summary>
    public string? Bio => Biography;
    public decimal? ConsultationFee { get; init; }
    public bool AcceptingNewPatients { get; init; }
    /// <summary>Compatibility alias for clients that previously used "isAcceptingPatients".</summary>
    public bool IsAcceptingPatients => AcceptingNewPatients;
    public string CurrentStatus { get; init; } = string.Empty;
    public List<Guid> HospitalIds { get; init; } = [];
    public int AverageAppointmentDuration { get; init; }
    public decimal? AverageRating { get; init; }
    public int TotalRatings { get; init; }

    public static PublicDoctorProfileResponseDto FromEntity(DoctorProfile profile)
    {
        var identity = profile.User is null
            ? null
            : new PublicDoctorIdentityDto
            {
                Id = profile.User.Id,
                FirstName = profile.User.FirstName,
                LastName = profile.User.LastName
            };

        return new PublicDoctorProfileResponseDto
        {
            Id = profile.Id,
            ProfileId = profile.Id,
            UserId = profile.UserId,
            DoctorUserId = profile.UserId,
            User = identity,
            DoctorName = identity?.FullName,
            Specialization = profile.Specialization.ToString(),
            AdditionalSpecializations = profile.AdditionalSpecializations.Select(value => value.ToString()).ToList(),
            YearsOfExperience = profile.YearsOfExperience,
            Certifications = [.. profile.Certifications],
            Education = [.. profile.Education],
            Languages = [.. profile.Languages],
            Biography = profile.Biography,
            ConsultationFee = profile.ConsultationFee,
            AcceptingNewPatients = profile.AcceptingNewPatients,
            CurrentStatus = profile.CurrentStatus.ToString(),
            HospitalIds = [.. profile.HospitalIds],
            AverageAppointmentDuration = profile.AverageAppointmentDuration,
            AverageRating = profile.AverageRating,
            TotalRatings = profile.TotalRatings
        };
    }
}

public sealed class PublicDoctorIdentityDto
{
    public Guid Id { get; init; }
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string FullName => $"{FirstName} {LastName}".Trim();
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
    [System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? ClinicalNotes { get; set; }
    public string? ResultNotes { get; set; }
    public bool IsExternal { get; set; }
    public string? ExternalProviderName { get; set; }
    public string? ExternalProviderContact { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    public static ReferralResponseDto FromEntity(Referral r, bool includeClinicalNotes = true) => new()
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
        ClinicalNotes = includeClinicalNotes ? r.ClinicalNotes : null,
        ResultNotes = r.ResultNotes,
        IsExternal = r.IsExternal,
        ExternalProviderName = r.ExternalProviderName,
        ExternalProviderContact = r.ExternalProviderContact,
        CreatedAt = r.CreatedAt,
        UpdatedAt = r.UpdatedAt,
        CompletedAt = r.CompletedAt
    };
}

// ─────────────────────────────────────────────────────────────────────────────
// Medical-test Response DTOs
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Medical test returned to authorized clients. It intentionally excludes EF
/// navigation graphs and storage internals such as document paths, object keys,
/// hashes, uploader accounts and patient account details.
/// </summary>
public sealed class MedicalTestResponseDto
{
    public Guid Id { get; init; }
    public Guid PatientId { get; init; }
    public Guid? ReferralId { get; init; }
    public bool IsEmergency { get; init; }
    public Guid? AppointmentId { get; init; }
    public Guid OrderedByDoctorId { get; init; }
    public string? OrderedByDoctorName { get; init; }
    public Guid? PerformedByDoctorId { get; init; }
    public string? PerformedByDoctorName { get; init; }
    public string TestType { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string? Notes { get; init; }
    public string? Findings { get; init; }
    public DateTime? PerformedAt { get; init; }
    public DateTime? ResultsAt { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
    public List<MedicalTestDocumentResponseDto> Documents { get; init; } = [];

    public static MedicalTestResponseDto FromEntity(MedicalTest test) => new()
    {
        Id = test.Id,
        PatientId = test.PatientId,
        ReferralId = test.ReferralId,
        IsEmergency = test.IsEmergency,
        AppointmentId = test.AppointmentId,
        OrderedByDoctorId = test.OrderedByDoctorId,
        OrderedByDoctorName = NameOf(test.OrderedByDoctor),
        PerformedByDoctorId = test.PerformedByDoctorId,
        PerformedByDoctorName = NameOf(test.PerformedByDoctor),
        TestType = test.TestType.ToString(),
        Status = test.Status.ToString(),
        Notes = test.Notes,
        Findings = test.Findings,
        PerformedAt = test.PerformedAt,
        ResultsAt = test.ResultsAt,
        CreatedAt = test.CreatedAt,
        UpdatedAt = test.UpdatedAt,
        Documents = test.Documents
            .Where(document => !document.IsArchived)
            .Select(MedicalTestDocumentResponseDto.FromEntity)
            .ToList()
    };

    private static string? NameOf(User? user) => user is null
        ? null
        : $"{user.FirstName} {user.LastName}".Trim();
}

public sealed class MedicalTestDocumentResponseDto
{
    public Guid Id { get; init; }
    public string DocumentType { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;
    public long FileSizeBytes { get; init; }
    public string? Title { get; init; }
    public DateTime UploadedAt { get; init; }
    public bool IsVerified { get; init; }

    public static MedicalTestDocumentResponseDto FromEntity(MedicalDocument document) => new()
    {
        Id = document.Id,
        DocumentType = document.DocumentType.ToString(),
        FileName = document.FileName,
        ContentType = document.ContentType,
        FileSizeBytes = document.FileSizeBytes,
        Title = document.Title,
        UploadedAt = document.UploadedAt,
        IsVerified = document.IsVerified
    };
}
