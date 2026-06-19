namespace EmmaServer;

using Microsoft.AspNetCore.Http;
using System.Security.Claims;

public interface IUserConnectionProvider
{
    string GetConnectionString();
    string GetConnectionStringPostresSQL();
    string GetEmmaConnectionString();
    string GetTenant();
}

public class UserConnectionProvider : IUserConnectionProvider
{
    public const string DATABASE_NAME = "emma";

    private readonly IHttpContextAccessor _httpContextAccessor;

    public UserConnectionProvider(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }
    
    public string GetEmmaConnectionString()
    {
        return $"Host=localhost:5432;Username=marco;Password=malt0mare;Database={DATABASE_NAME}";
    }
    public string GetConnectionStringPostresSQL()
    {
        return $"Host=localhost:5432;Username=postgres;Password=malt0mare;Database=postgres";
    }

    public string GetConnectionString()
    {
        return GetEmmaConnectionString();

        var context = _httpContextAccessor.HttpContext;
        if (context == null)
        {
            throw new Exception("Contesto HTTP non disponibile.");
        }
        
        var databaseName = context.User.FindFirst("database_name")?.Value;

        if (string.IsNullOrEmpty(databaseName))
        { 
            throw new UnauthorizedAccessException("Impossibile determinare il database dell'utente.");
        }
        
        return $"Host=localhost:5432;Username=marco;Password=malt0mare;Database={databaseName}";
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