namespace PIYA_API.Service.Interface;

public interface IJwtService
{
    public Task<TokenResponse?> GenerateSecurityToken(string username);
    public string? ValidateToken(string token);
    public Guid GetId(string token);
    public string GenerateRefreshToken();

    /// <summary>
    /// Validates the incoming refresh token, detects reuse attacks, rotates to a
    /// new refresh token, and returns fresh access + refresh tokens.
    /// Returns null if the token is invalid or expired.
    /// Throws <see cref="RefreshTokenReuseException"/> if reuse is detected,
    /// after revoking the entire token family.
    /// </summary>
    public Task<TokenResponse?> RefreshAccessToken(string refreshToken);
    public Task RevokeRefreshTokenAsync(string refreshToken);

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
}

public class TokenResponse
{
    public required string AccessToken { get; set; }
    public required string RefreshToken { get; set; }
    public DateTime ExpiresAt { get; set; }
}

/// <summary>Thrown when a refresh token that was already rotated is reused.</summary>
public sealed class RefreshTokenReuseException : Exception
{
    public Guid UserId { get; }
    public RefreshTokenReuseException(Guid userId)
        : base("Refresh token reuse detected — entire session family revoked.")
    {
        UserId = userId;
    }
}
