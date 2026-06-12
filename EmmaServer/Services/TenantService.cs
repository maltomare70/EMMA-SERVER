using EmmaServer.Entities;
using  EmmaServer.Repositories;

namespace EmmaServer.Services;

public interface ITenantService
{
    Task<EmmaTenant?> GetTenantAsync(int id);
    Task<IEnumerable<EmmaTenant>> GetAllAsync();
    Task<int?> AddTenantAsync(EmmaTenant user);
    Task<bool?> UpdateTenantAsync(EmmaTenant user);
}

public class TenantService : ITenantService
{
    private readonly IRepositoryGenerico<EmmaTenant> _repo;
    
    public TenantService(IRepositoryGenerico<EmmaTenant> repo, IUserConnectionProvider connectionProvider)
    {
        _repo = repo;
    }
    public async Task<int?> AddTenantAsync(EmmaTenant tenant)
    {
        return await _repo.AddAsync(tenant);
    }
    
    public async Task<bool?> UpdateTenantAsync(EmmaTenant tenant)
    {
        return await _repo.UpdateAsync(tenant);
    }
    
    public async Task<EmmaTenant?> GetTenantAsync(int id)
    {
        return await _repo.GetIdAsync(id);
    }
    
    public async Task<IEnumerable<EmmaTenant>> GetAllAsync()
    {
        return await _repo.GetAllAsync();
    }
}