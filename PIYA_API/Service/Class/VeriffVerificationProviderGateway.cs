using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PIYA_API.Configuration;
using PIYA_API.Model;
using PIYA_API.Service.Interface;

namespace PIYA_API.Service.Class;

/// <summary>
/// Veriff hosted document + selfie integration. Raw images never pass through
/// PIYA. Decisions are polled by our server and authenticated before use.
/// Veriff identity verification is NOT insurance eligibility verification.
/// </summary>
public sealed class VeriffVerificationProviderGateway(HttpClient http, IOptions<VeriffOptions> options)
    : IVerificationProviderGateway
{
    private readonly VeriffOptions _options = options.Value;
    private bool Configured => _options.Enabled &&
        !string.IsNullOrWhiteSpace(_options.ApiKey) && !string.IsNullOrWhiteSpace(_options.SharedSecret) &&
        SafeUrl(_options.BaseUrl, allowSessionHost: false) &&
        new Uri(_options.BaseUrl).AbsolutePath == "/" &&
        string.IsNullOrEmpty(new Uri(_options.BaseUrl).Query) && string.IsNullOrEmpty(new Uri(_options.BaseUrl).Fragment);

    public VerificationProviderCapability GetCapability(PatientVerificationKind kind) => kind switch
    {
        PatientVerificationKind.Identity => new(Configured, "Veriff", Configured
            ? (_options.LiveMode ? "Verify your identity securely with Veriff. Photos are captured by Veriff, not PIYA."
                : "Veriff test mode. Test decisions do not verify your PIYA identity.")
            : "Veriff identity verification is not activated yet. Your PIYA account remains usable."),
        _ => new(false, null, "Insurance coverage checks require a connected Azerbaijani insurer. PIYA cannot yet confirm your policy or benefits. An identity check does not verify insurance.")
    };

    public async Task<VerificationProviderResult> StartAsync(VerificationProviderStartContext context,
        CancellationToken cancellationToken = default)
    {
        if (context.Kind != PatientVerificationKind.Identity || !Configured) return NotConnected();
        var documentType = context.DocumentType switch
        {
            "Passport" => "PASSPORT", "NationalID" => "ID_CARD", "DriversLicense" => "DRIVERS_LICENSE",
            _ => throw new InvalidOperationException("Unsupported identity document type.")
        };
        var payload = JsonSerializer.SerializeToUtf8Bytes(new { verification = new {
            endUserId = context.PatientId.ToString(),
            document = new { type = documentType, country = context.CountryCode ?? "AZ" }
        } });
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(_options.BaseUrl), "v1/sessions"));
        request.Content = new ByteArrayContent(payload);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        using var result = await SendAsync(request, payload, cancellationToken);
        var verification = result.RootElement.GetProperty("verification");
        var reference = Text(verification, "id");
        var actionUrl = Text(verification, "url");
        if (!Guid.TryParse(reference, out _) || !SafeUrl(actionUrl, allowSessionHost: true) || actionUrl!.Length > 2000)
            throw new InvalidOperationException("Veriff returned an invalid verification session.");
        return new(PatientVerificationStatus.RequiresAction, "Veriff", reference, actionUrl,
            _options.LiveMode ? null : "provider_test_mode");
    }

    public async Task<VerificationProviderResult> RefreshAsync(PatientVerificationKind kind, string providerReference,
        CancellationToken cancellationToken = default)
    {
        if (kind != PatientVerificationKind.Identity || !Configured) return NotConnected();
        if (!Guid.TryParse(providerReference, out _)) throw new InvalidOperationException("Invalid Veriff session reference.");
        using var request = new HttpRequestMessage(HttpMethod.Get,
            new Uri(new Uri(_options.BaseUrl), $"v1/sessions/{providerReference}/decision"));
        using var result = await SendAsync(request, Encoding.UTF8.GetBytes(providerReference), cancellationToken);
        if (!result.RootElement.TryGetProperty("verification", out var verification))
            throw new InvalidOperationException("Veriff returned an incomplete decision.");
        if (verification.ValueKind == JsonValueKind.Null)
            return new(PatientVerificationStatus.Pending, "Veriff", providerReference,
                StatusReasonCode: _options.LiveMode ? null : "provider_test_mode");
        if (!Guid.TryParse(Text(verification, "id"), out var returnedID) || returnedID != Guid.Parse(providerReference))
            throw new InvalidOperationException("Veriff returned a different session.");
        var code = verification.TryGetProperty("code", out var value) && value.TryGetInt32(out var number) ? number : 0;
        var status = code switch {
            9001 when Text(verification, "status") == "approved" => PatientVerificationStatus.Verified,
            9102 => PatientVerificationStatus.Rejected,
            9103 => PatientVerificationStatus.RequiresAction,
            9104 or 9121 => PatientVerificationStatus.Expired,
            _ => PatientVerificationStatus.Pending
        };
        if (!_options.LiveMode)
            return new(PatientVerificationStatus.Pending, "Veriff", providerReference, StatusReasonCode: "provider_test_mode");
        VerifiedIdentity? identity = null;
        DateTime? expiresAt = null;
        if (status == PatientVerificationStatus.Verified)
        {
            if (verification.TryGetProperty("person", out var person) &&
                Guid.TryParse(Text(verification, "endUserId"), out var subject) &&
                DateOnly.TryParseExact(Text(person, "dateOfBirth"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var birth))
                identity = new(subject, Text(person, "firstName") ?? "", Text(person, "lastName") ?? "", birth);
            if (verification.TryGetProperty("document", out var document) &&
                DateOnly.TryParseExact(Text(document, "validUntil"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var expiry))
                expiresAt = expiry.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            if (identity == null || string.IsNullOrWhiteSpace(identity.FirstName) || string.IsNullOrWhiteSpace(identity.LastName))
                return new(PatientVerificationStatus.Rejected, "Veriff", providerReference, StatusReasonCode: "identity_data_missing");
            if (expiresAt <= DateTime.UtcNow) status = PatientVerificationStatus.Expired;
        }
        return new(status, "Veriff", providerReference, StatusReasonCode: code == 0 ? "provider_status_unknown" : null,
            ExpiresAt: expiresAt, Identity: identity);
    }

    private async Task<JsonDocument> SendAsync(HttpRequestMessage request, byte[] signedPayload, CancellationToken cancellationToken)
    {
        request.Headers.Add("X-AUTH-CLIENT", _options.ApiKey);
        request.Headers.Add("X-HMAC-SIGNATURE", Convert.ToHexString(Sign(signedPayload)).ToLowerInvariant());
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var response = await http.SendAsync(request, cancellationToken);
        // Deliberately never expose/log response bodies, document data or credentials.
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Veriff request failed (HTTP {(int)response.StatusCode}).");
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (bytes.Length > 1_048_576) throw new InvalidOperationException("Veriff response exceeded the permitted size.");
        var client = Header(response, "X-AUTH-CLIENT") ?? Header(response, "VRF-AUTH-CLIENT");
        var signature = Header(response, "X-HMAC-SIGNATURE") ?? Header(response, "VRF-HMAC-SIGNATURE");
        if (client != _options.ApiKey || !ValidSignature(signature, bytes))
            throw new InvalidOperationException("Veriff response authentication failed.");
        var result = JsonDocument.Parse(bytes);
        if (Text(result.RootElement, "status") != "success") {
            result.Dispose(); throw new InvalidOperationException("Veriff did not return a successful response.");
        }
        return result;
    }

    private byte[] Sign(byte[] bytes) => HMACSHA256.HashData(Encoding.UTF8.GetBytes(_options.SharedSecret), bytes);
    private bool ValidSignature(string? signature, byte[] bytes)
    {
        if (signature?.Length != 64) return false;
        try { return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(signature), Sign(bytes)); }
        catch (FormatException) { return false; }
    }
    private static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? values.SingleOrDefault() : null;
    private static string? Text(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object &&
        value.TryGetProperty(name, out var field) && field.ValueKind == JsonValueKind.String ? field.GetString() : null;
    private static bool SafeUrl(string? value, bool allowSessionHost) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.Port == 443 &&
        string.IsNullOrEmpty(uri.UserInfo) &&
        (uri.Host.EndsWith(".veriff.com", StringComparison.OrdinalIgnoreCase) || uri.Host.Equals("veriff.com", StringComparison.OrdinalIgnoreCase) ||
         (allowSessionHost && (uri.Host.EndsWith(".veriff.me", StringComparison.OrdinalIgnoreCase) || uri.Host.Equals("veriff.me", StringComparison.OrdinalIgnoreCase))));
    private static VerificationProviderResult NotConnected() => new(PatientVerificationStatus.NotConnected,
        StatusReasonCode: "provider_not_connected");
}
