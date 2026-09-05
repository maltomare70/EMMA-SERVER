using Dapper;
using EmmaServer.Entities;

namespace EmmaServer.Repositories;

public interface IFornitoriRepository : IRepositoryGenerico<EmmaFornitori>
{
    Task<EmmaFornitori?> GetFornitoreByCodiceAsync(string codice, string tenant);
}

public class FornitoriRepository : RepositoryGenerico<EmmaFornitori>, IFornitoriRepository
{
    public FornitoriRepository(IUserConnectionProvider connectionProvider) : base(connectionProvider)
    {
    }

    public async Task<EmmaFornitori?> GetFornitoreByCodiceAsync(string codice, string tenant)
    {
        using var db = await CreaConnessione();
        string query = "SELECT * FROM fornitori WHERE descrizione ILIKE @codice AND tenant = @tenant";
        return await db.QueryFirstOrDefaultAsync<EmmaFornitori>(query, new { codice = codice, tenant = tenant });
    }

}