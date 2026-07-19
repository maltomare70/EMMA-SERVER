using EmmaServer.Entities;
using EmmaServer.Helpers;
using  EmmaServer.Repositories;

namespace EmmaServer.Services;

public interface IUserService
{
    Task<EmmaUser?> GetUserAsync(int id);
    Task<IEnumerable<EmmaUser>> GetAllTenantAsync(string tenant);
    Task<int?> AddUserAsync(EmmaUser user);
    Task<bool?> UpdateUserAsync(EmmaUser user);
    Task<EmmaUser?> GetUserByEmailAsync(string email);

    Task<int> CambiaPasswordAsync(CambiaPasswordRequest cambiaPasswordRequest);
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
        if (string.IsNullOrWhiteSpace(user.tenant))
        {
            var tenant = _connectionProvider.GetTenant();
            user.tenant = tenant;
        }

        EmmaUser emmaUser = new EmmaUser()
        {
            email = user.email,
            pwd = PasswordHelper.GeneraHash(user.pwd),
            tenant = user.tenant
        };

        return await _repoUser.AddAsync(emmaUser);
    }
    
    public async Task<bool?> UpdateUserAsync(EmmaUser user)
    {
        if (string.IsNullOrWhiteSpace(user.tenant))
        {
            var tenant = _connectionProvider.GetTenant();
            user.tenant = tenant;
        }

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
    
    public async Task<IEnumerable<EmmaUser>> GetAllTenantAsync(string tenant)
    {
        return await _repoUser.GetAllTenantAsync(tenant);
    }

    public async Task<int> CambiaPasswordAsync(CambiaPasswordRequest cambiaPasswordRequest)
    {
        return await _repoUser.CambiaPasswordAsync(cambiaPasswordRequest);
    }

}