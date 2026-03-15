namespace PIYA_API.Model;

public class PharmacyCompany
{
    public Guid Id { get; set; }
    public required string Name { get; set; }

    /// <summary>
    /// The PharmacyNetworkOwner user who owns this pharmacy company/chain.
    /// Null when the company is not yet linked to a registered owner.
    /// </summary>
    public Guid? OwnerId { get; set; }
    public User? Owner { get; set; }

    public List<Pharmacy>? Pharmacies { get; set; }
    public List<User>? Staff { get; set; }
}
