using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;
using PIYA_API.Data;
using PIYA_API.Model;
using PIYA_API.Service.Class;
using PIYA_API.Service.Interface;
using Xunit;

namespace PIYA_API.Tests.Unit;

public sealed class FileUploadServiceTests : IDisposable
{
    private readonly PharmacyApiDbContext _context;
    private readonly Mock<IFileStorageService> _storage = new();
    private readonly Mock<IFileSecurityScanner> _scanner = new();
    private readonly FileUploadService _service;

    public FileUploadServiceTests()
    {
        _context = new PharmacyApiDbContext(
            new DbContextOptionsBuilder<PharmacyApiDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FileUpload:MaxFileSizeMB"] = "1"
            })
            .Build();
        _storage.Setup(x => x.UploadAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync((Stream _, string key, string _) => key);
        _service = new FileUploadService(_context, configuration, _storage.Object, _scanner.Object);
    }

    [Fact]
    public async Task Upload_ValidPng_ScansBeforePersistingAndSanitizesName()
    {
        await using var stream = new MemoryStream(
            [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, 0x01]);
        var owner = Guid.NewGuid();

        var document = await _service.UploadDocumentAsync(
            stream, "../../patient scan.png", "image/png; charset=binary",
            owner, MedicalDocumentType.LabReport, owner);

        document.FileName.Should().Be("patient scan.png");
        document.ContentType.Should().Be("image/png");
        document.ObjectKey.Should().StartWith($"patients/{owner:N}/docs/");
        _scanner.Verify(x => x.ScanAsync(stream, It.IsAny<CancellationToken>()), Times.Once);
        _storage.Verify(x => x.UploadAsync(stream, document.ObjectKey!, "image/png"), Times.Once);
        (await _context.MedicalDocuments.FindAsync(document.Id)).Should().NotBeNull();
    }

    [Fact]
    public async Task Upload_SpoofedPng_RejectsBeforeScanningOrStorage()
    {
        await using var stream = new MemoryStream("not a png"u8.ToArray());

        var action = () => _service.UploadDocumentAsync(
            stream, "report.png", "image/png", Guid.NewGuid(),
            MedicalDocumentType.LabReport, Guid.NewGuid());

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*does not match*");
        _scanner.Verify(x => x.ScanAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Never);
        _storage.Verify(x => x.UploadAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Upload_ScannerRejects_DoesNotStoreMetadataOrBlob()
    {
        await using var stream = new MemoryStream("%PDF-test"u8.ToArray());
        _scanner.Setup(x => x.ScanAsync(stream, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Malware detected"));

        var action = () => _service.UploadDocumentAsync(
            stream, "report.pdf", "application/pdf", Guid.NewGuid(),
            MedicalDocumentType.LabReport, Guid.NewGuid());

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Malware detected");
        _storage.Verify(x => x.UploadAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        (await _context.MedicalDocuments.CountAsync()).Should().Be(0);
    }

    public void Dispose() => _context.Dispose();
}
