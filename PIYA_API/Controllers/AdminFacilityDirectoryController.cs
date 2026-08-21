using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PIYA_API.DTOs;
using PIYA_API.Model;
using PIYA_API.Service.Interface;

namespace PIYA_API.Controllers;

[ApiController]
[Route("api/admin/facilities")]
[Authorize(Roles = "Admin,SuperAdmin")]
public sealed class AdminFacilityDirectoryController(
    IFacilityDirectoryService service,
    IFacilityDirectorySyncService syncService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<AdminDirectoryFacilityPageDto>> GetAll(
        [FromQuery] DirectoryFacilityKind? kind = null,
        [FromQuery] FacilityVerificationStatus? status = null,
        [FromQuery] string? source = null,
        [FromQuery(Name = "q")] string? query = null,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 100,
        CancellationToken cancellationToken = default) =>
        Ok(await service.GetAdminAsync(kind, status, source, query, pageNumber, pageSize, cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AdminDirectoryFacilityDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var facility = await service.GetAdminByIdAsync(id, cancellationToken);
        return facility == null ? NotFound(new { error = "Facility not found" }) : Ok(facility);
    }

    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<AdminDirectoryFacilityDto>> Update(
        Guid id,
        [FromBody] UpdateDirectoryFacilityRequest request,
        CancellationToken cancellationToken) =>
        Ok(await service.UpdateAsync(id, request, cancellationToken));

    [HttpGet("stats")]
    public async Task<ActionResult<FacilityDirectoryStatsDto>> GetStats(CancellationToken cancellationToken) =>
        Ok(await service.GetStatsAsync(cancellationToken));

    [HttpGet("sources")]
    public ActionResult<IReadOnlyCollection<string>> GetSources() => Ok(syncService.AvailableSources);

    [HttpPost("sync")]
    public async Task<ActionResult<List<FacilityImportRunDto>>> Sync(
        [FromBody] FacilitySyncRequest request,
        CancellationToken cancellationToken) =>
        Ok(await syncService.SyncAsync(request.DryRun, request.Sources, cancellationToken));

    [HttpGet("imports")]
    public async Task<ActionResult<List<FacilityImportRunDto>>> GetImports(
        [FromQuery] int count = 50,
        CancellationToken cancellationToken = default) =>
        Ok(await service.GetImportRunsAsync(count, cancellationToken));

    [HttpGet("claims")]
    public async Task<ActionResult<List<FacilityClaimDto>>> GetClaims(
        [FromQuery] FacilityClaimStatus? status = FacilityClaimStatus.Pending,
        CancellationToken cancellationToken = default) =>
        Ok(await service.GetClaimsAsync(status, cancellationToken));

    [HttpPost("claims/{id:guid}/review")]
    public async Task<ActionResult<FacilityClaimDto>> ReviewClaim(
        Guid id,
        [FromBody] ReviewFacilityClaimRequest request,
        CancellationToken cancellationToken)
    {
        return Ok(await service.ReviewClaimAsync(id, ReviewerId(), request, cancellationToken));
    }

    [HttpGet("duplicates")]
    public async Task<ActionResult<List<FacilityDuplicateDto>>> GetDuplicates(
        [FromQuery] FacilityDuplicateStatus? status = FacilityDuplicateStatus.Pending,
        CancellationToken cancellationToken = default) =>
        Ok(await service.GetDuplicatesAsync(status, cancellationToken));

    [HttpPost("duplicates/{id:guid}/review")]
    public async Task<IActionResult> ReviewDuplicate(
        Guid id,
        [FromBody] ReviewDuplicateRequest request,
        CancellationToken cancellationToken)
    {
        await service.ReviewDuplicateAsync(id, ReviewerId(), request.Merge, cancellationToken);
        return NoContent();
    }

    private Guid ReviewerId() => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value
        ?? throw new InvalidOperationException("Authenticated user identifier is missing."));
}
