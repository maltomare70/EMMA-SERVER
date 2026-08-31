using Dapper;
using EmmaServer.Entities;
using EmmaServer.Entities.Dtos;
namespace EmmaServer.Repositories;

public interface IConciliaRigheRepository : IRepositoryGenerico<EmmaConciliaRighe>
{
    Task<IEnumerable<EmmaConciliaRighe>> GetAllByTenantAsync(string tenant);
    Task DeleteAsync(string id_riga, string tenant);

    Task<List<EmmaConciliaRigheDto>> GetRigheConciliazioneAsync(string idMaster, string idRiga, string tenant);
}

public class ConciliaRigheRepository : RepositoryGenerico<EmmaConciliaRighe>, IConciliaRigheRepository
{
    private readonly IDocRepository _docRepository;
    public ConciliaRigheRepository(IUserConnectionProvider connectionProvider, IDocRepository docRepository) : base(connectionProvider)
    {
        _docRepository = docRepository;
    }
    public async Task<IEnumerable<EmmaConciliaRighe>> GetAllByTenantAsync(string tenant)
    {
        // Query SQL specifica per questa ricerca (Postgres usa il minuscolo di default)
        const string sql = "SELECT * FROM conciliarighe WHERE tenant = @Tenant;";

        // Sfruttiamo il metodo del padre per ottenere la connessione al database del tenant corrente
        using var db = await CreaConnessione();

        // Eseguiamo una normale query Dapper (non Contrib)
        return await db.QueryAsync<EmmaConciliaRighe>(sql, new { Tenant = tenant });
    }

    public async Task DeleteAsync(string id_riga, string tenant)
    {
        // Query SQL specifica per questa ricerca (Postgres usa il minuscolo di default)
        const string sql = "DELETE FROM conciliarighe WHERE tenant = @Tenant AND id_riga = @Id_riga;";

        // Sfruttiamo il metodo del padre per ottenere la connessione al database del tenant corrente
        using var db = await CreaConnessione();

        // Eseguiamo una normale query Dapper (non Contrib)
        await db.QueryAsync(sql, new { Tenant = tenant, Id_riga = id_riga });
    }

    public async Task<List<EmmaConciliaRigheDto>> GetRigheConciliazioneAsync(string idMaster, string idRiga, string tenant)
    {
        List<EmmaConciliaRighe> items;
        if (string.IsNullOrWhiteSpace(idRiga))
        {
            // Query SQL specifica per questa ricerca (Postgres usa il minuscolo di default)
            const string sql = "SELECT * FROM conciliarighe WHERE tenant = @Tenant AND id_master = @IdMaster;";
            // Sfruttiamo il metodo del padre per ottenere la connessione al database del tenant corrente
            using var db = await CreaConnessione();
            // Eseguiamo una normale query Dapper (non Contrib)
            var result = await db.QueryAsync<EmmaConciliaRighe>(sql, new { Tenant = tenant, IdMaster = idMaster });
            items =  result.ToList();
        }
        else
        {
            // Query SQL specifica per questa ricerca (Postgres usa il minuscolo di default)
            const string sql = "SELECT * FROM conciliarighe WHERE tenant = @Tenant AND id_master = @IdMaster AND id_riga = @IdRiga;";
            // Sfruttiamo il metodo del padre per ottenere la connessione al database del tenant corrente
            using var db = await CreaConnessione();
            // Eseguiamo una normale query Dapper (non Contrib)
            var result = await db.QueryAsync<EmmaConciliaRighe>(sql, new { Tenant = tenant, IdMaster = idMaster, IdRiga = idRiga });
            items =  result.ToList();
        }

        List<EmmaConciliaRigheDto> docs = new List<EmmaConciliaRigheDto>();
        //Per recuperare i dati del documento associato a ciascuna riga di conciliazione
        foreach (var item in items)
        {
            //recupero codice di abbinamento
            var codice = item.codice;
            var doc = await GetDocByCodiceAbbinamentoIdMasterAsync(idMaster, tenant, codice);
            var docEntity = doc?.ToDoc();

            EmmaConciliaRigheDto itemDto = new EmmaConciliaRigheDto
            {
                id = item.id,
                id_master = item.id_master,
                id_riga = item.id_riga,
                tenant = item.tenant,
                data_creazione = item.data_creazione,
                codice = item.codice,
                stato = item.stato,
                note = item.note,
                id_fornitore = item.id_fornitore,
                tipo_doc = item.tipo_doc,
                qta = item.qta,
                qta_canc = item.qta_canc,
                delta = item.delta,
                flag = item.flag,
                numero_doc_abbinamento = docEntity?.NumeroBolla ?? string.Empty,
                data_doc_abbinamento = docEntity?.DataBolla ?? string.Empty
            };

            docs.Add(itemDto);
        }

        return docs;
    }

    private async Task<EmmaDoc?> GetDocByCodiceAbbinamentoIdMasterAsync(string idMaster, string tenant, string codice)
    {
        //recupero id_master della riga di conciliazione con lo stesso codice ma diverso id_master
        using var db = await CreaConnessione();
        const string sql = "SELECT * FROM conciliarighe WHERE tenant = @Tenant AND codice = @Codice AND id_master <> @IdMaster;";
        var result = await db.QueryAsync<EmmaConciliaRighe>(sql, new { Tenant = tenant, Codice = codice, IdMaster = idMaster });
        var id = result.FirstOrDefault()?.id_master;    
        if (string.IsNullOrWhiteSpace(id)) return null;
        var docs = await _docRepository.GetDocsAsync(new EmmaDocFilters() { Id = id });
        return docs?.FirstOrDefault();
    }
}
