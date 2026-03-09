using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using PIYA_API.Configuration;
using PIYA_API.Service.Interface;
using System.Security.Claims;

namespace PIYA_API.Controllers;

[ApiController]
[Route("api/[controller]")]
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
    [Authorize(Roles = "Patient")]
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
    /// Validate and use QR code to retrieve prescription (Pharmacist only)
    /// </summary>
    [HttpPost("prescription/scan")]
    [Authorize(Roles = "Pharmacist")]
    [ProducesResponseType(typeof(PrescriptionScanResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<PrescriptionScanResponse>> ScanPrescriptionQR(
        [FromBody] ScanQRRequest request)
    {
        try
        {
            var pharmacistId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
            var userAgent = HttpContext.Request.Headers.UserAgent.ToString();

            var (isValid, prescriptionId, errorMessage) = 
                await _qrService.ValidateAndUsePrescriptionQrTokenAsync(
                    request.QrToken,
                    pharmacistId,
                    ipAddress,
                    userAgent
                );

            if (!isValid)
            {
                _logger.LogWarning("Invalid QR scan attempt by pharmacist {PharmacistId}: {Error}",
                    pharmacistId, errorMessage);
                return BadRequest(new { error = errorMessage });
            }

            // Fetch full prescription details
            var prescription = await _prescriptionService.GetByIdAsync(prescriptionId);
            if (prescription == null)
            {
                return NotFound(new { error = "Prescription not found" });
            }

            // Resolve the pharmacist's pharmacy and fulfill the prescription
            var pharmacyAssignments = await _pharmacyStaffService.GetUserPharmaciesAsync(pharmacistId, activeOnly: true);
            var pharmacyId = pharmacyAssignments.FirstOrDefault()?.PharmacyId;
            if (pharmacyId.HasValue)
            {
                try
                {
                    prescription = await _prescriptionService.FulfillPrescriptionAsync(prescriptionId, pharmacyId.Value);
                }
                catch (InvalidOperationException ex)
                {
                    _logger.LogWarning("Could not fulfill prescription {PrescriptionId} during QR scan: {Message}",
                        prescriptionId, ex.Message);
                    // Non-fatal: return the prescription details even if fulfillment failed (e.g. already fulfilled)
                }
            }
            else
            {
                _logger.LogWarning("Pharmacist {PharmacistId} has no active pharmacy assignment; prescription {PrescriptionId} not marked fulfilled",
                    pharmacistId, prescriptionId);
            }

            return Ok(new PrescriptionScanResponse
            {
                PrescriptionId = prescription.Id,
                PatientId = prescription.PatientId,
                DoctorId = prescription.DoctorId,
                Status = prescription.Status.ToString(),
                Medications = prescription.Items?.Select(item => new MedicationItemDto
                {
                    MedicationId = item.MedicationId,
                    MedicationName = item.Medication?.BrandName ?? "Unknown",
                    GenericName = item.Medication?.GenericName,
                    Quantity = item.Quantity,
                    Dosage = item.Dosage,
                    Frequency = item.Frequency,
                    Duration = item.Duration,
                    Instructions = item.Instructions
                }).ToList() ?? [],
                IssuedAt = prescription.IssuedAt,
                ExpiresAt = prescription.ExpiresAt,
                Message = "Prescription validated successfully. Status updated to Fulfilled."
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error scanning QR code");
            return StatusCode(500, new { error = "Failed to validate QR code" });
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
    [Authorize(Roles = "Patient,Doctor")]
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
    [Authorize(Roles = "Patient,Admin")]
    [ProducesResponseType(typeof(List<QRTokenHistoryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<List<QRTokenHistoryDto>>> GetQRHistory(Guid prescriptionId)
    {
        try
        {
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
}

public class PrescriptionScanResponse
{
    public Guid PrescriptionId { get; set; }
    public Guid PatientId { get; set; }
    public Guid DoctorId { get; set; }
    public string Status { get; set; } = string.Empty;
    public List<MedicationItemDto> Medications { get; set; } = [];
    public DateTime IssuedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class MedicationItemDto
{
    public Guid MedicationId { get; set; }
    public string MedicationName { get; set; } = string.Empty;
    public string? GenericName { get; set; }
    public int Quantity { get; set; }
    public string? Dosage { get; set; }
    public string? Frequency { get; set; }
    public string? Duration { get; set; }
    public string? Instructions { get; set; }
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
