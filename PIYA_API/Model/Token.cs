namespace PIYA_API.Model;

public class Token
{
    public Guid Id { get; set; }
    public string AccessToken { get; set; } = string.Empty;
    public string? RefreshToken { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime CreationTime { get; set; }
    public string DeviceInfo { get; set; } = string.Empty;

    /// <summary>FK to the owning User — enables O(1) purge on login without parsing every JWT row.</summary>
    public Guid? UserId { get; set; }

    /// <summary>
    /// Refresh token rotation family ID. All rotations of the same original token share this value.
    /// If a refresh token from this family is reused after rotation (reuse attack),
    /// the entire family is revoked.
    /// </summary>
    public Guid Family { get; set; } = Guid.NewGuid();

    public bool IsExpired()
    {
        return DateTime.UtcNow >= ExpiresAt;
    }

    public Token()
    {
        CreationTime = DateTime.UtcNow;
        ExpiresAt = CreationTime.AddMinutes(30);
    }
}
