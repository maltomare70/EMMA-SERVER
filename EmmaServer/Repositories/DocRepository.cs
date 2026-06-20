using EmmaServer.Entities;
using Dapper;


namespace EmmaServer.Repositories;

public interface IDocRepository: IRepositoryGenerico<EmmaDoc>
{
    Task<List<EmmaDoc?>> GetDocsByFornitore(string fornitore);
    Task<EmmaDoc?> GetDocAsync(string fornitore, string numeroDoc, string dataDoc);
}

public class DocRepository: RepositoryGenerico<EmmaDoc>, IDocRepository
{
    private IUserConnectionProvider _connectionProvider;
    public DocRepository(IUserConnectionProvider connectionProvider) : base(connectionProvider)
    {
        _connectionProvider =  connectionProvider;
        ;
    }
    
    public async  Task<List<EmmaDoc?>> GetDocsByFornitore(string fornitore)
    {

        var tenant = _connectionProvider.GetTenant();
            
        string sql = @$"SELECT id, file_name, data_creazione, content, tenant, stato  FROM docs 
                    WHERE tenant = @Tenant AND content->'document'->>'mittente' = @Mittente;";

        var parametri = new {
            Tenant = tenant,
            Mittente = fornitore
        };

        using var db = await CreaConnessione();
        
        var risultati = await db.QueryAsync<EmmaDoc>(sql, parametri);
        return risultati.ToList();
    }

    public async Task<EmmaDoc?> GetDocAsync(string fornitore, string numeroDoc, string dataDoc)
    {
        string sql = @"
            SELECT id, file_name, data_creazione, content, tenant, stato 
            FROM docs 
            WHERE content->'document'->>'mittente' = @Mittente
              AND content->'document'->>'numero_bolla' = @NumeroBolla
              AND content->'document'->>'data_bolla' = @DataBolla;";

        var parametri = new {
            Mittente = fornitore,
            NumeroBolla = numeroDoc,
            DataBolla = dataDoc,
        };

        using var db = await CreaConnessione();
        
        return await db.QueryFirstOrDefaultAsync<EmmaDoc>(sql, parametri);
    }
}