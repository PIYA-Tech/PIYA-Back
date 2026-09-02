using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PIYA_API.DTOs;
using PIYA_API.Service.Interface;

namespace PIYA_API.Controllers;

[ApiController]
[Route("api/internal/medication-reminders")]
[Authorize(Roles = "Admin,SuperAdmin")]
public sealed class MedicationReminderProcessingController(IMedicationReminderProcessor processor)
    : ControllerBase
{
    private readonly IMedicationReminderProcessor _processor = processor;

    [HttpPost("process")]
    public async Task<ActionResult<ReminderProcessingResponse>> Process(
        CancellationToken cancellationToken)
    {
        return Ok(await _processor.ProcessDueAsync(cancellationToken: cancellationToken));
    }
}
