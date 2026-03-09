namespace PIYA_API.Model;

/// <summary>
/// Records refresh tokens that have already been consumed (rotated away).
/// If a request arrives with a token matching one of these rows, it signals
/// a token-reuse attack — the entire token family is immediately revoked.
/// Rows are cleaned up by the RevokedTokenCleanupService once ExpiresAt passes.
/// </summary>
public class UsedRefreshToken
{
    /// <summary>SHA-256 hex hash of the raw refresh token string.</summary>
    public required string TokenHash { get; set; }

    /// <summary>The family this token belonged to (matches Token.Family).</summary>
    public Guid Family { get; set; }

    /// <summary>Owner — used for full family revocation.</summary>
    public Guid UserId { get; set; }

    /// <summary>When the original token expires (used for cleanup).</summary>
    public DateTime ExpiresAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
