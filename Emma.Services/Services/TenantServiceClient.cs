using Emma.Services.Http;
using EmmaServer.Entities;

namespace Emma.Services.Services;

public interface ITenantServiceClient
{
    Task<List<EmmaTenant>> GetsAsync();
    Task<int> AddAsync(EmmaTenant emmaTenant);
    Task<bool> EditAsync(EmmaTenant emmaTenant);

    Task<int> DeleteAsync(EmmaTenant emmaTenant);
}

public class TenantServiceClient : ServiceClientBase, ITenantServiceClient
{
    private const string Endpoint = "/api/tenants";

    public TenantServiceClient(string url, string user, string password)
        : base(url, user, password)
    {
    }

    public TenantServiceClient(HttpClient httpClient, string url, string user, string password)
        : base(httpClient, url, user, password)
    {
    }

    /// <inheritdoc />
    protected override Exception CreateError(HttpResponseMessage response, string body)
        => InvioError(response, body);

    public async Task<List<EmmaTenant>> GetsAsync()
        => await GetAsync<List<EmmaTenant>>(Endpoint, error: InvioErrorLegacy).ConfigureAwait(false)
           ?? new List<EmmaTenant>();

    /// <summary>AddTenantAsync lato server restituisce int?, quindi il null diventa 0.</summary>
    public async Task<int> AddAsync(EmmaTenant emmaTenant)
        => await PostAsync<int?>(Endpoint, emmaTenant).ConfigureAwait(false) ?? 0;

    /// <summary>UpdateTenantAsync lato server restituisce bool?, quindi il null diventa false.</summary>
    public async Task<bool> EditAsync(EmmaTenant emmaTenant)
        => await PutAsync<bool?>(Endpoint, emmaTenant).ConfigureAwait(false) ?? false;

    // Nessun endpoint DELETE /api/tenants lato server: la cancellazione non e' supportata.
    public Task<int> DeleteAsync(EmmaTenant emmaTenant) => Task.FromResult(0);
}
