using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PIYA_API.Configuration;
using PIYA_API.Data;
using PIYA_API.DTOs;
using PIYA_API.Model;
using PIYA_API.Service.Interface;

namespace PIYA_API.Service.Class;

public static class FacilityDirectorySources
{
    public const string TabibMain = "tabib-main";
    public const string TabibSubordinate = "tabib-subordinate";
    public const string ItsPrivate = "its-private";
    public const string OpenStreetMap = "openstreetmap";
}

public static class FacilityDirectoryNormalizer
{
    public static string Normalize(string value)
    {
        var text = value.Trim().ToLowerInvariant()
            .Replace('ə', 'e').Replace('ı', 'i').Replace('ş', 's')
            .Replace('ç', 'c').Replace('ö', 'o').Replace('ü', 'u').Replace('ğ', 'g');
        text = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(text.Length);
        var previousSpace = false;
        foreach (var character in text)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
                continue;
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
                previousSpace = false;
            }
            else if (!previousSpace && builder.Length > 0)
            {
                builder.Append(' ');
                previousSpace = true;
            }
        }
        return builder.ToString().Trim();
    }

    public static string? NormalizeNullable(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : Normalize(value);
}

internal sealed record FacilityImportCandidate(
    string SourceName,
    string ExternalId,
    string SourceUrl,
    string? DataLicense,
    DirectoryFacilityKind Kind,
    string Name,
    string? Address,
    string? District,
    IReadOnlyList<string> PhoneNumbers,
    string? Email,
    string? Website,
    string? OperatingHours,
    double? Latitude,
    double? Longitude,
    FacilityOwnershipType Ownership,
    FacilityVerificationStatus VerificationStatus,
    string? ParentName,
    string RawPayload);

