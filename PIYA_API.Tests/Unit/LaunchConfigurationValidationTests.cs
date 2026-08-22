using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using PIYA_API.Configuration;
using PIYA_API.Data;
using PIYA_API.Extensions;
using PIYA_API.Model;
using PIYA_API.Service.Class;
using PIYA_API.Service.Interface;
using Xunit;

namespace PIYA_API.Tests.Unit;

public class LaunchConfigurationValidationTests
{
    private const string TestQrSigningKey =
        "CONFIGURATION_TEST_ONLY_QR_SIGNING_KEY_64_CHARACTERS_LONG_123456";

    [Fact]
    public void CoolifyShortSmtpVariables_AreMappedToAspNetConfigurationKeys()
    {
        var configuration = new ConfigurationManager();
        var variables = new Dictionary<string, string?>
        {
            ["SMTP_ENABLED"] = "true",
            ["SMTP_HOST"] = "smtp.example.test",
            ["SMTP_PORT"] = "587",
            ["SMTP_USERNAME"] = "mailer@example.test",
            ["SMTP_PASSWORD"] = "test-only-password",
            ["SMTP_FROM_EMAIL"] = "noreply@example.test",
            ["SMTP_ENABLE_SSL"] = "true"
        };

        configuration.ApplyDeploymentEnvironmentAliases(
            name => variables.GetValueOrDefault(name));

        configuration["ExternalApis:EmailService:Enabled"].Should().Be("true");
        configuration["ExternalApis:EmailService:SmtpHost"].Should().Be("smtp.example.test");
        configuration["ExternalApis:EmailService:SmtpUsername"].Should().Be("mailer@example.test");
        configuration["ExternalApis:EmailService:SmtpPassword"].Should().Be("test-only-password");
    }

