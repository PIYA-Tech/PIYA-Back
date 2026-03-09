namespace PIYA_API.Model;

/// <summary>
/// Stores the jti (JWT ID) of access tokens that have been explicitly revoked
/// (e.g. via logout before expiry).  Rows are cleaned up once ExpiresAt passes
/// because a token that has already expired can no longer be used regardless.
/// </summary>
public class RevokedToken
{
    /// <summary>The jti claim value from the JWT (a GUID string).</summary>
    public required string Jti { get; set; }

    /// <summary>UTC time at which the original access token expires — used for cleanup.</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>UTC time this row was inserted.</summary>
    public DateTime RevokedAt { get; set; } = DateTime.UtcNow;
}
