using Xunit;
using Moq;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PIYA_API.Data;
using PIYA_API.Model;
using PIYA_API.Service.Class;
using PIYA_API.Service.Interface;

namespace PIYA_API.Tests.Unit;

public class DoctorNoteServiceTests : IDisposable
{
    private readonly PharmacyApiDbContext _context;
    private readonly DoctorNoteService _service;
    private readonly Guid _patientId = Guid.NewGuid();
    private readonly Guid _doctorId = Guid.NewGuid();

    public DoctorNoteServiceTests()
    {
        var options = new DbContextOptionsBuilder<PharmacyApiDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new PharmacyApiDbContext(options);

        // Seed patient and doctor users
        _context.Users.AddRange(
            new User
            {
                Id = _patientId,
                Email = "patient@test.com",
                Username = "patient_note",
                FirstName = "Patient",
                LastName = "Test",
                PasswordHash = "hash",
                Role = UserRole.Patient,
                DateOfBirth = DateTime.UtcNow.AddYears(-30),
                PhoneNumber = "0000000000",
                TokensInfo = new Token()
            },
            new User
            {
                Id = _doctorId,
                Email = "doctor@test.com",
                Username = "doctor_note",
                FirstName = "Doctor",
                LastName = "Test",
                PasswordHash = "hash",
                Role = UserRole.Doctor,
                DateOfBirth = DateTime.UtcNow.AddYears(-40),
                PhoneNumber = "1111111111",
                TokensInfo = new Token()
            });
        _context.SaveChanges();

        _service = new DoctorNoteService(
            _context,
            Mock.Of<IAuditService>(),
            Mock.Of<ILogger<DoctorNoteService>>());
    }

    [Fact]
    public async Task CreateNote_AssignsNoteNumberAndPublicTokenHash()
    {
        var (note, publicToken) = await _service.CreateNoteAsync(MakeNote());

        note.NoteNumber.Should().NotBeNullOrWhiteSpace();
        note.PublicTokenHash.Should().NotBeNullOrWhiteSpace();
        publicToken.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task CreateNote_PublicTokenHashDiffersFromToken()
    {
        var (note, publicToken) = await _service.CreateNoteAsync(MakeNote());

        note.PublicTokenHash.Should().NotBe(publicToken);
    }

    [Fact]
    public async Task CreateNote_SetsStatusToActive()
    {
        var (note, _) = await _service.CreateNoteAsync(MakeNote());

        note.Status.Should().Be(DoctorNoteStatus.Active);
    }

    [Fact]
    public async Task CreateNote_PersistsToDatabaseWithCorrectPatient()
    {
        var (note, _) = await _service.CreateNoteAsync(MakeNote());

        var stored = await _context.DoctorNotes.FindAsync(note.Id);
        stored.Should().NotBeNull();
        stored!.PatientId.Should().Be(_patientId);
        stored.DoctorId.Should().Be(_doctorId);
    }

    [Fact]
    public async Task GetById_ExistingNote_ReturnsNote()
    {
        var (note, _) = await _service.CreateNoteAsync(MakeNote());

        var result = await _service.GetByIdAsync(note.Id);

        result.Should().NotBeNull();
        result!.Id.Should().Be(note.Id);
    }

    [Fact]
    public async Task GetById_NonExistentId_ReturnsNull()
    {
        var result = await _service.GetByIdAsync(Guid.NewGuid());

        result.Should().BeNull();
    }

    [Fact]
    public async Task RevokeNote_ChangesStatusToRevoked()
    {
        var (note, _) = await _service.CreateNoteAsync(MakeNote());

        var result = await _service.RevokeNoteAsync(note.Id, "Testing revocation");

        result.Should().NotBeNull();
        result!.Status.Should().Be(DoctorNoteStatus.Revoked);
        result.RevokedAt.Should().NotBeNull();
        result.RevocationReason.Should().Be("Testing revocation");
    }

    [Fact]
    public async Task VerifyPublicToken_ValidToken_ReturnsNote()
    {
        var (note, publicToken) = await _service.CreateNoteAsync(MakeNote());

        var result = await _service.VerifyPublicTokenAsync(publicToken);

        result.Should().NotBeNull();
        result!.Id.Should().Be(note.Id);
    }

    [Fact]
    public async Task VerifyPublicToken_InvalidToken_ReturnsNull()
    {
        var result = await _service.VerifyPublicTokenAsync("invalid_token_that_does_not_exist");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetDoctorNotes_ReturnsOnlyDoctorsNotes()
    {
        var otherDoctorId = Guid.NewGuid();

        await _service.CreateNoteAsync(MakeNote());
        await _service.CreateNoteAsync(MakeNote());

        var results = await _service.GetDoctorNotesAsync(_doctorId);

        results.Should().HaveCount(2);
        results.Should().AllSatisfy(n => n.DoctorId.Should().Be(_doctorId));
    }

    // ── helpers ──────────────────────────────────────────────

    private DoctorNote MakeNote() => new()
    {
        PatientId = _patientId,
        DoctorId = _doctorId,
        Title = "Work Absence Certificate",
        Summary = "Patient is unable to work due to illness",
        ValidFrom = DateTime.UtcNow,
        ValidTo = DateTime.UtcNow.AddDays(7),
        NoteNumber = string.Empty,      // assigned by service
        PublicTokenHash = string.Empty  // assigned by service
    };

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }
}
