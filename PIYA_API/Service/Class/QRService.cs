using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PIYA_API.Configuration;
using PIYA_API.Data;
using PIYA_API.Model;
using PIYA_API.Service.Interface;
using QRCoder;

namespace PIYA_API.Service.Class;

public class QRService : IQRService
{
    private readonly PharmacyApiDbContext _context;
    private readonly SecurityOptions _securityOptions;
    private readonly ILogger<QRService> _logger;
    private readonly IAuditService _auditService;
    private readonly string _secretKey;
    private readonly int _tokenExpiryMinutes;

    public QRService(
        PharmacyApiDbContext context,
        IOptions<SecurityOptions> securityOptions,
        ILogger<QRService> logger,
        IAuditService auditService)
    {
        _context = context;
        _securityOptions = securityOptions.Value;
        _logger = logger;
        _auditService = auditService;
        _secretKey = _securityOptions.QrSigningKey;
        _tokenExpiryMinutes = _securityOptions.QrTokenExpiryMinutes;

        // Validate configuration on startup
        if (string.IsNullOrWhiteSpace(_secretKey) || _secretKey.Length < 32)
        {
            throw new InvalidOperationException(
                "Security:QrSigningKey must be configured and at least 32 characters. " +
                "Generate with: openssl rand -base64 32");
        }
    }

