using EmmaServer.Entities;
using Dapper;
using System.Text;


namespace EmmaServer.Repositories;

public interface IDocRepository: IRepositoryGenerico<EmmaDoc>
{
    //Task<List<EmmaDoc?>> GetDocsByFornitore(string fornitore);
    Task<List<EmmaDoc?>> GetDocsAsync(EmmaDocFilters docFilters);
}

public class DocRepository: RepositoryGenerico<EmmaDoc>, IDocRepository
{
    private IUserConnectionProvider _connectionProvider;
    public DocRepository(IUserConnectionProvider connectionProvider) : base(connectionProvider)
    {
        _connectionProvider =  connectionProvider;
        ;
    }
    
    // public async  Task<List<EmmaDoc?>> GetDocsByFornitore(string fornitore)
    // {
    //
    //     var tenant = _connectionProvider.GetTenant();
    //         
    //     string sql = @$"SELECT id, file_name, data_creazione, content, tenant, stato  FROM docs 
    //                 WHERE tenant = @Tenant AND content->'document'->>'mittente' = @Mittente;";
    //
    //     var parametri = new {
    //         Tenant = tenant,
    //         Mittente = fornitore
    //     };
    //
    //     using var db = await CreaConnessione();
    //     
    //     var risultati = await db.QueryAsync<EmmaDoc>(sql, parametri);
    //     return risultati.ToList();
    // }

    public async Task<List<EmmaDoc?>> GetDocsAsync(EmmaDocFilters docFilters)
    {
        var tenant = _connectionProvider.GetTenant();

        // 1. Base query and static conditions
        var sqlBuilder = new StringBuilder(@"
        SELECT id, file_name, data_creazione, content, tenant, stato 
        FROM docs 
        WHERE tenant = @Tenant AND stato = @Stato");

        // 2. Initialize DynamicParameters with static values
        var parametri = new DynamicParameters();
        parametri.Add("Tenant", tenant);
        parametri.Add("Stato", docFilters.Stato);

        // 3. Conditionally append SQL and parameters
        if (!string.IsNullOrWhiteSpace(docFilters.Fornitore))
        {
            sqlBuilder.Append(" AND content->'document'->>'mittente' = @Fornitore");
            parametri.Add("Fornitore", docFilters.Fornitore);
        }

        if (!string.IsNullOrWhiteSpace(docFilters.NumeroDoc))
        {
            sqlBuilder.Append(" AND content->'document'->>'numero_bolla' = @NumeroBolla");
            parametri.Add("NumeroBolla", docFilters.NumeroDoc);
        }

        if (!string.IsNullOrWhiteSpace(docFilters.DataDoc))
        {
            sqlBuilder.Append(" AND content->'document'->>'data_bolla' = @DataBolla");
            parametri.Add("DataBolla", docFilters.DataDoc);
        }

        if (docFilters.TipoDoc != 0)
        {
            sqlBuilder.Append(" AND content->'document'->>'tipo_documento' = @TipoDoc");
            parametri.Add("TipoDoc", docFilters.TipoDoc.ToString());
        }



        // Append the final semicolon safely
        sqlBuilder.Append(";");

        using var db = await CreaConnessione();
    
        var risultati =  await db.QueryAsync<EmmaDoc>(sqlBuilder.ToString(), parametri);
        return risultati.ToList();
    }
}