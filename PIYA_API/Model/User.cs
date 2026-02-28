namespace PIYA_API.Model;

public class User
{
    public Guid Id { get; set; }
    public required string Username { get; set; }
    
    /// <summary>
    /// BCrypt hashed password
    /// </summary>
    public string PasswordHash { get; set; } = string.Empty;
    
    public required string FirstName { get; set; }
    public string? MiddleName { get; set; }
    public required string LastName { get; set; }
    public required string Email { get; set; }
    public required string PhoneNumber { get; set; }
    public required DateTime DateOfBirth { get; set; }
    public required Token TokensInfo { get; set; }
    public string? SigningKey { get; set; }
    
    /// <summary>
    /// User role for RBAC (Patient, Doctor, Pharmacist, Admin)
    /// </summary>
    public UserRole Role { get; set; } = UserRole.Patient;
    
    /// <summary>
    /// Two-factor authentication settings
    /// </summary>
    public TwoFactorAuth? TwoFactorAuth { get; set; }
    
    /// <summary>
    /// Whether the user account is active
    /// </summary>
    public bool IsActive { get; set; } = true;
    
    /// <summary>
    /// Whether the email is verified
    /// </summary>
    public bool IsEmailVerified { get; set; } = false;
    
    /// <summary>
    /// Whether the phone number is verified
    /// </summary>
    public bool IsPhoneVerified { get; set; } = false;
    
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}