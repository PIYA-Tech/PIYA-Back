using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using PIYA_API.Data;
using PIYA_API.Model;
using PIYA_API.Service.Interface;

namespace PIYA_API.Service.Class;

public class JwtService(PharmacyApiDbContext dbContext, IConfiguration configuration) : IJwtService
{
    private readonly PharmacyApiDbContext _dbContext = dbContext;
    private readonly IConfiguration _configuration = configuration;
    
    private const string DefaultSecretKey = "PIYA_SECRET_KEY_CHANGE_THIS_IN_PRODUCTION_MIN_32_CHARS";
    private const string DefaultIssuer = "PIYA_API";
    private const string DefaultAudience = "PIYA_Clients";
    private const int DefaultExpirationMinutes = 30;

    public Guid GetId(string token)
    {
        var tokenObj = _dbContext.Tokens.FirstOrDefault(t => t.AccessToken == token);
        return tokenObj == null ? Guid.Empty : tokenObj.Id;
    }

    public async Task<TokenResponse?> GenerateSecurityToken(string username)
    {
        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Username == username);
        if (user == null)
        {
            return null;
        }

        // Get JWT settings from configuration
        var secretKey = _configuration["Jwt:SecretKey"] ?? DefaultSecretKey;
        var issuer = _configuration["Jwt:Issuer"] ?? DefaultIssuer;
        var audience = _configuration["Jwt:Audience"] ?? DefaultAudience;
        var expirationMinutes = int.TryParse(_configuration["Jwt:ExpirationMinutes"], out var mins) 
            ? mins 
            : DefaultExpirationMinutes;

