using EmmaServer.Entities;
using System.Net.Http.Headers;
using System.Text;
using System.Net.Http.Json;


namespace EmmaClientAv.Services;

public interface ILoginService
{
    Task<LoginResponse> LoginAsync();
}

public class LoginService : ILoginService
{
    private readonly HttpClient Client;
    private readonly string _url;
    private readonly string _user;
    private readonly string _password;

    public LoginService(string url, string user, string password)
    {
        _url = url;
        _user = user;
        _password = password;

        Client = new HttpClient();
    }

    public async Task<LoginResponse> LoginAsync()
    {
        string urlApi = $"{_url}/api/v1/auth";
        
        using var request = new HttpRequestMessage(HttpMethod.Post, urlApi);

        // Codifica "username:password" in Base64
        var authToken = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_user}:{_password}"));

        // Aggiungi l'header Authorization nel formato "Basic [Token]"
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", authToken);

        HttpResponseMessage response = await Client.SendAsync(request);

        if (response.IsSuccessStatusCode)
        {
            return  await response.Content.ReadFromJsonAsync<LoginResponse>();
            
        }
        else
        {
            throw new Exception($"Errore durante l'invio: {response.StatusCode} {response.Content}");
        }
    }
}