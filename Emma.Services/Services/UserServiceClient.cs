using Emma.Services.Http;
using EmmaServer.Entities;
using EmmaServer.Entities.Dtos;
namespace Emma.Services.Services;

public interface IUserServiceClient
{
    Task<List<EmmaUser>> GetsAsync(string tenant);
    Task<int> CambiaPasswordAsync(CambiaPasswordRequest cambiaPasswordRequest);
    Task<int> AddAsync(EmmaUser emmaUser);
    Task<bool> EditAsync(EmmaUser emmaUser);

    Task<int> DeleteAsync(EmmaUser emmaUser);
}

public class UserServiceClient : ServiceClientBase, IUserServiceClient
{
    private const string Endpoint = "/api/users";
    private const string EndpointPassword = "/api/users/password";

    public UserServiceClient(string url, string user, string password)
        : base(url, user, password)
    {
    }

    public UserServiceClient(HttpClient httpClient, string url, string user, string password)
        : base(httpClient, url, user, password)
    {
    }

    /// <inheritdoc />
    protected override Exception CreateError(HttpResponseMessage response, string body)
        => InvioError(response, body);

    /// <summary>L'endpoint degli utenti e' /api/users, con il tenant in querystring.</summary>
    public async Task<List<EmmaUser>> GetsAsync(string tenant)
        => await GetAsync<List<EmmaUser>>($"{Endpoint}?tenant={Uri.EscapeDataString(tenant)}").ConfigureAwait(false)
           ?? new List<EmmaUser>();

    /// <summary>Restituisce 1 se il cambio password e' andato a buon fine, 0 altrimenti. Non lancia.</summary>
    public async Task<int> CambiaPasswordAsync(CambiaPasswordRequest cambiaPasswordRequest)
        => await TrySendAsync(HttpMethod.Put, EndpointPassword, cambiaPasswordRequest).ConfigureAwait(false) ? 1 : 0;

    /// <summary>AddUserAsync lato server restituisce int?, quindi il null diventa 0.</summary>
    public async Task<int> AddAsync(EmmaUser emmaUser)
        => await PostAsync<int?>(Endpoint, emmaUser).ConfigureAwait(false) ?? 0;

    /// <summary>UpdateUserAsync lato server restituisce bool?, quindi il null diventa false.</summary>
    public async Task<bool> EditAsync(EmmaUser emmaUser)
        => await PutAsync<bool?>(Endpoint, emmaUser).ConfigureAwait(false) ?? false;

    // ATTENZIONE: lato server non esiste un endpoint DELETE /api/users,
    // quindi questa chiamata fallisce. Non e' esposta nella sezione admin.
    public async Task<int> DeleteAsync(EmmaUser emmaUser)
        => await base.DeleteAsync<int?>(Endpoint, emmaUser).ConfigureAwait(false) ?? 0;
}
