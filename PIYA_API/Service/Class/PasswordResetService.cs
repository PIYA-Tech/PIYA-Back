using Microsoft.EntityFrameworkCore;
using PIYA_API.Data;
using PIYA_API.Model;
using PIYA_API.Service.Interface;
using System.Security.Cryptography;
using System.Text;

namespace PIYA_API.Service.Class;

public class PasswordResetService : IPasswordResetService
{
    private readonly PharmacyApiDbContext _context;
    private readonly IEmailService _emailService;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IAuditService _auditService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<PasswordResetService> _logger;
    private readonly string _frontendUrl;

    public PasswordResetService(
        PharmacyApiDbContext context,
        IEmailService emailService,
        IPasswordHasher passwordHasher,
        IAuditService auditService,
        IConfiguration configuration,
        ILogger<PasswordResetService> logger)
    {
        _context = context;
        _emailService = emailService;
        _passwordHasher = passwordHasher;
        _auditService = auditService;
        _configuration = configuration;
        _logger = logger;
        _frontendUrl = _configuration["Frontend:BaseUrl"] ?? "http://localhost:3000";
    }

    public async Task<PasswordResetToken> GenerateResetTokenAsync(string email, string ipAddress, string userAgent)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user == null)
        {
            // Don't reveal that user doesn't exist for security reasons
            _logger.LogWarning("Password reset requested for non-existent email: {Email}", email);
            throw new KeyNotFoundException("If an account with that email exists, a reset link has been sent");
        }

        // Revoke existing tokens
        await RevokeAllTokensForUserAsync(user.Id);

        // Generate random token
        var tokenBytes = RandomNumberGenerator.GetBytes(32);
        var token = Convert.ToBase64String(tokenBytes).Replace("+", "-").Replace("/", "_").TrimEnd('=');

        // Hash token for storage
        var tokenHash = HashToken(token);

        var resetToken = new PasswordResetToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = tokenHash,
            Email = email,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddHours(1), // 1-hour validity
            RequestIpAddress = ipAddress,
            UserAgent = userAgent
        };

        _context.PasswordResetTokens.Add(resetToken);
        await _context.SaveChangesAsync();

        // Send password reset email
        var resetUrl = $"{_frontendUrl}/reset-password?token={token}";
        await _emailService.SendPasswordResetAsync(
            user.Email,
            $"{user.FirstName} {user.LastName}",
            token,
            resetUrl
        );

        // Log audit trail
        await _auditService.LogAsync(new AuditLog
        {
            UserId = user.Id,
            Action = "PasswordResetRequested",
            EntityType = "User",
            EntityId = user.Id.ToString(),
            Description = $"Password reset requested from IP: {ipAddress}",
            IpAddress = ipAddress,
            UserAgent = userAgent,
            CreatedAt = DateTime.UtcNow
        });

        _logger.LogInformation("Password reset token generated for user {UserId}", user.Id);

        return resetToken;
    }

    public async Task<bool> ValidateResetTokenAsync(string token)
    {
        var tokenHash = HashToken(token);

        var resetToken = await _context.PasswordResetTokens
            .FirstOrDefaultAsync(prt => prt.TokenHash == tokenHash && !prt.IsUsed && !prt.IsRevoked);

        if (resetToken == null)
        {
            return false;
        }

        if (resetToken.ExpiresAt < DateTime.UtcNow)
        {
            return false;
        }

        return true;
    }

    public async Task<bool> ResetPasswordAsync(string token, string newPassword, string ipAddress)
    {
        var tokenHash = HashToken(token);

        var resetToken = await _context.PasswordResetTokens
            .Include(prt => prt.User)
            .FirstOrDefaultAsync(prt => prt.TokenHash == tokenHash && !prt.IsUsed && !prt.IsRevoked);

        if (resetToken == null)
        {
            _logger.LogWarning("Invalid or already used password reset token");
            return false;
        }

        if (resetToken.ExpiresAt < DateTime.UtcNow)
        {
            _logger.LogWarning("Password reset token expired for user {UserId}", resetToken.UserId);
            return false;
        }

        // Hash new password
        var passwordHash = _passwordHasher.HashPassword(newPassword);

        // Update user password
        resetToken.User.PasswordHash = passwordHash;
        resetToken.User.UpdatedAt = DateTime.UtcNow;

        // Mark token as used
        resetToken.IsUsed = true;
        resetToken.UsedAt = DateTime.UtcNow;
        resetToken.UsedIpAddress = ipAddress;

        await _context.SaveChangesAsync();

        // Log audit trail
        await _auditService.LogAsync(new AuditLog
        {
            UserId = resetToken.UserId,
            Action = "PasswordReset",
            EntityType = "User",
            EntityId = resetToken.UserId.ToString(),
            Description = $"Password reset successfully from IP: {ipAddress}",
            IpAddress = ipAddress,
            CreatedAt = DateTime.UtcNow
        });

        _logger.LogInformation("Password reset successfully for user {UserId}", resetToken.UserId);

        return true;
    }

    public async Task RevokeTokenAsync(string token)
    {
        var tokenHash = HashToken(token);

        var resetToken = await _context.PasswordResetTokens
            .FirstOrDefaultAsync(prt => prt.TokenHash == tokenHash && !prt.IsUsed && !prt.IsRevoked);

        if (resetToken != null)
        {
            resetToken.IsRevoked = true;
            resetToken.RevokedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            _logger.LogInformation("Password reset token revoked");
        }
    }

    public async Task RevokeAllTokensForUserAsync(Guid userId)
    {
        var pendingTokens = await _context.PasswordResetTokens
            .Where(prt => prt.UserId == userId && !prt.IsUsed && !prt.IsRevoked)
            .ToListAsync();

        foreach (var token in pendingTokens)
        {
            token.IsRevoked = true;
            token.RevokedAt = DateTime.UtcNow;
        }

        if (pendingTokens.Count != 0)
        {
            await _context.SaveChangesAsync();
            _logger.LogInformation("Revoked {Count} pending password reset tokens for user {UserId}", 
                pendingTokens.Count, userId);
        }
    }

    private static string HashToken(string token)
    {
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(token));
        return Convert.ToBase64String(hashBytes);
    }
}
