using EmmaServer.Entities;
using EmmaServer.Repositories;
using EmmaServer.Entities.Dtos;

namespace EmmaServer.Services;

public interface IConciliaRigheService
{
    Task<int?> AddAsync(EmmaConciliaRighe riga);
    Task<IEnumerable<EmmaConciliaRighe>> GetAllByTenantAsync(string tenant);
    Task DeleteAsync(string id_riga, string tenant);
    Task<List<EmmaConciliaRigheDto>> GetRigheConciliazioneAsync(string idMaster, string idRiga, string tenant);
    Task SalvaConcilizione(PayloadRiconciliazione payload, string tenant);
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

    public async Task SalvaConcilizione(PayloadRiconciliazione payload, string tenant)
    {
        foreach (var b in payload.bolle)
        {
            var item = new EmmaConciliaRighe()
            {
                codice = payload.codice ?? string.Empty,
                delta = b.Qta - b.Qta_Conc,
                flag = b.Selezionato ? 1 : 0,
                id_fornitore = b.Fornitore ?? string.Empty,
                id_master = b.IdMaster ?? string.Empty,
                id_riga = b.IdRiga ?? string.Empty,
                note = b.Note ?? string.Empty,
                qta = b.Qta,
                stato = b.Stato ?? string.Empty,
                tipo_doc = (int)TipoDocEnum.DDT,
                qta_canc = b.Qta_Conc,
                tenant = tenant
            };

            await DeleteAsync(item.id_riga, tenant);

            if (b.Selezionato) await AddAsync(item);

        }

        foreach (var b in payload.fatture)
        {
            var item = new EmmaConciliaRighe()
            {
                codice = payload.codice ?? string.Empty,
                delta = b.Qta - b.Qta_Conc,
                flag = b.Selezionato ? 1 : 0,
                id_fornitore = b.Fornitore ?? string.Empty,
                id_master = b.IdMaster ?? string.Empty,
                id_riga = b.IdRiga ?? string.Empty,
                note = b.Note ?? string.Empty,
                qta = b.Qta,
                stato = b.Stato ?? string.Empty,
                tipo_doc = (int)TipoDocEnum.Fattura,
                qta_canc = b.Qta_Conc,
                tenant = tenant
            };

            await DeleteAsync(item.id_riga, tenant);

            if (b.Selezionato) await AddAsync(item);
        }

    }
}
