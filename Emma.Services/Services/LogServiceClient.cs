using Emma.Services.Http;
using EmmaServer.Entities;

namespace Emma.Services.Services;

public interface ILogServiceClient
{
    Task<List<EmmaLog>> GetAllAsync();
}

public class LogServiceClient : ServiceClientBase, ILogServiceClient
{
    private const string Endpoint = "/api/logs/tenant";

    private static readonly ApiErrorFactory LetturaLogError =
        (response, body) => new Exception($"Errore durante la lettura dei log: {response.StatusCode} {body}");

    public LogServiceClient(string url, string user, string password)
        : base(url, user, password)
    {
    }

    public LogServiceClient(HttpClient httpClient, string url, string user, string password)
        : base(httpClient, url, user, password)
    {
    }

    /// <summary>In caso di errore restituisce una lista vuota, non lancia.</summary>
    public async Task<List<EmmaLog>> GetAllAsync()
        => await TryGetAsync<List<EmmaLog>>(Endpoint).ConfigureAwait(false) ?? new List<EmmaLog>();

    /// <summary>
    /// Log di un tenant specifico. L'endpoint /api/logs/tenant ricava il tenant dai claim:
    /// per l'utente "admin" il BasicAuthenticationHandler legge l'header "x-tenant"
    /// (in sua assenza usa "emma"), quindi è così che l'amministratore sceglie il tenant.
    /// A differenza di GetAllAsync() gli errori non vengono silenziati.
    /// </summary>
    public async Task<List<EmmaLog>> GetAllAsync(string tenant)
        => await GetAsync<List<EmmaLog>>(Endpoint, tenant: tenant, error: LetturaLogError).ConfigureAwait(false)
           ?? new List<EmmaLog>();
}
