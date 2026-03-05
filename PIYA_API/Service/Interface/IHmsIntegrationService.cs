using PIYA_API.Model;

namespace PIYA_API.Service.Interface;

/// <summary>
/// Hospital Management System (HMS) integration for patient records, appointments, billing, and lab results
/// </summary>
public interface IHmsIntegrationService
{
    /// <summary>
    /// Sync patient demographics to HMS
    /// </summary>
    Task<HmsSyncResult> SyncPatientToHmsAsync(Guid userId);

    /// <summary>
    /// Sync appointment to HMS
    /// </summary>
    Task<HmsSyncResult> SyncAppointmentToHmsAsync(Guid appointmentId);

    /// <summary>
    /// Import patient record from HMS
    /// </summary>
    Task<HmsPatientRecord> ImportPatientRecordAsync(string hmsPatientId);

    /// <summary>
    /// Import lab results from HMS
    /// </summary>
    Task<List<HmsLabResult>> ImportLabResultsAsync(Guid userId, DateTime? fromDate = null);

    /// <summary>
    /// Sync billing information to HMS
    /// </summary>
    Task<HmsBillingResult> SyncBillingToHmsAsync(Guid appointmentId, decimal amount, string description);

    /// <summary>
    /// Get patient vitals from HMS
    /// </summary>
    Task<HmsVitalsRecord> GetPatientVitalsAsync(string hmsPatientId);

    /// <summary>
    /// Update patient admission status in HMS
    /// </summary>
    Task<HmsSyncResult> UpdateAdmissionStatusAsync(Guid userId, HmsAdmissionStatus status, Guid? hospitalId = null);

    /// <summary>
    /// Get HMS system health status
    /// </summary>
    Task<HmsHealthStatus> GetHmsHealthStatusAsync();
}

#region DTOs

public class HmsSyncResult
{
    public bool Success { get; set; }
    public string? HmsRecordId { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime SyncedAt { get; set; }
    public Dictionary<string, string>? Metadata { get; set; }
}

public class HmsPatientRecord
{
    public string HmsPatientId { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public DateTime DateOfBirth { get; set; }
    public string Gender { get; set; } = string.Empty;
    public string? BloodType { get; set; }
    public List<string> Allergies { get; set; } = [];
    public List<string> ChronicConditions { get; set; } = [];
    public string? EmergencyContact { get; set; }
    public string? InsuranceProvider { get; set; }
    public string? InsurancePolicyNumber { get; set; }
    public DateTime LastVisit { get; set; }
}

public class HmsLabResult
{
    public string LabResultId { get; set; } = string.Empty;
    public string TestName { get; set; } = string.Empty;
    public string TestCode { get; set; } = string.Empty;
    public string Result { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public string ReferenceRange { get; set; } = string.Empty;
    public bool IsAbnormal { get; set; }
    public DateTime TestDate { get; set; }
    public string? OrderingPhysician { get; set; }
    public string Status { get; set; } = string.Empty; // Pending, Completed, Verified
}

public class HmsBillingResult
{
    public bool Success { get; set; }
    public string? InvoiceId { get; set; }
    public decimal TotalAmount { get; set; }
    public string Currency { get; set; } = "AZN";
    public DateTime BillingDate { get; set; }
    public string PaymentStatus { get; set; } = string.Empty; // Pending, Paid, Cancelled
    public string? ErrorMessage { get; set; }
}

public class HmsVitalsRecord
{
    public string RecordId { get; set; } = string.Empty;
    public DateTime RecordedAt { get; set; }
    public decimal? BloodPressureSystolic { get; set; }
    public decimal? BloodPressureDiastolic { get; set; }
    public decimal? HeartRate { get; set; }
    public decimal? Temperature { get; set; }
    public string? TemperatureUnit { get; set; } // Celsius, Fahrenheit
    public decimal? RespiratoryRate { get; set; }
    public decimal? OxygenSaturation { get; set; }
    public decimal? Weight { get; set; }
    public string? WeightUnit { get; set; } // kg, lbs
    public decimal? Height { get; set; }
    public string? HeightUnit { get; set; } // cm, inches
    public string? RecordedBy { get; set; }
}

public class HmsHealthStatus
{
    public bool IsOnline { get; set; }
    public string Version { get; set; } = string.Empty;
    public DateTime LastSyncAt { get; set; }
    public int PendingSyncCount { get; set; }
    public Dictionary<string, bool> ModuleStatus { get; set; } = [];
    public string? ErrorMessage { get; set; }
}

public enum HmsAdmissionStatus
{
    Outpatient,
    Inpatient,
    Emergency,
    Discharged,
    Transferred
}

#endregion
