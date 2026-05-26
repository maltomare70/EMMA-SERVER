namespace EmmaServer;

using Microsoft.AspNetCore.Http;
using System.Security.Claims;

public interface IUserConnectionProvider
{
    string GetConnectionString();
}

public class UserConnectionProvider : IUserConnectionProvider
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public UserConnectionProvider(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public string GetConnectionString()
    {
        var context = _httpContextAccessor.HttpContext;
        if (context == null)
        {
            throw new Exception("Contesto HTTP non disponibile.");
        }
        
        var datbaseName = context.User.FindFirst("database_name")?.Value;

        if (string.IsNullOrEmpty(datbaseName))
        { 
            throw new UnauthorizedAccessException("Impossibile determinare il database dell'utente.");
        }
        
        return $"Host=localhost:5432;Username=marco;Password=malt0mare;Database={datbaseName}";
    }
}