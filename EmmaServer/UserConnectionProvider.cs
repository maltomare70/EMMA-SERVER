namespace EmmaServer;

using Microsoft.AspNetCore.Http;
using Npgsql;
using System.Security.Claims;

public interface IUserConnectionProvider
{
    string GetConnectionStringPostresSQL();
    string GetEmmaConnectionString();
    string GetTenant();
}

public class UserConnectionProvider : IUserConnectionProvider
{
    public const string DATABASE_MASTER = "postgres";
    public const string DATABASE_NAME = "emma";
    
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IConfiguration _configuration;
    
    public UserConnectionProvider(IHttpContextAccessor httpContextAccessor, IConfiguration configuration)
    {
        _httpContextAccessor = httpContextAccessor;
        _configuration =  configuration;
    }
    
    
    public string GetEmmaConnectionString()
    {
        
        var builder = new NpgsqlConnectionStringBuilder()
        {
            Host = _configuration["Database:Host"],
            Database = _configuration["Database:Database"],
            Username = _configuration["Database:UserName"],
            Password = _configuration["Database:Password"],
            SslMode = SslMode.Require,
            TrustServerCertificate = true,
            Timeout = 15
        };

        return builder.ConnectionString;
    }
    public string GetConnectionStringPostresSQL()
    {
        var server = _configuration["Database:server"] ?? "localhost:5432";
        var postgres = _configuration["Database:Master:Name"] ?? DATABASE_MASTER;
        var user = _configuration["Database:Master:User"] ?? "postgres";
        var password = _configuration["Database:Master:Password"] ?? "";
        return $"Host={server};Username={user};Password={password};Database={postgres}";
    }
    

    public string GetTenant()
    {
        var context = _httpContextAccessor.HttpContext;
        if (context == null)
        {
            throw new Exception("Contesto HTTP non disponibile.");
        }
        
        var tenant = context.User.FindFirst("tenant")?.Value;

        if (string.IsNullOrEmpty(tenant))
        { 
            throw new UnauthorizedAccessException("Impossibile determinare il tenant dell'utente.");
        }

        return tenant;
    }
}