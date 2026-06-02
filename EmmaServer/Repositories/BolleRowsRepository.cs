using Dapper; // Necessario per usare il metodo esteso QueryAsync
using EmmaServer.Entities;

namespace EmmaServer.Repositories;

public interface IBolleRowsRepository
{
    Task<IEnumerable<BolleRows>> GetRowsByMaster(int idMaster);
    Task<int> DeleteRowsByMaster(int idMaster);
}

public class BolleRowsRepository: RepositoryGenerico<BolleRows>, IBolleRowsRepository
{
    public BolleRowsRepository(IUserConnectionProvider connectionProvider) : base(connectionProvider)
    {
    }

    public async Task<IEnumerable<BolleRows>> GetRowsByMaster(int idMaster)
    {
        using var db = CreaConnessione();
        
        const string sql = "SELECT id FROM bolle_rows WHERE id_bolla = @Id_Bolla;";
        
        return await db.QueryAsync<BolleRows>(sql, new { Id_Bolla = idMaster });
    }
    
    public async Task<int> DeleteRowsByMaster(int idMaster)
    {
        using var db = CreaConnessione();
    
        const string sql = "DELETE FROM bolle_rows WHERE id_bolla = @IdMaster;";
    
        // ExecuteAsync restituisce il numero di righe coinvolte (affected rows)
        return await db.ExecuteAsync(sql, new { IdMaster = idMaster });
    }
}