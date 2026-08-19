using FluentAssertions;
using Microsoft.AspNetCore.Http;
using PIYA_API.Security;
using Xunit;

namespace PIYA_API.Tests.Unit;

public class RefreshTokenTransportPolicyTests
{
    [Theory]
    [InlineData("iOS")]
    [InlineData("Android")]
    [InlineData("Desktop")]
    public void ExplicitNativeClientWithoutBrowserMetadata_UsesResponseBody(string client)
    {
        var request = new DefaultHttpContext().Request;
        request.Headers[RefreshTokenTransportPolicy.ClientHeaderName] = client;

        RefreshTokenTransportPolicy.UsesResponseBody(request).Should().BeTrue();
        RefreshTokenTransportPolicy.GetDeviceInfo(request).Should().Be(client);
    }

    [Fact]
    public void NativeHeaderWithBrowserOrigin_UsesHttpOnlyCookie()
    {
        var request = new DefaultHttpContext().Request;
        request.Headers[RefreshTokenTransportPolicy.ClientHeaderName] = "iOS";
        request.Headers.Origin = "https://piya.life";

        RefreshTokenTransportPolicy.UsesResponseBody(request).Should().BeFalse();
        RefreshTokenTransportPolicy.GetDeviceInfo(request).Should().Be("Web");
    }

    [Fact]
    public void NativeHeaderWithFetchMetadata_UsesHttpOnlyCookie()
    {
        var request = new DefaultHttpContext().Request;
        request.Headers[RefreshTokenTransportPolicy.ClientHeaderName] = "Android";
        request.Headers["Sec-Fetch-Site"] = "same-site";

        RefreshTokenTransportPolicy.UsesResponseBody(request).Should().BeFalse();
    }

    [Fact]
    public void UserAgentAlone_IsNotANativeClientCredential()
    {
        var request = new DefaultHttpContext().Request;
        request.Headers.UserAgent = "okhttp/4.12";

        RefreshTokenTransportPolicy.UsesResponseBody(request).Should().BeFalse();
    }
}
