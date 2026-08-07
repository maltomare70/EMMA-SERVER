using EmmaServer.Entities;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;

namespace Emma.Services.Services;

public interface ILogService
{
    Task<List<EmmaLog>> GetAllAsync();
}

public class LogService
{
    private readonly HttpClient Client;
    private readonly string _url;
    private readonly string _user;
    private readonly string _password;
    public LogService(string url, string user, string password)
    {
        _url = url;
        _user = user;
        _password = password;

        Client = new HttpClient();
    }

    public async Task<List<EmmaLog>> GetAllAsync()
    {
        string urlApi = $"{_url}/api/logs/tenant";
        using var request = new HttpRequestMessage(HttpMethod.Get, urlApi);
        var authToken = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_user}:{_password}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", authToken);
        HttpResponseMessage response = await Client.SendAsync(request);
        if (response.IsSuccessStatusCode)
        {
            var emmaFornitoriList = await response.Content.ReadFromJsonAsync<List<EmmaLog>>().ConfigureAwait(false);
            return emmaFornitoriList?.ToList() ?? new List<EmmaLog>();
        }
        else
        {
            return new List<EmmaLog>();
        }
    }

    /// <summary>
    /// Log di un tenant specifico. L'endpoint /api/logs/tenant ricava il tenant dai claim:
    /// per l'utente "admin" il BasicAuthenticationHandler legge l'header "x-tenant"
    /// (in sua assenza usa "emma"), quindi è così che l'amministratore sceglie il tenant.
    /// A differenza di GetAllAsync() gli errori non vengono silenziati.
    /// </summary>
    public async Task<List<EmmaLog>> GetAllAsync(string tenant)
    {
        string urlApi = $"{_url}/api/logs/tenant";
        using var request = new HttpRequestMessage(HttpMethod.Get, urlApi);
        var authToken = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_user}:{_password}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", authToken);

        if (!string.IsNullOrWhiteSpace(tenant))
            request.Headers.Add("x-tenant", tenant);

        HttpResponseMessage response = await Client.SendAsync(request);

        if (response.IsSuccessStatusCode)
        {
            var logs = await response.Content.ReadFromJsonAsync<List<EmmaLog>>().ConfigureAwait(false);
            return logs ?? new List<EmmaLog>();
        }
        else
        {
            var errore = await response.Content.ReadAsStringAsync();
            throw new Exception($"Errore durante la lettura dei log: {response.StatusCode} {errore}");
        }
    }
}
