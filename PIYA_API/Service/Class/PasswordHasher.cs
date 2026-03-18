using Microsoft.Extensions.Options;
using PIYA_API.Configuration;
using PIYA_API.Service.Interface;

namespace PIYA_API.Service.Class;

public class PasswordHasher : IPasswordHasher
{
    private readonly int _workFactor;

    public PasswordHasher(IOptions<SecurityOptions> securityOptions)
    {
        // Clamp to a sensible range: BCrypt minimum is 4, recommended ceiling is 14.
        _workFactor = Math.Clamp(securityOptions.Value.PasswordHashWorkFactor, 10, 14);
    }

    /// <summary>
    /// Hashes a plain text password using BCrypt with the configured work factor.
    /// </summary>
    public string HashPassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password))
            throw new ArgumentException("Password cannot be null or empty", nameof(password));
        return BCrypt.Net.BCrypt.HashPassword(password, _workFactor);
    }

    public bool VerifyPassword(string password, string hash)
    {
        if (string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(hash))
            return false;
        try { return BCrypt.Net.BCrypt.Verify(password, hash); }
        catch { return false; }
    }
}
