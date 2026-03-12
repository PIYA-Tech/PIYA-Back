using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PIYA_API.Model;
using PIYA_API.Service.Interface;

namespace PIYA_API.Controllers;

[ApiController]
[Route("api/pharmacy-companies")]
public class PharmacyCompaniesController(
    IPharmacyCompanyService companyService,
    ILogger<PharmacyCompaniesController> logger) : ControllerBase
{
    private readonly IPharmacyCompanyService _companyService = companyService;
    private readonly ILogger<PharmacyCompaniesController> _logger = logger;

    /// <summary>
    /// Get all pharmacy companies
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<PharmacyCompanyDto>>> GetAll()
    {
        try
        {
            var companies = await _companyService.GetAllAsync();
            var dtos = new List<PharmacyCompanyDto>();

            foreach (var company in companies)
            {
                var pharmacyCount = await _companyService.GetPharmacyCountAsync(company.Id);
                dtos.Add(new PharmacyCompanyDto
                {
                    Id = company.Id,
                    Name = company.Name,
                    PharmacyCount = pharmacyCount
                });
            }

            return Ok(dtos);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving pharmacy companies");
            return StatusCode(500, new { error = "Failed to retrieve pharmacy companies" });
        }
    }

    /// <summary>
    /// Get pharmacy company by ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<PharmacyCompanyDetailDto>> GetById(Guid id)
    {
        try
        {
            var company = await _companyService.GetByIdAsync(id);
            if (company == null)
            {
                return NotFound(new { error = "Pharmacy company not found" });
            }

            var pharmacies = await _companyService.GetCompanyPharmaciesAsync(id);

            return Ok(new PharmacyCompanyDetailDto
            {
                Id = company.Id,
                Name = company.Name,
                PharmacyCount = pharmacies.Count,
                Pharmacies = pharmacies.Select(p => new PharmacyBasicDto
                {
                    Id = p.Id,
                    Name = p.Name,
                    Address = p.Address,
                    City = null, // City info not available in Coordinates model
                    Country = p.Country
                }).ToList()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving pharmacy company {CompanyId}", id);
            return StatusCode(500, new { error = "Failed to retrieve pharmacy company" });
        }
    }

    /// <summary>
    /// Create a new pharmacy company (Admin only)
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<ActionResult<PharmacyCompanyDto>> Create([FromBody] CreatePharmacyCompanyRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return BadRequest(new { error = "Company name is required" });
            }

            var company = new PharmacyCompany
            {
                Name = request.Name.Trim()
            };

            var created = await _companyService.CreateAsync(company);

            return CreatedAtAction(
                nameof(GetById),
                new { id = created.Id },
                new PharmacyCompanyDto
                {
                    Id = created.Id,
                    Name = created.Name,
                    PharmacyCount = 0
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating pharmacy company");
            return StatusCode(500, new { error = "Failed to create pharmacy company" });
        }
    }

    /// <summary>
    /// Update pharmacy company (Admin only)
    /// </summary>
    [HttpPut("{id}")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<ActionResult<PharmacyCompanyDto>> Update(Guid id, [FromBody] UpdatePharmacyCompanyRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return BadRequest(new { error = "Company name is required" });
            }

            var company = await _companyService.GetByIdAsync(id);
            if (company == null)
            {
                return NotFound(new { error = "Pharmacy company not found" });
            }

            company.Name = request.Name.Trim();
            var updated = await _companyService.UpdateAsync(company);
            var pharmacyCount = await _companyService.GetPharmacyCountAsync(id);

            return Ok(new PharmacyCompanyDto
            {
                Id = updated.Id,
                Name = updated.Name,
                PharmacyCount = pharmacyCount
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating pharmacy company {CompanyId}", id);
            return StatusCode(500, new { error = "Failed to update pharmacy company" });
        }
    }

    /// <summary>
    /// Delete pharmacy company (Admin only)
    /// </summary>
    [HttpDelete("{id}")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<ActionResult> Delete(Guid id)
    {
        try
        {
            await _companyService.DeleteAsync(id);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting pharmacy company {CompanyId}", id);
            return StatusCode(500, new { error = "Failed to delete pharmacy company" });
        }
    }

    /// <summary>
    /// Get all pharmacies for a company
    /// </summary>
    [HttpGet("{id}/pharmacies")]
    public async Task<ActionResult<List<PharmacyBasicDto>>> GetCompanyPharmacies(Guid id)
    {
        try
        {
            var company = await _companyService.GetByIdAsync(id);
            if (company == null)
            {
                return NotFound(new { error = "Pharmacy company not found" });
            }

            var pharmacies = await _companyService.GetCompanyPharmaciesAsync(id);

            return Ok(pharmacies.Select(p => new PharmacyBasicDto
            {
                Id = p.Id,
                Name = p.Name,
                Address = p.Address,
                City = null, // City info not available in Coordinates model
                Country = p.Country
            }).ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving pharmacies for company {CompanyId}", id);
            return StatusCode(500, new { error = "Failed to retrieve pharmacies" });
        }
    }
}

#region DTOs

public class PharmacyCompanyDto
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public int PharmacyCount { get; set; }
}

public class PharmacyCompanyDetailDto
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public int PharmacyCount { get; set; }
    public List<PharmacyBasicDto> Pharmacies { get; set; } = [];
}

public class PharmacyBasicDto
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? Country { get; set; }
}

public class CreatePharmacyCompanyRequest
{
    public required string Name { get; set; }
}

public class UpdatePharmacyCompanyRequest
{
    public required string Name { get; set; }
}

#endregion
