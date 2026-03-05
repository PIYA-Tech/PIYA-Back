using PIYA_API.Model;

namespace PIYA_API.Service.Interface;

/// <summary>
/// GDPR compliance service for handling patient data rights and privacy regulations
/// </summary>
public interface IGdprComplianceService
{
    /// <summary>
    /// Export all user data in machine-readable format (GDPR Right to Data Portability)
    /// </summary>
    Task<GdprDataExport> ExportUserDataAsync(Guid userId);
    
    /// <summary>
    /// Anonymize user data while preserving statistical integrity (GDPR Right to be Forgotten)
    /// </summary>
    Task<GdprAnonymizationResult> AnonymizeUserDataAsync(Guid userId, string reason);
    
    /// <summary>
    /// Hard delete user and all associated data (GDPR Right to Erasure)
    /// Only use when legal retention periods have expired
    /// </summary>
    Task<GdprDeletionResult> DeleteUserDataAsync(Guid userId, string reason, bool force = false);
    
    /// <summary>
    /// Get user consent records for data processing
    /// </summary>
    Task<List<UserConsent>> GetUserConsentsAsync(Guid userId);
    
    /// <summary>
    /// Record user consent for specific data processing activity
    /// </summary>
    Task<UserConsent> RecordConsentAsync(Guid userId, string purpose, bool granted, string? ipAddress = null);
    
    /// <summary>
    /// Revoke user consent for data processing
    /// </summary>
    Task<bool> RevokeConsentAsync(Guid userId, string purpose);
    
    /// <summary>
    /// Check if user has granted consent for specific purpose
    /// </summary>
    Task<bool> HasConsentAsync(Guid userId, string purpose);
    
    /// <summary>
    /// Get data retention status for user
    /// </summary>
    Task<DataRetentionStatus> GetRetentionStatusAsync(Guid userId);
    
    /// <summary>
    /// Process data retention policies (cleanup old data)
    /// </summary>
    Task<DataRetentionResult> ProcessRetentionPoliciesAsync();
    
    /// <summary>
    /// Generate GDPR compliance report
    /// </summary>
    Task<GdprComplianceReport> GenerateComplianceReportAsync(DateTime startDate, DateTime endDate);
}

#region DTOs

public class GdprDataExport
{
    public Guid UserId { get; set; }
    public DateTime ExportDate { get; set; }
    public required string Format { get; set; } // "JSON", "XML", "CSV"
    public required PersonalData PersonalData { get; set; }
    public List<AppointmentData> Appointments { get; set; } = [];
    public List<PrescriptionData> Prescriptions { get; set; } = [];
    public List<AuditLogEntry> ActivityLog { get; set; } = [];
    public List<ConsentRecord> Consents { get; set; } = [];
}

public class PersonalData
{
    public string? Email { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? PhoneNumber { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public DateTime RegisteredAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
}

public class AppointmentData
{
    public Guid Id { get; set; }
    public DateTime AppointmentDate { get; set; }
    public required string DoctorName { get; set; }
    public required string HospitalName { get; set; }
    public required string Status { get; set; }
}

public class PrescriptionData
{
    public Guid Id { get; set; }
    public DateTime IssuedDate { get; set; }
    public required string DoctorName { get; set; }
    public List<string> Medications { get; set; } = [];
    public required string Status { get; set; }
}

public class AuditLogEntry
{
    public DateTime Timestamp { get; set; }
    public required string Action { get; set; }
    public required string EntityType { get; set; }
    public string? Details { get; set; }
}

public class ConsentRecord
{
    public required string Purpose { get; set; }
    public bool Granted { get; set; }
    public DateTime GrantedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
}

public class GdprAnonymizationResult
{
    public bool Success { get; set; }
    public Guid UserId { get; set; }
    public DateTime AnonymizedAt { get; set; }
    public required string Reason { get; set; }
    public int RecordsAnonymized { get; set; }
    public List<string> EntitiesAffected { get; set; } = [];
}

public class GdprDeletionResult
{
    public bool Success { get; set; }
    public Guid UserId { get; set; }
    public DateTime DeletedAt { get; set; }
    public required string Reason { get; set; }
    public int RecordsDeleted { get; set; }
    public List<string> EntitiesDeleted { get; set; } = [];
    public string? ErrorMessage { get; set; }
}

public class UserConsent
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public required string Purpose { get; set; }
    public bool Granted { get; set; }
    public DateTime GrantedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
}

public class DataRetentionStatus
{
    public Guid UserId { get; set; }
    public DateTime LastActivityDate { get; set; }
    public int DaysSinceLastActivity { get; set; }
    public int RetentionPeriodDays { get; set; }
    public bool CanBeDeleted { get; set; }
    public DateTime? EligibleForDeletionDate { get; set; }
    public List<string> ActiveDataCategories { get; set; } = [];
}

public class DataRetentionResult
{
    public DateTime ProcessedAt { get; set; }
    public int UsersProcessed { get; set; }
    public int UsersAnonymized { get; set; }
    public int UsersDeleted { get; set; }
    public int RecordsArchived { get; set; }
    public List<string> Errors { get; set; } = [];
}

public class GdprComplianceReport
{
    public DateTime ReportDate { get; set; }
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public int TotalDataExportRequests { get; set; }
    public int TotalDeletionRequests { get; set; }
    public int TotalAnonymizationRequests { get; set; }
    public int ActiveConsents { get; set; }
    public int RevokedConsents { get; set; }
    public double AverageResponseTimeHours { get; set; }
    public List<ComplianceMetric> Metrics { get; set; } = [];
}

public class ComplianceMetric
{
    public required string MetricName { get; set; }
    public int Value { get; set; }
    public required string Unit { get; set; }
}

#endregion
