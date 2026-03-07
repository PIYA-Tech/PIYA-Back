using Microsoft.EntityFrameworkCore;
using PIYA_API.Data;
using PIYA_API.Model;
using PIYA_API.Service.Interface;
using System.Security.Cryptography;
using System.Text;

namespace PIYA_API.Service.Class;

public class EmailVerificationService : IEmailVerificationService
{
    private readonly PharmacyApiDbContext _context;
    private readonly IEmailService _emailService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<EmailVerificationService> _logger;
    private readonly string _frontendUrl;

    public EmailVerificationService(
        PharmacyApiDbContext context,
        IEmailService emailService,
        IConfiguration configuration,
        ILogger<EmailVerificationService> logger)
    {
        _context = context;
        _emailService = emailService;
        _configuration = configuration;
        _logger = logger;
        _frontendUrl = _configuration["Frontend:BaseUrl"] ?? "http://localhost:3000";
    }

    public async Task<EmailVerificationToken> GenerateVerificationTokenAsync(Guid userId, string ipAddress, string userAgent)
    {
        var user = await _context.Users.FindAsync(userId) ?? throw new KeyNotFoundException("User not found");
        if (user.IsEmailVerified)
        {
            throw new InvalidOperationException("Email is already verified");
        }

        // Revoke any existing tokens
        await RevokeAllTokensAsync(userId);

        // Generate random token
        var tokenBytes = RandomNumberGenerator.GetBytes(32);
        var token = Convert.ToBase64String(tokenBytes).Replace("+", "-").Replace("/", "_").TrimEnd('=');

        // Hash token for storage
        var tokenHash = HashToken(token);

        var verificationToken = new EmailVerificationToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = tokenHash,
            Email = user.Email,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddHours(24), // 24-hour validity
            IpAddress = ipAddress,
            UserAgent = userAgent
        };

        _context.EmailVerificationTokens.Add(verificationToken);
        await _context.SaveChangesAsync();

        // Send verification email
        var verificationUrl = $"{_frontendUrl}/verify-email?token={token}";
        await _emailService.SendEmailVerificationAsync(
            user.Email,
            $"{user.FirstName} {user.LastName}",
            token,
            verificationUrl
        );

        _logger.LogInformation("Email verification token generated for user {UserId}", userId);

        return verificationToken;
    }

    public async Task<bool> VerifyEmailAsync(string token)
    {
        var tokenHash = HashToken(token);

        var verificationToken = await _context.EmailVerificationTokens
            .Include(evt => evt.User)
            .FirstOrDefaultAsync(evt => evt.TokenHash == tokenHash && !evt.IsUsed);

        if (verificationToken == null)
        {
            _logger.LogWarning("Invalid or already used verification token");
            return false;
        }

        if (verificationToken.ExpiresAt < DateTime.UtcNow)
        {
            _logger.LogWarning("Verification token expired for user {UserId}", verificationToken.UserId);
            return false;
        }

        // Mark token as used
        verificationToken.IsUsed = true;
        verificationToken.UsedAt = DateTime.UtcNow;

        // Update user email verification status
        verificationToken.User.IsEmailVerified = true;
        verificationToken.User.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        _logger.LogInformation("Email verified successfully for user {UserId}", verificationToken.UserId);

        return true;
    }

    public async Task ResendVerificationEmailAsync(Guid userId)
    {
        var user = await _context.Users.FindAsync(userId) ?? throw new KeyNotFoundException("User not found");
        if (user.IsEmailVerified)
        {
            throw new InvalidOperationException("Email is already verified");
        }

        // Check if a recent token exists (prevent spam) — disabled in dev, enforce in production
        var recentToken = await _context.EmailVerificationTokens
            .Where(evt => evt.UserId == userId && !evt.IsUsed)
            .OrderByDescending(evt => evt.CreatedAt)
            .FirstOrDefaultAsync();

        var cooldownSeconds = _configuration.GetValue<int>("EmailVerification:ResendCooldownSeconds", 0);
        if (cooldownSeconds > 0 && recentToken != null && recentToken.CreatedAt > DateTime.UtcNow.AddSeconds(-cooldownSeconds))
        {
            throw new InvalidOperationException($"Please wait {cooldownSeconds} seconds before requesting another verification email");
        }

        // Generate new token (will auto-revoke old ones)
        await GenerateVerificationTokenAsync(userId, "", "");
    }

    public async Task<bool> IsEmailVerifiedAsync(Guid userId)
    {
        var user = await _context.Users.FindAsync(userId);
        return user?.IsEmailVerified ?? false;
    }

    public async Task RevokeAllTokensAsync(Guid userId)
    {
        var pendingTokens = await _context.EmailVerificationTokens
            .Where(evt => evt.UserId == userId && !evt.IsUsed)
            .ToListAsync();

        foreach (var token in pendingTokens)
        {
            token.IsUsed = true;
            token.UsedAt = DateTime.UtcNow;
        }

        if (pendingTokens.Any())
        {
            await _context.SaveChangesAsync();
            _logger.LogInformation("Revoked {Count} pending verification tokens for user {UserId}", 
                pendingTokens.Count, userId);
        }
    }

    private static string HashToken(string token)
    {
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToBase64String(hashBytes);
    }
}
