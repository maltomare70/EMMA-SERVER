using EmmaServer.Entities;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;

namespace Emma.Services.Services;

public interface ITenantServiceClient
{
    Task<List<EmmaTenant>> GetsAsync();
    Task<int> AddAsync(EmmaTenant emmaTenant);
    Task<bool> EditAsync(EmmaTenant emmaTenant);

    Task<int> DeleteAsync(EmmaTenant emmaTenant);
}
public class TenantServiceClient : ITenantServiceClient
{
    private readonly HttpClient Client;
    private readonly string _url;
    private readonly string _user;
    private readonly string _password;

    public TenantServiceClient(string url, string user, string password)
    {
        _url = url;
        _user = user;
        _password = password;

        Client = new HttpClient();
    }

    public async Task<List<EmmaTenant>> GetsAsync()
    {
        string urlApi = $"{_url}/api/tenants";

        using var request = new HttpRequestMessage(HttpMethod.Get, urlApi);

        // Codifica "username:password" in Base64
        var authToken = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_user}:{_password}"));

        // Aggiungi l'header Authorization nel formato "Basic [Token]"
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", authToken);

        HttpResponseMessage response = await Client.SendAsync(request);

        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadFromJsonAsync<List<EmmaTenant>>() ?? new List<EmmaTenant>();

        }
        else
        {
            throw new Exception($"Errore durante l'invio: {response.StatusCode} {response.Content}");
        }
    }

    public async Task<int> AddAsync(EmmaTenant emmaTenant)
    {
        string urlApi = $"{_url}/api/tenants";

        using var request = new HttpRequestMessage(HttpMethod.Post, urlApi);

        // Codifica "username:password" in Base64
        var authToken = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_user}:{_password}"));

        // Aggiungi l'header Authorization nel formato "Basic [Token]"
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", authToken);

        // L'endpoint POST /api/tenants attende il tenant nel body
        request.Content = JsonContent.Create(emmaTenant);

        HttpResponseMessage response = await Client.SendAsync(request);

        if (response.IsSuccessStatusCode)
        {
            // AddTenantAsync restituisce int?, quindi il server può serializzare null
            return await response.Content.ReadFromJsonAsync<int?>() ?? 0;

        }
        else
        {
            var errore = await response.Content.ReadAsStringAsync();
            throw new Exception($"Errore durante l'invio: {response.StatusCode} {errore}");
        }
    }

    public async Task<bool> EditAsync(EmmaTenant emmaTenant)
    {
        string urlApi = $"{_url}/api/tenants";

        using var request = new HttpRequestMessage(HttpMethod.Put, urlApi);

        // Codifica "username:password" in Base64
        var authToken = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_user}:{_password}"));

        // Aggiungi l'header Authorization nel formato "Basic [Token]"
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", authToken);

        // L'endpoint PUT /api/tenants attende il tenant nel body
        request.Content = JsonContent.Create(emmaTenant);

        HttpResponseMessage response = await Client.SendAsync(request);

        if (response.IsSuccessStatusCode)
        {
            // UpdateTenantAsync restituisce bool?, quindi il server serializza true/false/null
            return await response.Content.ReadFromJsonAsync<bool?>() ?? false;

        }
        else
        {
            var errore = await response.Content.ReadAsStringAsync();
            throw new Exception($"Errore durante l'invio: {response.StatusCode} {errore}");
        }
    }

    public async Task<int> DeleteAsync(EmmaTenant emmaTenant)
    {
        return 0;
    }
}