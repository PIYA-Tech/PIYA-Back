using PIYA_API.Data;
using PIYA_API.Model;
using PIYA_API.Service.Interface;
using Microsoft.EntityFrameworkCore;

namespace PIYA_API.Service.Class;

public class UserService(PharmacyApiDbContext dbContext, IPasswordHasher passwordHasher) : IUserService
{
    private readonly PharmacyApiDbContext _dbContext = dbContext;
    private readonly IPasswordHasher _passwordHasher = passwordHasher;

    public async Task<User> Authenticate(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username))
            throw new ArgumentException("Username or email is required");

        if (string.IsNullOrWhiteSpace(password))
            throw new ArgumentException("Password is required");

        var user = await _dbContext.Users
            .SingleOrDefaultAsync(x => x.Username == username || x.Email == username);

        // User not found
        if (user == null)
            return null!;

        // Verify password
        if (!_passwordHasher.VerifyPassword(password, user.PasswordHash))
            return null!;

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

        if (string.IsNullOrWhiteSpace(user.Username))
            throw new ArgumentException("Username is required");

        if (string.IsNullOrWhiteSpace(user.Email))
            throw new ArgumentException("Email is required");

        // Check if username already exists
        if (await _dbContext.Users.AnyAsync(x => x.Username == user.Username))
            throw new InvalidOperationException($"Username '{user.Username}' is already taken");

        // Check if email already exists
        if (await _dbContext.Users.AnyAsync(x => x.Email == user.Email))
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

    public async Task Delete(int id)
    {
        var user = await _dbContext.Users.FindAsync(id) ?? throw new KeyNotFoundException($"User with ID {id} not found");
        _dbContext.Users.Remove(user);
        await _dbContext.SaveChangesAsync();
    }

    public async Task<User> GetById(int id)
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
        if (!string.IsNullOrWhiteSpace(user.Email) && user.Email != existingUser.Email)
        {
            if (await _dbContext.Users.AnyAsync(x => x.Email == user.Email))
                throw new InvalidOperationException($"Email '{user.Email}' is already registered");

            existingUser.Email = user.Email;
        }

        // Update password if provided
        if (!string.IsNullOrWhiteSpace(password))
        {
            if (password.Length < 8)
                throw new ArgumentException("Password must be at least 8 characters long");

            existingUser.PasswordHash = _passwordHasher.HashPassword(password);
        }

        // Update other fields
        if (!string.IsNullOrWhiteSpace(user.FirstName))
            existingUser.FirstName = user.FirstName;

        if (!string.IsNullOrWhiteSpace(user.LastName))
            existingUser.LastName = user.LastName;

        if (!string.IsNullOrWhiteSpace(user.PhoneNumber))
            existingUser.PhoneNumber = user.PhoneNumber;

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
}
