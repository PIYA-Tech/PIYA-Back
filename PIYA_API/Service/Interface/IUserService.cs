using PIYA_API.Model;
namespace PIYA_API.Service.Interface;

public interface IUserService
{
    public Task<User?> Authenticate(string username, string password);
    public Task<User> GetById(Guid id);
    public Task<User?> GetByIdAsync(Guid id);
    public Task<User> Create(User user, string password);
    public Task Update(User user, string? password = null);
    public Task UpdateAsync(User user);
    public Task Delete(Guid id);
    public Task<List<User>> GetUsersByRoleAsync(UserRole role);
    public Task<List<User>> GetAllUsersAsync();
    public Task SetActiveAsync(Guid id, bool isActive);
    public Task HardDeleteAsync(Guid id);
}
