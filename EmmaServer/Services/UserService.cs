using EmmaServer.Entities;
using  EmmaServer.Repositories;

namespace EmmaServer.Services;

public interface IUserService
{
    Task<EmmaUser?> GetUserAsync(int id);
    Task<IEnumerable<EmmaUser>> GetAllTenantAsync();
    Task<int?> AddUserAsync(EmmaUser user);
    Task<bool?> UpdateUserAsync(EmmaUser user);
    Task<EmmaUser?> GetUserByEmailAsync(string email);
}

public class UserService : IUserService
{
    private readonly IUserRepository _repoUser;

    private readonly IUserConnectionProvider _connectionProvider;
    public UserService(IUserConnectionProvider connectionProvider,
        IUserRepository repoUser)
    {
        _repoUser = repoUser;
        _connectionProvider =  connectionProvider;
    }
    public async Task<int?> AddUserAsync(EmmaUser user)
    {
        var tenant = _connectionProvider.GetTenant();
        user.tenant = tenant;
        return await _repoUser.AddAsync(user);
    }
    
    public async Task<bool?> UpdateUserAsync(EmmaUser user)
    {
        var tenant = _connectionProvider.GetTenant();
        user.tenant = tenant;
        return await _repoUser.UpdateAsync(user);
    }
    
    public async Task<EmmaUser?> GetUserAsync(int id)
    {
        return await _repoUser.GetIdAsync(id);
    }
    
    public async Task<EmmaUser?> GetUserByEmailAsync(string email)
    {
        return await _repoUser.GetByEmailAsync(email);
    }
    
    public async Task<IEnumerable<EmmaUser>> GetAllTenantAsync()
    {
        return await _repoUser.GetAllTenantAsync();
    }
}