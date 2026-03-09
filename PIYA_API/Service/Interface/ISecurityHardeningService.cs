namespace PIYA_API.Service.Interface;

/// <summary>
/// Security hardening service for advanced threat detection and prevention
/// </summary>
public interface ISecurityHardeningService
{
    /// <summary>
    /// Detect and block suspicious login patterns
    /// </summary>
    Task<SuspiciousActivityResult> DetectSuspiciousLoginAsync(string email, string ipAddress, string? userAgent = null);
    
    /// <summary>
    /// Check if IP address is blocked
    /// </summary>
    Task<bool> IsIpBlockedAsync(string ipAddress);
    
    /// <summary>
    /// Block IP address temporarily or permanently
    /// </summary>
    Task BlockIpAddressAsync(string ipAddress, string reason, TimeSpan? duration = null);
    
    /// <summary>
    /// Unblock IP address
    /// </summary>
    Task UnblockIpAddressAsync(string ipAddress);
    
    /// <summary>
    /// Validate password strength against security policies
    /// </summary>
    Task<PasswordStrengthResult> ValidatePasswordStrengthAsync(string password);
    
    /// <summary>
    /// Check if password has been compromised in known breaches
    /// </summary>
    Task<bool> IsPasswordCompromisedAsync(string password);
    
    /// <summary>
    /// Detect SQL injection attempts
    /// </summary>
    Task<bool> DetectSqlInjectionAsync(string input);
    
    /// <summary>
    /// Detect XSS attempts
    /// </summary>
    Task<bool> DetectXssAsync(string input);
    
    /// <summary>
    /// Get failed login attempts for user
    /// </summary>
    Task<List<FailedLoginAttempt>> GetFailedLoginAttemptsAsync(string email, TimeSpan? within = null);
    
    /// <summary>
    /// Record a single failed login attempt for lockout tracking
    /// </summary>
    Task RecordFailedLoginAttemptAsync(string email, string? ipAddress);

    /// <summary>
    /// Reset failed login counter
    /// </summary>
    Task ResetFailedLoginAttemptsAsync(string email);
    
    /// <summary>
    /// Get security audit report
    /// </summary>
    Task<SecurityAuditReport> GetSecurityAuditReportAsync(DateTime startDate, DateTime endDate);
    
    /// <summary>
    /// Scan for potential vulnerabilities
    /// </summary>
    Task<VulnerabilityScanResult> ScanForVulnerabilitiesAsync();
}

#region DTOs

public class SuspiciousActivityResult
{
    public bool IsSuspicious { get; set; }
    public required string Reason { get; set; }
    public int RiskScore { get; set; } // 0-100
    public bool ShouldBlock { get; set; }
    public bool RequiresCaptcha { get; set; }
    public bool RequiresMfa { get; set; }
    public List<string> DetectedPatterns { get; set; } = [];
}

public class PasswordStrengthResult
{
    public bool IsStrong { get; set; }
    public int Score { get; set; } // 0-100
    public required string Strength { get; set; } // "Weak", "Fair", "Good", "Strong", "Very Strong"
    public List<string> Suggestions { get; set; } = [];
    public List<string> PassedRules { get; set; } = [];
    public List<string> FailedRules { get; set; } = [];
}

public class FailedLoginAttempt
{
    public Guid Id { get; set; }
    public required string Email { get; set; }
    public required string IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public DateTime AttemptedAt { get; set; }
    public required string FailureReason { get; set; }
}

public class SecurityAuditReport
{
    public DateTime ReportDate { get; set; }
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public int TotalFailedLogins { get; set; }
    public int BlockedIpAddresses { get; set; }
    public int SuspiciousActivities { get; set; }
    public int SqlInjectionAttempts { get; set; }
    public int XssAttempts { get; set; }
    public int PasswordResetRequests { get; set; }
    public int MfaChallenges { get; set; }
    public List<TopAttackSource> TopAttackSources { get; set; } = [];
    public List<SecurityIncident> RecentIncidents { get; set; } = [];
}

public class TopAttackSource
{
    public required string IpAddress { get; set; }
    public int AttackCount { get; set; }
    public required string Country { get; set; }
    public List<string> AttackTypes { get; set; } = [];
}

public class SecurityIncident
{
    public Guid Id { get; set; }
    public DateTime OccurredAt { get; set; }
    public required string IncidentType { get; set; }
    public required string Severity { get; set; } // "Low", "Medium", "High", "Critical"
    public required string Description { get; set; }
    public required string IpAddress { get; set; }
    public bool Resolved { get; set; }
}

public class VulnerabilityScanResult
{
    public DateTime ScannedAt { get; set; }
    public int TotalChecks { get; set; }
    public int VulnerabilitiesFound { get; set; }
    public List<Vulnerability> Vulnerabilities { get; set; } = [];
    public List<string> Recommendations { get; set; } = [];
}

public class Vulnerability
{
    public required string Name { get; set; }
    public required string Severity { get; set; }
    public required string Description { get; set; }
    public required string Mitigation { get; set; }
    public required string Category { get; set; }
}

#endregion
