using EmmaServer.Entities;
using EmmaServer.Repositories;

namespace EmmaServer.Services;


public interface ILogService
{
    Task<IEnumerable<EmmaLog>> GetAllAsync();
    Task<IEnumerable<EmmaLog>> GetAllByTenantAsync(string tenant);
    Task<int?> AddAsync(EmmaLog log);
}

public class LogService : ILogService
{
    private readonly IRepositoryGenerico<EmmaLog> _repo;

    public LogService(IRepositoryGenerico<EmmaLog> repo, IUserConnectionProvider connectionProvider)
    {
        _repo = repo;
    }

    public async Task<int?> AddAsync(EmmaLog log)
    {
        return await _repo.AddAsync(log);
    }

    public async Task<IEnumerable<EmmaLog>> GetAllAsync()
    {
        return await _repo.GetAllAsync();
    }

    public async Task<IEnumerable<EmmaLog>> GetAllByTenantAsync(string tenant)
    {
        return await _repo.GetAllTenantAsync(tenant);
    }
}
