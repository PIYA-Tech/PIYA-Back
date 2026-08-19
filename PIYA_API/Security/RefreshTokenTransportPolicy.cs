using Microsoft.Extensions.Primitives;

namespace PIYA_API.Security;

/// <summary>
/// Selects the refresh-token transport without trusting a spoofable User-Agent.
/// Browsers always use the HttpOnly cookie. Native applications must opt in with
/// an explicit client header and must not present browser fetch metadata.
/// </summary>
public static class RefreshTokenTransportPolicy
{
    public const string ClientHeaderName = "X-PIYA-Client";

    private static readonly HashSet<string> NativeClientNames =
        new(StringComparer.OrdinalIgnoreCase) { "iOS", "Android", "Desktop" };

    public static bool UsesResponseBody(HttpRequest request)
    {
        // Browser JavaScript cannot suppress Origin/Sec-Fetch metadata on a
        // cross-origin request. Checking it prevents XSS from opting into the
        // native transport simply by adding X-PIYA-Client.
        if (HasValue(request.Headers.Origin) ||
            HasValue(request.Headers["Sec-Fetch-Site"]) ||
            HasValue(request.Headers["Sec-Fetch-Mode"]))
        {
            return false;
        }

        var clientName = request.Headers[ClientHeaderName].ToString().Trim();
        return NativeClientNames.Contains(clientName);
    }

    public static string GetDeviceInfo(HttpRequest request)
    {
        if (!UsesResponseBody(request))
            return "Web";

        return request.Headers[ClientHeaderName].ToString().Trim();
    }

    private static bool HasValue(StringValues values) =>
        !StringValues.IsNullOrEmpty(values) &&
        values.Any(value => !string.IsNullOrWhiteSpace(value));
}
