using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PIYA_API.DTOs;
using PIYA_API.Model;
using PIYA_API.Service.Interface;

namespace PIYA_API.Controllers;

[ApiController]
[Route("api/directory/facilities")]
public sealed class FacilityDirectoryController(IFacilityDirectoryService service) : ControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<DirectoryFacilityPageDto>> GetAll(
        [FromQuery] DirectoryFacilityKind? kind = null,
        [FromQuery] string? city = "Baku",
        [FromQuery(Name = "q")] string? query = null,
        [FromQuery] bool verifiedOnly = false,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 250,
        CancellationToken cancellationToken = default) =>
        Ok(await service.GetPublishedAsync(kind, city, query, verifiedOnly, pageNumber, pageSize, cancellationToken));

    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    public async Task<ActionResult<DirectoryFacilityPublicDto>> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var facility = await service.GetPublishedByIdAsync(id, cancellationToken);
        return facility == null ? NotFound(new { error = "Facility not found" }) : Ok(facility);
    }

    [HttpGet("stats")]
    [AllowAnonymous]
    public async Task<ActionResult<FacilityDirectoryStatsDto>> GetStats(CancellationToken cancellationToken) =>
        Ok(await service.GetStatsAsync(cancellationToken));

    [HttpPost("{id:guid}/claims")]
    [Authorize]
    public async Task<ActionResult<FacilityClaimDto>> Claim(
        Guid id,
        [FromBody] FacilityClaimRequest request,
        CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? throw new InvalidOperationException("Authenticated user identifier is missing."));
        var result = await service.SubmitClaimAsync(id, userId, request, cancellationToken);
        return Created($"/api/directory/facilities/{id}", result);
    }
}
