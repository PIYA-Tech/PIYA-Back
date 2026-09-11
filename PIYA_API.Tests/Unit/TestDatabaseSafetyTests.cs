using FluentAssertions;
using Xunit;

namespace PIYA_API.Tests.Unit;

public class TestDatabaseSafetyTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Host=localhost;Database=piya_db")]
    [InlineData("Host=localhost;Database=postgres")]
    [InlineData("Host=localhost;Database=latest")]
    public void RejectsMissingOrApplicationDatabase(string? connection)
    {
        var action = () => PiyaWebApplicationFactory.ValidateTestConnection(connection);
        action.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData("Host=localhost;Database=piya_test")]
    [InlineData("Host=/tmp/isolated;Database=piya_audit")]
    public void AllowsExplicitDisposableDatabase(string connection) =>
        PiyaWebApplicationFactory.ValidateTestConnection(connection).Should().Be(connection);
}
