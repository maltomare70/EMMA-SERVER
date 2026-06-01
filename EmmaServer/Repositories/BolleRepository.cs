using EmmaServer.Entities;
using Dapper;


namespace EmmaServer.Repositories;

public interface IBolleRepository: IRepositoryGenerico<Bolle>
{
    Task<List<Bolle?>> GetBolleByFornitore(string fornitore);
    Task<Bolle?> GetBollaAsync(string fornitore, string numeroBolla, string dataBolla);
}

public class BolleRepository: RepositoryGenerico<Bolle>, IBolleRepository
{
    public BolleRepository(IUserConnectionProvider connectionProvider) : base(connectionProvider)
    {
    }
    
    public async  Task<List<Bolle?>> GetBolleByFornitore(string fornitore)
    {
        string sql = @"SELECT id, file_name, data  FROM bolle 
                    WHERE data->'document'->>'mittente' = @Mittente;";

        var parametri = new {
            Mittente = fornitore
        };

        using var db = CreaConnessione();
        
        var risultati = await db.QueryAsync<Bolle>(sql, parametri);
        return risultati.ToList();
    }

    public async Task<Bolle?> GetBollaAsync(string fornitore, string numeroBolla, string dataBolla)
    {
        string sql = @"
            SELECT id, file_name, data 
            FROM bolle 
            WHERE data->'document'->>'mittente' = @Mittente
              AND data->'document'->>'numero_bolla' = @NumeroBolla
              AND data->'document'->>'data_bolla' = @DataBolla;";

        var parametri = new {
            Mittente = fornitore,
            NumeroBolla = numeroBolla,
            DataBolla = dataBolla,
        };

        using var db = CreaConnessione();
        
        return await db.QueryFirstOrDefaultAsync<Bolle>(sql, parametri);
    }
}