    [Fact]
    public async Task S3Provider_WithWhitespaceRequiredSettings_FailsHostStartup()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Storage:Provider"] = "S3",
            ["Storage:S3:BucketName"] = " ",
            ["Storage:S3:Region"] = " ",
            ["Storage:S3:AccessKeyId"] = "\t",
            ["Storage:S3:SecretAccessKey"] = " "
        });
        builder.Services.AddPiyaFileStorage(builder.Configuration);
        using var host = builder.Build();

        var start = () => host.StartAsync();

        var exception = await start.Should().ThrowAsync<OptionsValidationException>();
        exception.Which.Failures.Should().Contain(
            failure => failure.Contains("BucketName", StringComparison.Ordinal));
        exception.Which.Failures.Should().Contain(
            failure => failure.Contains("AccessKeyId", StringComparison.Ordinal));
        exception.Which.Failures.Should().Contain(
            failure => failure.Contains("SecretAccessKey", StringComparison.Ordinal));
    }

    [Fact]
    public void EnabledEmail_WithBlankCredentials_FailsOptionsValidation()
    {
        var configuration = CreateBaseConfiguration(new Dictionary<string, string?>
        {
            ["ExternalApis:EmailService:Enabled"] = "true",
            ["ExternalApis:EmailService:SmtpHost"] = "smtp.example.test",
            ["ExternalApis:EmailService:SmtpUsername"] = " ",
            ["ExternalApis:EmailService:SmtpPassword"] = "\t",
            ["ExternalApis:EmailService:FromEmail"] = "noreply@example.test"
        });
        using var provider = BuildConfigurationProvider(
            configuration,
            Environments.Development);

        var resolve = () =>
            provider.GetRequiredService<IOptions<EmailServiceOptions>>().Value;

        resolve.Should().Throw<OptionsValidationException>()
            .WithMessage("*SmtpUsername*SmtpPassword*");
    }

    [Fact]
    public void EnabledSms_WithBlankCredentials_FailsOptionsValidation()
    {
        var configuration = CreateBaseConfiguration(new Dictionary<string, string?>
        {
            ["ExternalApis:SmsService:Enabled"] = "true",
            ["ExternalApis:SmsService:AccountSid"] = " ",
            ["ExternalApis:SmsService:AuthToken"] = "\t",
            ["ExternalApis:SmsService:FromPhoneNumber"] = "+15555550100"
        });
        using var provider = BuildConfigurationProvider(
            configuration,
            Environments.Development);

        var resolve = () =>
            provider.GetRequiredService<IOptions<SmsServiceOptions>>().Value;

        resolve.Should().Throw<OptionsValidationException>()
            .WithMessage("*AccountSid*AuthToken*");
    }

    [Fact]
    public void ProductionFrontend_UsesCanonicalSecureDefault()
    {
        var configuration = CreateBaseConfiguration();
        using var provider = BuildConfigurationProvider(
            configuration,
            Environments.Production);

        var options =
            provider.GetRequiredService<IOptions<FrontendOptions>>().Value;

        options.BaseUrl.Should().Be("https://piya.life");
    }

    [Theory]
    [InlineData("")]
    [InlineData("http://piya.life")]
    public void ProductionFrontend_RejectsExplicitBlankOrInsecureUrl(string baseUrl)
    {
        var configuration = CreateBaseConfiguration(new Dictionary<string, string?>
        {
            ["Frontend:BaseUrl"] = baseUrl
        });
        using var provider = BuildConfigurationProvider(
            configuration,
            Environments.Production);

        var resolve = () =>
            provider.GetRequiredService<IOptions<FrontendOptions>>().Value;

        resolve.Should().Throw<OptionsValidationException>()
            .WithMessage("*Frontend:BaseUrl*");
    }

    [Theory]
    [InlineData("")]
    [InlineData(TestQrSigningKey)]
    public void ProductionPrescriptionSigningKey_MustBePresentAndDistinct(
        string prescriptionSigningKey)
    {
        var configuration = CreateBaseConfiguration(
            new Dictionary<string, string?>
            {
                ["Security:PrescriptionSigningKey"] = prescriptionSigningKey
            });
        using var provider = BuildConfigurationProvider(
            configuration,
            Environments.Production);

        var resolve = () =>
            provider.GetRequiredService<IOptions<SecurityOptions>>().Value;

        resolve.Should().Throw<OptionsValidationException>()
            .WithMessage("*PrescriptionSigningKey*different*QrSigningKey*");
    }

    [Fact]
    public async Task PasswordReset_NormalizesEmailAndUsesValidDefaultFrontendUrl()
    {
        await using var context = CreateContext();
        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = "reset-user",
            FirstName = "Reset",
            LastName = "User",
            Email = "reset-user@example.test",
            PhoneNumber = "+15555550100"
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        string? resetUrl = null;
        var email = new Mock<IEmailService>();
        email.Setup(candidate => candidate.SendPasswordResetAsync(
                user.Email,
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>()))
            .Callback<string, string, string, string>(
                (_, _, _, url) => resetUrl = url)
            .Returns(Task.CompletedTask);
        var audit = new Mock<IAuditService>();
        audit.Setup(candidate => candidate.LogAsync(It.IsAny<AuditLog>()))
            .Returns(Task.CompletedTask);
        var service = new PasswordResetService(
            context,
            email.Object,
            Mock.Of<IPasswordHasher>(),
            audit.Object,
            Options.Create(new FrontendOptions()),
            Mock.Of<ILogger<PasswordResetService>>());

        await service.GenerateResetTokenAsync(
            "  RESET-USER@EXAMPLE.TEST  ",
            "127.0.0.1",
            "test-agent");

        resetUrl.Should().StartWith(
            "https://piya.life/reset-password?token=");
        Uri.TryCreate(resetUrl, UriKind.Absolute, out var parsed).Should().BeTrue();
        parsed!.Scheme.Should().Be(Uri.UriSchemeHttps);
    }

    private static PharmacyApiDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<PharmacyApiDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new PharmacyApiDbContext(options);
    }

    private static IConfiguration CreateBaseConfiguration(
        Dictionary<string, string?>? overrides = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] =
                "Host=localhost;Database=configuration_test",
            ["Security:QrSigningKey"] = TestQrSigningKey
        };
        if (overrides != null)
        {
            foreach (var (key, value) in overrides)
                values[key] = value;
        }

        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }

    private static ServiceProvider BuildConfigurationProvider(
        IConfiguration configuration,
        string environmentName)
    {
        var environment = new Mock<IWebHostEnvironment>();
        environment.SetupGet(candidate => candidate.EnvironmentName)
            .Returns(environmentName);
        var services = new ServiceCollection();
        services.AddPiyaConfiguration(configuration, environment.Object);
        return services.BuildServiceProvider();
    }
}
