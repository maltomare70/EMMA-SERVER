using EmmaServer.Entities;
using  EmmaServer.Repositories;

namespace EmmaServer.Services;

public interface IUserService
{
    Task<User?> GetUserAsync(int id);
    Task<int?> AddUserAsync(User user);
}

public class UserService : IUserService
{
    private readonly IRepositoryGenerico<User> _repo;

    public UserService(IRepositoryGenerico<User> repo)
    {
        _repo = repo;
    }
    public async Task<int?> AddUserAsync(User user)
    {
        return await _repo.AddAsync(user);
    }
    
    public async Task<User?> GetUserAsync(int id)
    {
        return await _repo.GetIdAsync(id);
    }
}