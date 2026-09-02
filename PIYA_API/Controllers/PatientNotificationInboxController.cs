using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PIYA_API.DTOs;
using PIYA_API.Service.Interface;

namespace PIYA_API.Controllers;

[ApiController]
[Route("api/notifications/inbox")]
[Authorize]
public sealed class PatientNotificationInboxController(IPatientNotificationInboxService inbox)
    : ControllerBase
{
    private readonly IPatientNotificationInboxService _inbox = inbox;

    [HttpGet]
    public async Task<ActionResult<PatientNotificationPageResponse>> GetPage(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 30,
        [FromQuery] bool unreadOnly = false,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        return Ok(await _inbox.GetPageAsync(userId, page, pageSize, unreadOnly, cancellationToken));
    }

    [HttpGet("unread-count")]
    public async Task<ActionResult<object>> GetUnreadCount(CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        return Ok(new { unreadCount = await _inbox.GetUnreadCountAsync(userId, cancellationToken) });
    }

    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        return await _inbox.MarkReadAsync(userId, id, cancellationToken)
            ? NoContent()
            : NotFound(new { error = "Notification not found." });
    }

    [HttpPost("read-all")]
    public async Task<ActionResult<object>> MarkAllRead(CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        return Ok(new { updated = await _inbox.MarkAllReadAsync(userId, cancellationToken) });
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Archive(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        return await _inbox.ArchiveAsync(userId, id, cancellationToken)
            ? NoContent()
            : NotFound(new { error = "Notification not found." });
    }

    private bool TryGetUserId(out Guid userId) => Guid.TryParse(
        User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);
}
