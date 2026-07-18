using Dapper;
using Dapper.Contrib.Extensions;
using EmmaServer.Entities;

namespace EmmaServer.Repositories;


public interface ILogRepository
{
    Task<IEnumerable<EmmaLog>> GetAllByTenantAsync(string tenant);
}
public class LogRepository : RepositoryGenerico<EmmaLog>, ILogRepository
{
    public LogRepository(IUserConnectionProvider connectionProvider) : base(connectionProvider)
    {
    }

    public async Task<IEnumerable<EmmaLog>> GetAllByTenantAsync(string tenant)
    {
        // Query SQL specifica per questa ricerca (Postgres usa il minuscolo di default)
        const string sql = "SELECT * FROM log WHERE tenant = @Tenant ORDER BY data_creazione DESC;";

        // Sfruttiamo il metodo del padre per ottenere la connessione al database del tenant corrente
        using var db = await CreaConnessione();

        // Eseguiamo una normale query Dapper (non Contrib)
        return await db.QueryAsync<EmmaLog>(sql, new { Tenant = tenant });
    }
}
