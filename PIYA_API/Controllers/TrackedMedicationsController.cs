using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PIYA_API.DTOs;
using PIYA_API.Model;
using PIYA_API.Service.Class;
using PIYA_API.Service.Interface;

namespace PIYA_API.Controllers;

[ApiController]
[Route("api/patient/medications")]
[Authorize(Roles = "Patient")]
public sealed class TrackedMedicationsController(IPatientMedicationService medications) : ControllerBase
{
    private readonly IPatientMedicationService _medications = medications;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<TrackedMedicationResponse>>> GetAll(
        [FromQuery] bool includeArchived = false)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        return Ok(await _medications.GetAsync(userId, includeArchived));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TrackedMedicationResponse>> Get(Guid id)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        var result = await _medications.GetAsync(userId, id);
        return result is null ? NotFound(new { error = "Tracked medication not found." }) : Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<TrackedMedicationResponse>> Create(
        [FromBody] CreateTrackedMedicationRequest request)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        try
        {
            var result = await _medications.CreateAsync(userId, request);
            return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
        }
        catch (PatientHealthNotFoundException exception) { return NotFound(new { error = exception.Message }); }
        catch (PatientHealthConflictException exception) { return Conflict(new { error = exception.Message }); }
        catch (ArgumentException exception) { return BadRequest(new { error = exception.Message }); }
    }

    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<TrackedMedicationResponse>> Update(
        Guid id, [FromBody] UpdateTrackedMedicationRequest request)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        try
        {
            return Ok(await _medications.UpdateAsync(userId, id, request));
        }
        catch (PatientHealthNotFoundException exception) { return NotFound(new { error = exception.Message }); }
        catch (ArgumentException exception) { return BadRequest(new { error = exception.Message }); }
    }

    [HttpPost("{id:guid}/schedules")]
    public async Task<ActionResult<DoseScheduleResponse>> AddSchedule(
        Guid id, [FromBody] UpsertDoseScheduleRequest request)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        try
        {
            return Ok(await _medications.AddScheduleAsync(userId, id, request));
        }
        catch (PatientHealthNotFoundException exception) { return NotFound(new { error = exception.Message }); }
        catch (ArgumentException exception) { return BadRequest(new { error = exception.Message }); }
    }

    [HttpPut("schedules/{scheduleId:guid}")]
    public async Task<ActionResult<DoseScheduleResponse>> UpdateSchedule(
        Guid scheduleId, [FromBody] UpsertDoseScheduleRequest request)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        try
        {
            return Ok(await _medications.UpdateScheduleAsync(userId, scheduleId, request));
        }
        catch (PatientHealthNotFoundException exception) { return NotFound(new { error = exception.Message }); }
        catch (ArgumentException exception) { return BadRequest(new { error = exception.Message }); }
    }

    [HttpDelete("schedules/{scheduleId:guid}")]
    public async Task<IActionResult> DeleteSchedule(Guid scheduleId)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        try
        {
            await _medications.DeleteScheduleAsync(userId, scheduleId);
            return NoContent();
        }
        catch (PatientHealthNotFoundException exception) { return NotFound(new { error = exception.Message }); }
    }

    [HttpGet("doses")]
    public async Task<ActionResult<IReadOnlyList<ScheduledMedicationDoseResponse>>> GetDoses(
        [FromQuery] DateOnly? date = null, [FromQuery] string timeZone = "Asia/Baku")
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        try
        {
            var localDate = date ?? DateOnly.FromDateTime(
                TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, MedicationScheduleClock.GetTimeZone(timeZone)));
            return Ok(await _medications.GetDosesForDateAsync(userId, localDate, timeZone));
        }
        catch (ArgumentException exception) { return BadRequest(new { error = exception.Message }); }
    }

    [HttpPost("doses")]
    public async Task<ActionResult<MedicationDoseResponse>> RecordDose(
        [FromBody] RecordMedicationDoseRequest request)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        try
        {
            return Ok(await _medications.RecordDoseAsync(userId, request));
        }
        catch (PatientHealthNotFoundException exception) { return NotFound(new { error = exception.Message }); }
        catch (PatientHealthConflictException exception) { return Conflict(new { error = exception.Message }); }
        catch (ArgumentException exception) { return BadRequest(new { error = exception.Message }); }
    }

    [HttpGet("doses/history")]
    public async Task<ActionResult<MedicationAdherenceHistoryResponse>> GetDoseHistory(
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] MedicationDoseStatus? status = null)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        try
        {
            var end = to ?? DateTime.UtcNow.AddMinutes(1);
            var start = from ?? end.AddDays(-30);
            return Ok(await _medications.GetDoseHistoryAsync(userId, start, end, status));
        }
        catch (ArgumentException exception) { return BadRequest(new { error = exception.Message }); }
    }

    private bool TryGetUserId(out Guid userId) => Guid.TryParse(
        User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);
}
