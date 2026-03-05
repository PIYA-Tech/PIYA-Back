using Microsoft.EntityFrameworkCore;
using PIYA_API.Data;
using PIYA_API.Model;
using PIYA_API.Service.Interface;

namespace PIYA_API.Service.Class;

/// <summary>
/// Pharmacist license verification service
/// </summary>
public class PharmacistLicenseService(
    PharmacyApiDbContext context,
    ILogger<PharmacistLicenseService> logger,
    IEmailService emailService,
    ISmsService smsService) : IPharmacistLicenseService
{
    private readonly PharmacyApiDbContext _context = context;
    private readonly ILogger<PharmacistLicenseService> _logger = logger;
    private readonly IEmailService _emailService = emailService;
    private readonly ISmsService _smsService = smsService;

    public async Task<LicenseVerificationResult> VerifyLicenseAsync(string licenseNumber)
    {
        if (string.IsNullOrWhiteSpace(licenseNumber))
        {
            return new LicenseVerificationResult
            {
                IsValid = false,
                Status = PharmacistLicenseStatus.Pending,
                Message = "License number is required",
                VerifiedAt = DateTime.UtcNow
            };
        }

        // Check if license exists in our database
        var profile = await _context.PharmacistProfiles
            .FirstOrDefaultAsync(p => p.LicenseNumber == licenseNumber);

        if (profile == null)
        {
            _logger.LogWarning("License number {LicenseNumber} not found in database", licenseNumber);
            return new LicenseVerificationResult
            {
                IsValid = false,
                Status = PharmacistLicenseStatus.Pending,
                Message = "License not found in database",
                VerifiedAt = DateTime.UtcNow
            };
        }

        // Check expiry
        var isExpired = profile.LicenseExpiryDate.HasValue && 
                       profile.LicenseExpiryDate.Value < DateTime.UtcNow;

        if (isExpired)
        {
            profile.LicenseStatus = PharmacistLicenseStatus.Expired;
            await _context.SaveChangesAsync();
        }

        // Update last verification date
        profile.LastVerificationDate = DateTime.UtcNow;
        
        // Set next verification date (e.g., 6 months from now)
        profile.NextVerificationDate = DateTime.UtcNow.AddMonths(6);
        
        await _context.SaveChangesAsync();

        var result = new LicenseVerificationResult
        {
            IsValid = profile.LicenseStatus == PharmacistLicenseStatus.Active,
            Status = profile.LicenseStatus,
            ExpiryDate = profile.LicenseExpiryDate,
            Message = GetStatusMessage(profile.LicenseStatus),
            VerifiedAt = DateTime.UtcNow
        };

        _logger.LogInformation("Verified license {LicenseNumber}: {Status}", 
            licenseNumber, result.Status);

        return result;
    }

    public async Task<PharmacistProfile?> GetPharmacistProfileAsync(Guid userId)
    {
        return await _context.PharmacistProfiles
            .Include(p => p.User)
            .Include(p => p.PrimaryPharmacy)
            .FirstOrDefaultAsync(p => p.UserId == userId);
    }

    public async Task<PharmacistProfile> UpsertPharmacistProfileAsync(Guid userId, PharmacistProfileDto profileDto)
    {
        var existingProfile = await _context.PharmacistProfiles
            .FirstOrDefaultAsync(p => p.UserId == userId);

        if (existingProfile != null)
        {
            // Update existing profile
            existingProfile.LicenseNumber = profileDto.LicenseNumber;
            existingProfile.LicenseAuthority = profileDto.LicenseAuthority;
            existingProfile.LicenseIssueDate = profileDto.LicenseIssueDate;
            existingProfile.LicenseExpiryDate = profileDto.LicenseExpiryDate;
            existingProfile.YearsOfExperience = profileDto.YearsOfExperience;
            existingProfile.Education = profileDto.Education ?? [];
            existingProfile.Certifications = profileDto.Certifications ?? [];
            existingProfile.Languages = profileDto.Languages ?? [];
            existingProfile.Specializations = profileDto.Specializations ?? [];
            existingProfile.Biography = profileDto.Biography;
            existingProfile.PrimaryPharmacyId = profileDto.PrimaryPharmacyId;
            existingProfile.EnableExpiryReminders = profileDto.EnableExpiryReminders;
            existingProfile.ReminderDaysBeforeExpiry = profileDto.ReminderDaysBeforeExpiry;
            existingProfile.UpdatedAt = DateTime.UtcNow;

            _context.PharmacistProfiles.Update(existingProfile);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Updated pharmacist profile for user {UserId}", userId);
            return existingProfile;
        }
        else
        {
            // Create new profile
            var newProfile = new PharmacistProfile
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                LicenseNumber = profileDto.LicenseNumber,
                LicenseAuthority = profileDto.LicenseAuthority,
                LicenseIssueDate = profileDto.LicenseIssueDate,
                LicenseExpiryDate = profileDto.LicenseExpiryDate,
                YearsOfExperience = profileDto.YearsOfExperience,
                Education = profileDto.Education ?? [],
                Certifications = profileDto.Certifications ?? [],
                Languages = profileDto.Languages ?? [],
                Specializations = profileDto.Specializations ?? [],
                Biography = profileDto.Biography,
                PrimaryPharmacyId = profileDto.PrimaryPharmacyId,
                EnableExpiryReminders = profileDto.EnableExpiryReminders,
                ReminderDaysBeforeExpiry = profileDto.ReminderDaysBeforeExpiry,
                LicenseStatus = PharmacistLicenseStatus.Active,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.PharmacistProfiles.Add(newProfile);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Created pharmacist profile for user {UserId}", userId);
            return newProfile;
        }
    }

    public async Task<List<PharmacistProfile>> GetExpiringLicensesAsync(int daysThreshold = 30)
    {
        var thresholdDate = DateTime.UtcNow.AddDays(daysThreshold);

        var expiringProfiles = await _context.PharmacistProfiles
            .Include(p => p.User)
            .Where(p => p.LicenseExpiryDate.HasValue &&
                       p.LicenseExpiryDate.Value <= thresholdDate &&
                       p.LicenseExpiryDate.Value >= DateTime.UtcNow &&
                       p.LicenseStatus == PharmacistLicenseStatus.Active)
            .OrderBy(p => p.LicenseExpiryDate)
            .ToListAsync();

        _logger.LogInformation("Found {Count} licenses expiring within {Days} days", 
            expiringProfiles.Count, daysThreshold);

        return expiringProfiles;
    }

    public async Task<bool> UpdateLicenseStatusAsync(
        Guid pharmacistProfileId, 
        PharmacistLicenseStatus newStatus, 
        string? notes = null)
    {
        var profile = await _context.PharmacistProfiles
            .Include(p => p.User)
            .FirstOrDefaultAsync(p => p.Id == pharmacistProfileId);

        if (profile == null)
        {
            _logger.LogWarning("Pharmacist profile {ProfileId} not found", pharmacistProfileId);
            return false;
        }

        var oldStatus = profile.LicenseStatus;
        profile.LicenseStatus = newStatus;
        profile.UpdatedAt = DateTime.UtcNow;

        if (!string.IsNullOrEmpty(notes))
        {
            profile.VerificationNotes = $"{DateTime.UtcNow:yyyy-MM-dd HH:mm}: {notes}\n{profile.VerificationNotes}";
        }

        await _context.SaveChangesAsync();

        _logger.LogInformation(
            "Updated license status for pharmacist {UserId} from {OldStatus} to {NewStatus}",
            profile.UserId, oldStatus, newStatus);

        // Send notification to pharmacist
        if (newStatus == PharmacistLicenseStatus.Expired || 
            newStatus == PharmacistLicenseStatus.Suspended ||
            newStatus == PharmacistLicenseStatus.Revoked)
        {
            await SendStatusChangeNotificationAsync(profile, newStatus);
        }

        return true;
    }

    public async Task SendExpiryRemindersAsync()
    {
        // Get licenses expiring in the next 30 days
        var expiringProfiles = await GetExpiringLicensesAsync(30);

        foreach (var profile in expiringProfiles)
        {
            if (!profile.EnableExpiryReminders)
                continue;

            var daysUntilExpiry = profile.LicenseExpiryDate.HasValue
                ? (int)(profile.LicenseExpiryDate.Value - DateTime.UtcNow).TotalDays
                : 0;

            // Send reminder based on threshold
            if (daysUntilExpiry <= profile.ReminderDaysBeforeExpiry)
            {
                await SendExpiryReminderAsync(profile, daysUntilExpiry);
            }
        }

        _logger.LogInformation("Sent expiry reminders to {Count} pharmacists", expiringProfiles.Count);
    }

    private async Task SendExpiryReminderAsync(PharmacistProfile profile, int daysUntilExpiry)
    {
        var subject = "Pharmacist License Expiry Reminder";
        var message = $@"
Dear {profile.User.FirstName} {profile.User.LastName},

This is a reminder that your pharmacist license (#{profile.LicenseNumber}) will expire in {daysUntilExpiry} days on {profile.LicenseExpiryDate:MMMM dd, yyyy}.

Please renew your license before the expiry date to avoid service interruption.

License Authority: {profile.LicenseAuthority ?? "N/A"}

If you have already renewed your license, please update your profile with the new expiry date.

Best regards,
PIYA Healthcare Team
";

        // Send email
        await _emailService.SendEmailAsync(profile.User.Email, subject, message);

        // Send SMS if phone number is available
        if (!string.IsNullOrEmpty(profile.User.PhoneNumber))
        {
            var smsMessage = $"PIYA: Your pharmacist license #{profile.LicenseNumber} expires in {daysUntilExpiry} days. Please renew to avoid service interruption.";
            await _smsService.SendSmsAsync(profile.User.PhoneNumber, smsMessage);
        }

        _logger.LogInformation("Sent expiry reminder to pharmacist {UserId} ({DaysUntilExpiry} days)", 
            profile.UserId, daysUntilExpiry);
    }

    private async Task SendStatusChangeNotificationAsync(PharmacistProfile profile, PharmacistLicenseStatus newStatus)
    {
        var subject = "Pharmacist License Status Update";
        var message = $@"
Dear {profile.User.FirstName} {profile.User.LastName},

Your pharmacist license status has been updated to: {newStatus}

License Number: {profile.LicenseNumber}
License Authority: {profile.LicenseAuthority ?? "N/A"}

{GetStatusMessage(newStatus)}

If you have any questions, please contact support.

Best regards,
PIYA Healthcare Team
";

        await _emailService.SendEmailAsync(profile.User.Email, subject, message);

        _logger.LogInformation("Sent status change notification to pharmacist {UserId}", profile.UserId);
    }

    private static string GetStatusMessage(PharmacistLicenseStatus status)
    {
        return status switch
        {
            PharmacistLicenseStatus.Active => "License is valid and active",
            PharmacistLicenseStatus.Expired => "License has expired. Please renew immediately.",
            PharmacistLicenseStatus.Suspended => "License is currently suspended. Contact licensing authority.",
            PharmacistLicenseStatus.Revoked => "License has been revoked. Contact licensing authority.",
            PharmacistLicenseStatus.Pending => "License verification is pending",
            _ => "Unknown license status"
        };
    }
}
