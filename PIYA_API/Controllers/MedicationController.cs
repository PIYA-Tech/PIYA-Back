using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PIYA_API.DTOs;
using PIYA_API.Model;
using PIYA_API.Service.Interface;

namespace PIYA_API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class MedicationController(IMedicationService medicationService, ILogger<MedicationController> logger) : ControllerBase
{
    private readonly IMedicationService _medicationService = medicationService;
    private readonly ILogger<MedicationController> _logger = logger;

    /// <summary>
    /// Search medications by name (public access for pharmacy search)
    /// </summary>
    [HttpGet("search")]
    [AllowAnonymous]
    public async Task<ActionResult<List<Medication>>> Search([FromQuery] string query)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(query) || query.Length < 2)
            {
                return BadRequest(new { error = "Search query must be at least 2 characters" });
            }

            var results = await _medicationService.SearchByNameAsync(query);
            return Ok(results);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching medications");
            return StatusCode(500, new { error = "Failed to search medications" });
        }
    }

    /// <summary>
    /// Get all available medications (public, paginated, max 200 per page)
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? search = null,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 50)
    {
        try
        {
            pageSize = Math.Min(pageSize, 200);
            var (items, total) = await _medicationService.GetAllAdminAsync(
                search, null, true /* availableOnly */, pageNumber, pageSize);
            return Ok(new
            {
                items,
                totalCount = total,
                pageNumber,
                pageSize,
                totalPages = (int)Math.Ceiling(total / (double)pageSize),
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving medications");
            return StatusCode(500, new { error = "Failed to retrieve medications" });
        }
    }

    /// <summary>
    /// Get all medications for admin — no availability filter, supports search/filter/paging
    /// </summary>
    [HttpGet("admin")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> GetAllAdmin(
        [FromQuery] string? search = null,
        [FromQuery] bool? requiresPrescription = null,
        [FromQuery] bool? isAvailable = null,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 50)
    {
        try
        {
            pageSize = Math.Min(pageSize, 200);
            var (items, total) = await _medicationService.GetAllAdminAsync(
                search, requiresPrescription, isAvailable, pageNumber, pageSize);

            return Ok(new
            {
                items,
                totalCount = total,
                pageNumber,
                pageSize,
                totalPages = (int)Math.Ceiling(total / (double)pageSize),
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving medications (admin)");
            return StatusCode(500, new { error = "Failed to retrieve medications" });
        }
    }

    /// <summary>
    /// Get medication by ID
    /// </summary>
    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    public async Task<ActionResult<Medication>> GetById(Guid id)
    {
        try
        {
            var medication = await _medicationService.GetByIdAsync(id);
            if (medication == null)
            {
                return NotFound(new { error = "Medication not found" });
            }

            return Ok(medication);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving medication {MedicationId}", id);
            return StatusCode(500, new { error = "Failed to retrieve medication" });
        }
    }

    /// <summary>
    /// Create new medication (Admin only)
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<ActionResult<Medication>> Create([FromBody] MedicationUpsertDto request)
    {
        try
        {
            var medication = ToMedication(request);
            var created = await _medicationService.CreateAsync(medication);
            return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating medication");
            return StatusCode(500, new { error = "Failed to create medication" });
        }
    }

    /// <summary>
    /// Search by active ingredient
    /// </summary>
    [HttpGet("ingredient/{ingredient}")]
    [AllowAnonymous]
    public async Task<ActionResult<List<Medication>>> SearchByIngredient(string ingredient)
    {
        try
        {
            var results = await _medicationService.SearchByIngredientAsync(ingredient);
            return Ok(results);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching by ingredient");
            return StatusCode(500, new { error = "Failed to search by ingredient" });
        }
    }

    /// <summary>
    /// Update medication — Admin only
    /// </summary>
    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<ActionResult<Medication>> Update(Guid id, [FromBody] MedicationUpsertDto request)
    {
        try
        {
            var medication = ToMedication(request);
            medication.Id = id;
            var updated = await _medicationService.UpdateAsync(medication);
            return Ok(updated);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { error = "Medication not found" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating medication {MedicationId}", id);
            return StatusCode(500, new { error = "Failed to update medication" });
        }
    }

    /// <summary>
    /// Soft-delete (deactivate) a medication — Admin only
    /// </summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> Delete(Guid id)
    {
        try
        {
            var success = await _medicationService.DeleteAsync(id);
            if (!success)
                return NotFound(new { error = "Medication not found" });

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting medication {MedicationId}", id);
            return StatusCode(500, new { error = "Failed to delete medication" });
        }
    }

    private static Medication ToMedication(MedicationUpsertDto request) => new()
    {
        BrandName = request.BrandName.Trim(),
        GenericName = request.GenericName.Trim(),
        ActiveIngredients = request.ActiveIngredients
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList(),
        AtcCode = request.AtcCode?.Trim(),
        Form = request.Form.Trim(),
        Strength = request.Strength.Trim(),
        Manufacturer = request.Manufacturer?.Trim(),
        RequiresPrescription = request.RequiresPrescription,
        IsControlledSubstance = request.IsControlledSubstance,
        GenericAlternatives = request.GenericAlternatives,
        Usage = request.Usage?.Trim(),
        SideEffects = request.SideEffects?.Trim(),
        Contraindications = request.Contraindications?.Trim(),
        IsAvailable = request.IsAvailable,
        Country = request.Country.Trim(),
        Barcode = request.Barcode?.Trim(),
    };
}
