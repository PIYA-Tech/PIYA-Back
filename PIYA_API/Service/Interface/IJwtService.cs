namespace PIYA_API.Service.Interface;

public interface IJwtService
{
    public Task<TokenResponse?> GenerateSecurityToken(string username);
    public string? ValidateToken(string token);
    public Guid GetId(string token);
    public string GenerateRefreshToken();
    public Task<TokenResponse?> RefreshAccessToken(string refreshToken);
    public Task RevokeRefreshTokenAsync(string refreshToken);
}

public class TokenResponse
{
    public required string AccessToken { get; set; }
    public required string RefreshToken { get; set; }
    public DateTime ExpiresAt { get; set; }
}