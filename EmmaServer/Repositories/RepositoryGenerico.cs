
using Dapper;
using Dapper.Contrib.Extensions;
using EmmaServer.Entities;
using Npgsql;
using System.Data;
using System.Reflection;


namespace EmmaServer.Repositories;


public interface IRepositoryGenerico<T> where T : class, IEntity
{
   // Task InitializeAsync();
    Task<int> AddAsync(T entita);
    Task<T?> GetIdAsync(int id);
    Task<IEnumerable<T>> GetAllAsync();
    Task<IEnumerable<T>> GetAllTenantAsync(string tenant);
    Task<bool> UpdateAsync(T entita);
    Task<bool> DeleteAsync(T entita);
}

public class RepositoryGenerico<T> : IRepositoryGenerico<T> where T : class, IEntity
{
 
    private readonly string? _connectionString;

    private readonly IUserConnectionProvider _connectionProvider;
    private readonly string? _tenant;
    public RepositoryGenerico(IUserConnectionProvider connectionProvider)
    {
        //_connectionString = "Host=localhost:5432;Username=marco;Password=malt0mare;Database=nome_db";

        _connectionProvider = connectionProvider;
       
        _tenant = _connectionProvider.GetTenant();
        
        // Chicca: Questo dice a Dapper di mappare automaticamente 
        // le proprietà PascalCase (C#) con le colonne snake_case (Postgres)
        Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true;
    }

    protected async Task<IDbConnection> CreaConnessione() 
    {
        //await CreateDatabaseIfNotExistsAsync();
        string stringaDinamica = _connectionProvider.GetEmmaConnectionString();
        return new NpgsqlConnection(stringaDinamica);
    }


    // CREATE
    public async Task<int> AddAsync(T entita)
    {
        using var db = await CreaConnessione();
        // Inserisce l'entità e restituisce l'ID generato (long, che castiamo a int)
        long idGenerato = await db.InsertAsync(entita);
        return (int)idGenerato;
    }

    // READ (Singolo)
    public async Task<T?> GetIdAsync(int id)
    {
        using var db = await  CreaConnessione();
        return await db.GetAsync<T>(id);
    }

    // READ (Tutti)
    public async Task<IEnumerable<T>> GetAllAsync()
    {
        using var db = await  CreaConnessione();
        return await db.GetAllAsync<T>();
    }
    
    public async Task<IEnumerable<T>> GetAllTenantAsync(string tenant)
    {
        using var db = await  CreaConnessione();

        // Recupera dinamicamente il nome della tabella dall'attributo [Table("...")]
        var tableAttr = typeof(T).GetCustomAttribute<TableAttribute>();
        string tableName = tableAttr != null ? tableAttr.Name : typeof(T).Name;

        // Query sicura parametrizzata
        string query = $"SELECT * FROM {tableName} WHERE tenant = @tenant";

        return await db.QueryAsync<T>(query, new { tenant = tenant });
    }

    // UPDATE
    public async Task<bool> UpdateAsync(T entita)
    {
        using var db = await CreaConnessione();
        // Ritorna true se l'aggiornamento è andato a buon fine
        return await db.UpdateAsync(entita);
    }

    // DELETE
    public async Task<bool> DeleteAsync(T entita)
    {
        using var db = await  CreaConnessione();
        return await db.DeleteAsync(entita);
    }
}
