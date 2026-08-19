using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using PIYA_API.Configuration;
using PIYA_API.DTOs;
using PIYA_API.Model;
using PIYA_API.Service.Interface;
using System.Security.Claims;

namespace PIYA_API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class PrescriptionController(
    IPrescriptionService prescriptionService,
    IQRService qrService,
    IPharmacyStaffService pharmacyStaffService,
    IAppointmentService appointmentService,
    IOptions<SecurityOptions> securityOptions,
    ILogger<PrescriptionController> logger) : ControllerBase
{
    private readonly IPrescriptionService _prescriptionService = prescriptionService;
    private readonly IQRService _qrService = qrService;
    private readonly IPharmacyStaffService _pharmacyStaffService = pharmacyStaffService;
    private readonly IAppointmentService _appointmentService = appointmentService;
    private readonly SecurityOptions _securityOptions = securityOptions.Value;
    private readonly ILogger<PrescriptionController> _logger = logger;

    /// <summary>
    /// Create a new prescription (Doctor only)
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Doctor,Admin,SuperAdmin")]
    public async Task<ActionResult<Prescription>> Create([FromBody] CreatePrescriptionDto dto)
    {
        try
        {
            var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value!);
            var userRole = User.FindFirst(ClaimTypes.Role)?.Value;

            // Doctors must link prescriptions to a completed or active referral appointment
            if (userRole == "Doctor")
            {
                if (!dto.AppointmentId.HasValue)
                    return BadRequest(new { error = "A completed appointment is required to create a prescription." });

                var appointment = await _appointmentService.GetByIdAsync(dto.AppointmentId.Value);
                if (appointment == null)
                    return NotFound(new { error = "Appointment not found." });
                bool isReferralStub = appointment.Status == AppointmentStatus.Confirmed && appointment.ReferralId.HasValue;
                if (appointment.Status != AppointmentStatus.Completed && !isReferralStub)
                    return BadRequest(new { error = "Prescriptions can only be created for completed appointments or active referral appointments." });
                if (appointment.DoctorId != userId)
                    return Forbid();
                if (appointment.PatientId != dto.PatientId)
                    return BadRequest(new { error = "Patient does not match the appointment." });
            }

            var prescription = new Prescription
            {
                PatientId = dto.PatientId,
                AppointmentId = dto.AppointmentId,
                DoctorId = userRole == "Doctor" ? userId : dto.DoctorId,
                Diagnosis = dto.Diagnosis,
                Instructions = dto.Instructions,
                ExpiresAt = dto.ExpiresAt == default ? DateTime.UtcNow.AddDays(30) : dto.ExpiresAt,
                Items = dto.Items?.Select(i => new PrescriptionItem
                {
                    MedicationId = i.MedicationId,
                    Dosage = i.Dosage,
                    Frequency = i.Frequency,
                    Duration = i.Duration,
                    Instructions = i.Instructions,
                    Quantity = i.Quantity
                }).ToList() ?? []
            };

            var created = await _prescriptionService.CreatePrescriptionAsync(prescription);
            return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating prescription");
            return StatusCode(500, new { error = "Failed to create prescription" });
        }
    }

    /// <summary>
    /// Get prescription by ID
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<Prescription>> GetById(Guid id)
    {
        try
        {
            var prescription = await _prescriptionService.GetByIdAsync(id);
            if (prescription == null)
            {
                return NotFound(new { error = "Prescription not found" });
            }

            var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value!);
            var userRole = User.FindFirst(ClaimTypes.Role)?.Value;

            // Verify user has access
            if (userRole == "Admin" || userRole == "SuperAdmin")
            {
                // admins can see all
            }
            else if (userRole == "Pharmacist")
            {
                // An unassigned prescription is disclosed only through the one-time patient
                // QR presentation flow. Direct ID lookup is limited to prescriptions already
                // assigned to a pharmacy where the caller is active staff.
                if (!prescription.FulfilledByPharmacyId.HasValue ||
                    !await _pharmacyStaffService.IsStaffAtPharmacyAsync(
                        prescription.FulfilledByPharmacyId.Value, userId))
                {
                    return Forbid();
                }
            }
            else if (prescription.PatientId != userId && prescription.DoctorId != userId)
            {
                return Forbid();
            }

            return Ok(PrescriptionResponseDto.FromEntity(prescription));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving prescription {PrescriptionId}", id);
            return StatusCode(500, new { error = "Failed to retrieve prescription" });
        }
    }

    /// <summary>
    /// Get my prescriptions (Patient view)
    /// </summary>
    [HttpGet("my-prescriptions")]
    [Authorize(Roles = "Patient,SuperAdmin")]
    public async Task<ActionResult<List<Prescription>>> GetMyPrescriptions([FromQuery] string? status = null)
    {
        try
        {
            var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value!);

            PrescriptionStatus? prescriptionStatus = null;
            if (!string.IsNullOrEmpty(status) && Enum.TryParse<PrescriptionStatus>(status, true, out var parsedStatus))
            {
                prescriptionStatus = parsedStatus;
            }

            var prescriptions = await _prescriptionService.GetPatientPrescriptionsAsync(userId, prescriptionStatus);
            return Ok(prescriptions.Select(PrescriptionResponseDto.FromEntity));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving patient prescriptions");
            return StatusCode(500, new { error = "Failed to retrieve prescriptions" });
        }
    }

    /// <summary>
    /// Get prescriptions created by doctor
    /// </summary>
    [HttpGet("doctor/{doctorId}")]
    [Authorize(Roles = "Doctor,Admin,SuperAdmin")]
    public async Task<ActionResult<List<Prescription>>> GetDoctorPrescriptions(Guid doctorId)
    {
        try
        {
            var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value!);
            var userRole = User.FindFirst(ClaimTypes.Role)?.Value;

            // Doctor can only view their own prescriptions
            if (userRole == "Doctor" && doctorId != userId)
            {
                return Forbid();
            }

            var prescriptions = await _prescriptionService.GetDoctorPrescriptionsAsync(doctorId);
            return Ok(prescriptions.Select(PrescriptionResponseDto.FromEntity));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving doctor prescriptions for {DoctorId}", doctorId);
            return StatusCode(500, new { error = "Failed to retrieve prescriptions" });
        }
    }

    /// <summary>
    /// Generate QR code for prescription (5-minute validity).
    /// Canonical endpoint: POST /api/qrvalidation/prescription/{id}/generate
    /// This route is kept for backward-compatibility and delegates to the QR service directly.
    /// </summary>
    [HttpPost("{id:guid}/generate-qr")]
    [Authorize(Roles = "Patient,SuperAdmin")]
    public async Task<ActionResult<object>> GenerateQrCode(Guid id)
    {
        try
        {
            var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value!);
            var prescription = await _prescriptionService.GetByIdAsync(id);

            if (prescription == null)
                return NotFound(new { error = "Prescription not found" });

            if (prescription.PatientId != userId)
                return Forbid();

            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
            var userAgent = Request.Headers.UserAgent.ToString();

            var (token, tokenId) = await _qrService.GeneratePrescriptionQrTokenAsync(id, userId, ipAddress, userAgent);
            return Ok(new
            {
                qrToken = token,
                tokenId,
                prescriptionId = id,
                expiresAt = DateTime.UtcNow.AddMinutes(_securityOptions.QrTokenExpiryMinutes),
                message = $"QR code is valid for {_securityOptions.QrTokenExpiryMinutes} minutes"
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating QR code for prescription {PrescriptionId}", id);
            return StatusCode(500, new { error = "Failed to generate QR code" });
        }
    }

    /// <summary>
    /// Legacy atomic QR dispensing route. New clients should use
    /// POST /api/QRValidation/prescription/scan.
    /// </summary>
    [HttpPost("validate-qr")]
    [Authorize(Roles = "Pharmacist,PharmacyManager,SuperAdmin")]
    public async Task<ActionResult<Prescription>> ValidateQrCode([FromBody] ValidateQrRequest request)
    {
        try
        {
            var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value!);
            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
            var userAgent = Request.Headers.UserAgent.ToString();

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
                    userId, activeOnly: true);
                if (request.PharmacyId.HasValue)
                {
                    if (!assignments.Any(a => a.PharmacyId == request.PharmacyId.Value))
                        return Forbid();
                    pharmacyId = request.PharmacyId.Value;
                }
                else
                {
                    var ids = assignments.Select(a => a.PharmacyId).Distinct().ToList();
                    if (ids.Count == 0)
                        return Forbid();
                    if (ids.Count > 1)
                        return BadRequest(new { error = "pharmacyId is required for accounts assigned to multiple pharmacies." });
                    pharmacyId = ids[0];
                }
            }

            var prescription = await _prescriptionService.FulfillPrescriptionByQrAsync(
                request.QrToken,
                userId,
                pharmacyId,
                ipAddress,
                userAgent);

            return Ok(PrescriptionResponseDto.FromEntity(prescription));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException ex) when (
            ex.Message.StartsWith("Insufficient stock", StringComparison.Ordinal))
        {
            return Conflict(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating QR code");
            return StatusCode(500, new { error = "Failed to validate QR code" });
        }
    }

    /// <summary>
    /// Fulfill prescription (Pharmacist only)
    /// </summary>
    [HttpPost("{id:guid}/fulfill")]
    [Authorize(Roles = "Pharmacist,Admin,SuperAdmin")]
    public async Task<ActionResult<Prescription>> FulfillPrescription(Guid id, [FromBody] FulfillPrescriptionRequest request)
    {
        try
        {
            // Ensure the calling pharmacist is actually staff at the requested pharmacy
            // and that the prescription was already assigned through a patient-presented
            // QR flow. A raw prescription GUID is never sufficient to start dispensing.
            if (!User.IsInRole("Admin") && !User.IsInRole("SuperAdmin"))
            {
                var callerId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
                if (!await _pharmacyStaffService.IsStaffAtPharmacyAsync(request.PharmacyId, callerId))
                    return Forbid();

                var prescription = await _prescriptionService.GetByIdAsync(id);
                if (prescription == null)
                    return NotFound(new { error = "Prescription not found" });
                if (!prescription.FulfilledByPharmacyId.HasValue)
                {
                    return BadRequest(new
                    {
                        error = "Patient presentation is required. Scan the prescription QR code before dispensing."
                    });
                }
                if (prescription.FulfilledByPharmacyId.Value != request.PharmacyId)
                    return Forbid();
            }

            var fulfilled = await _prescriptionService.FulfillPrescriptionAsync(id, request.PharmacyId);
            return Ok(fulfilled);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { error = "Prescription not found" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fulfilling prescription {PrescriptionId}", id);
            return StatusCode(500, new { error = "Failed to fulfill prescription" });
        }
    }

    /// <summary>
    /// Fulfill prescription item (Pharmacist only)
    /// </summary>
    [HttpPost("item/{itemId:guid}/fulfill")]
    [Authorize(Roles = "Pharmacist,Admin,SuperAdmin")]
    public async Task<ActionResult<PrescriptionItem>> FulfillPrescriptionItem(Guid itemId)
    {
        try
        {
            // Pharmacists must be staff at the dispensing pharmacy
            if (!User.IsInRole("Admin") && !User.IsInRole("SuperAdmin"))
            {
                var item = await _prescriptionService.GetPrescriptionItemAsync(itemId);
                if (item == null)
                    return NotFound(new { error = "Prescription item not found" });
                if (item.Prescription?.FulfilledByPharmacyId is not Guid pharmacyId)
                    return BadRequest(new { error = "Patient presentation is required before dispensing an item." });

                var callerId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
                if (!await _pharmacyStaffService.IsStaffAtPharmacyAsync(pharmacyId, callerId))
                    return Forbid();
            }

            var fulfilledItem = await _prescriptionService.FulfillPrescriptionItemAsync(itemId);
            return Ok(fulfilledItem);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { error = "Prescription item not found" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fulfilling prescription item {ItemId}", itemId);
            return StatusCode(500, new { error = "Failed to fulfill prescription item" });
        }
    }

    /// <summary>
    /// Cancel prescription (Doctor only)
    /// </summary>
    [HttpPost("{id:guid}/cancel")]
    [Authorize(Roles = "Doctor,Admin,SuperAdmin")]
    public async Task<ActionResult<Prescription>> Cancel(Guid id, [FromBody] CancelPrescriptionRequest request)
    {
        try
        {
            var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value!);
            var prescription = await _prescriptionService.GetByIdAsync(id);

            if (prescription == null)
            {
                return NotFound(new { error = "Prescription not found" });
            }

            var userRole = User.FindFirst(ClaimTypes.Role)?.Value;
            if (userRole != "Admin" && userRole != "SuperAdmin" && prescription.DoctorId != userId)
            {
                return Forbid();
            }

            var cancelled = await _prescriptionService.CancelPrescriptionAsync(id, request.Reason);
            return Ok(cancelled);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cancelling prescription {PrescriptionId}", id);
            return StatusCode(500, new { error = "Failed to cancel prescription" });
        }
    }

    /// <summary>
    /// Check if prescription is expired
    /// </summary>
    [HttpGet("{id:guid}/is-expired")]
    public async Task<ActionResult<object>> IsExpired(Guid id)
    {
        try
        {
            var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value!);
            var prescription = await _prescriptionService.GetByIdAsync(id);

            if (prescription == null)
            {
                return NotFound(new { error = "Prescription not found" });
            }

            var userRole = User.FindFirst(ClaimTypes.Role)?.Value;
            if (userRole == "Admin" || userRole == "SuperAdmin")
            {
                // admins can check any prescription
            }
            else if (userRole == "Pharmacist")
            {
                var callerId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
                var pharmacyAssignments = await _pharmacyStaffService.GetUserPharmaciesAsync(callerId, activeOnly: true);
                var pharmacyIds = pharmacyAssignments.Select(a => a.PharmacyId).ToHashSet();
                if (prescription.FulfilledByPharmacyId == null || !pharmacyIds.Contains(prescription.FulfilledByPharmacyId.Value))
                    return Forbid();
            }
            else if (prescription.PatientId != userId && prescription.DoctorId != userId)
            {
                return Forbid();
            }

            var isExpired = await _prescriptionService.IsExpiredAsync(id);
            return Ok(new { prescriptionId = id, isExpired });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking prescription expiry {PrescriptionId}", id);
            return StatusCode(500, new { error = "Failed to check expiry" });
        }
    }

    /// <summary>
    /// Get all prescriptions — Admin only, paginated, optional status filter
    /// </summary>
    [HttpGet("all")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<ActionResult<object>> GetAll(
        [FromQuery] string? status = null,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 50)
    {
        try
        {
            PrescriptionStatus? prescriptionStatus = null;
            if (!string.IsNullOrEmpty(status) && Enum.TryParse<PrescriptionStatus>(status, true, out var parsed))
                prescriptionStatus = parsed;

            var prescriptions = await _prescriptionService.GetAllAsync(prescriptionStatus, pageNumber, pageSize);
            return Ok(prescriptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving all prescriptions");
            return StatusCode(500, new { error = "Failed to retrieve prescriptions" });
        }
    }

    /// <summary>
    /// Permanently delete a prescription — Admin only
    /// </summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> Delete(Guid id)
    {
        try
        {
            await _prescriptionService.DeleteAsync(id);
            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { error = "Prescription not found" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting prescription {PrescriptionId}", id);
            return StatusCode(500, new { error = "Failed to delete prescription" });
        }
    }

    /// <summary>
    /// Get prescriptions expiring soon (Admin/Pharmacist)
    /// </summary>
    [HttpGet("expiring-soon")]
    [Authorize(Roles = "Admin,SuperAdmin,Pharmacist")]
    public async Task<ActionResult<List<Prescription>>> GetExpiringSoon([FromQuery] int daysThreshold = 7)
    {
        try
        {
            var prescriptions = await _prescriptionService.GetExpiringSoonAsync(daysThreshold);
            if (User.IsInRole("Pharmacist"))
            {
                var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
                var assignments = await _pharmacyStaffService.GetUserPharmaciesAsync(userId, activeOnly: true);
                var pharmacyIds = assignments.Select(a => a.PharmacyId).ToHashSet();
                prescriptions = prescriptions
                    .Where(p => p.FulfilledByPharmacyId.HasValue &&
                                pharmacyIds.Contains(p.FulfilledByPharmacyId.Value))
                    .ToList();
            }
            return Ok(prescriptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving expiring prescriptions");
            return StatusCode(500, new { error = "Failed to retrieve prescriptions" });
        }
    }

    /// <summary>
    /// Get Active/PartiallyFulfilled prescriptions assigned to a specific pharmacy.
    /// Pharmacist must be staff at that pharmacy; Admin may query any pharmacy.
    /// </summary>
    [HttpGet("pharmacy/{pharmacyId:guid}")]
    [Authorize(Roles = "Pharmacist,PharmacyManager,Admin,SuperAdmin")]
    public async Task<ActionResult<List<Prescription>>> GetByPharmacy(Guid pharmacyId)
    {
        try
        {
            // Pharmacists and PharmacyManagers must belong to the requested pharmacy
            if (!User.IsInRole("Admin") && !User.IsInRole("SuperAdmin"))
            {
                var callerId = Guid.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
                if (!await _pharmacyStaffService.IsStaffAtPharmacyAsync(pharmacyId, callerId))
                    return Forbid();
            }

            var prescriptions = await _prescriptionService.GetByPharmacyAsync(pharmacyId);
            return Ok(prescriptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving prescriptions for pharmacy {PharmacyId}", pharmacyId);
            return StatusCode(500, new { error = "Failed to retrieve prescriptions" });
        }
    }
}

public record ValidateQrRequest(string QrToken, Guid? PharmacyId = null);

public record FulfillPrescriptionRequest(Guid PharmacyId);

public class CreatePrescriptionDto
{
    public Guid PatientId { get; set; }
    public Guid? AppointmentId { get; set; }
    /// <summary>Only used when caller is Admin. Ignored for Doctor role (their ID is used).</summary>
    public Guid DoctorId { get; set; }
    public string? Diagnosis { get; set; }
    public string? Instructions { get; set; }
    public DateTime ExpiresAt { get; set; }
    public List<CreatePrescriptionItemDto>? Items { get; set; }
}

public class CreatePrescriptionItemDto
{
    public Guid MedicationId { get; set; }
    public required string Dosage { get; set; }
    public required string Frequency { get; set; }
    public required string Duration { get; set; }
    public int Quantity { get; set; }
    public string? Instructions { get; set; }
}
