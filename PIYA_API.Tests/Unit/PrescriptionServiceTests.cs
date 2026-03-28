using Xunit;
using Moq;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PIYA_API.Data;
using PIYA_API.Model;
using PIYA_API.Service.Class;
using PIYA_API.Service.Interface;

namespace PIYA_API.Tests.Unit;

public class PrescriptionServiceTests : IDisposable
{
    private readonly PharmacyApiDbContext _context;
    private readonly PrescriptionService _service;
    private readonly Mock<IAuditService> _auditMock;
    private readonly Mock<IQRService> _qrMock;
    private readonly Mock<IInventoryService> _inventoryMock;

    public PrescriptionServiceTests()
    {
        var options = new DbContextOptionsBuilder<PharmacyApiDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new PharmacyApiDbContext(options);

        _auditMock = new Mock<IAuditService>();
        _qrMock = new Mock<IQRService>();
        _inventoryMock = new Mock<IInventoryService>();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["SomeKey"] = "value" })
            .Build();

        _service = new PrescriptionService(
            _context,
            _auditMock.Object,
            _qrMock.Object,
            config,
            _inventoryMock.Object,
            Mock.Of<ILogger<PrescriptionService>>());
    }

    [Fact]
    public async Task CreatePrescription_SetsStatusToActive()
    {
        var prescription = MakePrescription();

        var result = await _service.CreatePrescriptionAsync(prescription);

        result.Status.Should().Be(PrescriptionStatus.Active);
    }

    [Fact]
    public async Task CreatePrescription_AssignsNewId_WhenEmpty()
    {
        var prescription = MakePrescription();
        prescription.Id = Guid.Empty;

        var result = await _service.CreatePrescriptionAsync(prescription);

        result.Id.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task CreatePrescription_GeneratesDigitalSignature()
    {
        var prescription = MakePrescription();

        var result = await _service.CreatePrescriptionAsync(prescription);

        result.DigitalSignature.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task CreatePrescription_PersistsToDatabase()
    {
        var prescription = MakePrescription();

        var result = await _service.CreatePrescriptionAsync(prescription);

        var stored = await _context.Prescriptions.FindAsync(result.Id);
        stored.Should().NotBeNull();
    }

    [Fact]
    public async Task GetById_ExistingPrescription_ReturnsPrescription()
    {
        var prescription = MakePrescription();
        await _service.CreatePrescriptionAsync(prescription);

        var result = await _service.GetByIdAsync(prescription.Id);

        result.Should().NotBeNull();
        result!.Id.Should().Be(prescription.Id);
    }

    [Fact]
    public async Task GetById_NonExistentId_ReturnsNull()
    {
        var result = await _service.GetByIdAsync(Guid.NewGuid());

        result.Should().BeNull();
    }

    [Fact]
    public async Task IsExpired_WhenExpiryInPast_ReturnsTrue()
    {
        var prescription = MakePrescription(expiresAt: DateTime.UtcNow.AddDays(-1));
        await _service.CreatePrescriptionAsync(prescription);

        var expired = await _service.IsExpiredAsync(prescription.Id);

        expired.Should().BeTrue();
    }

    [Fact]
    public async Task IsExpired_WhenExpiryInFuture_ReturnsFalse()
    {
        var prescription = MakePrescription(expiresAt: DateTime.UtcNow.AddDays(30));
        await _service.CreatePrescriptionAsync(prescription);

        var expired = await _service.IsExpiredAsync(prescription.Id);

        expired.Should().BeFalse();
    }

    [Fact]
    public async Task CancelPrescription_ChangesStatusToCancelled()
    {
        var prescription = MakePrescription();
        await _service.CreatePrescriptionAsync(prescription);

        var result = await _service.CancelPrescriptionAsync(prescription.Id, "Test cancellation");

        result.Status.Should().Be(PrescriptionStatus.Cancelled);
    }

    [Fact]
    public async Task ExpireAsync_ChangesStatusToExpired()
    {
        var prescription = MakePrescription();
        await _service.CreatePrescriptionAsync(prescription);

        await _service.ExpireAsync(prescription.Id);

        var updated = await _context.Prescriptions.FindAsync(prescription.Id);
        updated!.Status.Should().Be(PrescriptionStatus.Expired);
    }

    [Fact]
    public async Task GetPatientPrescriptions_ReturnsOnlyPatientPrescriptions()
    {
        var patientId = Guid.NewGuid();
        var otherPatientId = Guid.NewGuid();

        await _service.CreatePrescriptionAsync(MakePrescription(patientId: patientId));
        await _service.CreatePrescriptionAsync(MakePrescription(patientId: patientId));
        await _service.CreatePrescriptionAsync(MakePrescription(patientId: otherPatientId));

        var results = await _service.GetPatientPrescriptionsAsync(patientId);

        results.Should().HaveCount(2);
        results.Should().AllSatisfy(p => p.PatientId.Should().Be(patientId));
    }

    [Fact]
    public async Task CountDoctorPrescriptions_ReturnsCorrectCount()
    {
        var doctorId = Guid.NewGuid();
        await _service.CreatePrescriptionAsync(MakePrescription(doctorId: doctorId));
        await _service.CreatePrescriptionAsync(MakePrescription(doctorId: doctorId));

        var count = await _service.CountDoctorPrescriptionsAsync(doctorId);

        count.Should().Be(2);
    }

    // ── helpers ──────────────────────────────────────────────

    private static Prescription MakePrescription(
        Guid? patientId = null,
        Guid? doctorId = null,
        DateTime? expiresAt = null) => new()
    {
        Id = Guid.NewGuid(),
        PatientId = patientId ?? Guid.NewGuid(),
        DoctorId = doctorId ?? Guid.NewGuid(),
        Status = PrescriptionStatus.Active,
        IssuedAt = DateTime.UtcNow,
        ExpiresAt = expiresAt ?? DateTime.UtcNow.AddDays(30),
        Diagnosis = "Hypertension",
        Instructions = "Take once daily"
    };

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }
}
