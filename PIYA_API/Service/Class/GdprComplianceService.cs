using Microsoft.EntityFrameworkCore;
using PIYA_API.Data;
using PIYA_API.Model;
using PIYA_API.Service.Interface;
using System.Security.Cryptography;
using System.Text.Json;

namespace PIYA_API.Service.Class;

/// <summary>
/// GDPR compliance service implementation
/// </summary>
public class GdprComplianceService(
    PharmacyApiDbContext context,
    IAuditService auditService,
    ILogger<GdprComplianceService> logger) : IGdprComplianceService
{
    private readonly PharmacyApiDbContext _context = context;
    private readonly IAuditService _auditService = auditService;
    private readonly ILogger<GdprComplianceService> _logger = logger;

    public async Task<GdprDataExport> ExportUserDataAsync(Guid userId)
    {
        try
        {
            _logger.LogInformation("Exporting GDPR data for user {UserId}", userId);

            var user = await _context.Users.FindAsync(userId) ?? throw new InvalidOperationException($"User {userId} not found");

            // Personal data
            var personalData = new PersonalData
            {
                Email = user.Email,
                FirstName = user.FirstName,
                LastName = user.LastName,
                PhoneNumber = user.PhoneNumber,
                DateOfBirth = user.DateOfBirth,
                RegisteredAt = user.CreatedAt,
                LastLoginAt = user.UpdatedAt // Using UpdatedAt as proxy for last activity
            };

            // Appointments
            var appointments = await _context.Appointments
                .Where(a => a.PatientId == userId)
                .Include(a => a.Doctor)
                .Include(a => a.Hospital)
                .Select(a => new AppointmentData
                {
                    Id = a.Id,
                    AppointmentDate = a.ScheduledAt,
                    DoctorName = $"Dr. {a.Doctor.FirstName} {a.Doctor.LastName}",
                    HospitalName = a.Hospital.Name,
                    Status = a.Status.ToString()
                })
                .ToListAsync();

            // Prescriptions
            var prescriptions = await _context.Prescriptions
                .Where(p => p.PatientId == userId)
                .Include(p => p.Doctor)
                .Include(p => p.Items)
                    .ThenInclude(pi => pi.Medication)
                .Select(p => new PrescriptionData
                {
                    Id = p.Id,
                    IssuedDate = p.IssuedAt,
                    DoctorName = $"Dr. {p.Doctor.FirstName} {p.Doctor.LastName}",
                    Medications = p.Items.Select(pi => pi.Medication.BrandName).ToList(),
                    Status = p.Status.ToString()
                })
                .ToListAsync();

            // Audit logs
            var auditLogs = await _context.AuditLogs
                .Where(a => a.UserId == userId)
                .OrderByDescending(a => a.CreatedAt)
                .Take(1000) // Limit to last 1000 entries
                .Select(a => new AuditLogEntry
                {
                    Timestamp = a.CreatedAt,
                    Action = a.Action,
                    EntityType = a.EntityType ?? "",
                    Details = a.Description
                })
                .ToListAsync();

            // Consents
            var consents = await GetUserConsentsAsync(userId);
            var consentRecords = consents.Select(c => new ConsentRecord
            {
                Purpose = c.Purpose,
                Granted = c.Granted,
                GrantedAt = c.GrantedAt,
                RevokedAt = c.RevokedAt
            }).ToList();

            var export = new GdprDataExport
            {
                UserId = userId,
                ExportDate = DateTime.UtcNow,
                Format = "JSON",
                PersonalData = personalData,
                Appointments = appointments,
                Prescriptions = prescriptions,
                ActivityLog = auditLogs,
                Consents = consentRecords
            };

            // Audit the export
            await _auditService.LogAsync(new AuditLog
            {
                Action = "GDPR_DATA_EXPORT",
                EntityType = "User",
                EntityId = userId.ToString(),
                Description = "User data exported for GDPR compliance",
                UserId = userId
            });

            _logger.LogInformation("Successfully exported data for user {UserId}", userId);
            return export;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting GDPR data for user {UserId}", userId);
            throw;
        }
    }

    public async Task<GdprAnonymizationResult> AnonymizeUserDataAsync(Guid userId, string reason)
    {
        try
        {
            _logger.LogInformation("Anonymizing data for user {UserId}, Reason: {Reason}", userId, reason);

            var user = await _context.Users.FindAsync(userId) ?? throw new InvalidOperationException($"User {userId} not found");
            var anonymizationDate = DateTime.UtcNow;
            var recordsAnonymized = 0;
            var entitiesAffected = new List<string>();

            // Anonymize user record
            var anonymousMarker = Guid.NewGuid().ToString("N");
            user.Username = $"deleted-{anonymousMarker}";
            user.Email = $"deleted-{anonymousMarker}@invalid.piya";
            user.FirstName = "Anonymized";
            user.LastName = "User";
            user.MiddleName = null;
            user.PhoneNumber = string.Empty;
            user.DateOfBirth = null;
            user.PasswordHash = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            user.SigningKey = null;
            user.IsActive = false;
            user.IsEmailVerified = false;
            user.IsPhoneVerified = false;
            user.UpdatedAt = anonymizationDate;
            recordsAnonymized++;
            entitiesAffected.Add("User");

            // Anonymize related data
            var appointments = await _context.Appointments
                .Where(a => a.PatientId == userId)
                .ToListAsync();
            foreach (var appointment in appointments)
            {
                appointment.AppointmentNotes = "[ANONYMIZED]";
                appointment.UpdatedAt = anonymizationDate;
                recordsAnonymized++;
            }
            if (appointments.Count > 0) entitiesAffected.Add($"Appointments ({appointments.Count})");

            // Doctor notes - anonymize patient-specific fields
            var doctorNotes = await _context.DoctorNotes
                .Where(dn => dn.PatientId == userId)
                .ToListAsync();
            foreach (var note in doctorNotes)
            {
                note.Summary = "[ANONYMIZED PER GDPR REQUEST]";
                note.UpdatedAt = anonymizationDate;
                recordsAnonymized++;
            }
            if (doctorNotes.Count > 0) entitiesAffected.Add($"DoctorNotes ({doctorNotes.Count})");

            // Remove every authentication and recovery credential so an anonymized
            // identity cannot be reactivated through an old session or reset token.
            _context.Tokens.RemoveRange(await _context.Tokens.Where(item => item.UserId == userId).ToListAsync());
            _context.UsedRefreshTokens.RemoveRange(
                await _context.UsedRefreshTokens.Where(item => item.UserId == userId).ToListAsync());
            _context.TwoFactorAuths.RemoveRange(
                await _context.TwoFactorAuths.Where(item => item.UserId == userId).ToListAsync());
            _context.DeviceTokens.RemoveRange(
                await _context.DeviceTokens.Where(item => item.UserId == userId).ToListAsync());
            _context.EmailVerificationTokens.RemoveRange(
                await _context.EmailVerificationTokens.Where(item => item.UserId == userId).ToListAsync());
            _context.PasswordResetTokens.RemoveRange(
                await _context.PasswordResetTokens.Where(item => item.UserId == userId).ToListAsync());

            // Save changes
            await _context.SaveChangesAsync();

            // Audit the anonymization
            await _auditService.LogAsync(new AuditLog
            {
                Action = "GDPR_ANONYMIZATION",
                EntityType = "User",
                EntityId = userId.ToString(),
                Description = $"User data anonymized. Reason: {reason}. Records affected: {recordsAnonymized}",
                UserId = userId
            });

            var result = new GdprAnonymizationResult
            {
                Success = true,
                UserId = userId,
                AnonymizedAt = anonymizationDate,
                Reason = reason,
                RecordsAnonymized = recordsAnonymized,
                EntitiesAffected = entitiesAffected
            };

            _logger.LogInformation("Successfully anonymized data for user {UserId}, {Records} records affected", 
                userId, recordsAnonymized);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error anonymizing data for user {UserId}", userId);
            throw;
        }
    }

    public async Task<GdprDeletionResult> DeleteUserDataAsync(Guid userId, string reason, bool force = false)
    {
        try
        {
            _logger.LogInformation("Deleting data for user {UserId}, Reason: {Reason}, Force: {Force}", 
                userId, reason, force);

            var user = await _context.Users.FindAsync(userId) ?? throw new InvalidOperationException($"User {userId} not found");

            // Check retention policy if not forced
            if (!force)
            {
                var retentionStatus = await GetRetentionStatusAsync(userId);
                if (!retentionStatus.CanBeDeleted)
                {
                    return new GdprDeletionResult
                    {
                        Success = false,
                        UserId = userId,
                        DeletedAt = DateTime.UtcNow,
                        Reason = reason,
                        RecordsDeleted = 0,
                        EntitiesDeleted = [],
                        ErrorMessage = $"User cannot be deleted yet. Eligible for deletion on: {retentionStatus.EligibleForDeletionDate}"
                    };
                }
            }

            var deletedAt = DateTime.UtcNow;
            var recordsDeleted = 0;
            var entitiesDeleted = new List<string>();

            // Delete related entities (cascading delete handled by EF Core configurations)
            
            // Delete appointments
            var appointments = await _context.Appointments.Where(a => a.PatientId == userId).ToListAsync();
            _context.Appointments.RemoveRange(appointments);
            recordsDeleted += appointments.Count;
            if (appointments.Count > 0) entitiesDeleted.Add($"Appointments ({appointments.Count})");

            // Delete prescriptions
            var prescriptions = await _context.Prescriptions.Where(p => p.PatientId == userId).ToListAsync();
            _context.Prescriptions.RemoveRange(prescriptions);
            recordsDeleted += prescriptions.Count;
            if (prescriptions.Count > 0) entitiesDeleted.Add($"Prescriptions ({prescriptions.Count})");

            // Delete doctor notes
            var doctorNotes = await _context.DoctorNotes.Where(dn => dn.PatientId == userId).ToListAsync();
            _context.DoctorNotes.RemoveRange(doctorNotes);
            recordsDeleted += doctorNotes.Count;
            if (doctorNotes.Count > 0) entitiesDeleted.Add($"DoctorNotes ({doctorNotes.Count})");

            var consents = await _context.UserConsents.Where(consent => consent.UserId == userId).ToListAsync();
            _context.UserConsents.RemoveRange(consents);
            recordsDeleted += consents.Count;
            if (consents.Count > 0) entitiesDeleted.Add($"UserConsents ({consents.Count})");

            // Delete user
            _context.Users.Remove(user);
            recordsDeleted++;
            entitiesDeleted.Add("User");

            // Save all deletions
            await _context.SaveChangesAsync();

            // Note: Audit log entry is created before deletion since user won't exist after
            _logger.LogInformation("Successfully deleted user {UserId} and {Records} related records", 
                userId, recordsDeleted);

            return new GdprDeletionResult
            {
                Success = true,
                UserId = userId,
                DeletedAt = deletedAt,
                Reason = reason,
                RecordsDeleted = recordsDeleted,
                EntitiesDeleted = entitiesDeleted
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting data for user {UserId}", userId);
            return new GdprDeletionResult
            {
                Success = false,
                UserId = userId,
                DeletedAt = DateTime.UtcNow,
                Reason = reason,
                RecordsDeleted = 0,
                EntitiesDeleted = [],
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<List<UserConsent>> GetUserConsentsAsync(Guid userId)
    {
        return await _context.UserConsents
            .AsNoTracking()
            .Where(consent => consent.UserId == userId)
            .OrderByDescending(consent => consent.GrantedAt)
            .ToListAsync();
    }

    public async Task<UserConsent> RecordConsentAsync(Guid userId, string purpose, bool granted, string? ipAddress = null)
    {
        purpose = purpose.Trim();
        if (string.IsNullOrWhiteSpace(purpose))
            throw new ArgumentException("Consent purpose is required", nameof(purpose));
        if (!await _context.Users.AnyAsync(user => user.Id == userId))
            throw new KeyNotFoundException("User not found");

        var now = DateTime.UtcNow;
        var current = await _context.UserConsents
            .Where(consent => consent.UserId == userId && consent.Purpose == purpose && consent.RevokedAt == null)
            .ToListAsync();
        foreach (var currentConsent in current) currentConsent.RevokedAt = now;

        var consent = new UserConsent
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Purpose = purpose,
            Granted = granted,
            GrantedAt = now,
            IpAddress = ipAddress
        };
        _context.UserConsents.Add(consent);
        await _context.SaveChangesAsync();

        await _auditService.LogAsync(new AuditLog
        {
            Action = "CONSENT_RECORDED",
            EntityType = "UserConsent",
            EntityId = userId.ToString(),
            Description = $"Consent {(granted ? "granted" : "denied")} for: {purpose}",
            UserId = userId
        });

        return consent;
    }

    public async Task<bool> RevokeConsentAsync(Guid userId, string purpose)
    {
        var consent = await _context.UserConsents
            .Where(item => item.UserId == userId && item.Purpose == purpose && item.RevokedAt == null)
            .OrderByDescending(item => item.GrantedAt)
            .FirstOrDefaultAsync();
        if (consent == null) return false;
        consent.RevokedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        await _auditService.LogAsync(new AuditLog
        {
            Action = "CONSENT_REVOKED",
            EntityType = "UserConsent",
            EntityId = userId.ToString(),
            Description = $"Consent revoked for: {purpose}",
            UserId = userId
        });
        
        return true;
    }

    public async Task<bool> HasConsentAsync(Guid userId, string purpose)
    {
        return await _context.UserConsents.AnyAsync(
            consent => consent.UserId == userId && consent.Purpose == purpose &&
                       consent.Granted && consent.RevokedAt == null);
    }

    public async Task<DataRetentionStatus> GetRetentionStatusAsync(Guid userId)
    {
        var user = await _context.Users.FindAsync(userId) ?? throw new InvalidOperationException($"User {userId} not found");
        var lastActivity = user.UpdatedAt;
        var daysSinceLastActivity = (DateTime.UtcNow - lastActivity).Days;
        var retentionPeriodDays = 365 * 7; // 7 years for medical data
        var canBeDeleted = daysSinceLastActivity > retentionPeriodDays;

        var activeDataCategories = new List<string>();
        if (await _context.Appointments.AnyAsync(a => a.PatientId == userId))
            activeDataCategories.Add("Appointments");
        if (await _context.Prescriptions.AnyAsync(p => p.PatientId == userId))
            activeDataCategories.Add("Prescriptions");
        if (await _context.DoctorNotes.AnyAsync(dn => dn.PatientId == userId))
            activeDataCategories.Add("Medical Certificates");

        return new DataRetentionStatus
        {
            UserId = userId,
            LastActivityDate = lastActivity,
            DaysSinceLastActivity = daysSinceLastActivity,
            RetentionPeriodDays = retentionPeriodDays,
            CanBeDeleted = canBeDeleted,
            EligibleForDeletionDate = canBeDeleted ? null : lastActivity.AddDays(retentionPeriodDays),
            ActiveDataCategories = activeDataCategories
        };
    }

    public async Task<DataRetentionResult> ProcessRetentionPoliciesAsync()
    {
        var result = new DataRetentionResult
        {
            ProcessedAt = DateTime.UtcNow
        };

        try
        {
            _logger.LogInformation("Processing data retention policies");

            var inactiveUsers = await _context.Users
                .Where(u => u.UpdatedAt < DateTime.UtcNow.AddYears(-7))
                .Take(100) // Process in batches
                .ToListAsync();

            result.UsersProcessed = inactiveUsers.Count;

            foreach (var user in inactiveUsers)
            {
                var retentionStatus = await GetRetentionStatusAsync(user.Id);
                
                if (retentionStatus.CanBeDeleted)
                {
                    var anonymizationResult = await AnonymizeUserDataAsync(user.Id, "Automatic retention policy");
                    if (anonymizationResult.Success)
                    {
                        result.UsersAnonymized++;
                    }
                    else
                    {
                        result.Errors.Add($"Failed to anonymize user {user.Id}");
                    }
                }
            }

            _logger.LogInformation("Data retention processing complete: {Anonymized} users anonymized", 
                result.UsersAnonymized);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing data retention policies");
            result.Errors.Add(ex.Message);
            return result;
        }
    }

    public async Task<GdprComplianceReport> GenerateComplianceReportAsync(DateTime startDate, DateTime endDate)
    {
        // Get audit logs for GDPR-related activities
        var gdprAuditLogs = await _context.AuditLogs
            .Where(a => a.CreatedAt >= startDate && a.CreatedAt <= endDate)
            .Where(a => a.Action.StartsWith("GDPR_") || a.Action.StartsWith("CONSENT_"))
            .ToListAsync();

        var report = new GdprComplianceReport
        {
            ReportDate = DateTime.UtcNow,
            PeriodStart = startDate,
            PeriodEnd = endDate,
            TotalDataExportRequests = gdprAuditLogs.Count(a => a.Action == "GDPR_DATA_EXPORT"),
            TotalDeletionRequests = gdprAuditLogs.Count(a => a.Action == "GDPR_DELETION"),
            TotalAnonymizationRequests = gdprAuditLogs.Count(a => a.Action == "GDPR_ANONYMIZATION"),
            ActiveConsents = await _context.UserConsents.CountAsync(
                consent => consent.Granted && consent.RevokedAt == null),
            RevokedConsents = await _context.UserConsents.CountAsync(
                consent => consent.RevokedAt >= startDate && consent.RevokedAt <= endDate),
            AverageResponseTimeHours = 0,
            Metrics =
            [
                new ComplianceMetric { MetricName = "Total GDPR Requests", Value = gdprAuditLogs.Count, Unit = "requests" },
                new ComplianceMetric { MetricName = "Active Users", Value = await _context.Users.CountAsync(), Unit = "users" }
            ]
        };

        return report;
    }
}