    public async Task<(string Token, Guid TokenId)> GeneratePrescriptionQrTokenAsync(
        Guid prescriptionId, 
        Guid userId, 
        string? ipAddress = null, 
        string? userAgent = null)
    {
        try
        {
            _logger.LogInformation("Generating QR token for prescription {PrescriptionId} by user {UserId}", prescriptionId, userId);

            // Verify prescription exists and belongs to user
            var prescription = await _context.Prescriptions
                .FirstOrDefaultAsync(p => p.Id == prescriptionId && p.PatientId == userId) ?? throw new InvalidOperationException($"Prescription {prescriptionId} not found or access denied");

            // Check if prescription is already used or expired
            if (prescription.Status == PrescriptionStatus.Fulfilled)
            {
                throw new InvalidOperationException("Prescription has already been fulfilled");
            }

            if (prescription.Status == PrescriptionStatus.Expired || 
                (prescription.ExpiresAt > DateTime.MinValue && prescription.ExpiresAt < DateTime.UtcNow))
            {
                throw new InvalidOperationException("Prescription has expired");
            }

            return await GenerateQrTokenAsync(prescriptionId, "Prescription", userId, validityMinutes: 5, ipAddress, userAgent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating prescription QR token");
            throw;
        }
    }

    public async Task<(string Token, Guid TokenId)> GenerateQrTokenAsync(
        Guid entityId, 
        string entityType, 
        Guid userId, 
        int? validityMinutes = null, 
        string? ipAddress = null, 
        string? userAgent = null)
    {
        try
        {
            var tokenId = Guid.NewGuid();
            var expiryMinutes = validityMinutes ?? _tokenExpiryMinutes;
            var expiresAt = DateTime.UtcNow.AddMinutes(expiryMinutes);

            // The QR token string is the opaque tokenId only — no plaintext payload is
            // embedded. All data (entityId, entityType, expiry) is resolved from the DB
            // row at scan time, so the QR code itself reveals nothing sensitive.
            var tokenString = tokenId.ToString();
            var tokenHash = ComputeSha256Hash(tokenString);

            var qrToken = new QRToken
            {
                Id = tokenId,
                TokenHash = tokenHash,
                EntityType = entityType,
                EntityId = entityId,
                GeneratedByUserId = userId,
                GeneratedAt = DateTime.UtcNow,
                ExpiresAt = expiresAt,
                GeneratedFromIp = ipAddress,
                GeneratedFromDevice = userAgent
            };

            _context.QRTokens.Add(qrToken);
            await _context.SaveChangesAsync();

            await _auditService.LogEntityActionAsync("QR_TOKEN_GENERATED", entityType, entityId.ToString(), userId,
                $"Generated QR token for {entityType} {entityId}");

            _logger.LogInformation("Generated QR token {TokenId} for {EntityType} {EntityId}, expires at {ExpiresAt}", 
                tokenId, entityType, entityId, expiresAt);

            return (tokenString, tokenId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating QR token");
            throw;
        }
    }

    public async Task<(bool IsValid, Guid PrescriptionId, string ErrorMessage)> ValidateAndUsePrescriptionQrTokenAsync(
        string token, 
        Guid pharmacistUserId, 
        string? ipAddress = null, 
        string? userAgent = null)
    {
        try
        {
            _logger.LogInformation("Validating prescription QR token by pharmacist {PharmacistId}", pharmacistUserId);

            // A dispensing operation owns a wider transaction that also covers stock
            // deduction and prescription state. Participate in that transaction when one
            // exists; otherwise keep this lower-level operation atomic on its own.
            if (_context.Database.CurrentTransaction != null)
                return await ValidateAndUsePrescriptionQrTokenCoreAsync(
                    token, pharmacistUserId, ipAddress, userAgent);

            var strategy = _context.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                await using var tx = await _context.Database.BeginTransactionAsync(
                    System.Data.IsolationLevel.Serializable);
                try
                {
                    var result = await ValidateAndUsePrescriptionQrTokenCoreAsync(
                        token, pharmacistUserId, ipAddress, userAgent);
                    if (!result.IsValid)
                    {
                        await tx.RollbackAsync();
                        return result;
                    }

                    await tx.CommitAsync();
                    return result;
                }
                catch
                {
                    await tx.RollbackAsync();
                    throw;
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating prescription QR token");
            return (false, Guid.Empty, "QR token validation failed");
        }
    }

    private async Task<(bool IsValid, Guid PrescriptionId, string ErrorMessage)>
        ValidateAndUsePrescriptionQrTokenCoreAsync(
            string token,
            Guid pharmacistUserId,
            string? ipAddress,
            string? userAgent)
    {
        var (isValid, entityId, entityType, _, errorMessage) =
            await ValidateQrTokenAsync(token);

        if (!isValid)
            return (false, Guid.Empty, errorMessage);

        if (!string.Equals(entityType, "Prescription", StringComparison.Ordinal))
            return (false, Guid.Empty, "Invalid token type - expected Prescription QR token");

        var marked = await MarkTokenAsUsedAsync(
            token, pharmacistUserId, ipAddress, userAgent);
        if (!marked)
            return (false, Guid.Empty, "Token was already used or not found");

        await _auditService.LogEntityActionAsync(
            "PRESCRIPTION_QR_SCANNED",
            "Prescription",
            entityId.ToString(),
            pharmacistUserId,
            $"Scanned and validated prescription {entityId}");

        _logger.LogInformation(
            "Successfully validated and used prescription QR token for prescription {PrescriptionId}",
            entityId);

        return (true, entityId, string.Empty);
    }

    public async Task<(bool IsValid, Guid EntityId, string EntityType, DateTime ExpiresAt, string ErrorMessage)> ValidateQrTokenAsync(string token)
    {
        try
        {
            // Token is an opaque UUID string — parse it directly.
            if (!Guid.TryParse(token, out var tokenId))
                return (false, Guid.Empty, string.Empty, DateTime.MinValue, "Invalid token format");

            var tokenHash = ComputeSha256Hash(token);
            var dbToken = await _context.QRTokens
                .FirstOrDefaultAsync(t => t.TokenHash == tokenHash);

            if (dbToken == null)
                return (false, Guid.Empty, string.Empty, DateTime.MinValue, "Token not found");

            if (dbToken.IsRevoked)
                return (false, Guid.Empty, string.Empty, DateTime.MinValue, $"Token has been revoked: {dbToken.RevocationReason}");

            if (dbToken.IsUsed)
                return (false, Guid.Empty, string.Empty, DateTime.MinValue, $"Token has already been used at {dbToken.UsedAt:yyyy-MM-dd HH:mm:ss} UTC");

            if (DateTime.UtcNow > dbToken.ExpiresAt)
                return (false, Guid.Empty, string.Empty, DateTime.MinValue, $"Token expired at {dbToken.ExpiresAt:yyyy-MM-dd HH:mm:ss} UTC");

            return (true, dbToken.EntityId, dbToken.EntityType, dbToken.ExpiresAt, string.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating QR token");
            return (false, Guid.Empty, string.Empty, DateTime.MinValue, $"Validation error: {ex.Message}");
        }
    }

    public async Task<bool> MarkTokenAsUsedAsync(string token, Guid usedByUserId, string? ipAddress = null, string? userAgent = null)
    {
        try
        {
            var tokenHash = ComputeSha256Hash(token);
            var dbToken = await _context.QRTokens
                .FirstOrDefaultAsync(t => t.TokenHash == tokenHash);

            if (dbToken == null)
            {
                _logger.LogWarning("Attempted to mark non-existent token as used");
                return false;
            }

            if (dbToken.IsUsed)
            {
                _logger.LogWarning("Token {TokenId} already marked as used", dbToken.Id);
                return false;
            }

            dbToken.IsUsed = true;
            dbToken.UsedAt = DateTime.UtcNow;
            dbToken.UsedByUserId = usedByUserId;
            dbToken.UsedFromIp = ipAddress;
            dbToken.UsedFromDevice = userAgent;
            // Record the actual scan in the validation counter (moved here from ValidateQrTokenAsync
            // so that read-only preview calls don't inflate the count).
            dbToken.ValidationAttempts++;
            dbToken.LastValidationAttempt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            await _auditService.LogEntityActionAsync("QR_TOKEN_USED", dbToken.EntityType, dbToken.EntityId.ToString(), usedByUserId,
                $"Used QR token for {dbToken.EntityType} {dbToken.EntityId}");

            _logger.LogInformation("Marked token {TokenId} as used by user {UserId}", dbToken.Id, usedByUserId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error marking token as used");
            return false;
        }
    }

    public async Task<bool> RevokeTokenAsync(string token, Guid revokedByUserId, string reason)
    {
        try
        {
            var tokenHash = ComputeSha256Hash(token);
            var dbToken = await _context.QRTokens
                .FirstOrDefaultAsync(t => t.TokenHash == tokenHash);

            if (dbToken == null)
            {
                _logger.LogWarning("Attempted to revoke non-existent token");
                return false;
            }

            if (dbToken.IsRevoked)
            {
                _logger.LogWarning("Token {TokenId} already revoked", dbToken.Id);
                return false;
            }

            dbToken.IsRevoked = true;
            dbToken.RevokedAt = DateTime.UtcNow;
            dbToken.RevokedByUserId = revokedByUserId;
            dbToken.RevocationReason = reason;

            await _context.SaveChangesAsync();

            await _auditService.LogEntityActionAsync("QR_TOKEN_REVOKED", dbToken.EntityType, dbToken.EntityId.ToString(), revokedByUserId,
                $"Revoked QR token for {dbToken.EntityType} {dbToken.EntityId}: {reason}");

            _logger.LogInformation("Revoked token {TokenId} by user {UserId}: {Reason}", dbToken.Id, revokedByUserId, reason);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error revoking token");
            return false;
        }
    }

    public async Task<(QRTokenStatus Status, DateTime? ExpiresAt)> GetTokenStatusAsync(string token)
    {
        try
        {
            var tokenHash = ComputeSha256Hash(token);
            var dbToken = await _context.QRTokens
                .FirstOrDefaultAsync(t => t.TokenHash == tokenHash);

            if (dbToken == null)
            {
                return (QRTokenStatus.Expired, null); // Treat non-existent as expired
            }

            if (dbToken.IsRevoked)
            {
                return (QRTokenStatus.Revoked, dbToken.ExpiresAt);
            }

            if (dbToken.IsUsed)
            {
                return (QRTokenStatus.Used, dbToken.ExpiresAt);
            }

            if (DateTime.UtcNow > dbToken.ExpiresAt)
            {
                return (QRTokenStatus.Expired, dbToken.ExpiresAt);
            }

            return (QRTokenStatus.Active, dbToken.ExpiresAt);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting token status");
            return (QRTokenStatus.Expired, null);
        }
    }

    public async Task<List<QRToken>> GetTokenHistoryAsync(Guid entityId, string entityType)
    {
        try
        {
            return await _context.QRTokens
                .Where(t => t.EntityId == entityId && t.EntityType == entityType)
                .OrderByDescending(t => t.GeneratedAt)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting token history");
            return [];
        }
    }

    public async Task<QRToken?> GetActiveTokenAsync(Guid entityId, string entityType)
    {
        try
        {
            return await _context.QRTokens
                .Where(t => t.EntityId == entityId && 
                           t.EntityType == entityType &&
                           !t.IsUsed && 
                           !t.IsRevoked && 
                           t.ExpiresAt > DateTime.UtcNow)
                .OrderByDescending(t => t.GeneratedAt)
                .FirstOrDefaultAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting active token");
            return null;
        }
    }

    public async Task<int> CleanupExpiredTokensAsync(int daysOld = 7)
    {
        try
        {
            var cutoffDate = DateTime.UtcNow.AddDays(-daysOld);
            var expiredTokens = await _context.QRTokens
                .Where(t => t.ExpiresAt < cutoffDate)
                .ToListAsync();

            if (expiredTokens.Count > 0)
            {
                _context.QRTokens.RemoveRange(expiredTokens);
                await _context.SaveChangesAsync();

                _logger.LogInformation("Cleaned up {Count} expired QR tokens older than {Days} days", 
                    expiredTokens.Count, daysOld);
            }

            return expiredTokens.Count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cleaning up expired tokens");
            return 0;
        }
    }

    public Task<string> GenerateQrCodeImageAsync(string data)
    {
        if (string.IsNullOrWhiteSpace(data))
            throw new ArgumentException("QR data is required", nameof(data));
        if (Encoding.UTF8.GetByteCount(data) > 2048)
            throw new ArgumentException("QR data exceeds the supported size", nameof(data));

        using var generator = new QRCodeGenerator();
        using var qrData = generator.CreateQrCode(data, QRCodeGenerator.ECCLevel.Q);
        using var qrCode = new PngByteQRCode(qrData);
        return Task.FromResult(Convert.ToBase64String(qrCode.GetGraphic(12)));
    }

    // Private helper methods
    private string GenerateHmacSignature(string data)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_secretKey));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
        return Convert.ToBase64String(hash);
    }

    private static string ComputeSha256Hash(string rawData)
    {
        using var sha256 = SHA256.Create();
        var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(rawData));
        return Convert.ToBase64String(bytes);
    }
}
