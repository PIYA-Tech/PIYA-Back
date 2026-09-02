using Microsoft.EntityFrameworkCore;
using PIYA_API.Data;
using PIYA_API.Model;
using PIYA_API.Service.Interface;

namespace PIYA_API.Service.Class;

public sealed class CareCircleAccessService(PharmacyApiDbContext db) : ICareCircleAccessService
{
    private readonly PharmacyApiDbContext _db = db;

    public async Task<bool> HasScopeAsync(
        Guid patientId,
        Guid memberUserId,
        CareCircleScope requiredScope,
        CancellationToken cancellationToken = default)
    {
        if (requiredScope == CareCircleScope.None) return false;
        if (patientId == memberUserId) return true;
        var now = DateTime.UtcNow;
        var member = await _db.Set<CareCircleMember>().AsNoTracking().SingleOrDefaultAsync(item =>
            item.PatientId == patientId && item.MemberUserId == memberUserId &&
            item.Status == CareCircleMemberStatus.Active && item.ExpiresAt > now,
            cancellationToken);
        if (member is null) return false;
        return await _db.Set<CareCircleConsent>().AsNoTracking().AnyAsync(item =>
            item.PatientId == patientId && item.MemberId == member.Id &&
            item.Status == CareCircleConsentStatus.Active && item.ExpiresAt > now &&
            (item.Scopes & requiredScope) == requiredScope,
            cancellationToken);
    }
}
