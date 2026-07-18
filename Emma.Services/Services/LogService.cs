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
}
