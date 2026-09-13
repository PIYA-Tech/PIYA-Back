using System.ComponentModel.DataAnnotations;

namespace PIYA_API.Model;

/// <summary>An explicitly accepted care episode, separate from emergency record access.</summary>
public sealed class ClinicalCase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PatientId { get; set; }
    public Guid AttendingDoctorId { get; set; }
    public Guid HospitalId { get; set; }
    public Guid AdmissionGrantId { get; set; }
    [MaxLength(100)] public string Department { get; set; } = "";
    [MaxLength(80)] public string Bed { get; set; } = "";
    [MaxLength(1000)] public string AdmissionReason { get; set; } = "";
    [MaxLength(20)] public string Status { get; set; } = "Active";
    public bool IsDemo { get; set; }
    public DateTime AdmittedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ClosedAt { get; set; }
    [ConcurrencyCheck] public int Version { get; set; } = 1;
    public List<ClinicalCaseEvent> Events { get; set; } = [];
}

/// <summary>Append-only timeline. Demo observations are never production device observations.</summary>
public sealed class ClinicalCaseEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ClinicalCaseId { get; set; }
    public Guid AuthorId { get; set; }
    [MaxLength(40)] public string Kind { get; set; } = "Note";
    [MaxLength(4000)] public string Text { get; set; } = "";
    public DateTime RecordedAt { get; set; } = DateTime.UtcNow;
    public Guid? RelatedEventId { get; set; }
    public double? HeartRate { get; set; }
    public double? OxygenSaturation { get; set; }
    [MaxLength(100)] public string? Source { get; set; }
}
