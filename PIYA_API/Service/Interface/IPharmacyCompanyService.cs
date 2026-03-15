using PIYA_API.Model;

namespace PIYA_API.Service.Interface;

public interface IPharmacyCompanyService
{
    Task<PharmacyCompany?> GetByIdAsync(Guid id);
    Task<List<PharmacyCompany>> GetAllAsync();
    Task<PharmacyCompany> CreateAsync(PharmacyCompany company);
    Task<PharmacyCompany> UpdateAsync(PharmacyCompany company);
    Task DeleteAsync(Guid id);
    Task<List<Pharmacy>> GetCompanyPharmaciesAsync(Guid companyId);
    Task<int> GetPharmacyCountAsync(Guid companyId);
    /// <summary>Get the company owned by a PharmacyNetworkOwner user. Returns null if none assigned.</summary>
    Task<PharmacyCompany?> GetByOwnerAsync(Guid ownerId);
    /// <summary>Assign or unassign (null) an owner to a company. User must have PharmacyNetworkOwner role.</summary>
    Task<PharmacyCompany> AssignOwnerAsync(Guid companyId, Guid? ownerId);
    /// <summary>Returns true when the given pharmacy belongs to the company owned by ownerId.</summary>
    Task<bool> IsPharmacyInOwnerNetworkAsync(Guid pharmacyId, Guid ownerId);
}
