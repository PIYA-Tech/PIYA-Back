using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using PIYA_API.Configuration;
using PIYA_API.Model;
using PIYA_API.Service.Interface;
using System.Security.Claims;

namespace PIYA_API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class QRValidationController(
    IQRService qrService,
    IPrescriptionService prescriptionService,
    IPharmacyStaffService pharmacyStaffService,
    IOptions<SecurityOptions> securityOptions,
    ILogger<QRValidationController> logger) : ControllerBase
{
    private readonly IQRService _qrService = qrService;
    private readonly IPrescriptionService _prescriptionService = prescriptionService;
    private readonly IPharmacyStaffService _pharmacyStaffService = pharmacyStaffService;
    private readonly SecurityOptions _securityOptions = securityOptions.Value;
    private readonly ILogger<QRValidationController> _logger = logger;

    /// <summary>
    /// Generate QR code for a prescription (Patient only)
    /// </summary>
    [HttpPost("prescription/{prescriptionId}/generate")]
    [Authorize(Roles = "Patient,SuperAdmin")]
    [ProducesResponseType(typeof(QRTokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<QRTokenResponse>> GeneratePrescriptionQR(Guid prescriptionId)
    {
        try
        {
            var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
            var userAgent = HttpContext.Request.Headers.UserAgent.ToString();

            // On-demand expiry check: the hourly background service may not have run yet.
            // Expire the prescription immediately if it is past its ExpiresAt date so a
            // patient cannot generate a QR against a prescription the doctor intended to
            // have expired — closes the up-to-1-hour clinical safety window.
            var prescription = await _prescriptionService.GetByIdAsync(prescriptionId);
            if (prescription == null)
                return NotFound(new { error = "Prescription not found." });
            if (prescription.PatientId != userId && !User.IsInRole("SuperAdmin"))
                return Forbid();
            if (prescription.Status == PrescriptionStatus.Active && prescription.ExpiresAt < DateTime.UtcNow)
            {
                // Inline expire — matches the logic in PrescriptionExpiryService
                prescription.Status = PrescriptionStatus.Expired;
                // We can't update via the controller DbContext directly; delegate to the service.
                await _prescriptionService.ExpireAsync(prescriptionId);
                return BadRequest(new { error = "This prescription has expired and can no longer be used." });
            }
            if (prescription.Status != PrescriptionStatus.Active &&
                prescription.Status != PrescriptionStatus.PartiallyFulfilled)
            {
                return BadRequest(new { error = $"Cannot generate QR for a prescription with status '{prescription.Status}'." });
            }

            var (token, tokenId) = await _qrService.GeneratePrescriptionQrTokenAsync(
                prescriptionId,
                userId,
                ipAddress,
                userAgent
            );

            var expiresAt = DateTime.UtcNow.AddMinutes(_securityOptions.QrTokenExpiryMinutes);

            return Ok(new QRTokenResponse
            {
                Token = token,
                TokenId = tokenId,
                PrescriptionId = prescriptionId,
                ExpiresAt = expiresAt,
                ValidityMinutes = _securityOptions.QrTokenExpiryMinutes,
                Message = $"QR code generated successfully. Valid for {_securityOptions.QrTokenExpiryMinutes} minutes."
            });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("Failed to generate QR for prescription {PrescriptionId}: {Message}",
                prescriptionId, ex.Message);
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating QR for prescription {PrescriptionId}", prescriptionId);
            return StatusCode(500, new { error = "Failed to generate QR code" });
        }
    }

    /// <summary>
    /// Atomically validate the QR code, deduct stock, and fulfill the prescription.
    /// </summary>
    [HttpPost("prescription/scan")]
    [Authorize(Roles = "Pharmacist,PharmacyManager,SuperAdmin")]
    [ProducesResponseType(typeof(PrescriptionScanResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<PrescriptionScanResponse>> ScanPrescriptionQR(
        [FromBody] ScanQRRequest request)
    {
        try
        {
            var pharmacistId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
            var userAgent = HttpContext.Request.Headers.UserAgent.ToString();

            Guid pharmacyId;
            if (User.IsInRole("SuperAdmin"))
            {
                if (!request.PharmacyId.HasValue)
                    return BadRequest(new { error = "pharmacyId is required for SuperAdmin dispensing." });
                pharmacyId = request.PharmacyId.Value;
            }
            else
            {
                var assignments = await _pharmacyStaffService.GetUserPharmaciesAsync(
                    pharmacistId, activeOnly: true);
                if (request.PharmacyId.HasValue)
                {
                    if (!assignments.Any(a => a.PharmacyId == request.PharmacyId.Value))
                        return Forbid();
                    pharmacyId = request.PharmacyId.Value;
                }
                else
                {
                    var distinctPharmacyIds = assignments
                        .Select(a => a.PharmacyId)
                        .Distinct()
                        .ToList();
                    if (distinctPharmacyIds.Count == 0)
                        return Forbid();
                    if (distinctPharmacyIds.Count > 1)
                    {
                        return BadRequest(new
                        {
                            error = "pharmacyId is required when the account has multiple active pharmacy assignments."
                        });
                    }

                    pharmacyId = distinctPharmacyIds[0];
                }
            }

            var prescription = await _prescriptionService.FulfillPrescriptionByQrAsync(
                request.QrToken,
                pharmacistId,
                pharmacyId,
                ipAddress,
                userAgent);

            return Ok(new PrescriptionScanResponse
            {
                PrescriptionId = prescription.Id,
                PatientId = prescription.PatientId,
                DoctorId = prescription.DoctorId,
                Status = prescription.Status.ToString(),
                Diagnosis = prescription.Diagnosis,
                Instructions = prescription.Instructions,
                FulfilledByPharmacyId = prescription.FulfilledByPharmacyId,
                FulfilledAt = prescription.FulfilledAt,
                Medications = prescription.Items?.Select(item => new MedicationItemDto
                {
                    Id = item.Id,
                    MedicationId = item.MedicationId,
                    MedicationName = item.Medication?.BrandName ?? "Unknown",
                    GenericName = item.Medication?.GenericName,
                    Quantity = item.Quantity,
                    Dosage = item.Dosage,
                    Frequency = item.Frequency,
                    Duration = item.Duration,
                    Instructions = item.Instructions,
                    IsFulfilled = item.IsFulfilled
                }).ToList() ?? [],
                IssuedAt = prescription.IssuedAt,
                ExpiresAt = prescription.ExpiresAt,
                Message = "Prescription dispensed successfully."
            });
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException ex) when (
            ex.Message.StartsWith("Insufficient stock", StringComparison.Ordinal))
        {
            _logger.LogWarning("Prescription QR dispense rejected for stock: {Message}", ex.Message);
            return Conflict(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("Prescription QR dispense rejected: {Message}", ex.Message);
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error scanning QR code");
            return StatusCode(500, new { error = "Failed to dispense prescription" });
        }
    }

    /// <summary>
    /// Check QR token status (Any authenticated user)
    /// </summary>
    [HttpGet("status")]
    [Authorize]
    [ProducesResponseType(typeof(QRStatusResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<QRStatusResponse>> GetQRStatus([FromQuery] string token)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return BadRequest(new { error = "Token is required" });
            }

            var (status, expiresAt) = await _qrService.GetTokenStatusAsync(token);

            return Ok(new QRStatusResponse
            {
                Status = status.ToString(),
                ExpiresAt = expiresAt,
                IsActive = status == Model.QRTokenStatus.Active,
                Message = status switch
                {
                    Model.QRTokenStatus.Active => "Token is valid and can be used",
                    Model.QRTokenStatus.Used => "Token has already been used",
                    Model.QRTokenStatus.Expired => "Token has expired",
                    Model.QRTokenStatus.Revoked => "Token has been revoked",
                    _ => "Unknown status"
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking QR status");
            return BadRequest(new { error = "Invalid token format" });
        }
    }

    /// <summary>
    /// Revoke a QR token (Patient or Doctor only)
    /// </summary>
    [HttpPost("revoke")]
    [Authorize(Roles = "Patient,Doctor,SuperAdmin")]
    [ProducesResponseType(typeof(RevokeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<RevokeResponse>> RevokeQR([FromBody] RevokeQRRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Token))
            {
                return BadRequest(new { error = "Token is required" });
            }

            if (string.IsNullOrWhiteSpace(request.Reason))
            {
                return BadRequest(new { error = "Revocation reason is required" });
            }

            var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var role = User.FindFirst(ClaimTypes.Role)?.Value;

            // Resolve the bearer QR to its prescription before mutating it. A
            // Patient may revoke only their own prescription QR; a Doctor may
            // revoke only one they issued. SuperAdmin retains break-glass access.
            var (isValid, entityId, entityType, _, validationError) =
                await _qrService.ValidateQrTokenAsync(request.Token);
            if (!isValid)
                return BadRequest(new { error = validationError });
            if (!string.Equals(entityType, "Prescription", StringComparison.Ordinal))
                return Forbid();

            var prescription = await _prescriptionService.GetByIdAsync(entityId);
            if (prescription == null)
                return NotFound(new { error = "Prescription not found" });

            var ownsToken = role switch
            {
                "Patient" => prescription.PatientId == userId,
                "Doctor" => prescription.DoctorId == userId,
                "SuperAdmin" => true,
                _ => false
            };
            if (!ownsToken)
                return Forbid();

            var revoked = await _qrService.RevokeTokenAsync(
                request.Token,
                userId,
                request.Reason
            );

            if (!revoked)
            {
                return BadRequest(new { error = "Token not found or already revoked" });
            }

            return Ok(new RevokeResponse
            {
                Success = true,
                Message = "QR token revoked successfully",
                RevokedAt = DateTime.UtcNow,
                RevokedByUserId = userId,
                Reason = request.Reason
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error revoking QR token");
            return StatusCode(500, new { error = "Failed to revoke QR token" });
        }
    }

    /// <summary>
    /// Get QR token history for a prescription (Patient or Admin only)
    /// </summary>
    [HttpGet("prescription/{prescriptionId}/history")]
    [Authorize(Roles = "Patient,Admin,SuperAdmin")]
    [ProducesResponseType(typeof(List<QRTokenHistoryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<List<QRTokenHistoryDto>>> GetQRHistory(Guid prescriptionId)
    {
        try
        {
            // Patients may only view QR history for their own prescriptions.
            // Admins may query any prescription.
            var userRole = User.FindFirst(ClaimTypes.Role)?.Value;
            if (userRole == "Patient")
            {
                var callerId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
                var prescription = await _prescriptionService.GetByIdAsync(prescriptionId);
                if (prescription == null)
                    return NotFound(new { error = "Prescription not found" });
                if (prescription.PatientId != callerId)
                    return Forbid();
            }

            var history = await _qrService.GetTokenHistoryAsync(prescriptionId, "Prescription");

            var result = history.Select(token => new QRTokenHistoryDto
            {
                TokenId = token.Id,
                GeneratedAt = token.GeneratedAt,
                ExpiresAt = token.ExpiresAt,
                GeneratedFromIp = token.GeneratedFromIp,
                GeneratedFromDevice = token.GeneratedFromDevice,
                IsUsed = token.IsUsed,
                UsedAt = token.UsedAt,
                UsedByUserId = token.UsedByUserId,
                UsedFromIp = token.UsedFromIp,
                IsRevoked = token.IsRevoked,
                RevokedAt = token.RevokedAt,
                RevokedByUserId = token.RevokedByUserId,
                RevocationReason = token.RevocationReason,
                ValidationAttempts = token.ValidationAttempts,
                Status = token.IsRevoked ? "Revoked" :
                         token.IsUsed ? "Used" :
                         token.ExpiresAt < DateTime.UtcNow ? "Expired" : "Active"
            }).ToList();

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching QR history for prescription {PrescriptionId}", prescriptionId);
            return StatusCode(500, new { error = "Failed to fetch QR history" });
        }
    }

    /// <summary>
    /// Validate QR without using it (for preview/testing)
    /// </summary>
    [HttpPost("validate")]
    [Authorize]
    [ProducesResponseType(typeof(ValidationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ValidationResponse>> ValidateQR([FromBody] ValidateQRRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Token))
            {
                return BadRequest(new { error = "Token is required" });
            }

            var (isValid, entityId, entityType, expiresAt, errorMessage) = 
                await _qrService.ValidateQrTokenAsync(request.Token);

            return Ok(new ValidationResponse
            {
                IsValid = isValid,
                EntityId = isValid ? entityId : null,
                EntityType = isValid ? entityType : null,
                ExpiresAt = isValid ? expiresAt : null,
                ErrorMessage = errorMessage,
                Message = isValid ? "Token is valid" : $"Token validation failed: {errorMessage}"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating QR token");
            return BadRequest(new { error = "Invalid token format" });
        }
    }
}

#region DTOs

public class QRTokenResponse
{
    public string Token { get; set; } = string.Empty;
    public Guid TokenId { get; set; }
    public Guid PrescriptionId { get; set; }
    public DateTime ExpiresAt { get; set; }
    public int ValidityMinutes { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class ScanQRRequest
{
    public string QrToken { get; set; } = string.Empty;
    public Guid? PharmacyId { get; set; }
}

public class PrescriptionScanResponse
{
    public Guid PrescriptionId { get; set; }
    public Guid PatientId { get; set; }
    public Guid DoctorId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Diagnosis { get; set; }
    public string? Instructions { get; set; }
    public Guid? FulfilledByPharmacyId { get; set; }
    public DateTime? FulfilledAt { get; set; }
    public List<MedicationItemDto> Medications { get; set; } = [];
    public DateTime IssuedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class MedicationItemDto
{
    public Guid Id { get; set; }
    public Guid MedicationId { get; set; }
    public string MedicationName { get; set; } = string.Empty;
    public string? GenericName { get; set; }
    public int Quantity { get; set; }
    public string? Dosage { get; set; }
    public string? Frequency { get; set; }
    public string? Duration { get; set; }
    public string? Instructions { get; set; }
    public bool IsFulfilled { get; set; }
}

public class QRStatusResponse
{
    public string Status { get; set; } = string.Empty;
    public DateTime? ExpiresAt { get; set; }
    public bool IsActive { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class RevokeQRRequest
{
    public string Token { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

public class RevokeResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTime RevokedAt { get; set; }
    public Guid RevokedByUserId { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public class QRTokenHistoryDto
{
    public Guid TokenId { get; set; }
    public DateTime GeneratedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public string? GeneratedFromIp { get; set; }
    public string? GeneratedFromDevice { get; set; }
    public bool IsUsed { get; set; }
    public DateTime? UsedAt { get; set; }
    public Guid? UsedByUserId { get; set; }
    public string? UsedFromIp { get; set; }
    public bool IsRevoked { get; set; }
    public DateTime? RevokedAt { get; set; }
    public Guid? RevokedByUserId { get; set; }
    public string? RevocationReason { get; set; }
    public int ValidationAttempts { get; set; }
    public string Status { get; set; } = string.Empty;
}

public class ValidateQRRequest
{
    public string Token { get; set; } = string.Empty;
}

public class ValidationResponse
{
    public bool IsValid { get; set; }
    public Guid? EntityId { get; set; }
    public string? EntityType { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public string? ErrorMessage { get; set; }
    public string Message { get; set; } = string.Empty;
}

#endregion
