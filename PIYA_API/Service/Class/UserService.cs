using PIYA_API.Data;
using PIYA_API.Model;
using PIYA_API.Service.Interface;
using Microsoft.EntityFrameworkCore;

namespace PIYA_API.Service.Class;

public class UserService(PharmacyApiDbContext dbContext, IPasswordHasher passwordHasher) : IUserService
{
    private readonly PharmacyApiDbContext _dbContext = dbContext;
    private readonly IPasswordHasher _passwordHasher = passwordHasher;

    public async Task<User?> Authenticate(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username))
            throw new ArgumentException("Username or email is required");

        if (string.IsNullOrWhiteSpace(password))
            throw new ArgumentException("Password is required");

        var normalizedIdentifier = username.Trim();
        var normalizedEmail = normalizedIdentifier.ToLowerInvariant();
        var user = await _dbContext.Users
            .SingleOrDefaultAsync(x =>
                x.Username == normalizedIdentifier || x.Email.ToLower() == normalizedEmail);

        // User not found — return null (same as wrong password to avoid user enumeration)
        if (user == null)
            return null;

        // Deactivated accounts cannot authenticate
        if (!user.IsActive)
            return null;

        // Verify password
        if (!_passwordHasher.VerifyPassword(password, user.PasswordHash))
            return null;

        // Authentication successful
        return user;
    }

    public async Task<User> Create(User user, string password)
    {
        // Validation
        if (string.IsNullOrWhiteSpace(password))
            throw new ArgumentException("Password is required");

        if (password.Length < 8)
            throw new ArgumentException("Password must be at least 8 characters long");

        // Require at least one digit and one non-alphanumeric character
        if (!password.Any(char.IsDigit))
            throw new ArgumentException("Password must contain at least one digit");

        if (password.All(char.IsLetterOrDigit))
            throw new ArgumentException("Password must contain at least one special character");

        if (string.IsNullOrWhiteSpace(user.Username))
            throw new ArgumentException("Username is required");

        if (string.IsNullOrWhiteSpace(user.Email))
            throw new ArgumentException("Email is required");

        user.Username = user.Username.Trim();
        user.Email = user.Email.Trim().ToLowerInvariant();

        // Check if username already exists
        if (await _dbContext.Users.AnyAsync(x => x.Username == user.Username))
            throw new InvalidOperationException($"Username '{user.Username}' is already taken");

        // Check if email already exists
        if (await _dbContext.Users.AnyAsync(x => x.Email.ToLower() == user.Email))
            throw new InvalidOperationException($"Email '{user.Email}' is already registered");

        // Hash password
        user.PasswordHash = _passwordHasher.HashPassword(password);
        user.CreatedAt = DateTime.UtcNow;
        user.UpdatedAt = DateTime.UtcNow;

        // Save user
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        return user;
    }

    public async Task Delete(Guid id)
    {
        var user = await _dbContext.Users.FindAsync(id) ?? throw new KeyNotFoundException($"User with ID {id} not found");
        // Soft-delete: deactivate the account rather than removing the row.
        // Hard-delete would cascade-remove Appointments, Prescriptions, DoctorProfiles, etc.
        user.IsActive = false;
        user.UpdatedAt = DateTime.UtcNow;
        _dbContext.Users.Update(user);
        await _dbContext.SaveChangesAsync();
    }

    public async Task<User> GetById(Guid id)
    {
        var user = await _dbContext.Users.FindAsync(id) ?? throw new KeyNotFoundException($"User with ID {id} not found");
        return user;
    }

    public async Task Update(User user, string? password = null)
    {
        var existingUser = await _dbContext.Users.FindAsync(user.Id) ?? throw new KeyNotFoundException($"User with ID {user.Id} not found");

        // Update username if changed and not already taken
        if (!string.IsNullOrWhiteSpace(user.Username) && user.Username != existingUser.Username)
        {
            if (await _dbContext.Users.AnyAsync(x => x.Username == user.Username))
                throw new InvalidOperationException($"Username '{user.Username}' is already taken");

            existingUser.Username = user.Username;
        }

        // Update email if changed and not already taken
        if (!string.IsNullOrWhiteSpace(user.Email))
        {
            var normalizedEmail = user.Email.Trim().ToLowerInvariant();
            if (!string.Equals(normalizedEmail, existingUser.Email, StringComparison.OrdinalIgnoreCase))
            {
                if (await _dbContext.Users.AnyAsync(x =>
                        x.Id != user.Id && x.Email.ToLower() == normalizedEmail))
                    throw new InvalidOperationException($"Email '{user.Email}' is already registered");

                existingUser.Email = normalizedEmail;
                // Verification belongs to the address, not just the account. A new
                // address must prove ownership before it can be trusted again.
                existingUser.IsEmailVerified = false;
            }
        }

        // Update password if provided
        if (!string.IsNullOrWhiteSpace(password))
        {
            if (password.Length < 8)
                throw new ArgumentException("Password must be at least 8 characters long");

            if (!password.Any(char.IsDigit))
                throw new ArgumentException("Password must contain at least one digit");

            if (password.All(char.IsLetterOrDigit))
                throw new ArgumentException("Password must contain at least one special character");

            existingUser.PasswordHash = _passwordHasher.HashPassword(password);
        }

        // Update other fields
        if (!string.IsNullOrWhiteSpace(user.FirstName))
            existingUser.FirstName = user.FirstName;

        if (!string.IsNullOrWhiteSpace(user.LastName))
            existingUser.LastName = user.LastName;

        existingUser.MiddleName = string.IsNullOrWhiteSpace(user.MiddleName)
            ? null
            : user.MiddleName.Trim();

        if (!string.IsNullOrWhiteSpace(user.PhoneNumber))
            existingUser.PhoneNumber = user.PhoneNumber;

        if (user.DateOfBirth.HasValue)
        {
            var dateOfBirth = user.DateOfBirth.Value.Date;
            if (dateOfBirth > DateTime.UtcNow.Date.AddYears(-18))
                throw new ArgumentException("You must be at least 18 years old.");

            existingUser.DateOfBirth = DateTime.SpecifyKind(dateOfBirth, DateTimeKind.Utc);
        }

        existingUser.UpdatedAt = DateTime.UtcNow;

        _dbContext.Users.Update(existingUser);
        await _dbContext.SaveChangesAsync();
    }

    public async Task<User?> GetByIdAsync(Guid id)
    {
        return await _dbContext.Users.FindAsync(id);
    }

    public async Task UpdateAsync(User user)
    {
        var existingUser = await _dbContext.Users.FindAsync(user.Id) ?? throw new KeyNotFoundException($"User with ID {user.Id} not found");
        existingUser.Role = user.Role;
        existingUser.UpdatedAt = DateTime.UtcNow;

        _dbContext.Users.Update(existingUser);
        await _dbContext.SaveChangesAsync();
    }

    public async Task<List<User>> GetUsersByRoleAsync(UserRole role)
    {
        return await _dbContext.Users
            .Where(u => u.Role == role)
            .OrderBy(u => u.LastName)
            .ThenBy(u => u.FirstName)
            .ToListAsync();
    }

    public async Task<List<User>> GetAllUsersAsync()
    {
        return await _dbContext.Users
            .OrderBy(u => u.CreatedAt)
            .ToListAsync();
    }

    public async Task SetActiveAsync(Guid id, bool isActive)
    {
        var user = await _dbContext.Users.FindAsync(id)
            ?? throw new KeyNotFoundException($"User with ID {id} not found");
        user.IsActive = isActive;
        user.UpdatedAt = DateTime.UtcNow;
        _dbContext.Users.Update(user);
        await _dbContext.SaveChangesAsync();
    }

    public async Task ChangePasswordAsync(Guid userId, string currentPassword, string newPassword)
    {
        var user = await _dbContext.Users.FindAsync(userId)
            ?? throw new KeyNotFoundException($"User with ID {userId} not found");

        // Verify the current password before allowing a change
        if (!_passwordHasher.VerifyPassword(currentPassword, user.PasswordHash))
            throw new ArgumentException("Current password is incorrect.");

        // Enforce the same complexity rules as Create / Update
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 8)
            throw new ArgumentException("New password must be at least 8 characters long.");

        if (!newPassword.Any(char.IsDigit))
            throw new ArgumentException("New password must contain at least one digit.");

        if (newPassword.All(char.IsLetterOrDigit))
            throw new ArgumentException("New password must contain at least one special character.");

        // Prevent reuse of the current password
        if (_passwordHasher.VerifyPassword(newPassword, user.PasswordHash))
            throw new ArgumentException("New password must be different from the current password.");

        user.PasswordHash = _passwordHasher.HashPassword(newPassword);
        user.UpdatedAt = DateTime.UtcNow;
        _dbContext.Users.Update(user);
        await _dbContext.SaveChangesAsync();
    }

    public async Task HardDeleteAsync(Guid id)
    {
        var user = await _dbContext.Users.FindAsync(id)
            ?? throw new KeyNotFoundException($"User with ID {id} not found");

        // ── Nullify Restrict FK references so the DB will allow the row deletion ──

        // MedicalDocuments where this user is the owner/uploader/verifier
        await _dbContext.MedicalDocuments
            .Where(md => md.UserId == id)
            .ExecuteDeleteAsync();

        await _dbContext.MedicalDocuments
            .Where(md => md.UploadedByUserId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(md => md.UploadedByUserId, (Guid?)null));

        await _dbContext.MedicalDocuments
            .Where(md => md.VerifiedByUserId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(md => md.VerifiedByUserId, (Guid?)null));

        // Referrals where this user is the patient or referring doctor
        await _dbContext.Referrals
            .Where(r => r.PatientId == id || r.ReferringDoctorId == id)
            .ExecuteDeleteAsync();

        // MedicalTests where this user ordered them (Restrict FK)
        await _dbContext.MedicalTests
            .Where(mt => mt.OrderedByDoctorId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(mt => mt.OrderedByDoctorId, (Guid?)null));

        // Now EF cascade + SetNull rules handle the rest; remove the user row
        _dbContext.Users.Remove(user);
        await _dbContext.SaveChangesAsync();
    }
}
