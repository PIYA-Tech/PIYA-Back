using Microsoft.EntityFrameworkCore;
using PIYA_API.Data;
using PIYA_API.Model;
using PIYA_API.Service.Interface;

namespace PIYA_API.Service.Class;

public class MedicalTestService(
    PharmacyApiDbContext context,
    ILogger<MedicalTestService> logger) : IMedicalTestService
{
    private readonly PharmacyApiDbContext _context = context;
    private readonly ILogger<MedicalTestService> _logger = logger;

    public async Task<MedicalTest> CreateAsync(MedicalTest test)
    {
        test.Id = Guid.NewGuid();
        test.CreatedAt = DateTime.UtcNow;
        test.UpdatedAt = DateTime.UtcNow;
        _context.MedicalTests.Add(test);
        await _context.SaveChangesAsync();
        _logger.LogInformation("MedicalTest {TestId} created for referral {ReferralId}", test.Id, test.ReferralId);
        return test;
    }

    public async Task<MedicalTest?> GetByIdAsync(Guid id) =>
        await _context.MedicalTests
            .Include(t => t.OrderedByDoctor)
            .Include(t => t.PerformedByDoctor)
            .Include(t => t.Referral)
            .Include(t => t.Appointment)
            .Include(t => t.Documents)
            .FirstOrDefaultAsync(t => t.Id == id);

    public async Task<List<MedicalTest>> GetByReferralAsync(Guid referralId) =>
        await _context.MedicalTests
            .Include(t => t.OrderedByDoctor)
            .Include(t => t.PerformedByDoctor)
            .Include(t => t.Documents)
            .Where(t => t.ReferralId == referralId)
            .OrderBy(t => t.CreatedAt)
            .ToListAsync();

    public async Task<List<MedicalTest>> GetByAppointmentAsync(Guid appointmentId) =>
        await _context.MedicalTests
            .Include(t => t.OrderedByDoctor)
            .Include(t => t.PerformedByDoctor)
            .Include(t => t.Documents)
            .Where(t => t.AppointmentId == appointmentId)
            .OrderBy(t => t.CreatedAt)
            .ToListAsync();

    public async Task<MedicalTest> UpdateStatusAsync(
        Guid id,
        MedicalTestStatus status,
        string? findings = null,
        Guid? performedByDoctorId = null)
    {
        var test = await RequireAsync(id);
        test.Status = status;
        test.UpdatedAt = DateTime.UtcNow;

        if (performedByDoctorId.HasValue)
        {
            if (test.PerformedByDoctorId.HasValue && test.PerformedByDoctorId != performedByDoctorId)
                throw new UnauthorizedAccessException("This test has already been claimed by another doctor.");
            test.PerformedByDoctorId ??= performedByDoctorId;
        }

        if (findings is not null)
            test.Findings = findings;

        if (status == MedicalTestStatus.ResultsReady || status == MedicalTestStatus.Reviewed)
            test.ResultsAt ??= DateTime.UtcNow;

        if (status == MedicalTestStatus.InProgress)
            test.PerformedAt ??= DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return test;
    }

    public async Task<MedicalTest> AttachDocumentAsync(
        Guid testId,
        Guid documentId,
        Guid attachingUserId,
        bool isAdministrator = false)
    {
        var test = await RequireAsync(testId);

        var doc = await _context.MedicalDocuments.FindAsync(documentId)
            ?? throw new KeyNotFoundException($"Document {documentId} not found.");

        if (doc.UserId != test.PatientId)
            throw new InvalidOperationException("Document owner does not match the medical test patient.");
        if (doc.IsArchived)
            throw new InvalidOperationException("Archived documents cannot be attached to medical tests.");
        if (doc.MedicalTestId.HasValue && doc.MedicalTestId != testId)
            throw new InvalidOperationException("Document is already attached to another medical test.");
        if (!isAdministrator &&
            doc.MedicalTestId != testId &&
            doc.UploadedByUserId != attachingUserId)
        {
            throw new UnauthorizedAccessException(
                "Only the document uploader may attach it to a medical test.");
        }

        doc.MedicalTestId = testId;
        doc.MedicalTest = test;
        doc.ModifiedAt = DateTime.UtcNow;
        test.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return test;
    }

    public async Task<List<MedicalTest>> GetByPatientAsync(Guid patientId) =>
        await _context.MedicalTests
            .Include(t => t.Referral)
            .Include(t => t.Appointment)
            .Include(t => t.OrderedByDoctor)
            .Include(t => t.PerformedByDoctor)
            .Include(t => t.Documents)
            .Where(t => t.PatientId == patientId)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync();

    public async Task<MedicalTest> CreateStandaloneAsync(MedicalTest test)
    {
        if (test.PatientId == Guid.Empty)
            throw new ArgumentException("PatientId is required for a standalone test.");
        if (test.OrderedByDoctorId == Guid.Empty)
            throw new ArgumentException("OrderedByDoctorId is required for a standalone test.");
        if (test.ReferralId is not null)
            throw new ArgumentException("Use CreateAsync for tests that belong to a referral.");

        test.Id = Guid.NewGuid();
        test.IsEmergency = true;
        test.CreatedAt = DateTime.UtcNow;
        test.UpdatedAt = DateTime.UtcNow;

        _context.MedicalTests.Add(test);
        await _context.SaveChangesAsync();

        _logger.LogInformation(
            "Standalone/emergency MedicalTest {TestId} created for patient {PatientId} by doctor {DoctorId}",
            test.Id, test.PatientId, test.OrderedByDoctorId);

        return test;
    }

    private async Task<MedicalTest> RequireAsync(Guid id) =>
        await _context.MedicalTests.FindAsync(id)
        ?? throw new KeyNotFoundException($"MedicalTest {id} not found.");
}
