using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using PIYA_API.Data;
using PIYA_API.Model;
using PIYA_API.Service.Class;
using PIYA_API.Service.Interface;
using Xunit;

namespace PIYA_API.Tests.Unit;

public sealed class MedicationBarcodeSearchTests
{
    [Theory]
    [InlineData("0012345678905", "0012345678905")]
    [InlineData("  0012345678905\t", "0012345678905")]
    [InlineData("0012345678905", " 0012345678905 ")]
    public async Task ExactBarcodeMatch_TrimsWhitespaceAndPreservesLeadingZeros(
        string search, string storedBarcode)
    {
        await using var db = CreateContext();
        var expected = Medication("Alpha", storedBarcode);
        db.Medications.AddRange(expected, Medication("Beta", "12345678905"), Medication("Gamma", null));
        await db.SaveChangesAsync();

        var result = await Service(db).GetAllAdminAsync(search, null, true, 1, 20);

        result.TotalCount.Should().Be(1);
        result.Items.Should().ContainSingle().Which.Id.Should().Be(expected.Id);
        result.Items.Single().Barcode.Should().Be(storedBarcode, "search must not rewrite stored identifiers");
    }

    [Theory]
    [InlineData("123456")]
    [InlineData("001234567890")]
    [InlineData("12345678905")]
    [InlineData("00012345678905")]
    public async Task PartialOrNumericallyEquivalentBarcode_DoesNotMatch(string search)
    {
        await using var db = CreateContext();
        db.Medications.Add(Medication("Alpha", "0012345678905"));
        await db.SaveChangesAsync();

        var result = await Service(db).GetAllAdminAsync(search, null, true, 1, 20);

        result.TotalCount.Should().Be(0);
        result.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task BarcodeMatch_StillHonorsAvailabilityAndPrescriptionFilters()
    {
        await using var db = CreateContext();
        var expected = Medication("Alpha", "0001112223334");
        var unavailable = Medication("Beta", "0001112223334");
        unavailable.IsAvailable = false;
        var nonPrescription = Medication("Gamma", "0001112223334");
        nonPrescription.RequiresPrescription = false;
        db.Medications.AddRange(expected, unavailable, nonPrescription);
        await db.SaveChangesAsync();

        var result = await Service(db).GetAllAdminAsync("0001112223334", true, true, 1, 20);

        result.TotalCount.Should().Be(1);
        result.Items.Should().ContainSingle().Which.Id.Should().Be(expected.Id);
    }

    [Fact]
    public async Task BarcodeMatch_PreservesSortedPaginationAndFullCount()
    {
        await using var db = CreateContext();
        db.Medications.AddRange(Medication("Gamma", "0001112223334"),
            Medication("Alpha", "0001112223334"), Medication("Beta", "0001112223334"));
        await db.SaveChangesAsync();

        var result = await Service(db).GetAllAdminAsync("0001112223334", null, true, 2, 1);

        result.TotalCount.Should().Be(3);
        result.Items.Should().ContainSingle().Which.BrandName.Should().Be("Beta");
    }

    [Fact]
    public async Task NameSearch_RemainsCaseInsensitiveWithNullBarcode()
    {
        await using var db = CreateContext();
        var expected = Medication("Paracetamol", null);
        db.Medications.AddRange(expected, Medication("Other", "0001112223334"));
        await db.SaveChangesAsync();

        var result = await Service(db).GetAllAdminAsync("  pARac  ", null, true, 1, 20);

        result.Items.Should().ContainSingle().Which.Id.Should().Be(expected.Id);
    }

    private static PharmacyApiDbContext CreateContext() => new(
        new DbContextOptionsBuilder<PharmacyApiDbContext>()
            .UseInMemoryDatabase($"medication-barcode-{Guid.NewGuid()}").Options);

    private static MedicationService Service(PharmacyApiDbContext db) => new(
        db, Mock.Of<IAuditService>(), Mock.Of<ILogger<MedicationService>>());

    private static Medication Medication(string name, string? barcode) => new()
    {
        Id = Guid.NewGuid(), BrandName = name, GenericName = "Active ingredient",
        Form = "Tablet", Strength = "500mg", Barcode = barcode,
        IsAvailable = true, RequiresPrescription = true
    };
}
