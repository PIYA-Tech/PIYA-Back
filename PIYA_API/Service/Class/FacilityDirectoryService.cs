using Microsoft.EntityFrameworkCore;
using PIYA_API.Data;
using PIYA_API.DTOs;
using PIYA_API.Model;
using PIYA_API.Service.Interface;

namespace PIYA_API.Service.Class;

public sealed class FacilityDirectoryService(PharmacyApiDbContext context) : IFacilityDirectoryService
{
    private readonly PharmacyApiDbContext _context = context;

    public async Task<DirectoryFacilityPageDto> GetPublishedAsync(
        DirectoryFacilityKind? kind,
        string? city,
        string? query,
        bool verifiedOnly,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        pageNumber = Math.Max(1, pageNumber);
        pageSize = Math.Clamp(pageSize, 1, 1000);
        var facilities = _context.DirectoryFacilities.AsNoTracking()
            .Where(item => item.IsPublished && item.IsActive);

        if (kind.HasValue)
            facilities = facilities.Where(item => item.Kind == kind.Value);
        if (!string.IsNullOrWhiteSpace(city))
            facilities = facilities.Where(item => item.City.ToLower() == city.Trim().ToLower());
        if (verifiedOnly)
            facilities = facilities.Where(item =>
                item.VerificationStatus == FacilityVerificationStatus.RegistryVerified ||
                item.VerificationStatus == FacilityVerificationStatus.OwnerVerified ||
                item.VerificationStatus == FacilityVerificationStatus.Onboarded);
        if (!string.IsNullOrWhiteSpace(query))
        {
            var search = $"%{query.Trim()}%";
            facilities = facilities.Where(item =>
                EF.Functions.ILike(item.Name, search) ||
                (item.Address != null && EF.Functions.ILike(item.Address, search)) ||
                (item.District != null && EF.Functions.ILike(item.District, search)));
        }

        var total = await facilities.CountAsync(cancellationToken);
        var items = await facilities
            .OrderByDescending(item => item.VerificationStatus)
            .ThenBy(item => item.Name)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(item => ToPublicDto(item))
            .ToListAsync(cancellationToken);

        return new DirectoryFacilityPageDto
        {
            Items = items,
            TotalCount = total,
            PageNumber = pageNumber,
            PageSize = pageSize
        };
    }

