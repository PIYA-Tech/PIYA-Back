using System.IdentityModel.Tokens.Jwt;
using System.Data;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using PIYA_API.Configuration;
using PIYA_API.Data;
using PIYA_API.Model;
using PIYA_API.Service.Interface;

namespace PIYA_API.Service.Class;

public class JwtService(
    PharmacyApiDbContext dbContext,
    IConfiguration configuration,
    IDistributedCacheWrapper distributedCache,
    IOptions<SecurityOptions> securityOptions,
    ILogger<JwtService> logger) : IJwtService
{
    private readonly PharmacyApiDbContext _dbContext = dbContext;
    private readonly IConfiguration _configuration = configuration;
    private readonly IDistributedCacheWrapper _cache = distributedCache;
    private readonly SecurityOptions _securityOptions = securityOptions.Value;
    private readonly ILogger<JwtService> _logger = logger;

    private const string DefaultIssuer = "PIYA_API";
    private const string DefaultAudience = "PIYA_Clients";
    private const int DefaultExpirationMinutes = 15;
    // Prefix for jti blocklist keys in the distributed cache
    private const string RevokedJtiPrefix = "revoked_jti:";

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static string HashToken(string rawToken)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private (string secret, string issuer, string audience, int expiryMinutes) GetJwtConfig()
    {
        var secret = _configuration["Jwt:SecretKey"];
        // Fail fast rather than silently using an insecure default.
        // Startup validation in Program.cs (AddOptions + ValidateOnStart) should catch
        // this before any request reaches here; this guard is a defence-in-depth backstop.
        if (string.IsNullOrWhiteSpace(secret) || secret.Contains("CHANGE", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Jwt:SecretKey is not configured or still contains the placeholder value. " +
                "Set a strong random key (minimum 32 chars) via environment variable or secrets.");
        var issuer = _configuration["Jwt:Issuer"] ?? DefaultIssuer;
        var audience = _configuration["Jwt:Audience"] ?? DefaultAudience;
        var expiry = int.TryParse(_configuration["Jwt:ExpirationMinutes"], out var m) ? m : DefaultExpirationMinutes;
        return (secret, issuer, audience, expiry);
    }

    private string BuildAccessToken(User user, Guid family, string secret, string issuer, string audience, int expiryMinutes)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Role, user.Role.ToString()),
            new Claim("firstName", user.FirstName),
            new Claim("lastName", user.LastName),
            new Claim("role", user.Role.ToString()),
            new Claim("piya_session", family.ToString()),
            new Claim("piya_security_stamp", user.SecurityStamp.ToString()),
        };
        var descriptor = new JwtSecurityToken(issuer, audience, claims,
            expires: DateTime.UtcNow.AddMinutes(expiryMinutes),
            signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(descriptor);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Generate (Login / Register)
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<TokenResponse?> GenerateSecurityToken(string username, string deviceInfo = "Unknown")
    {
        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Username == username);
        if (user == null || !user.IsActive) return null;

        var (secret, issuer, audience, expiryMinutes) = GetJwtConfig();
        var family = Guid.NewGuid();
        var jwtToken = BuildAccessToken(user, family, secret, issuer, audience, expiryMinutes);
        var refreshToken = GenerateRefreshToken();
        var refreshTokenHash = HashToken(refreshToken);

        // NpgsqlRetryingExecutionStrategy requires all manual transactions to be
        // wrapped in CreateExecutionStrategy so the strategy can retry on transient
        // failures without throwing "does not support user-initiated transactions".
        var strategy = _dbContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
        await using var tx = await _dbContext.Database.BeginTransactionAsync();
        try
        {
            // ── Session limit enforcement ──────────────────────────────────────
            var maxSessions = _securityOptions.MaxConcurrentSessions;
            if (maxSessions > 0)
            {
                var sessions = await _dbContext.Tokens
                    .Where(t => t.UserId == user.Id)
                    .OrderBy(t => t.CreationTime)
                    .ToListAsync();

                // Evict oldest sessions until we are below the limit (leaving room for the new one)
                while (sessions.Count >= maxSessions)
                {
                    var oldest = sessions[0];
                    _dbContext.Tokens.Remove(oldest);
                    sessions.RemoveAt(0);
                    _logger.LogInformation(
                        "Session limit: evicted oldest session for user {UserId} (family {Family})",
                        user.Id, oldest.Family);
                }
            }

            var tokenEntity = new Token
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                SecurityStamp = user.SecurityStamp,
                // Do NOT store the raw JWT — it is a bearer secret and should never be
                // persisted to the database. Revocation is handled via the jti blocklist
                // in the distributed cache (RevokeAccessTokenAsync / IsTokenRevokedAsync).
                AccessToken = string.Empty,
                // Store SHA-256 hash — raw token never persisted to DB
                RefreshToken = refreshTokenHash,
                Family = family,               // each new login starts a fresh family
                ExpiresAt = DateTime.UtcNow.AddMinutes(expiryMinutes),
                CreationTime = DateTime.UtcNow,
                DeviceInfo = deviceInfo,
            };

            _dbContext.Tokens.Add(tokenEntity);
            await _dbContext.SaveChangesAsync();
            await tx.CommitAsync();

            return new TokenResponse
            {
                AccessToken = jwtToken,
                RefreshToken = refreshToken,
                ExpiresAt = tokenEntity.ExpiresAt,
            };
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
        });
    }

    public string GenerateRefreshToken()
    {
        var bytes = new byte[64];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Refresh — with Rotation + Reuse Detection
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<TokenResponse?> RefreshAccessToken(string refreshToken)
    {
        var hash = HashToken(refreshToken);
        var strategy = _dbContext.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            // SERIALIZABLE makes the "not used + currently active + rotate" sequence
            // one indivisible operation. A competing request is retried and then sees
            // the used-token row instead of minting a second refresh token.
            await using var tx = await _dbContext.Database.BeginTransactionAsync(
                IsolationLevel.Serializable);
            var now = DateTime.UtcNow;

            var usedEntry = await _dbContext.UsedRefreshTokens
                .FirstOrDefaultAsync(u => u.TokenHash == hash);

            if (usedEntry != null)
            {
                var grace = TimeSpan.FromSeconds(
                    _securityOptions.RefreshTokenConcurrencyGraceSeconds);
                if (now - usedEntry.CreatedAt <= grace)
                {
                    _logger.LogInformation(
                        "Rejected concurrent refresh for user {UserId} family {Family} without revoking the session.",
                        usedEntry.UserId, usedEntry.Family);
                    await tx.CommitAsync();
                    throw new ConcurrentRefreshTokenException(usedEntry.UserId);
                }

                _logger.LogWarning(
                    "Refresh token reuse detected for user {UserId} family {Family}. Revoking all sessions.",
                    usedEntry.UserId, usedEntry.Family);

                var familySessions = await _dbContext.Tokens
                    .Where(t => t.Family == usedEntry.Family)
                    .ToListAsync();
                _dbContext.Tokens.RemoveRange(familySessions);
                await _dbContext.SaveChangesAsync();
                await tx.CommitAsync();
                throw new RefreshTokenReuseException(usedEntry.UserId);
            }

            var tokenEntity = await _dbContext.Tokens
                .FirstOrDefaultAsync(t => t.RefreshToken == hash);

            if (tokenEntity == null)
            {
                await tx.CommitAsync();
                return null;
            }

            // Absolute expiry: 7 days from original issuance (non-sliding)
            if (tokenEntity.CreationTime.AddDays(7) < now)
            {
                _dbContext.Tokens.Remove(tokenEntity);
                await _dbContext.SaveChangesAsync();
                await tx.CommitAsync();
                return null;
            }

            var user = await _dbContext.Users.FindAsync(tokenEntity.UserId);
            if (user is null || !user.IsActive || tokenEntity.SecurityStamp == Guid.Empty || tokenEntity.SecurityStamp != user.SecurityStamp)
            {
                _dbContext.Tokens.Remove(tokenEntity);
                await _dbContext.SaveChangesAsync();
                await tx.CommitAsync();
                return null;
            }

            var (secret, issuer, audience, expiryMinutes) = GetJwtConfig();
            var newAccessToken = BuildAccessToken(user, tokenEntity.Family, secret, issuer, audience, expiryMinutes);
            var newRefreshToken = GenerateRefreshToken();
            var newRefreshHash = HashToken(newRefreshToken);
            var newExpiry = now.AddMinutes(expiryMinutes);

            _dbContext.UsedRefreshTokens.Add(new UsedRefreshToken
            {
                TokenHash = hash,
                Family = tokenEntity.Family,
                UserId = user.Id,
                ExpiresAt = tokenEntity.CreationTime.AddDays(8),
                CreatedAt = now,
            });

            tokenEntity.AccessToken = string.Empty;
            tokenEntity.RefreshToken = newRefreshHash;
            tokenEntity.ExpiresAt = newExpiry;
            // CreationTime intentionally stays fixed to enforce absolute expiry.

            await _dbContext.SaveChangesAsync();
            await tx.CommitAsync();

            return new TokenResponse
            {
                AccessToken = newAccessToken,
                RefreshToken = newRefreshToken,
                ExpiresAt = newExpiry,
            };
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Revocation
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<bool> RevokeRefreshTokenAsync(string refreshToken, Guid expectedUserId)
    {
        var hash = HashToken(refreshToken);
        var tokenEntity = await _dbContext.Tokens
            .FirstOrDefaultAsync(t => t.RefreshToken == hash && t.UserId == expectedUserId);
        if (tokenEntity == null)
            return false;

        _dbContext.Tokens.Remove(tokenEntity);
        await _dbContext.SaveChangesAsync();
        return true;
    }

    public async Task RevokeSessionAsync(Guid userId, Guid family)
    {
        var sessions = await _dbContext.Tokens.Where(t => t.UserId == userId && t.Family == family).ToListAsync();
        _dbContext.Tokens.RemoveRange(sessions);
        await _dbContext.SaveChangesAsync();
    }

    public async Task RevokeAccessTokenAsync(string accessToken)
    {
        try
        {
            var jwt = new JwtSecurityTokenHandler().ReadJwtToken(accessToken);
            var jti = jwt.Id;
            if (string.IsNullOrWhiteSpace(jti)) return;

            var ttl = jwt.ValidTo - DateTime.UtcNow;
            if (ttl <= TimeSpan.Zero) return; // already expired — no need to blocklist

            // ── Persist to DB ────────────────────────────────────────────────
            if (!await _dbContext.RevokedTokens.AnyAsync(r => r.Jti == jti))
            {
                _dbContext.RevokedTokens.Add(new RevokedToken
                {
                    Jti = jti,
                    ExpiresAt = jwt.ValidTo,
                });
                await _dbContext.SaveChangesAsync();
            }

            // ── Cache in Redis / IDistributedCache with matching TTL ─────────
            await _cache.SetStringAsync(
                $"{RevokedJtiPrefix}{jti}",
                "1",
                ttl);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to revoke access token — malformed JWT?");
        }
    }

    /// <summary>
    /// Cache-first jti revocation check.
    /// Redis hit → O(1), no DB round-trip.
    /// Cache miss → DB fallback, then backfills the cache for future requests.
    /// </summary>
    public async Task<bool> IsJtiRevokedAsync(string jti)
    {
        var cacheKey = $"{RevokedJtiPrefix}{jti}";

        // Fast path: cache hit
        var cached = await _cache.GetStringAsync(cacheKey);
        if (cached is not null) return true;

        // Slow path: DB
        var inDb = await _dbContext.RevokedTokens.AnyAsync(r => r.Jti == jti);
        if (inDb)
        {
            // Backfill cache — use a 5-min TTL so we don't hold stale entries forever
            // (the cleanup service will purge the DB row once the token expires anyway)
            await _cache.SetStringAsync(cacheKey, "1", TimeSpan.FromMinutes(5));
        }
        return inDb;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Validation
    // ─────────────────────────────────────────────────────────────────────────

    private IQueryable<Token> CurrentSessionQuery(ClaimsPrincipal principal)
    {
        // Legacy tokens without a session/stamp are intentionally rejected. The
        // deployment handoff requires signing in again after this security upgrade.
        if (!Guid.TryParse(principal.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId) ||
            !Guid.TryParse(principal.FindFirst("piya_session")?.Value, out var family) ||
            !Guid.TryParse(principal.FindFirst("piya_security_stamp")?.Value, out var stamp) || stamp == Guid.Empty)
            return _dbContext.Tokens.Where(_ => false);
        var role = principal.FindFirst(ClaimTypes.Role)?.Value;
        if (!Enum.TryParse<UserRole>(role, out var parsedRole))
            return _dbContext.Tokens.Where(_ => false);
        var oldest = DateTime.UtcNow.AddDays(-7);
        return from session in _dbContext.Tokens.AsNoTracking()
               join user in _dbContext.Users.AsNoTracking() on session.UserId equals user.Id
               where user.Id == userId && user.IsActive && user.Role == parsedRole &&
                     user.SecurityStamp == stamp && session.SecurityStamp == stamp &&
                     session.Family == family && session.CreationTime > oldest
               select session;
    }

    public Task<bool> IsSessionCurrentAsync(ClaimsPrincipal principal) => CurrentSessionQuery(principal).AnyAsync();

    public string? ValidateToken(string token)
    {
        try
        {
            var (secret, issuer, audience, _) = GetJwtConfig();
            var tokenHandler = new JwtSecurityTokenHandler();
            var principal = tokenHandler.ValidateToken(token,
                new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
                    ValidateIssuer = true, ValidIssuer = issuer,
                    ValidateAudience = true, ValidAudience = audience,
                    ValidateLifetime = true, ClockSkew = TimeSpan.Zero,
                }, out _);

            var jti = principal.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;
            if (!string.IsNullOrWhiteSpace(jti))
            {
                // Use async-over-sync as last resort — this legacy path is only called
                // by POST /api/auth/validate-token which is not a hot path.
                // The JWT middleware (hot path) uses IsJtiRevokedAsync properly.
                var revoked = _dbContext.RevokedTokens
                    .AsNoTracking()
                    .Any(r => r.Jti == jti);
                if (revoked) throw new UnauthorizedAccessException("Token has been revoked");
            }

            if (!CurrentSessionQuery(principal).Any())
                throw new UnauthorizedAccessException("This session has ended. Sign in again.");
            return principal.FindFirst(ClaimTypes.Name)?.Value;
        }
        catch (SecurityTokenExpiredException)
        {
            throw new UnauthorizedAccessException("Token has expired");
        }
        catch (UnauthorizedAccessException) { throw; }
        catch (Exception ex)
        {
            throw new UnauthorizedAccessException($"Token validation failed: {ex.Message}");
        }
    }
}
