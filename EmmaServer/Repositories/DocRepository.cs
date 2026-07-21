using EmmaServer.Entities;
using Dapper;
using System.Text;
using System.Text.Json;


namespace EmmaServer.Repositories;

public interface IDocRepository: IRepositoryGenerico<EmmaDoc>
{
    Task<List<EmmaDoc?>> GetDocsAsync(EmmaDocFilters docFilters);
    Task UpdateRigaDocAsync(ArticoloBolla articoloBolla);
    Task InsertRigaDocAsync(ArticoloBolla articoloBolla);
    Task DeleteRigaDocAsync(ArticoloBolla articoloBolla);
    
    Task CambiaStatoAsync(CambioStato cambioStato);
    Task<int> CleanDocAsync();
}

public class DocRepository: RepositoryGenerico<EmmaDoc>, IDocRepository
{
    private IUserConnectionProvider _connectionProvider;
    public DocRepository(IUserConnectionProvider connectionProvider) : base(connectionProvider)
    {
        _connectionProvider =  connectionProvider;
        ;
    }

    public async Task DeleteRigaDocAsync(ArticoloBolla articoloBolla)
    {
        var tenant = _connectionProvider.GetTenant();

        // 1. Base query and static conditions
        var sqlBuilder = new StringBuilder(@"
        SELECT id, file_name, data_creazione, content, tenant, stato 
        FROM docs 
        WHERE tenant = @Tenant");
        var parametri = new DynamicParameters();
        parametri.Add("Tenant", tenant);
        
        sqlBuilder.Append(" AND content->'document'->>'id' = @Id");
        parametri.Add("Id", articoloBolla.Id_Master);
        
        sqlBuilder.Append(";");

        using var db = await CreaConnessione();
        var emmaDoc = await db.QuerySingleAsync<EmmaDoc>(sqlBuilder.ToString(), parametri);
        //contenuto del content
        var ddtResponse = emmaDoc?.content?.Deserialize<DdtResponse>();
        var doc = ddtResponse?.Document;
        var riga = doc?.Articoli.FirstOrDefault((x => x.Id_Riga.ToLower() == articoloBolla.Id_Riga.ToLower()));
        doc.Articoli.Remove(riga);

        using JsonDocument ddtResponseModificato = ConvertObjectToJsonDocument(ddtResponse);
        emmaDoc.content = ddtResponseModificato;

        await UpdateAsync(emmaDoc);
    }
    

    public async Task InsertRigaDocAsync(ArticoloBolla articoloBolla)
    {
        var tenant = _connectionProvider.GetTenant();

        // 1. Base query and static conditions
        var sqlBuilder = new StringBuilder(@"
        SELECT id, file_name, data_creazione, content, tenant, stato 
        FROM docs 
        WHERE tenant = @Tenant");
        var parametri = new DynamicParameters();
        parametri.Add("Tenant", tenant);
        
        sqlBuilder.Append(" AND content->'document'->>'id' = @Id");
        parametri.Add("Id", articoloBolla.Id_Master);
        
        sqlBuilder.Append(";");

        using var db = await CreaConnessione();
        var emmaDoc = await db.QuerySingleAsync<EmmaDoc>(sqlBuilder.ToString(), parametri);
        
        var ddtResponse = emmaDoc?.content?.Deserialize<DdtResponse>();
        
        var riga = new ArticoloBolla();
        riga.Id_Master = articoloBolla.Id_Master;
        riga.Id_Riga = articoloBolla.Id_Riga;
        riga.Quantita = articoloBolla.Quantita;
        riga.Codice = articoloBolla.Codice;
        riga.Descrizione = articoloBolla.Descrizione;
        riga.Imponibile = articoloBolla.Imponibile;
        riga.Totale = articoloBolla.Totale;
        riga.Iva = articoloBolla.Iva;
        riga.UnitaMisura = articoloBolla.UnitaMisura;
        ddtResponse.Document.Articoli.Add(riga);

        using JsonDocument ddtResponseModificato = ConvertObjectToJsonDocument(ddtResponse);
        emmaDoc.content = ddtResponseModificato;

        await UpdateAsync(emmaDoc);
        
    }
    
    public async Task UpdateRigaDocAsync(ArticoloBolla articoloBolla)
    {
        var tenant = _connectionProvider.GetTenant();

        // 1. Base query and static conditions
        var sqlBuilder = new StringBuilder(@"
        SELECT id, file_name, data_creazione, content, tenant, stato 
        FROM docs 
        WHERE tenant = @Tenant");
        var parametri = new DynamicParameters();
        parametri.Add("Tenant", tenant);
        
        sqlBuilder.Append(" AND content->'document'->>'id' = @Id");
        parametri.Add("Id", articoloBolla.Id_Master);
        
        sqlBuilder.Append(";");

        using var db = await CreaConnessione();
        var emmaDoc = await db.QuerySingleAsync<EmmaDoc>(sqlBuilder.ToString(), parametri);
        //contenuto del content
        var ddtResponse = emmaDoc?.content?.Deserialize<DdtResponse>();
        var doc = ddtResponse?.Document;
        var riga = doc?.Articoli.FirstOrDefault((x => x.Id_Riga.ToLower() == articoloBolla.Id_Riga.ToLower()));
       
        riga.Quantita = articoloBolla.Quantita;
        riga.Codice = articoloBolla.Codice;
        riga.Descrizione = articoloBolla.Descrizione;
        riga.Imponibile = articoloBolla.Imponibile;
        riga.Totale = articoloBolla.Totale;
        riga.Iva = articoloBolla.Iva;
        riga.UnitaMisura = articoloBolla.UnitaMisura;
            

        using JsonDocument ddtResponseModificato = ConvertObjectToJsonDocument(ddtResponse);
        emmaDoc.content = ddtResponseModificato;

        await UpdateAsync(emmaDoc);
        
    }
    
    public static JsonDocument ConvertObjectToJsonDocument<T>(T obj)
    {
        // Serializza l'oggetto direttamente in un array di byte (più veloce rispetto alla stringa)
        byte[] jsonBytes = JsonSerializer.SerializeToUtf8Bytes(obj);
    
        // Parsifica i byte per ottenere il JsonDocument
        return JsonDocument.Parse(jsonBytes);
    }

    public async Task<List<EmmaDoc?>> GetDocsAsync(EmmaDocFilters docFilters)
    {
        var tenant = _connectionProvider.GetTenant();

        // 1. Base query and static conditions
        var sqlBuilder = new StringBuilder(@"
        SELECT id, file_name, data_creazione, content, tenant, stato , allegato
        FROM docs 
        WHERE tenant = @Tenant ");

        // 2. Initialize DynamicParameters with static values
        var parametri = new DynamicParameters();
        parametri.Add("Tenant", tenant);
        
        if (docFilters.Stato > -1)
        {
            sqlBuilder.Append(" AND stato = @Stato");
            parametri.Add("Stato", docFilters.Stato);
        }

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

    public async Task CambiaStatoAsync(CambioStato cambioStato)
    {
        var tenant = _connectionProvider.GetTenant();
        
        var sqlBuilder = new StringBuilder(@"
        UPDATE docs SET stato = @Stato         
        WHERE tenant = @Tenant");
        var parametri = new DynamicParameters();
        parametri.Add("Stato", cambioStato.Stato);
        parametri.Add("Tenant", tenant);
        
        sqlBuilder.Append(" AND content->'document'->>'id' = @Id");
        parametri.Add("Id", cambioStato.Id);
        
        sqlBuilder.Append(";");
        
        using var db = await CreaConnessione();
        var ret = await db.ExecuteAsync(sqlBuilder.ToString(), parametri);
    }

    /// <summary>
    /// Servizio in background che cancella i documenti chiusi con data superiore ai 30gg
    /// Servizio in background che cancella i documenti aperti con data superiore ai 180gg
    /// </summary>
    /// <returns></returns>
    public async Task<int> CleanDocAsync()
    {
        var sql = """
            DELETE FROM docs 
            WHERE stato = 1 
              AND data_creazione < NOW() - INTERVAL '30 days'
            """;
        using var db = await CreaConnessione();
        var ret = await db.ExecuteAsync(sql);

        sql = """
            DELETE FROM docs 
            WHERE stato = 0 
              AND data_creazione < NOW() - INTERVAL '180 days'
            """;

        ret = ret + await db.ExecuteAsync(sql);

        return ret;
    }
}