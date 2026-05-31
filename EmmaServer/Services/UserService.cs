using EmmaServer.Entities;
using  EmmaServer.Repositories;

namespace EmmaServer.Services;

public interface IUserService
{
    Task<EmmaUser?> GetUserAsync(int id);
    Task<int?> AddUserAsync(EmmaUser user);
}

public class UserService : IUserService
{
    private readonly IRepositoryGenerico<EmmaUser> _repo;

    public UserService(IRepositoryGenerico<EmmaUser> repo)
    {
        _repo = repo;
    }
    public async Task<int?> AddUserAsync(EmmaUser user)
    {
        return await _repo.AddAsync(user);
    }
    
    public async Task<EmmaUser?> GetUserAsync(int id)
    {
        return await _repo.GetIdAsync(id);
    }
}