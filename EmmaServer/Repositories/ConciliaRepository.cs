using Dapper;
using EmmaServer.Entities;

namespace EmmaServer.Repositories;

public interface IConciliaRigheRepository : IRepositoryGenerico<EmmaConciliaRighe>
{
    Task<IEnumerable<EmmaConciliaRighe>> GetAllByTenantAsync(string tenant);
    Task DeleteAsync(string id_riga, string tenant);

    Task<List<EmmaConciliaRighe>> GetRigheConciliazioneAsync(string idMaster, string idRiga, string tenant);
}

public class ConciliaRigheRepository : RepositoryGenerico<EmmaConciliaRighe>, IConciliaRigheRepository
{
    public ConciliaRigheRepository(IUserConnectionProvider connectionProvider) : base(connectionProvider)
    {

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

    public async Task<List<EmmaConciliaRighe>> GetRigheConciliazioneAsync(string idMaster, string idRiga, string tenant)
    {
        if (string.IsNullOrWhiteSpace(idRiga))
        {
            // Query SQL specifica per questa ricerca (Postgres usa il minuscolo di default)
            const string sql = "SELECT * FROM conciliarighe WHERE tenant = @Tenant AND id_master = @IdMaster;";
            // Sfruttiamo il metodo del padre per ottenere la connessione al database del tenant corrente
            using var db = await CreaConnessione();
            // Eseguiamo una normale query Dapper (non Contrib)
            var result = await db.QueryAsync<EmmaConciliaRighe>(sql, new { Tenant = tenant, IdMaster = idMaster });
            return result.ToList();
        }
        else
        {
            // Query SQL specifica per questa ricerca (Postgres usa il minuscolo di default)
            const string sql = "SELECT * FROM conciliarighe WHERE tenant = @Tenant AND id_master = @IdMaster AND id_riga = @IdRiga;";
            // Sfruttiamo il metodo del padre per ottenere la connessione al database del tenant corrente
            using var db = await CreaConnessione();
            // Eseguiamo una normale query Dapper (non Contrib)
            var result = await db.QueryAsync<EmmaConciliaRighe>(sql, new { Tenant = tenant, IdMaster = idMaster, IdRiga = idRiga });
            return result.ToList();
        }
    }
}
