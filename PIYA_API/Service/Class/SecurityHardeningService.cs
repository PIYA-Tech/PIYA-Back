using Microsoft.EntityFrameworkCore;
using PIYA_API.Data;
using PIYA_API.Service.Interface;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PIYA_API.Service.Class;

/// <summary>
/// Security hardening service implementation.
/// Brute-force state and IP blocks are stored in IDistributedCache (Redis in production,
/// in-memory fallback in development) so the data is consistent across all pods/instances.
/// IAuditService is Scoped, so it is resolved per-call via IServiceScopeFactory.
/// </summary>
public class SecurityHardeningService(
    ILogger<SecurityHardeningService> logger,
    IServiceScopeFactory scopeFactory,
    IDistributedCacheWrapper cache) : ISecurityHardeningService
{
    private readonly ILogger<SecurityHardeningService> _logger = logger;
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    private readonly IDistributedCacheWrapper _cache = cache;

    // Cache key helpers
    private static string BlockedIpKey(string ip)     => $"security:blocked_ip:{ip}";
    private static string FailedLoginsKey(string email) => $"security:failed_logins:{email}";

    /// <summary>
    /// Resolves a short-lived scope to audit-log without holding a scoped IAuditService instance.
    /// </summary>
    private async Task AuditAsync(Model.AuditLog entry)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditService>();
        await audit.LogAsync(entry);
    }

    // Common SQL injection patterns
    private static readonly Regex[] SqlInjectionPatterns = new[]
    {
        new Regex(@"(\b(SELECT|INSERT|UPDATE|DELETE|DROP|CREATE|ALTER|EXEC|EXECUTE|UNION|DECLARE)\b)", RegexOptions.IgnoreCase),
        new Regex(@"(--|\#|\/\*|\*\/)", RegexOptions.None),
        new Regex(@"('|('')|(\%27)|(0x27))", RegexOptions.None),
        new Regex(@"(\bOR\b\s*\d+\s*=\s*\d+|\bAND\b\s*\d+\s*=\s*\d+)", RegexOptions.IgnoreCase)
    };

    // Common XSS patterns
    private static readonly Regex[] XssPatterns = new[]
    {
        new Regex(@"<script[^>]*>.*?</script>", RegexOptions.IgnoreCase | RegexOptions.Singleline),
        new Regex(@"javascript:", RegexOptions.IgnoreCase),
        new Regex(@"on\w+\s*=", RegexOptions.IgnoreCase),
        new Regex(@"<iframe", RegexOptions.IgnoreCase),
        new Regex(@"<embed", RegexOptions.IgnoreCase),
        new Regex(@"<object", RegexOptions.IgnoreCase)
    };

    public async Task<SuspiciousActivityResult> DetectSuspiciousLoginAsync(string email, string ipAddress, string? userAgent = null)
    {
        var result = new SuspiciousActivityResult
        {
            IsSuspicious = false,
            Reason = "No suspicious activity detected",
            RiskScore = 0,
            ShouldBlock = false,
            RequiresCaptcha = false,
            RequiresMfa = false
        };

        var failedAttempts = await GetFailedLoginAttemptsAsync(email, TimeSpan.FromHours(1));
        
        // Check for brute force attempts
        if (failedAttempts.Count >= 5)
        {
            result.IsSuspicious = true;
            result.RiskScore += 40;
            result.Reason = "Multiple failed login attempts detected";
            result.DetectedPatterns.Add("BRUTE_FORCE");
            result.RequiresCaptcha = true;
        }

        if (failedAttempts.Count >= 10)
        {
            result.ShouldBlock = true;
            result.RiskScore += 30;
            result.RequiresMfa = true;
        }

        // Check for IP-based suspicious activity
        var recentFailuresFromIp = failedAttempts.Count(f => f.IpAddress == ipAddress);
        if (recentFailuresFromIp >= 3)
        {
            result.RiskScore += 20;
            result.DetectedPatterns.Add("IP_BASED_ATTACK");
        }

        // Check for different IPs from same email
        var uniqueIps = failedAttempts.Select(f => f.IpAddress).Distinct().Count();
        if (uniqueIps >= 5)
        {
            result.RiskScore += 30;
            result.DetectedPatterns.Add("DISTRIBUTED_ATTACK");
            result.RequiresMfa = true;
        }

        result.IsSuspicious = result.RiskScore > 30;
        result.ShouldBlock = result.RiskScore > 70;

        if (result.IsSuspicious)
        {
            await AuditAsync(new Model.AuditLog
            {
                Action = "SUSPICIOUS_LOGIN_DETECTED",
                EntityType = "Security",
                EntityId = null,
                Description = $"Suspicious login detected for {email} from {ipAddress}. Risk score: {result.RiskScore}",
                UserId = null
            });
        }

        return result;
    }

    public async Task<bool> IsIpBlockedAsync(string ipAddress)
    {
        var value = await _cache.GetStringAsync(BlockedIpKey(ipAddress));
        // Any non-null value means the key exists (and hasn't expired via TTL)
        return value != null;
    }

    public async Task BlockIpAddressAsync(string ipAddress, string reason, TimeSpan? duration = null)
    {
        // Store the reason as the cache value; TTL drives expiry automatically.
        // Permanent blocks use a 365-day TTL as a practical upper bound.
        var ttl = duration ?? TimeSpan.FromDays(365);
        await _cache.SetStringAsync(BlockedIpKey(ipAddress), reason, ttl);

        await AuditAsync(new Model.AuditLog
        {
            Action = "IP_BLOCKED",
            EntityType = "Security",
            EntityId = null,
            Description = $"IP {ipAddress} blocked. Reason: {reason}. Duration: {duration?.TotalHours ?? 0} hours",
            UserId = null
        });

        _logger.LogWarning("Blocked IP: {IpAddress}, Reason: {Reason}", ipAddress, reason);
    }

    public async Task UnblockIpAddressAsync(string ipAddress)
    {
        await _cache.RemoveAsync(BlockedIpKey(ipAddress));

        await AuditAsync(new Model.AuditLog
        {
            Action = "IP_UNBLOCKED",
            EntityType = "Security",
            EntityId = null,
            Description = $"IP {ipAddress} unblocked",
            UserId = null
        });

        _logger.LogInformation("Unblocked IP: {IpAddress}", ipAddress);
    }

    public async Task<PasswordStrengthResult> ValidatePasswordStrengthAsync(string password)
    {
        var result = new PasswordStrengthResult
        {
            IsStrong = false,
            Score = 0,
            Strength = "Weak"
        };

        // Length check
        if (password.Length >= 12)
        {
            result.Score += 25;
            result.PassedRules.Add("Length >= 12 characters");
        }
        else if (password.Length >= 8)
        {
            result.Score += 15;
            result.PassedRules.Add("Length >= 8 characters");
        }
        else
        {
            result.FailedRules.Add("Password too short (minimum 8 characters)");
            result.Suggestions.Add("Use at least 8 characters, preferably 12 or more");
        }

        // Uppercase check
        if (Regex.IsMatch(password, @"[A-Z]"))
        {
            result.Score += 20;
            result.PassedRules.Add("Contains uppercase letters");
        }
        else
        {
            result.FailedRules.Add("No uppercase letters");
            result.Suggestions.Add("Include at least one uppercase letter");
        }

        // Lowercase check
        if (Regex.IsMatch(password, @"[a-z]"))
        {
            result.Score += 20;
            result.PassedRules.Add("Contains lowercase letters");
        }
        else
        {
            result.FailedRules.Add("No lowercase letters");
            result.Suggestions.Add("Include at least one lowercase letter");
        }

        // Number check
        if (Regex.IsMatch(password, @"[0-9]"))
        {
            result.Score += 20;
            result.PassedRules.Add("Contains numbers");
        }
        else
        {
            result.FailedRules.Add("No numbers");
            result.Suggestions.Add("Include at least one number");
        }

        // Special character check
        if (Regex.IsMatch(password, @"[!@#$%^&*(),.?""':;{}|<>]"))
        {
            result.Score += 15;
            result.PassedRules.Add("Contains special characters");
        }
        else
        {
            result.FailedRules.Add("No special characters");
            result.Suggestions.Add("Include at least one special character");
        }

        // Common password check
        if (IsCommonPassword(password))
        {
            result.Score = Math.Max(0, result.Score - 50);
            result.FailedRules.Add("Password is too common");
            result.Suggestions.Add("Avoid common passwords like 'password123'");
        }

        // Calculate final strength
        result.IsStrong = result.Score >= 70;
        result.Strength = result.Score switch
        {
            >= 90 => "Very Strong",
            >= 70 => "Strong",
            >= 50 => "Good",
            >= 30 => "Fair",
            _ => "Weak"
        };

        return await Task.FromResult(result);
    }

    public async Task<bool> IsPasswordCompromisedAsync(string password)
    {
        // In production, integrate with Have I Been Pwned API
        // For now, just check against common passwords
        return await Task.FromResult(IsCommonPassword(password));
    }

    public async Task<bool> DetectSqlInjectionAsync(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return false;

        foreach (var pattern in SqlInjectionPatterns)
        {
            if (pattern.IsMatch(input))
            {
                _logger.LogWarning("SQL injection pattern detected in input: {Input}", input.Substring(0, Math.Min(input.Length, 100)));
                return await Task.FromResult(true);
            }
        }

        return false;
    }

    public async Task<bool> DetectXssAsync(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return false;

        foreach (var pattern in XssPatterns)
        {
            if (pattern.IsMatch(input))
            {
                _logger.LogWarning("XSS pattern detected in input: {Input}", input.Substring(0, Math.Min(input.Length, 100)));
                return await Task.FromResult(true);
            }
        }

        return false;
    }

    public async Task<List<FailedLoginAttempt>> GetFailedLoginAttemptsAsync(string email, TimeSpan? within = null)
    {
        var json = await _cache.GetStringAsync(FailedLoginsKey(email));
        if (json == null) return [];

        var attempts = JsonSerializer.Deserialize<List<FailedLoginAttempt>>(json) ?? [];

        if (within.HasValue)
        {
            var cutoff = DateTime.UtcNow - within.Value;
            return attempts.Where(a => a.AttemptedAt >= cutoff).ToList();
        }

        return attempts;
    }

    public async Task RecordFailedLoginAttemptAsync(string email, string? ipAddress)
    {
        var key = FailedLoginsKey(email);
        var json = await _cache.GetStringAsync(key);
        var attempts = json != null
            ? JsonSerializer.Deserialize<List<FailedLoginAttempt>>(json) ?? []
            : new List<FailedLoginAttempt>();

        var cutoff = DateTime.UtcNow.AddMinutes(-120);
        // Prune stale entries to prevent unbounded growth
        attempts.RemoveAll(a => a.AttemptedAt < cutoff);

        attempts.Add(new FailedLoginAttempt
        {
            Email = email,
            IpAddress = ipAddress ?? "unknown",
            AttemptedAt = DateTime.UtcNow,
            FailureReason = "InvalidCredentials"
        });

        await _cache.SetStringAsync(key, JsonSerializer.Serialize(attempts), TimeSpan.FromHours(2));
    }

    public async Task ResetFailedLoginAttemptsAsync(string email)
    {
        await _cache.RemoveAsync(FailedLoginsKey(email));
    }

    public async Task<SecurityAuditReport> GetSecurityAuditReportAsync(DateTime startDate, DateTime endDate)
    {
        var report = new SecurityAuditReport
        {
            ReportDate = DateTime.UtcNow,
            PeriodStart = startDate,
            PeriodEnd = endDate,
            // With Redis-backed state we no longer have a full in-memory enumerable;
            // counts should be derived from audit logs in a production implementation.
            TotalFailedLogins = 0,
            BlockedIpAddresses = 0,
            SuspiciousActivities = 0,
            SqlInjectionAttempts = 0,
            XssAttempts = 0,
            PasswordResetRequests = 0,
            MfaChallenges = 0,
            TopAttackSources = []
        };

        return await Task.FromResult(report);
    }

    public async Task<VulnerabilityScanResult> ScanForVulnerabilitiesAsync()
    {
        var result = new VulnerabilityScanResult
        {
            ScannedAt = DateTime.UtcNow,
            TotalChecks = 10
        };

        // Check 1: Weak password policy
        // Check 2: Missing security headers
        // Check 3: Outdated dependencies
        // etc.

        result.Recommendations.Add("Enable Content Security Policy (CSP) headers");
        result.Recommendations.Add("Implement rate limiting on all endpoints");
        result.Recommendations.Add("Enable HSTS (HTTP Strict Transport Security)");
        result.Recommendations.Add("Regular security updates for dependencies");

        return await Task.FromResult(result);
    }

    // Helper methods
    private static bool IsCommonPassword(string password)
    {
        var commonPasswords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "password", "123456", "12345678", "qwerty", "abc123", "monkey",
            "1234567", "letmein", "trustno1", "dragon", "baseball", "iloveyou",
            "master", "sunshine", "ashley", "bailey", "passw0rd", "shadow",
            "123123", "654321", "superman", "qazwsx", "michael", "football"
        };

        return commonPasswords.Contains(password);
    }
}