public sealed class FacilityDirectorySyncService(
    PharmacyApiDbContext context,
    IHttpClientFactory clientFactory,
    IOptions<FacilityDirectoryOptions> options,
    ILogger<FacilityDirectorySyncService> logger) : IFacilityDirectorySyncService
{
    private static readonly SemaphoreSlim SyncLock = new(1, 1);
    private static readonly string[] Sources =
    [
        FacilityDirectorySources.TabibMain,
        FacilityDirectorySources.TabibSubordinate,
        FacilityDirectorySources.ItsPrivate,
        FacilityDirectorySources.OpenStreetMap
    ];

    private readonly PharmacyApiDbContext _context = context;
    private readonly IHttpClientFactory _clientFactory = clientFactory;
    private readonly FacilityDirectoryOptions _options = options.Value;
    private readonly ILogger<FacilityDirectorySyncService> _logger = logger;

    public IReadOnlyCollection<string> AvailableSources => Sources;

    public async Task<List<FacilityImportRunDto>> SyncAsync(
        bool dryRun,
        IReadOnlyCollection<string>? requestedSources = null,
        CancellationToken cancellationToken = default)
    {
        var selected = requestedSources is { Count: > 0 }
            ? requestedSources.Select(value => value.Trim().ToLowerInvariant()).Distinct().ToArray()
            : Sources;
        var unknown = selected.Except(Sources, StringComparer.OrdinalIgnoreCase).ToArray();
        if (unknown.Length > 0)
            throw new InvalidOperationException($"Unknown facility sources: {string.Join(", ", unknown)}.");

        if (!await SyncLock.WaitAsync(0, cancellationToken))
            throw new InvalidOperationException("A facility directory sync is already running.");

        try
        {
            var results = new List<FacilityImportRunDto>();
            foreach (var source in selected)
                results.Add(await SyncSourceAsync(source, dryRun, cancellationToken));
            return results;
        }
        finally
        {
            SyncLock.Release();
        }
    }

    private async Task<FacilityImportRunDto> SyncSourceAsync(
        string source,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        var run = new FacilityImportRun
        {
            Id = Guid.NewGuid(),
            SourceName = source,
            DryRun = dryRun
        };
        _context.FacilityImportRuns.Add(run);
        await _context.SaveChangesAsync(cancellationToken);

        try
        {
            var candidates = source switch
            {
                FacilityDirectorySources.TabibMain => await ReadCkanAsync(
                    source, "tebib-tabeli-tibb-muessiseleri", MapTabibMain, cancellationToken),
                FacilityDirectorySources.TabibSubordinate => await ReadCkanAsync(
                    source, "tibb-muessiseleri-ve-alt-muessiseler", MapTabibSubordinate, cancellationToken),
                FacilityDirectorySources.ItsPrivate => await ReadCkanAsync(
                    source, "itsda-ile-muqavile-olan-ozel-tibb-muessiseleri", MapItsPrivate, cancellationToken),
                FacilityDirectorySources.OpenStreetMap => await ReadOpenStreetMapAsync(cancellationToken),
                _ => []
            };
            run.RecordsRead = candidates.Count;
            await UpsertAsync(candidates, run, dryRun, cancellationToken);
            run.Status = FacilityImportStatus.Succeeded;
            run.CompletedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Facility directory import failed for {Source}", source);
            run.Status = FacilityImportStatus.Failed;
            run.Error = exception.Message.Length > 2000 ? exception.Message[..2000] : exception.Message;
            run.CompletedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);
        }

        return FacilityDirectoryService.ToImportRunDto(run);
    }

    private async Task<List<FacilityImportCandidate>> ReadCkanAsync(
        string sourceName,
        string packageId,
        Func<IReadOnlyDictionary<string, string>, string, string, FacilityImportCandidate?> mapper,
        CancellationToken cancellationToken)
    {
        var client = _clientFactory.CreateClient("FacilityDirectory");
        var metadataUrl = $"{_options.CkanPackageApiUrl}?id={Uri.EscapeDataString(packageId)}";
        using var metadataResponse = await client.GetAsync(metadataUrl, cancellationToken);
        metadataResponse.EnsureSuccessStatusCode();
        await using var metadataStream = await metadataResponse.Content.ReadAsStreamAsync(cancellationToken);
        using var metadata = await JsonDocument.ParseAsync(metadataStream, cancellationToken: cancellationToken);
        if (!metadata.RootElement.GetProperty("success").GetBoolean())
            throw new InvalidOperationException($"CKAN did not return package '{packageId}'.");
        var resource = metadata.RootElement.GetProperty("result").GetProperty("resources")
            .EnumerateArray()
            .Where(item => string.Equals(item.GetProperty("format").GetString(), "CSV", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.TryGetProperty("last_modified", out var modified) ? modified.GetString() : string.Empty)
            .FirstOrDefault();
        if (resource.ValueKind == JsonValueKind.Undefined)
            throw new InvalidOperationException($"CKAN package '{packageId}' has no CSV resource.");
        var sourceUrl = resource.GetProperty("url").GetString()
            ?? throw new InvalidOperationException($"CKAN package '{packageId}' has an invalid resource URL.");

        using var csvResponse = await client.GetAsync(sourceUrl, cancellationToken);
        csvResponse.EnsureSuccessStatusCode();
        var csv = await csvResponse.Content.ReadAsStringAsync(cancellationToken);
        var rows = CsvRows.Parse(csv);
        var result = new List<FacilityImportCandidate>();
        foreach (var row in rows)
        {
            var candidate = mapper(row, sourceName, sourceUrl);
            if (candidate != null) result.Add(candidate);
        }
        return result.GroupBy(item => item.ExternalId).Select(group => group.First()).ToList();
    }

    private async Task<List<FacilityImportCandidate>> ReadOpenStreetMapAsync(CancellationToken cancellationToken)
    {
        const string query = """
            [out:json][timeout:120];
            (
              nwr["amenity"="pharmacy"](40.2983937,49.7597276,40.4413231,50.0013381);
              nwr["amenity"~"^(hospital|clinic)$"](40.2983937,49.7597276,40.4413231,50.0013381);
              nwr["healthcare"~"^(hospital|clinic|laboratory)$"](40.2983937,49.7597276,40.4413231,50.0013381);
            );
            out center tags;
            """;
        var client = _clientFactory.CreateClient("FacilityDirectory");
        using var content = new FormUrlEncodedContent(new Dictionary<string, string> { ["data"] = query });
        using var response = await client.PostAsync(_options.OverpassApiUrl, content, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var result = new List<FacilityImportCandidate>();
        foreach (var element in document.RootElement.GetProperty("elements").EnumerateArray())
        {
            if (!element.TryGetProperty("tags", out var tags)) continue;
            var name = Tag(tags, "name") ?? Tag(tags, "name:az") ?? Tag(tags, "operator");
            if (string.IsNullOrWhiteSpace(name)) continue;
            var latitude = Number(element, "lat") ?? CenterNumber(element, "lat");
            var longitude = Number(element, "lon") ?? CenterNumber(element, "lon");
            if (!latitude.HasValue || !longitude.HasValue) continue;
            var type = element.GetProperty("type").GetString() ?? "element";
            var id = element.GetProperty("id").GetInt64();
            var amenity = Tag(tags, "amenity");
            var healthcare = Tag(tags, "healthcare");
            var kind = ClassifyKind(name, amenity == "pharmacy" ? "pharmacy" : healthcare ?? amenity);
            var address = Tag(tags, "addr:full") ?? BuildOsmAddress(tags);
            var phones = SplitPhones(Tag(tags, "contact:phone") ?? Tag(tags, "phone"));
            var raw = element.GetRawText();
            result.Add(new FacilityImportCandidate(
                FacilityDirectorySources.OpenStreetMap,
                $"{type}/{id}",
                $"https://www.openstreetmap.org/{type}/{id}",
                "Open Database License (ODbL)",
                kind,
                name.Trim(),
                EmptyToNull(address),
                EmptyToNull(Tag(tags, "addr:district")),
                phones,
                EmptyToNull(Tag(tags, "contact:email") ?? Tag(tags, "email")),
                EmptyToNull(Tag(tags, "contact:website") ?? Tag(tags, "website")),
                EmptyToNull(Tag(tags, "opening_hours")),
                latitude,
                longitude,
                FacilityOwnershipType.Unknown,
                FacilityVerificationStatus.Discovered,
                null,
                raw));
        }
        return result.GroupBy(item => item.ExternalId).Select(group => group.First()).ToList();
    }

    private async Task UpsertAsync(
        IReadOnlyCollection<FacilityImportCandidate> candidates,
        FacilityImportRun run,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var existingSources = await _context.FacilitySourceRecords.Include(item => item.Facility)
            .Where(item => item.SourceName == run.SourceName).ToListAsync(cancellationToken);
        var byExternalId = existingSources.ToDictionary(item => item.ExternalId, StringComparer.OrdinalIgnoreCase);
        var facilities = await _context.DirectoryFacilities.Include(item => item.Sources)
            .Where(item => item.City == "Baku" && item.IsActive).ToListAsync(cancellationToken);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!dryRun && run.SourceName == FacilityDirectorySources.OpenStreetMap)
            RepairMergedOpenStreetMapLocations(candidates, existingSources, facilities, run, now);

        foreach (var candidate in candidates)
        {
            seen.Add(candidate.ExternalId);
            var hash = Sha256(candidate.RawPayload);
            if (byExternalId.TryGetValue(candidate.ExternalId, out var source))
            {
                run.RecordsMatched++;
                var changed = source.PayloadHash != hash;
                if (changed) run.RecordsUpdated++;
                if (!dryRun)
                {
                    source.PayloadHash = hash;
                    source.RawName = candidate.Name;
                    source.RawAddress = candidate.Address;
                    source.RawPayload = candidate.RawPayload;
                    source.SourceUrl = candidate.SourceUrl;
                    source.DataLicense = candidate.DataLicense;
                    source.IsCurrent = true;
                    source.LastSeenAt = now;
                    ApplyCandidate(source.Facility, candidate, changed);
                }
                continue;
            }

            var match = FindBestMatch(facilities, candidate);
            DirectoryFacility facility;
            if (match.Score >= 92)
            {
                facility = match.Facility!;
                run.RecordsMatched++;
                if (!dryRun) ApplyCandidate(facility, candidate, false);
            }
            else
            {
                facility = CreateFacility(candidate, now);
                run.RecordsCreated++;
                if (!dryRun)
                {
                    _context.DirectoryFacilities.Add(facility);
                    facilities.Add(facility);
                }
            }

            if (!dryRun)
            {
                var record = new FacilitySourceRecord
                {
                    Id = Guid.NewGuid(),
                    Facility = facility,
                    SourceName = candidate.SourceName,
                    ExternalId = candidate.ExternalId,
                    SourceUrl = candidate.SourceUrl,
                    DataLicense = candidate.DataLicense,
                    PayloadHash = hash,
                    RawName = candidate.Name,
                    RawAddress = candidate.Address,
                    RawPayload = candidate.RawPayload,
                    FirstSeenAt = now,
                    LastSeenAt = now
                };
                _context.FacilitySourceRecords.Add(record);
                facility.Sources.Add(record);
            }

            if (match.Facility != null && match.Score is >= 82 and < 92 && match.Facility.Id != facility.Id)
            {
                run.DuplicateCandidates++;
                if (!dryRun)
                    AddDuplicateCandidate(facility, match.Facility, match.Score, match.Reason, now);
            }
        }

        if (!dryRun)
        {
            var staleFacilities = new HashSet<DirectoryFacility>();
            foreach (var oldSource in existingSources.Where(item => !seen.Contains(item.ExternalId)))
            {
                oldSource.IsCurrent = false;
                staleFacilities.Add(oldSource.Facility);
            }
            foreach (var staleFacility in staleFacilities.Where(item => item.Sources.All(source => !source.IsCurrent)))
            {
                staleFacility.IsActive = false;
                staleFacility.IsPublished = false;
                staleFacility.UpdatedAt = now;
            }
            await _context.FacilityDuplicateCandidates
                .Where(item => item.Status == FacilityDuplicateStatus.Pending && item.ConfidenceScore < 82)
                .ExecuteUpdateAsync(update => update.SetProperty(
                    item => item.Status,
                    FacilityDuplicateStatus.Dismissed), cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    private void AddDuplicateCandidate(
        DirectoryFacility facility,
        DirectoryFacility possibleDuplicate,
        int score,
        string reason,
        DateTime now)
    {
        var firstId = facility.Id.CompareTo(possibleDuplicate.Id) < 0 ? facility.Id : possibleDuplicate.Id;
        var secondId = firstId == facility.Id ? possibleDuplicate.Id : facility.Id;
        if (_context.FacilityDuplicateCandidates.Local.Any(item =>
                item.FacilityId == firstId && item.PossibleDuplicateId == secondId)) return;
        _context.FacilityDuplicateCandidates.Add(new FacilityDuplicateCandidate
        {
            Id = Guid.NewGuid(),
            FacilityId = firstId,
            PossibleDuplicateId = secondId,
            ConfidenceScore = score,
            Reason = reason,
            CreatedAt = now
        });
    }

    private static (DirectoryFacility? Facility, int Score, string Reason) FindBestMatch(
        IEnumerable<DirectoryFacility> facilities,
        FacilityImportCandidate candidate)
    {
        var normalizedName = FacilityDirectoryNormalizer.Normalize(candidate.Name);
        var normalizedAddress = FacilityDirectoryNormalizer.NormalizeNullable(candidate.Address);
        DirectoryFacility? best = null;
        var bestScore = 0;
        var reason = string.Empty;
        foreach (var facility in facilities.Where(item => item.Kind == candidate.Kind ||
                     (item.Kind != DirectoryFacilityKind.Pharmacy && candidate.Kind != DirectoryFacilityKind.Pharmacy)))
        {
            var score = NameSimilarity(facility.NormalizedName, normalizedName);
            var matchReason = $"name similarity {score}%";
            if (normalizedAddress != null && facility.NormalizedAddress != null)
            {
                var addressScore = NameSimilarity(facility.NormalizedAddress, normalizedAddress);
                score = (score * 2 + addressScore) / 3;
                matchReason += $", address similarity {addressScore}%";
                if (facility.NormalizedName == normalizedName && facility.NormalizedAddress == normalizedAddress)
                    score = 100;
            }
            var phones = candidate.PhoneNumbers.Select(NormalizePhone).Where(value => value.Length >= 7).ToHashSet();
            if (phones.Count > 0 && facility.PhoneNumbers.Select(NormalizePhone).Any(phones.Contains))
            {
                score = Math.Max(score, 97);
                matchReason += ", phone match";
            }
            if (candidate.Latitude.HasValue && facility.Latitude.HasValue)
            {
                var distance = DistanceMeters(candidate.Latitude.Value, candidate.Longitude!.Value,
                    facility.Latitude.Value, facility.Longitude!.Value);
                score = ApplyDistanceGuard(score, distance);
                matchReason += $", {Math.Round(distance)}m apart";
            }
            if (score <= bestScore) continue;
            best = facility;
            bestScore = score;
            reason = matchReason;
        }
        return (best, bestScore, reason);
    }

    internal static int ApplyDistanceGuard(int nameAndAddressScore, double distanceMeters)
    {
        if (distanceMeters > 250) return Math.Min(nameAndAddressScore, 75);
        if (distanceMeters <= 75 && nameAndAddressScore >= 70) return Math.Max(nameAndAddressScore, 94);
        return nameAndAddressScore;
    }

    private void RepairMergedOpenStreetMapLocations(
        IReadOnlyCollection<FacilityImportCandidate> candidates,
        IReadOnlyCollection<FacilitySourceRecord> existingSources,
        ICollection<DirectoryFacility> facilities,
        FacilityImportRun run,
        DateTime now)
    {
        var currentCandidates = candidates.ToDictionary(item => item.ExternalId, StringComparer.OrdinalIgnoreCase);
        var mergedGroups = existingSources
            .Where(source => currentCandidates.ContainsKey(source.ExternalId))
            .GroupBy(source => source.FacilityId)
            .Where(group => group.Count() > 1)
            .ToList();

        foreach (var group in mergedGroups)
        {
            foreach (var source in group.Skip(1))
            {
                var candidate = currentCandidates[source.ExternalId];
                var original = source.Facility;
                var split = CreateFacility(candidate, now);
                original.Sources.Remove(source);
                source.FacilityId = split.Id;
                source.Facility = split;
                split.Sources.Add(source);
                _context.DirectoryFacilities.Add(split);
                facilities.Add(split);
                run.RecordsCreated++;
            }
        }
    }

    internal static int NameSimilarity(string first, string second)
    {
        if (first == second) return 100;
        if (first.Length == 0 || second.Length == 0) return 0;
        var rows = new int[second.Length + 1];
        for (var index = 0; index <= second.Length; index++) rows[index] = index;
        for (var firstIndex = 1; firstIndex <= first.Length; firstIndex++)
        {
            var diagonal = rows[0];
            rows[0] = firstIndex;
            for (var secondIndex = 1; secondIndex <= second.Length; secondIndex++)
            {
                var old = rows[secondIndex];
                rows[secondIndex] = Math.Min(
                    Math.Min(rows[secondIndex] + 1, rows[secondIndex - 1] + 1),
                    diagonal + (first[firstIndex - 1] == second[secondIndex - 1] ? 0 : 1));
                diagonal = old;
            }
        }
        return (int)Math.Round(100d * (1d - rows[second.Length] / (double)Math.Max(first.Length, second.Length)));
    }

    private static DirectoryFacility CreateFacility(FacilityImportCandidate candidate, DateTime now) => new()
    {
        Id = Guid.NewGuid(),
        Kind = candidate.Kind,
        Name = candidate.Name,
        NormalizedName = FacilityDirectoryNormalizer.Normalize(candidate.Name),
        Address = candidate.Address,
        NormalizedAddress = FacilityDirectoryNormalizer.NormalizeNullable(candidate.Address),
        City = "Baku",
        Country = "Azerbaijan",
        District = candidate.District,
        PhoneNumbers = candidate.PhoneNumbers.ToList(),
        Email = candidate.Email,
        Website = candidate.Website,
        OperatingHours = candidate.OperatingHours,
        Latitude = candidate.Latitude,
        Longitude = candidate.Longitude,
        Ownership = candidate.Ownership,
        VerificationStatus = candidate.VerificationStatus,
        PrimarySourceName = candidate.SourceName,
        IsPublished = true,
        IsActive = true,
        FirstSeenAt = now,
        LastSeenAt = now,
        LastVerifiedAt = candidate.VerificationStatus == FacilityVerificationStatus.RegistryVerified ? now : null,
        CreatedAt = now,
        UpdatedAt = now
    };

    private static void ApplyCandidate(DirectoryFacility facility, FacilityImportCandidate candidate, bool sourceChanged)
    {
        facility.LastSeenAt = DateTime.UtcNow;
        facility.IsActive = true;
        facility.IsPublished = true;
        facility.Address ??= candidate.Address;
        facility.NormalizedAddress ??= FacilityDirectoryNormalizer.NormalizeNullable(candidate.Address);
        facility.District ??= candidate.District;
        facility.Email ??= candidate.Email;
        facility.Website ??= candidate.Website;
        facility.OperatingHours ??= candidate.OperatingHours;
        facility.Latitude ??= candidate.Latitude;
        facility.Longitude ??= candidate.Longitude;
        facility.PhoneNumbers = facility.PhoneNumbers.Union(candidate.PhoneNumbers)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (candidate.VerificationStatus > facility.VerificationStatus)
        {
            facility.VerificationStatus = candidate.VerificationStatus;
            facility.PrimarySourceName = candidate.SourceName;
            facility.Ownership = candidate.Ownership;
        }
        if (candidate.VerificationStatus == FacilityVerificationStatus.RegistryVerified)
            facility.LastVerifiedAt = DateTime.UtcNow;
        if (sourceChanged) facility.UpdatedAt = DateTime.UtcNow;
    }

    private static FacilityImportCandidate? MapTabibMain(
        IReadOnlyDictionary<string, string> row,
        string source,
        string sourceUrl)
    {
        var name = Value(row, "tibb muesisesi", "tibb muessisesi");
        var city = Value(row, "seher");
        var territory = Value(row, "tibbi erazi bolmesi");
        if (string.IsNullOrWhiteSpace(name) ||
            !(ContainsBaku(city) || ContainsBaku(territory))) return null;
        var address = EmptyToNull(Value(row, "unvan"));
        var raw = JsonSerializer.Serialize(row);
        return OfficialCandidate(source, sourceUrl, name, address,
            SplitPhones(Value(row, "elaqe nomresi")), null, null, null, raw);
    }

    private static FacilityImportCandidate? MapTabibSubordinate(
        IReadOnlyDictionary<string, string> row,
        string source,
        string sourceUrl)
    {
        var name = Value(row, "tabeli tibb muesisesi", "tabeli tibb muessisesi");
        var parent = Value(row, "esas tibb muesisesi", "esas tibb muessisesi");
        if (string.IsNullOrWhiteSpace(name) || !ContainsBaku(parent)) return null;
        var raw = JsonSerializer.Serialize(row);
        return OfficialCandidate(source, sourceUrl, name, null, [], null, null, parent, raw);
    }

    private static FacilityImportCandidate? MapItsPrivate(
        IReadOnlyDictionary<string, string> row,
        string source,
        string sourceUrl)
    {
        var name = Value(row, "muqavileli tibb muesisesi", "muqavileli tibb muessisesi");
        var address = Value(row, "unvan");
        if (string.IsNullOrWhiteSpace(name) || !ContainsBaku(address)) return null;
        var email = EmptyToNull(Value(row, "elektron poct"));
        var website = EmptyToNull(Value(row, "veb sayt"));
        var raw = JsonSerializer.Serialize(row);
        return OfficialCandidate(source, sourceUrl, name, address,
            SplitPhones(Value(row, "elaqe nomresi")), email, website, null, raw,
            FacilityOwnershipType.Private);
    }

    private static FacilityImportCandidate OfficialCandidate(
        string source,
        string sourceUrl,
        string name,
        string? address,
        IReadOnlyList<string> phones,
        string? email,
        string? website,
        string? parent,
        string raw,
        FacilityOwnershipType ownership = FacilityOwnershipType.Public)
    {
        var externalId = Sha256($"{FacilityDirectoryNormalizer.Normalize(name)}|{FacilityDirectoryNormalizer.NormalizeNullable(address)}|{FacilityDirectoryNormalizer.NormalizeNullable(parent)}")[..24];
        return new FacilityImportCandidate(
            source, externalId, sourceUrl, "Azerbaijan Open Data Portal terms",
            ClassifyKind(name), name.Trim(), EmptyToNull(address), ExtractDistrict(address),
            phones, email, website, null, null, null, ownership,
            FacilityVerificationStatus.RegistryVerified, EmptyToNull(parent), raw);
    }

    internal static DirectoryFacilityKind ClassifyKind(string name, string? explicitKind = null)
    {
        var value = FacilityDirectoryNormalizer.Normalize($"{explicitKind} {name}");
        if (value.Contains("pharmacy") || value.Contains("aptek")) return DirectoryFacilityKind.Pharmacy;
        if (value.Contains("laborator")) return DirectoryFacilityKind.Laboratory;
        if (value.Contains("poliklin")) return DirectoryFacilityKind.Polyclinic;
        if (value.Contains("diagnost")) return DirectoryFacilityKind.DiagnosticCenter;
        if (value.Contains("hospital") || value.Contains("xestexana")) return DirectoryFacilityKind.Hospital;
        if (value.Contains("clinic") || value.Contains("klinika")) return DirectoryFacilityKind.Clinic;
        if (value.Contains("merkez") || value.Contains("center")) return DirectoryFacilityKind.MedicalCenter;
        return DirectoryFacilityKind.Other;
    }

    private static string Value(IReadOnlyDictionary<string, string> row, params string[] keys)
    {
        foreach (var key in keys)
            if (row.TryGetValue(key, out var value)) return value.Trim();
        return string.Empty;
    }

    private static bool ContainsBaku(string? value) =>
        FacilityDirectoryNormalizer.NormalizeNullable(value)?.Contains("baki") == true;
    private static string? ExtractDistrict(string? address)
    {
        if (string.IsNullOrWhiteSpace(address)) return null;
        var match = Regex.Match(address, @"(?<district>[\p{L}]+)\s+rayonu", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups["district"].Value : null;
    }
    private static IReadOnlyList<string> SplitPhones(string? value) =>
        string.IsNullOrWhiteSpace(value) ? [] : value.Split(['•', ';', '|'], StringSplitOptions.RemoveEmptyEntries)
            .Select(item => item.Trim()).Where(item => item.Length >= 5).Distinct().ToList();
    private static string NormalizePhone(string value) => new(value.Where(char.IsDigit).ToArray());
    private static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string Sha256(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static string? Tag(JsonElement tags, string name) =>
        tags.TryGetProperty(name, out var value) ? value.GetString() : null;
    private static double? Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetDouble(out var number) ? number : null;
    private static double? CenterNumber(JsonElement element, string name) =>
        element.TryGetProperty("center", out var center) ? Number(center, name) : null;
    private static string? BuildOsmAddress(JsonElement tags)
    {
        var parts = new[] { Tag(tags, "addr:street"), Tag(tags, "addr:housenumber"), Tag(tags, "addr:suburb") }
            .Where(value => !string.IsNullOrWhiteSpace(value)).ToArray();
        return parts.Length == 0 ? null : string.Join(", ", parts);
    }
    private static double DistanceMeters(double lat1, double lon1, double lat2, double lon2)
    {
        const double earthRadius = 6_371_000;
        var dLat = (lat2 - lat1) * Math.PI / 180;
        var dLon = (lon2 - lon1) * Math.PI / 180;
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return earthRadius * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }
}

internal static class CsvRows
{
    public static List<IReadOnlyDictionary<string, string>> Parse(string content)
    {
        var records = ParseRecords(content);
        if (records.Count == 0) return [];
        var headers = records[0].Select(value => FacilityDirectoryNormalizer.Normalize(value.TrimStart('\uFEFF'))).ToArray();
        var result = new List<IReadOnlyDictionary<string, string>>();
        foreach (var values in records.Skip(1))
        {
            if (values.All(string.IsNullOrWhiteSpace)) continue;
            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < headers.Length; index++)
                row[headers[index]] = index < values.Count ? values[index] : string.Empty;
            result.Add(row);
        }
        return result;
    }

    private static List<List<string>> ParseRecords(string content)
    {
        var records = new List<List<string>>();
        var record = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        for (var index = 0; index < content.Length; index++)
        {
            var character = content[index];
            if (character == '"')
            {
                if (quoted && index + 1 < content.Length && content[index + 1] == '"')
                {
                    field.Append('"');
                    index++;
                }
                else quoted = !quoted;
            }
            else if (character == ',' && !quoted)
            {
                record.Add(field.ToString());
                field.Clear();
            }
            else if ((character == '\n' || character == '\r') && !quoted)
            {
                if (character == '\r' && index + 1 < content.Length && content[index + 1] == '\n') index++;
                record.Add(field.ToString());
                field.Clear();
                records.Add(record);
                record = [];
            }
            else field.Append(character);
        }
        if (field.Length > 0 || record.Count > 0)
        {
            record.Add(field.ToString());
            records.Add(record);
        }
        return records;
    }
}

public sealed class FacilityDirectorySyncWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<FacilityDirectoryOptions> options,
    IHostEnvironment environment,
    ILogger<FacilityDirectorySyncWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!environment.IsProduction() || !settings.Enabled) return;
        if (settings.RunOnStartup)
        {
            await Task.Delay(TimeSpan.FromSeconds(settings.StartupDelaySeconds), stoppingToken);
            await RunOnceAsync(stoppingToken);
        }
        using var timer = new PeriodicTimer(TimeSpan.FromHours(settings.SyncIntervalHours));
        while (await timer.WaitForNextTickAsync(stoppingToken)) await RunOnceAsync(stoppingToken);
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<IFacilityDirectorySyncService>();
            await service.SyncAsync(false, cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            logger.LogError(exception, "Scheduled facility directory sync failed.");
        }
    }
}
