using Dapper; // Necessario per usare il metodo esteso QueryAsync
using EmmaServer.Entities;

namespace EmmaServer.Repositories;

public interface IBolleRowsRepository
{
    Task<IEnumerable<BolleRows>> GetRowsByMaster(int idMaster);
}

public class BolleRowsRepository: RepositoryGenerico<BolleRows>, IBolleRowsRepository
{
    public BolleRowsRepository(IUserConnectionProvider connectionProvider) : base(connectionProvider)
    {
    }

    public async Task<IEnumerable<BolleRows>> GetRowsByMaster(int idMaster)
    {
        using var db = await CreaConnessione();
        
        const string sql = "SELECT * FROM bolle_rows WHERE id_bolla = @Id_Bolla;";
        
        return await db.QueryAsync<BolleRows>(sql, new { Id_Bolla = idMaster });
    }
}