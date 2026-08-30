using EmmaServer.Entities;
using EmmaServer.Repositories;

namespace EmmaServer.Services;

public interface IConciliaRigheService
{
    Task<int?> AddAsync(EmmaConciliaRighe riga);
    Task<IEnumerable<EmmaConciliaRighe>> GetAllByTenantAsync(string tenant);
    Task DeleteAsync(string id_riga, string tenant);
    Task<List<EmmaConciliaRigheDto>> GetRigheConciliazioneAsync(string idMaster, string idRiga, string tenant);
}

public class ConciliaRigheService : IConciliaRigheService
{
    private readonly IConciliaRigheRepository _repo;

    public ConciliaRigheService(IConciliaRigheRepository repo, IUserConnectionProvider connectionProvider)
    {
        _repo = repo;
    }

    public async Task<int?> AddAsync(EmmaConciliaRighe riga)
    {
        return await _repo.AddAsync(riga);
    }
    public async Task<IEnumerable<EmmaConciliaRighe>> GetAllByTenantAsync(string tenant)
    {
        return await _repo.GetAllTenantAsync(tenant);
    }
    public async  Task DeleteAsync(string id_riga, string tenant)
    {
        await _repo.DeleteAsync(id_riga, tenant);
    }

    public async Task<List<EmmaConciliaRigheDto>> GetRigheConciliazioneAsync(string idMaster, string idRiga, string tenant)
    {
        return await _repo.GetRigheConciliazioneAsync(idMaster, idRiga, tenant);  
    }
}
