using Dapper;

using EmmaServer.Entities;

namespace EmmaServer.Repositories;


public interface IArticoliRepository : IRepositoryGenerico<EmmaArticoli>
{
    Task<IEnumerable<EmmaArticoli>> GetAllTenantByFornitoreAsync(string tenant, int idFornitore);
}

public class ArticoliRepository : RepositoryGenerico<EmmaArticoli>, IArticoliRepository
{
    public ArticoliRepository(IUserConnectionProvider connectionProvider) : base(connectionProvider)
    {
    }

    public async Task<IEnumerable<EmmaArticoli>> GetAllTenantByFornitoreAsync(string tenant, int idFornitore)
    {
        using var db = await  CreaConnessione();
        
        // Query sicura parametrizzata
        string query = $"SELECT * FROM articoli WHERE tenant = @tenant AND idFornitore = @idFornitore";

        return await db.QueryAsync<EmmaArticoli>(query, new { tenant = tenant, idFornitore = idFornitore });
    }
}