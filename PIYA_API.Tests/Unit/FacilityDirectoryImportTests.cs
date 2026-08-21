using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PIYA_API.Configuration;
using PIYA_API.Data;
using PIYA_API.Model;
using PIYA_API.Service.Class;
using Xunit;

namespace PIYA_API.Tests.Unit;

public sealed class FacilityDirectoryImportTests
{
    [Theory]
    [InlineData("Bakı Sağlamlıq Mərkəzi", "baki saglamliq merkezi")]
    [InlineData("  1 nömrəli Şəhər Poliklinikası ", "1 nomreli seher poliklinikasi")]
    public void Normalizer_produces_stable_search_keys(string input, string expected)
    {
        FacilityDirectoryNormalizer.Normalize(input).Should().Be(expected);
    }

    [Fact]
    public void Csv_parser_handles_bom_quotes_and_embedded_commas()
    {
        var csv = "\uFEFFAd,Ünvan,Telefon\r\n\"Mərkəzi Klinika\",\"Bakı, Parlament prospekti 76\",\"(+994 12) 105\"\r\n";

        var rows = CsvRows.Parse(csv);

        rows.Should().ContainSingle();
        rows[0]["ad"].Should().Be("Mərkəzi Klinika");
        rows[0]["unvan"].Should().Be("Bakı, Parlament prospekti 76");
    }

    [Theory]
    [InlineData("7 nömrəli Şəhər Poliklinikası", DirectoryFacilityKind.Polyclinic)]
    [InlineData("Caspian International Hospital", DirectoryFacilityKind.Hospital)]
    [InlineData("Referans Medical Laboratory", DirectoryFacilityKind.Laboratory)]
    [InlineData("Zəfəran Aptek", DirectoryFacilityKind.Pharmacy)]
    public void Facility_kind_is_classified_from_azerbaijani_and_english_names(
        string name,
        DirectoryFacilityKind expected)
    {
        FacilityDirectorySyncService.ClassifyKind(name).Should().Be(expected);
    }

    [Fact]
    public void Similarity_tolerates_small_registry_spelling_changes()
    {
        var score = FacilityDirectorySyncService.NameSimilarity(
            FacilityDirectoryNormalizer.Normalize("Respublika Neyrocərrahiyyə Xəstəxanası"),
            FacilityDirectoryNormalizer.Normalize("Respublika Neyrocerrahiyye Xestexanasi"));

        score.Should().BeGreaterThanOrEqualTo(80);
    }

    [Fact]
    public void Exact_chain_names_are_not_enough_to_identify_a_physical_branch()
    {
        FacilityDirectorySyncService.NameSimilarity(
            FacilityDirectoryNormalizer.Normalize("Zəfəran Aptek"),
            FacilityDirectoryNormalizer.Normalize("Zəfəran Aptek"))
            .Should().Be(100);

        // Branch identity is finalized by the importer's coordinate guard.
        FacilityDirectorySyncService.ApplyDistanceGuard(100, 500)
            .Should().BeLessThan(92);
    }

    [Fact]
    public async Task Operational_source_reads_connected_hospitals_and_pharmacies()
    {
        var databaseOptions = new DbContextOptionsBuilder<PharmacyApiDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var context = new PharmacyApiDbContext(databaseOptions);
        var company = new PharmacyCompany { Id = Guid.NewGuid(), Name = "PIYA Pharmacy Network" };
        context.Hospitals.Add(new Hospital
        {
            Id = Guid.NewGuid(), Name = "Connected Hospital", Address = "Bakı",
            City = "Bakı", Country = "Azerbaijan", PhoneNumber = "+994121234567"
        });
        context.Pharmacies.Add(new Pharmacy
        {
            Id = Guid.NewGuid(), Name = "Connected Pharmacy", Address = "Bakı",
            City = "Bakı", Country = "Azerbaijan", Company = company,
            Coordinates = new Coordinates { Id = Guid.NewGuid(), Latitude = 40.4, Longitude = 49.9 }
        });
        await context.SaveChangesAsync();

        var service = new FacilityDirectorySyncService(
            context,
            Mock.Of<IHttpClientFactory>(),
            Options.Create(new FacilityDirectoryOptions()),
            NullLogger<FacilityDirectorySyncService>.Instance);

        var result = await service.SyncAsync(true, [FacilityDirectorySources.PiyaOperational]);

        result.Should().ContainSingle();
        result[0].Status.Should().Be(FacilityImportStatus.Succeeded);
        result[0].RecordsRead.Should().Be(2);
        result[0].Error.Should().BeNull();
    }
}