        // Create security key
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        // Create claims
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Role, user.Role.ToString()),
            new Claim("firstName", user.FirstName),
            new Claim("lastName", user.LastName),
            new Claim("role", user.Role.ToString())
        };

        // Create token
        var tokenDescriptor = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(expirationMinutes),
            signingCredentials: credentials
        );

        var jwtToken = new JwtSecurityTokenHandler().WriteToken(tokenDescriptor);

        // Remove all previous tokens for this user via the indexed UserId column (O(log n))
        var existingTokens = await _dbContext.Tokens
            .Where(t => t.UserId == user.Id)
            .ToListAsync();

        if (existingTokens.Count > 0)
        {
            _dbContext.Tokens.RemoveRange(existingTokens);
        }

        // Save new token — stamp UserId so future purges are index-driven
        var refreshToken = GenerateRefreshToken();
        var tokenEntity = new Token
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            AccessToken = jwtToken,
            RefreshToken = refreshToken,
            ExpiresAt = DateTime.UtcNow.AddMinutes(expirationMinutes),
            CreationTime = DateTime.UtcNow,
            DeviceInfo = "Web"
        };

        _dbContext.Tokens.Add(tokenEntity);
        await _dbContext.SaveChangesAsync();

        return new TokenResponse
        {
            AccessToken = jwtToken,
            RefreshToken = refreshToken,
            ExpiresAt = DateTime.UtcNow.AddMinutes(expirationMinutes)
        };
    }

    public string GenerateRefreshToken()
    {
        var randomBytes = new byte[64];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomBytes);
        return Convert.ToBase64String(randomBytes);
    }

    public async Task<TokenResponse?> RefreshAccessToken(string refreshToken)
    {
        var tokenEntity = await _dbContext.Tokens
            .FirstOrDefaultAsync(t => t.RefreshToken == refreshToken);

        if (tokenEntity == null)
        {
            return null;
        }

        // Check if refresh token is expired (refresh tokens valid for 7 days)
        if (tokenEntity.CreationTime.AddDays(7) < DateTime.UtcNow)
        {
            return null;
        }

        // Find user — prefer the indexed UserId FK, fall back to JWT claim for legacy rows without UserId
        User? user = tokenEntity.UserId.HasValue
            ? await _dbContext.Users.FindAsync(tokenEntity.UserId.Value)
            : await _dbContext.Users.FirstOrDefaultAsync(u =>
                u.Username == new JwtSecurityTokenHandler()
                    .ReadJwtToken(tokenEntity.AccessToken)
                    .Claims.FirstOrDefault(c => c.Type == ClaimTypes.Name)!.Value);
        if (user == null) return null;

        // Build a new access token directly (without inserting a new Token row)
        var secretKey = _configuration["Jwt:SecretKey"] ?? DefaultSecretKey;
        var issuer = _configuration["Jwt:Issuer"] ?? DefaultIssuer;
        var audience = _configuration["Jwt:Audience"] ?? DefaultAudience;
        var expirationMinutes = int.TryParse(_configuration["Jwt:ExpirationMinutes"], out var mins)
            ? mins : DefaultExpirationMinutes;

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Role, user.Role.ToString()),
            new Claim("firstName", user.FirstName),
            new Claim("lastName", user.LastName),
            new Claim("role", user.Role.ToString())
        };

        var tokenDescriptor = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(expirationMinutes),
            signingCredentials: credentials
        );

        var newAccessToken = new JwtSecurityTokenHandler().WriteToken(tokenDescriptor);
        var newExpiry = DateTime.UtcNow.AddMinutes(expirationMinutes);

        // Rotate the refresh token — generate a fresh one so the old value is invalidated
        var newRefreshToken = GenerateRefreshToken();

        // Update the existing token row in place — new access token + rotated refresh token
        tokenEntity.UserId = user.Id;   // backfill for legacy rows that predate the UserId column
        tokenEntity.AccessToken = newAccessToken;
        tokenEntity.RefreshToken = newRefreshToken;
        tokenEntity.ExpiresAt = newExpiry;
        // Reset creation time so the 7-day refresh window starts fresh
        tokenEntity.CreationTime = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();

        return new TokenResponse
        {
            AccessToken = newAccessToken,
            RefreshToken = newRefreshToken,
            ExpiresAt = newExpiry
        };
    }

    public async Task RevokeRefreshTokenAsync(string refreshToken)
    {
        var tokenEntity = await _dbContext.Tokens
            .FirstOrDefaultAsync(t => t.RefreshToken == refreshToken);
        if (tokenEntity != null)
        {
            _dbContext.Tokens.Remove(tokenEntity);
            await _dbContext.SaveChangesAsync();
        }
    }

    public async Task RevokeAccessTokenAsync(string accessToken)
    {
        try
        {
            var jwt = new JwtSecurityTokenHandler().ReadJwtToken(accessToken);
            var jti = jwt.Id; // JwtRegisteredClaimNames.Jti
            if (string.IsNullOrWhiteSpace(jti)) return;

            // Skip if already revoked
            if (await _dbContext.RevokedTokens.AnyAsync(r => r.Jti == jti)) return;

            _dbContext.RevokedTokens.Add(new RevokedToken
            {
                Jti = jti,
                ExpiresAt = jwt.ValidTo,
            });
            await _dbContext.SaveChangesAsync();
        }
        catch
        {
            // Malformed token — nothing to revoke
        }
    }

    public string? ValidateToken(string token)
    {
        try
        {
            var secretKey = _configuration["Jwt:SecretKey"] ?? DefaultSecretKey;
            var issuer = _configuration["Jwt:Issuer"] ?? DefaultIssuer;
            var audience = _configuration["Jwt:Audience"] ?? DefaultAudience;

            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.UTF8.GetBytes(secretKey);

            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(key),
                ValidateIssuer = true,
                ValidIssuer = issuer,
                ValidateAudience = true,
                ValidAudience = audience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero
            };

            var principal = tokenHandler.ValidateToken(token, validationParameters, out _);

            // Check jti revocation blocklist
            var jti = principal.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;
            if (!string.IsNullOrWhiteSpace(jti))
            {
                var revoked = _dbContext.RevokedTokens.Any(r => r.Jti == jti);
                if (revoked) throw new UnauthorizedAccessException("Token has been revoked");
            }

            return principal.FindFirst(ClaimTypes.Name)?.Value;
        }
        catch (SecurityTokenExpiredException)
        {
            throw new UnauthorizedAccessException("Token has expired");
        }
        catch (UnauthorizedAccessException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new UnauthorizedAccessException($"Token validation failed: {ex.Message}");
        }
    }
}
