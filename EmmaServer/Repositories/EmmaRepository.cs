
using Dapper;
using Dapper.Contrib.Extensions;
using EmmaServer.Entities;
using Npgsql;
using System.Data;
using System.Reflection;
using System.Text;
using System.Text.Json;


namespace EmmaServer.Repositories;

public interface IEmmaRepository
{
    Task InitializeAsync();
    Task TestAsync();
}


public class EmmaRepository: IEmmaRepository
{
    private readonly IUserConnectionProvider _connectionProvider;
    private readonly IConfiguration _configuration;
    public EmmaRepository(IUserConnectionProvider connectionProvider, IConfiguration config)
    {
        _connectionProvider = connectionProvider;
        _configuration = config;

    }

    public async Task TestAsync()
    {
        string stringaDinamica = _connectionProvider.GetEmmaConnectionString();

        using (var conn = new NpgsqlConnection(stringaDinamica))
        {
            // Se si blocca qui, la porta 5432 è probabilmente bloccata dal tuo provider internet o firewall
            await conn.OpenAsync(); 
        
            // Fai una query di test per essere sicuro al 100%
            using (var cmd = new NpgsqlCommand("SELECT 1", conn))
            {
                await cmd.ExecuteScalarAsync();
            }
        }
    }
    

    public async Task InitializeAsync()
    {
        //await CreateDatabaseIfNotExistsAsync();
        await CreateTableFromClassAsync<EmmaTenant>();
        await CreateTableFromClassAsync<EmmaUser>();
        await CreateTableFromClassAsync<EmmaDoc>();
        await CreateTableFromClassAsync<EmmaFornitori>();
        await CreateTableFromClassAsync<EmmaArticoli>();
    }
    

    private async Task<IDbConnection> CreaConnessionePostreSQL()
    {
        string stringaDinamica = _connectionProvider.GetConnectionStringPostresSQL();
        return new NpgsqlConnection(stringaDinamica);
    }

    protected async Task<IDbConnection> CreaConnessione()
    {
        //await CreateDatabaseIfNotExistsAsync();
        string stringaDinamica = _connectionProvider.GetEmmaConnectionString();
        return new NpgsqlConnection(stringaDinamica);
    }

    private async Task CreateDatabaseIfNotExistsAsync()
    {
        using var db = await CreaConnessionePostreSQL();
        // Verifica se il database esiste già
        string checkDbSql = "SELECT 1 FROM pg_database WHERE datname = @DbName;";
        var exists = await db.ExecuteScalarAsync<int?>(checkDbSql, new { DbName = UserConnectionProvider.DATABASE_NAME });

        if (exists == null)
        {
            // Crea il database (Le query CREATE DATABASE non accettano parametri, serve la stringa formattata)
            // Assicurati che _databaseName sia sicuro e non provenga da input utente
            string createDbSql = $"CREATE DATABASE \"{UserConnectionProvider.DATABASE_NAME}\";";
            await db.ExecuteAsync(createDbSql);
            Console.WriteLine($"Database '{UserConnectionProvider.DATABASE_NAME}' creato con successo.");
        }
    }

    public async Task CreateTableFromClassAsync<T>()
    {
        var type = typeof(T);
        //// PostgreSQL preferisce i nomi delle tabelle in minuscolo/snake_case
        //string tableName = type.Name.ToLower();

        var tableAttr = typeof(T).GetCustomAttribute<TableAttribute>();
        string tableName = tableAttr != null ? tableAttr.Name : typeof(T).Name;

        var proprietàDaEscludere = new[] { "IsDirty"};
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => !proprietàDaEscludere.Contains(p.Name)).ToArray();
        var columnDefinitions = new StringBuilder();

        foreach (var prop in properties)
        {
            string columnName = prop.Name.ToLower(); // Nome colonna in minuscolo
            string sqlDataType = MapCsharpTypeToPostgres(prop.PropertyType, columnName);

            if (columnDefinitions.Length > 0)
                columnDefinitions.Append(", ");

            columnDefinitions.Append($"\"{columnName}\" {sqlDataType}");
        }

        columnDefinitions.Append($" , CONSTRAINT {tableName}_pk PRIMARY KEY(id) ");


        string sql = $"CREATE TABLE IF NOT EXISTS \"{tableName}\" ({columnDefinitions});";


        if (tableName.ToLower() == "users")
            sql +=  " CREATE UNIQUE INDEX IF NOT EXISTS users_email_idx ON public.users USING btree (email);";
        else if (tableName.ToLower() == "articoli")
            sql +=  " CREATE INDEX IF NOT EXISTS id_fornitore_idx ON public.articoli USING btree (idFornitore);";
        
        using var db = await CreaConnessione();
        await db.ExecuteAsync(sql);

        Console.WriteLine($"Tabella '{tableName}' creata o verificata con successo.");
    }

    private static string MapCsharpTypeToPostgres(Type type, string columnName)
    {
        // Gestione dei tipi Nullable<T>
        var underlyingType = Nullable.GetUnderlyingType(type) ?? type;

        // Se la proprietà si chiama 'id' ed è un intero, la impostiamo come chiave primaria auto-incrementante
        if (columnName == "id" && (underlyingType == typeof(int) || underlyingType == typeof(long)))
        {
            return "INTEGER GENERATED ALWAYS AS IDENTITY";
        }

        string postgresType = underlyingType switch
        {
            _ when underlyingType == typeof(int) => "INTEGER",
            _ when underlyingType == typeof(long) => "BIGINT",
            _ when underlyingType == typeof(string) => "TEXT",
            _ when underlyingType == typeof(bool) => "BOOLEAN",
            _ when underlyingType == typeof(DateTime) => "TIMESTAMP",
            _ when underlyingType == typeof(decimal) => "NUMERIC",
            _ when underlyingType == typeof(double) => "DOUBLE PRECISION",
            _ when underlyingType == typeof(Guid) => "UUID",
            _ when underlyingType == typeof(JsonDocument) => "JSONB",
            _ when underlyingType == typeof(byte[]) => "BYTEA",
            _ => "TEXT" // Tipo di fallback
        };

        // Aggiunge NOT NULL se il tipo originale non è Nullable (e non è una stringa, che in C# è reference type)
        if (type.IsValueType && Nullable.GetUnderlyingType(type) == null)
        {
            if (postgresType == "TIMESTAMP")
                postgresType += " DEFAULT now() NOT NULL";
            else
                postgresType += " NOT NULL";
        }

        return postgresType;
    }
}
