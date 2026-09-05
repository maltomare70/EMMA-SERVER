using Dapper;

using EmmaServer.Entities;

namespace EmmaServer.Repositories;


public interface IArticoliRepository : IRepositoryGenerico<EmmaArticoli>
{
    Task<IEnumerable<EmmaArticoli>> GetAllTenantByFornitoreAsync(string tenant, int idFornitore);
    Task<string> GetRifByCodiceAsync(string rifCodice, int idfornitore);
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

    public async Task<string> GetRifByCodiceAsync(string rifCodice, int idfornitore)
    {
        using var db = await CreaConnessione();

        string query = $"SELECT * FROM articoli WHERE rifcodice = @rifcodice AND idfornitore = @idfornitore";

        var result = await db.QueryAsync<EmmaArticoli>(query, new { rifCodice = rifCodice, idfornitore = idfornitore });
        return result.FirstOrDefault()?.codice ?? string.Empty;
    }
}