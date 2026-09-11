namespace PIYA_API.Service.Interface;

public interface IJwtService
{
    /// <summary>
    /// Generates a new access + refresh token pair for the given username.
    /// <paramref name="deviceInfo"/> is stored on the session row for audit purposes
    /// (e.g. "Web", "iOS", "Android"). Defaults to "Unknown" when not supplied.
    /// </summary>
    public Task<TokenResponse?> GenerateSecurityToken(string username, string deviceInfo = "Unknown");
    public string? ValidateToken(string token);
    public string GenerateRefreshToken();

    /// <summary>
    /// Validates the incoming refresh token, detects reuse attacks, rotates to a
    /// new refresh token, and returns fresh access + refresh tokens.
    /// Returns null if the token is invalid or expired.
    /// Throws <see cref="ConcurrentRefreshTokenException"/> when another request
    /// rotated the same token within the configured concurrency grace window.
    /// Throws <see cref="RefreshTokenReuseException"/> if reuse is detected,
    /// after revoking the entire token family.
    /// </summary>
    public Task<TokenResponse?> RefreshAccessToken(string refreshToken);
    /// <summary>
    /// Revokes a refresh token only when it belongs to <paramref name="expectedUserId"/>.
    /// Returns false for an unknown token or an ownership mismatch.
    /// </summary>
    public Task<bool> RevokeRefreshTokenAsync(string refreshToken, Guid expectedUserId);
    public Task RevokeSessionAsync(Guid userId, Guid family);

    /// <summary>
    /// Extracts the jti claim from a raw JWT string and records it in the
    /// revocation store so <see cref="ValidateToken"/> rejects it immediately,
    /// even if it has not yet expired.
    /// </summary>
    public Task RevokeAccessTokenAsync(string accessToken);

    /// <summary>
    /// Returns true if the given jti is in the revocation blocklist.
    /// Checks the distributed cache first (Redis), then falls back to the database.
    /// </summary>
    public Task<bool> IsJtiRevokedAsync(string jti);

    /// <summary>Checks current account state and the still-active session family.
    /// No positive cache is used: reset, deletion and logout take effect immediately.</summary>
    public Task<bool> IsSessionCurrentAsync(System.Security.Claims.ClaimsPrincipal principal);
}

public class TokenResponse
{
    public required string AccessToken { get; set; }
    public required string RefreshToken { get; set; }
    public DateTime ExpiresAt { get; set; }
}

/// <summary>Thrown when a refresh token that was already rotated is reused.</summary>
public sealed class RefreshTokenReuseException(Guid userId) : Exception("Refresh token reuse detected — entire session family revoked.")
{
    public Guid UserId { get; } = userId;
}

/// <summary>
/// Thrown for a harmless duplicate refresh that races with a successful rotation.
/// The session remains valid and clients should retry after accepting the winning token.
/// </summary>
public sealed class ConcurrentRefreshTokenException(Guid userId) : Exception("A concurrent token refresh already completed.")
{
    public Guid UserId { get; } = userId;
}