    public async Task<DirectoryFacilityPublicDto?> GetPublishedByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default) =>
        await _context.DirectoryFacilities.AsNoTracking()
            .Where(item => item.Id == id && item.IsPublished && item.IsActive)
            .Select(item => ToPublicDto(item))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<AdminDirectoryFacilityPageDto> GetAdminAsync(
        DirectoryFacilityKind? kind,
        FacilityVerificationStatus? status,
        string? source,
        string? query,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        pageNumber = Math.Max(1, pageNumber);
        pageSize = Math.Clamp(pageSize, 1, 250);
        var facilities = _context.DirectoryFacilities.AsNoTracking().Include(item => item.Sources).AsQueryable();
        if (kind.HasValue)
            facilities = facilities.Where(item => item.Kind == kind.Value);
        if (status.HasValue)
            facilities = facilities.Where(item => item.VerificationStatus == status.Value);
        if (!string.IsNullOrWhiteSpace(source))
            facilities = facilities.Where(item => item.Sources.Any(record => record.SourceName == source));
        if (!string.IsNullOrWhiteSpace(query))
        {
            var search = $"%{query.Trim()}%";
            facilities = facilities.Where(item =>
                EF.Functions.ILike(item.Name, search) ||
                (item.Address != null && EF.Functions.ILike(item.Address, search)));
        }

        var total = await facilities.CountAsync(cancellationToken);
        var entities = await facilities.OrderByDescending(item => item.UpdatedAt).ThenBy(item => item.Name)
            .Skip((pageNumber - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return new AdminDirectoryFacilityPageDto
        {
            Items = entities.Select(ToAdminDto).ToList(),
            TotalCount = total,
            PageNumber = pageNumber,
            PageSize = pageSize
        };
    }

    public async Task<AdminDirectoryFacilityDto?> GetAdminByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var entity = await _context.DirectoryFacilities.AsNoTracking().Include(item => item.Sources)
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        return entity == null ? null : ToAdminDto(entity);
    }

    public async Task<AdminDirectoryFacilityDto> UpdateAsync(
        Guid id,
        UpdateDirectoryFacilityRequest request,
        CancellationToken cancellationToken = default)
    {
        var entity = await _context.DirectoryFacilities.Include(item => item.Sources)
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("Directory facility not found.");

        if (request.Kind.HasValue) entity.Kind = request.Kind.Value;
        if (request.Name != null)
        {
            entity.Name = RequireValue(request.Name, "Name");
            entity.NormalizedName = FacilityDirectoryNormalizer.Normalize(entity.Name);
        }
        if (request.LegalName != null) entity.LegalName = EmptyToNull(request.LegalName);
        if (request.Address != null)
        {
            entity.Address = EmptyToNull(request.Address);
            entity.NormalizedAddress = FacilityDirectoryNormalizer.NormalizeNullable(entity.Address);
        }
        if (request.City != null) entity.City = RequireValue(request.City, "City");
        if (request.Country != null) entity.Country = RequireValue(request.Country, "Country");
        if (request.District != null) entity.District = EmptyToNull(request.District);
        if (request.PhoneNumbers != null) entity.PhoneNumbers = CleanList(request.PhoneNumbers);
        if (request.Email != null) entity.Email = EmptyToNull(request.Email);
        if (request.Website != null) entity.Website = EmptyToNull(request.Website);
        if (request.OperatingHours != null) entity.OperatingHours = EmptyToNull(request.OperatingHours);
        if (request.Services != null) entity.Services = CleanList(request.Services);
        if (request.Coordinates != null)
        {
            ValidateCoordinates(request.Coordinates.Lat, request.Coordinates.Lng);
            entity.Latitude = request.Coordinates.Lat;
            entity.Longitude = request.Coordinates.Lng;
        }
        if (request.Ownership.HasValue) entity.Ownership = request.Ownership.Value;
        if (request.VerificationStatus.HasValue) entity.VerificationStatus = request.VerificationStatus.Value;
        if (request.LicenseNumber != null) entity.LicenseNumber = EmptyToNull(request.LicenseNumber);
        if (request.TaxId != null) entity.TaxId = EmptyToNull(request.TaxId);
        if (request.IsPublished.HasValue) entity.IsPublished = request.IsPublished.Value;
        if (request.IsActive.HasValue) entity.IsActive = request.IsActive.Value;
        if (request.OperationalHospitalId.HasValue)
            entity.OperationalHospitalId = request.OperationalHospitalId == Guid.Empty ? null : request.OperationalHospitalId;
        if (request.OperationalPharmacyId.HasValue)
            entity.OperationalPharmacyId = request.OperationalPharmacyId == Guid.Empty ? null : request.OperationalPharmacyId;
        entity.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
        return ToAdminDto(entity);
    }

    public async Task<FacilityClaimDto> SubmitClaimAsync(
        Guid facilityId,
        Guid userId,
        FacilityClaimRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!await _context.DirectoryFacilities.AnyAsync(item => item.Id == facilityId && item.IsActive, cancellationToken))
            throw new KeyNotFoundException("Directory facility not found.");
        if (await _context.FacilityClaims.AnyAsync(item =>
                item.FacilityId == facilityId && item.ClaimedByUserId == userId &&
                item.Status == FacilityClaimStatus.Pending, cancellationToken))
            throw new InvalidOperationException("You already have a pending claim for this facility.");

        var claim = new FacilityClaim
        {
            Id = Guid.NewGuid(),
            FacilityId = facilityId,
            ClaimedByUserId = userId,
            OrganizationName = RequireValue(request.OrganizationName, "Organization name"),
            LicenseNumber = EmptyToNull(request.LicenseNumber),
            TaxId = EmptyToNull(request.TaxId),
            ContactEmail = RequireValue(request.ContactEmail, "Contact email"),
            ContactPhone = EmptyToNull(request.ContactPhone),
            EvidenceNotes = EmptyToNull(request.EvidenceNotes)
        };
        _context.FacilityClaims.Add(claim);
        await _context.SaveChangesAsync(cancellationToken);
        claim.Facility = await _context.DirectoryFacilities.FindAsync([facilityId], cancellationToken)
            ?? throw new KeyNotFoundException("Directory facility not found.");
        return ToClaimDto(claim);
    }

    public async Task<List<FacilityClaimDto>> GetClaimsAsync(
        FacilityClaimStatus? status,
        CancellationToken cancellationToken = default)
    {
        var query = _context.FacilityClaims.AsNoTracking().Include(item => item.Facility).AsQueryable();
        if (status.HasValue) query = query.Where(item => item.Status == status.Value);
        return (await query.OrderByDescending(item => item.CreatedAt).Take(500).ToListAsync(cancellationToken))
            .Select(ToClaimDto).ToList();
    }

    public async Task<FacilityClaimDto> ReviewClaimAsync(
        Guid claimId,
        Guid reviewerId,
        ReviewFacilityClaimRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Status is not (FacilityClaimStatus.Approved or FacilityClaimStatus.Rejected))
            throw new InvalidOperationException("A claim can only be approved or rejected.");
        var claim = await _context.FacilityClaims.Include(item => item.Facility)
            .FirstOrDefaultAsync(item => item.Id == claimId, cancellationToken)
            ?? throw new KeyNotFoundException("Facility claim not found.");
        claim.Status = request.Status;
        claim.ReviewNotes = EmptyToNull(request.ReviewNotes);
        claim.ReviewedByUserId = reviewerId;
        claim.ReviewedAt = DateTime.UtcNow;
        claim.UpdatedAt = DateTime.UtcNow;
        if (request.Status == FacilityClaimStatus.Approved)
        {
            claim.Facility.VerificationStatus = FacilityVerificationStatus.OwnerVerified;
            claim.Facility.LastVerifiedAt = DateTime.UtcNow;
            claim.Facility.IsPublished = true;
            claim.Facility.UpdatedAt = DateTime.UtcNow;
        }
        await _context.SaveChangesAsync(cancellationToken);
        return ToClaimDto(claim);
    }

    public async Task<List<FacilityDuplicateDto>> GetDuplicatesAsync(
        FacilityDuplicateStatus? status,
        CancellationToken cancellationToken = default)
    {
        var query = _context.FacilityDuplicateCandidates.AsNoTracking()
            .Include(item => item.Facility).Include(item => item.PossibleDuplicate).AsQueryable();
        if (status.HasValue) query = query.Where(item => item.Status == status.Value);
        return (await query.OrderByDescending(item => item.ConfidenceScore).Take(500).ToListAsync(cancellationToken))
            .Select(ToDuplicateDto).ToList();
    }

    public async Task ReviewDuplicateAsync(
        Guid duplicateId,
        Guid reviewerId,
        bool merge,
        CancellationToken cancellationToken = default)
    {
        var duplicate = await _context.FacilityDuplicateCandidates
            .Include(item => item.Facility).ThenInclude(item => item.Sources)
            .Include(item => item.PossibleDuplicate).ThenInclude(item => item.Sources)
            .FirstOrDefaultAsync(item => item.Id == duplicateId, cancellationToken)
            ?? throw new KeyNotFoundException("Duplicate candidate not found.");
        if (duplicate.Status != FacilityDuplicateStatus.Pending)
            throw new InvalidOperationException("Duplicate candidate has already been reviewed.");

        duplicate.ReviewedByUserId = reviewerId;
        duplicate.ReviewedAt = DateTime.UtcNow;
        if (!merge)
        {
            duplicate.Status = FacilityDuplicateStatus.Distinct;
            await _context.SaveChangesAsync(cancellationToken);
            return;
        }

        var primary = PickPrimary(duplicate.Facility, duplicate.PossibleDuplicate);
        var secondary = primary.Id == duplicate.Facility.Id ? duplicate.PossibleDuplicate : duplicate.Facility;
        MergeMissing(primary, secondary);
        foreach (var source in secondary.Sources.ToList()) source.FacilityId = primary.Id;
        var claims = await _context.FacilityClaims.Where(item => item.FacilityId == secondary.Id).ToListAsync(cancellationToken);
        foreach (var claim in claims) claim.FacilityId = primary.Id;
        secondary.IsPublished = false;
        secondary.IsActive = false;
        secondary.VerificationStatus = FacilityVerificationStatus.Rejected;
        secondary.UpdatedAt = DateTime.UtcNow;
        primary.UpdatedAt = DateTime.UtcNow;
        duplicate.Status = FacilityDuplicateStatus.Merged;
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<List<FacilityImportRunDto>> GetImportRunsAsync(
        int count,
        CancellationToken cancellationToken = default) =>
        (await _context.FacilityImportRuns.AsNoTracking().OrderByDescending(item => item.StartedAt)
            .Take(Math.Clamp(count, 1, 200)).ToListAsync(cancellationToken)).Select(ToImportRunDto).ToList();

    public async Task<FacilityDirectoryStatsDto> GetStatsAsync(CancellationToken cancellationToken = default)
    {
        var published = _context.DirectoryFacilities.AsNoTracking().Where(item => item.IsPublished && item.IsActive);
        var sourceCounts = await _context.FacilitySourceRecords.AsNoTracking().Where(item => item.IsCurrent)
            .GroupBy(item => item.SourceName).Select(group => new { group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.Key, item => item.Count, cancellationToken);
        var kindCounts = await published.GroupBy(item => item.Kind)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.Key.ToString(), item => item.Count, cancellationToken);
        return new FacilityDirectoryStatsDto
        {
            PublishedFacilities = await published.CountAsync(cancellationToken),
            RegistryVerified = await published.CountAsync(item => item.VerificationStatus == FacilityVerificationStatus.RegistryVerified, cancellationToken),
            Onboarded = await published.CountAsync(item => item.VerificationStatus == FacilityVerificationStatus.Onboarded, cancellationToken),
            PendingClaims = await _context.FacilityClaims.CountAsync(item => item.Status == FacilityClaimStatus.Pending, cancellationToken),
            PendingDuplicates = await _context.FacilityDuplicateCandidates.CountAsync(item => item.Status == FacilityDuplicateStatus.Pending, cancellationToken),
            CountsByKind = kindCounts,
            CountsBySource = sourceCounts,
            LastSuccessfulSyncAt = await _context.FacilityImportRuns.AsNoTracking()
                .Where(item => item.Status == FacilityImportStatus.Succeeded && !item.DryRun)
                .MaxAsync(item => (DateTime?)item.CompletedAt, cancellationToken)
        };
    }

    private static DirectoryFacilityPublicDto ToPublicDto(DirectoryFacility item) => new()
    {
        Id = item.Id,
        Kind = item.Kind,
        Name = item.Name,
        Address = item.Address,
        City = item.City,
        Country = item.Country,
        District = item.District,
        PhoneNumbers = item.PhoneNumbers,
        Email = item.Email,
        Website = item.Website,
        OperatingHours = item.OperatingHours,
        Services = item.Services,
        Coordinates = item.Latitude.HasValue && item.Longitude.HasValue
            ? new CoordinatesDto { Lat = item.Latitude.Value, Lng = item.Longitude.Value }
            : null,
        Ownership = item.Ownership,
        VerificationStatus = item.VerificationStatus,
        IsOnboarded = item.VerificationStatus == FacilityVerificationStatus.Onboarded ||
                      item.OperationalHospitalId.HasValue || item.OperationalPharmacyId.HasValue,
        PrimarySourceName = item.PrimarySourceName,
        Attribution = item.PrimarySourceName switch
        {
            FacilityDirectorySources.OpenStreetMap => "© OpenStreetMap contributors (ODbL)",
            FacilityDirectorySources.PiyaOperational => "PIYA connected provider",
            _ => "Official Azerbaijan open data"
        },
        LastVerifiedAt = item.LastVerifiedAt
    };

    private static AdminDirectoryFacilityDto ToAdminDto(DirectoryFacility item)
    {
        var publicDto = ToPublicDto(item);
        return new AdminDirectoryFacilityDto
        {
            Id = publicDto.Id, Kind = publicDto.Kind, Name = publicDto.Name, Address = publicDto.Address,
            City = publicDto.City, Country = publicDto.Country, District = publicDto.District,
            PhoneNumbers = publicDto.PhoneNumbers, Email = publicDto.Email, Website = publicDto.Website,
            OperatingHours = publicDto.OperatingHours, Services = publicDto.Services,
            Coordinates = publicDto.Coordinates, Ownership = publicDto.Ownership,
            VerificationStatus = publicDto.VerificationStatus, IsOnboarded = publicDto.IsOnboarded,
            PrimarySourceName = publicDto.PrimarySourceName, Attribution = publicDto.Attribution,
            LastVerifiedAt = publicDto.LastVerifiedAt, LegalName = item.LegalName,
            LicenseNumber = item.LicenseNumber, TaxId = item.TaxId, IsPublished = item.IsPublished,
            IsActive = item.IsActive, OperationalHospitalId = item.OperationalHospitalId,
            OperationalPharmacyId = item.OperationalPharmacyId, FirstSeenAt = item.FirstSeenAt,
            LastSeenAt = item.LastSeenAt, UpdatedAt = item.UpdatedAt,
            Sources = item.Sources.Select(source => new FacilitySourceDto
            {
                Id = source.Id, SourceName = source.SourceName, ExternalId = source.ExternalId,
                SourceUrl = source.SourceUrl, DataLicense = source.DataLicense,
                IsCurrent = source.IsCurrent, LastSeenAt = source.LastSeenAt
            }).ToList()
        };
    }

    private static FacilityClaimDto ToClaimDto(FacilityClaim item) => new()
    {
        Id = item.Id, FacilityId = item.FacilityId, FacilityName = item.Facility.Name,
        ClaimedByUserId = item.ClaimedByUserId, OrganizationName = item.OrganizationName,
        LicenseNumber = item.LicenseNumber, TaxId = item.TaxId, ContactEmail = item.ContactEmail,
        ContactPhone = item.ContactPhone, EvidenceNotes = item.EvidenceNotes, Status = item.Status,
        ReviewNotes = item.ReviewNotes, CreatedAt = item.CreatedAt, ReviewedAt = item.ReviewedAt
    };

    internal static FacilityImportRunDto ToImportRunDto(FacilityImportRun item) => new()
    {
        Id = item.Id, SourceName = item.SourceName, Status = item.Status, DryRun = item.DryRun,
        RecordsRead = item.RecordsRead, RecordsCreated = item.RecordsCreated,
        RecordsUpdated = item.RecordsUpdated, RecordsMatched = item.RecordsMatched,
        DuplicateCandidates = item.DuplicateCandidates, RecordsSkipped = item.RecordsSkipped,
        Error = item.Error, StartedAt = item.StartedAt, CompletedAt = item.CompletedAt
    };

    private static FacilityDuplicateDto ToDuplicateDto(FacilityDuplicateCandidate item) => new()
    {
        Id = item.Id, FacilityId = item.FacilityId, FacilityName = item.Facility.Name,
        PossibleDuplicateId = item.PossibleDuplicateId,
        PossibleDuplicateName = item.PossibleDuplicate.Name, ConfidenceScore = item.ConfidenceScore,
        Reason = item.Reason, Status = item.Status, CreatedAt = item.CreatedAt
    };

    private static DirectoryFacility PickPrimary(DirectoryFacility first, DirectoryFacility second) =>
        Score(first) >= Score(second) ? first : second;

    private static int Score(DirectoryFacility item) =>
        VerificationRank(item.VerificationStatus) * 100 + item.Sources.Count * 10 +
        (item.Latitude.HasValue ? 2 : 0) + (item.Address != null ? 1 : 0);

    private static void MergeMissing(DirectoryFacility primary, DirectoryFacility secondary)
    {
        primary.Address ??= secondary.Address;
        primary.NormalizedAddress ??= secondary.NormalizedAddress;
        primary.District ??= secondary.District;
        primary.Email ??= secondary.Email;
        primary.Website ??= secondary.Website;
        primary.OperatingHours ??= secondary.OperatingHours;
        primary.LicenseNumber ??= secondary.LicenseNumber;
        primary.TaxId ??= secondary.TaxId;
        primary.Latitude ??= secondary.Latitude;
        primary.Longitude ??= secondary.Longitude;
        primary.OperationalHospitalId ??= secondary.OperationalHospitalId;
        primary.OperationalPharmacyId ??= secondary.OperationalPharmacyId;
        primary.PhoneNumbers = primary.PhoneNumbers.Union(secondary.PhoneNumbers).Distinct().ToList();
        primary.Services = primary.Services.Union(secondary.Services).Distinct().ToList();
        primary.IsPublished |= secondary.IsPublished;
        primary.IsActive |= secondary.IsActive;
        if (VerificationRank(secondary.VerificationStatus) > VerificationRank(primary.VerificationStatus))
            primary.VerificationStatus = secondary.VerificationStatus;
    }

    private static int VerificationRank(FacilityVerificationStatus status) => status switch
    {
        FacilityVerificationStatus.Rejected => -1,
        FacilityVerificationStatus.Discovered => 0,
        FacilityVerificationStatus.RegistryVerified => 1,
        FacilityVerificationStatus.OwnerVerified => 2,
        FacilityVerificationStatus.Onboarded => 3,
        _ => 0
    };

    private static string RequireValue(string value, string field) =>
        string.IsNullOrWhiteSpace(value) ? throw new InvalidOperationException($"{field} is required.") : value.Trim();
    private static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static List<string> CleanList(IEnumerable<string> values) => values.Where(value => !string.IsNullOrWhiteSpace(value))
        .Select(value => value.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    private static void ValidateCoordinates(double lat, double lng)
    {
        if (lat is < -90 or > 90 || lng is < -180 or > 180)
            throw new InvalidOperationException("Coordinates are outside valid latitude/longitude ranges.");
    }
}
