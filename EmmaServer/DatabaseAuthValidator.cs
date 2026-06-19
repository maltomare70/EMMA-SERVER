using System.Data;
using Npgsql;
using EmmaServer.Entities;
using Dapper;
using EmmaServer.Helpers;



namespace EmmaServer;

public record AuthValidationResult(bool IsValid, string? DatabaseName, string? Tenant);

public class DatabaseAuthValidator: IBasicAuthValidator
{
    private readonly IUserConnectionProvider _connectionProvider;

    public DatabaseAuthValidator(IUserConnectionProvider connectionProvider)
    {
        _connectionProvider = connectionProvider;
        Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true;
    }
    
    protected IDbConnection CreaConnessione() 
    {
        string stringaDinamica = _connectionProvider.GetEmmaConnectionString();
        return new NpgsqlConnection(stringaDinamica);
    }
    
    public async Task<AuthValidationResult> ValidaCredenzialiAsync(string email, string password)
    {
            using var db = CreaConnessione();
            const string sql = "SELECT * FROM users WHERE email = @email;";
            var user = await db.QueryFirstAsync<EmmaUser>(sql, new { email = email });
            
            if ( user is null || string.IsNullOrWhiteSpace(user.pwd)) return new AuthValidationResult(false, null, null);

        if (PasswordHelper.VerificaPassword(password, user.pwd!))
            {
                if (string.IsNullOrWhiteSpace(user.tenant))  return new AuthValidationResult(false, null, null);
                // Restituiamo successo e il nome del database associato!
                return new AuthValidationResult(true, UserConnectionProvider.DATABASE_NAME, user.tenant);
            }
            return new AuthValidationResult(false, null, null);
    }
}