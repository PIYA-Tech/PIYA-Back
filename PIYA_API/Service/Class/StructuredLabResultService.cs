using Microsoft.EntityFrameworkCore;
using PIYA_API.Data;
using PIYA_API.DTOs;
using PIYA_API.Model;
using PIYA_API.Service.Interface;

namespace PIYA_API.Service.Class;

public sealed class StructuredLabResultService(
    PharmacyApiDbContext db,
    IPatientNotificationInboxService inbox,
    INotificationService notifications,
    IAuditService audit,
    ILogger<StructuredLabResultService> logger) : IStructuredLabResultService
{
    private readonly PharmacyApiDbContext _db = db;
    private readonly IPatientNotificationInboxService _inbox = inbox;
    private readonly INotificationService _notifications = notifications;
    private readonly IAuditService _audit = audit;
    private readonly ILogger<StructuredLabResultService> _logger = logger;

    public async Task<IReadOnlyList<StructuredLabReportResponse>> GetPatientReportsAsync(Guid patientId)
    {
        var tests = await _db.Set<MedicalTest>().AsNoTracking()
            .Where(item => item.PatientId == patientId)
            .OrderByDescending(item => item.ResultsAt ?? item.CreatedAt)
            .Take(200).ToListAsync();
        return await BuildReportsAsync(tests);
    }

    public async Task<StructuredLabReportResponse?> GetPatientReportAsync(
        Guid patientId, Guid medicalTestId)
    {
        var test = await _db.Set<MedicalTest>().AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == medicalTestId && item.PatientId == patientId);
        if (test is null) return null;
        return (await BuildReportsAsync([test])).Single();
    }

    public async Task<StructuredLabReportResponse> ReplaceAnalytesAsync(
        Guid medicalTestId,
        Guid actorId,
        bool isSuperAdmin,
        ReplaceLabAnalytesRequest request)
    {
        if (request.Analytes.Count > 200)
            throw new ArgumentException("A report cannot contain more than 200 analytes.");
        var test = await _db.Set<MedicalTest>()
            .SingleOrDefaultAsync(item => item.Id == medicalTestId)
            ?? throw new PatientHealthNotFoundException("Medical test not found.");

        if (!isSuperAdmin && test.PerformedByDoctorId != actorId &&
            !(test.PerformedByDoctorId is null && test.OrderedByDoctorId == actorId))
            throw new UnauthorizedAccessException("Only the assigned doctor can record these results.");

        var normalized = new List<(LabAnalyteInput Input, string Code, string Name)>();
        var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var input in request.Analytes)
        {
            var code = input.Code.Trim().ToUpperInvariant();
            var name = input.Name.Trim();
            if (code.Length is 0 or > 80 || name.Length is 0 or > 200)
                throw new ArgumentException("Every analyte needs a valid code and name.");
            if (!codes.Add(code))
                throw new ArgumentException($"Analyte code '{code}' appears more than once.");
            if (!input.NumericValue.HasValue && string.IsNullOrWhiteSpace(input.TextValue))
                throw new ArgumentException($"Analyte '{code}' needs a numeric or text value.");
            if (input.NumericValue.HasValue && !string.IsNullOrWhiteSpace(input.TextValue))
                throw new ArgumentException($"Analyte '{code}' cannot have both numeric and text values.");
            if (input.ReferenceLow.HasValue && input.ReferenceHigh.HasValue &&
                input.ReferenceLow > input.ReferenceHigh)
                throw new ArgumentException($"Analyte '{code}' has an invalid reference range.");
            if (input.Flag.HasValue && !Enum.IsDefined(input.Flag.Value))
                throw new ArgumentException($"Analyte '{code}' has an unknown flag.");
            normalized.Add((input, code, name));
        }

        var prior = await _db.Set<MedicalTestAnalyteResult>()
            .Where(item => item.MedicalTestId == test.Id).ToListAsync();
        _db.Set<MedicalTestAnalyteResult>().RemoveRange(prior);

        var now = DateTime.UtcNow;
        var results = normalized.Select(entry => new MedicalTestAnalyteResult
        {
            Id = Guid.NewGuid(),
            MedicalTestId = test.Id,
            Code = entry.Code,
            Name = entry.Name,
            NumericValue = entry.Input.NumericValue,
            TextValue = Clean(entry.Input.TextValue),
            Unit = Clean(entry.Input.Unit),
            ReferenceLow = entry.Input.ReferenceLow,
            ReferenceHigh = entry.Input.ReferenceHigh,
            ReferenceText = Clean(entry.Input.ReferenceText),
            Flag = entry.Input.Flag ?? CalculateFlag(
                entry.Input.NumericValue, entry.Input.ReferenceLow, entry.Input.ReferenceHigh),
            SortOrder = entry.Input.SortOrder,
            ObservedAt = entry.Input.ObservedAt.HasValue ? ToUtc(entry.Input.ObservedAt.Value) : test.PerformedAt,
            EnteredByUserId = actorId,
            CreatedAt = now,
            UpdatedAt = now
        }).ToList();
        _db.Set<MedicalTestAnalyteResult>().AddRange(results);

        if (!isSuperAdmin) test.PerformedByDoctorId ??= actorId;
        test.PerformedAt ??= now;
        test.ResultsAt = now;
        test.Status = MedicalTestStatus.ResultsReady;
        if (request.Findings is not null) test.Findings = Clean(request.Findings);
        test.UpdatedAt = now;
        await _db.SaveChangesAsync();

        await _audit.LogEntityActionAsync(
            "ReplaceStructuredLabResults", nameof(MedicalTest), test.Id.ToString(), actorId,
            "Assigned professional recorded structured medical test results");
        await _inbox.EnqueueAsync(
            test.PatientId,
            PatientNotificationCategory.LabResult,
            "Test results are ready",
            "New results are available in My Health.",
            $"piya://health/tests/{test.Id}",
            new Dictionary<string, string>
            {
                ["type"] = "lab_result",
                ["medicalTestId"] = test.Id.ToString()
            },
            $"lab-results:{test.Id}:{now.Ticks}");
        try
        {
            await _notifications.SendPushNotificationAsync(
                test.PatientId,
                "Test results are ready",
                "New results are available in PIYA.",
                new Dictionary<string, string>
                {
                    ["type"] = "lab_result",
                    ["medicalTestId"] = test.Id.ToString()
                });
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Test {TestId} saved but push delivery failed", test.Id);
        }

        return Report(test, results);
    }

    private async Task<IReadOnlyList<StructuredLabReportResponse>> BuildReportsAsync(List<MedicalTest> tests)
    {
        if (tests.Count == 0) return [];
        var ids = tests.Select(item => item.Id).ToList();
        var analytes = await _db.Set<MedicalTestAnalyteResult>().AsNoTracking()
            .Where(item => ids.Contains(item.MedicalTestId))
            .OrderBy(item => item.SortOrder).ThenBy(item => item.Name)
            .ToListAsync();
        var lookup = analytes.ToLookup(item => item.MedicalTestId);
        return tests.Select(item => Report(item, lookup[item.Id])).ToList();
    }

    private static StructuredLabReportResponse Report(
        MedicalTest test, IEnumerable<MedicalTestAnalyteResult> analytes) => new(
        test.Id, test.PatientId, test.TestType, test.Status, test.Findings,
        test.PerformedAt, test.ResultsAt,
        analytes.OrderBy(item => item.SortOrder).ThenBy(item => item.Name)
            .Select(LabAnalyteResponse.From).ToList());

    private static LabAnalyteFlag CalculateFlag(decimal? value, decimal? low, decimal? high)
    {
        if (!value.HasValue) return LabAnalyteFlag.Unknown;
        if (low.HasValue && value.Value < low.Value) return LabAnalyteFlag.Low;
        if (high.HasValue && value.Value > high.Value) return LabAnalyteFlag.High;
        return low.HasValue || high.HasValue ? LabAnalyteFlag.Normal : LabAnalyteFlag.Unknown;
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}
