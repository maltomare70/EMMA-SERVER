using EmmaServer.Entities;
using System;
using System.Collections.Generic;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;

namespace Emma.Services.Services;

public interface IUserService
{
    Task<List<EmmaUser>> GetsAsync(string tenant);
    Task<int> CambiaPasswordAsync(CambiaPasswordRequest cambiaPasswordRequest);
    Task<int> AddAsync(EmmaUser emmaUser);
    Task<bool> EditAsync(EmmaUser emmaUser);

    Task<int> DeleteAsync(EmmaUser emmaUser);
}

public class UserService : IUserService
{
    private readonly HttpClient Client;
    private readonly string _url;
    private readonly string _user;
    private readonly string _password;
    public UserService(string url, string user, string password)
    {
        _url = url;
        _user = user;
        _password = password;

        Client = new HttpClient();
    }

    public async Task<List<EmmaUser>> GetsAsync(string tenant)
    {
        // L'endpoint degli utenti è /api/users, con il tenant in querystring
        string urlApi = $"{_url}/api/users?tenant={Uri.EscapeDataString(tenant)}";

        using var request = new HttpRequestMessage(HttpMethod.Get, urlApi);

        // Codifica "username:password" in Base64
        var authToken = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_user}:{_password}"));

        // Aggiungi l'header Authorization nel formato "Basic [Token]"
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", authToken);

        HttpResponseMessage response = await Client.SendAsync(request);

        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadFromJsonAsync<List<EmmaUser>>() ?? new List<EmmaUser>();

        }
        else
        {
            var errore = await response.Content.ReadAsStringAsync();
            throw new Exception($"Errore durante l'invio: {response.StatusCode} {errore}");
        }
    }


    public async Task<int> CambiaPasswordAsync(CambiaPasswordRequest cambiaPasswordRequest)
    {
        string urlApi = $"{_url}/api/users/password";
        using var request = new HttpRequestMessage(HttpMethod.Put, urlApi);
        var authToken = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_user}:{_password}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", authToken);
        request.Content = JsonContent.Create(cambiaPasswordRequest);
        HttpResponseMessage response = await Client.SendAsync(request);
        if (response.IsSuccessStatusCode)
        {
            return 1;
        }
        else
        {
            return 0;
        }
    }

    public async Task<int> AddAsync(EmmaUser emmaUser)
    {
        string urlApi = $"{_url}/api/users";

        using var request = new HttpRequestMessage(HttpMethod.Post, urlApi);

        // Codifica "username:password" in Base64
        var authToken = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_user}:{_password}"));

        // Aggiungi l'header Authorization nel formato "Basic [Token]"
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", authToken);

        // L'endpoint POST /api/users attende l'utente nel body
        request.Content = JsonContent.Create(emmaUser);

        HttpResponseMessage response = await Client.SendAsync(request);

        if (response.IsSuccessStatusCode)
        {
            // AddUserAsync restituisce int?, quindi il server può serializzare null
            return await response.Content.ReadFromJsonAsync<int?>() ?? 0;

        }
        else
        {
            var errore = await response.Content.ReadAsStringAsync();
            throw new Exception($"Errore durante l'invio: {response.StatusCode} {errore}");
        }
    }

    public async Task<bool> EditAsync(EmmaUser emmaUser)
    {
        string urlApi = $"{_url}/api/users";

        using var request = new HttpRequestMessage(HttpMethod.Put, urlApi);

        // Codifica "username:password" in Base64
        var authToken = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_user}:{_password}"));

        // Aggiungi l'header Authorization nel formato "Basic [Token]"
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", authToken);

        // L'endpoint PUT /api/users attende l'utente nel body
        request.Content = JsonContent.Create(emmaUser);

        HttpResponseMessage response = await Client.SendAsync(request);

        if (response.IsSuccessStatusCode)
        {
            // UpdateUserAsync restituisce bool?, quindi il server serializza true/false/null
            return await response.Content.ReadFromJsonAsync<bool?>() ?? false;

        }
        else
        {
            var errore = await response.Content.ReadAsStringAsync();
            throw new Exception($"Errore durante l'invio: {response.StatusCode} {errore}");
        }
    }

    // ATTENZIONE: lato server non esiste un endpoint DELETE /api/users,
    // quindi questa chiamata fallisce. Non è esposta nella sezione admin.
    public async Task<int> DeleteAsync(EmmaUser emmaUser)
    {
        string urlApi = $"{_url}/api/users";

        using var request = new HttpRequestMessage(HttpMethod.Delete, urlApi);

        // Codifica "username:password" in Base64
        var authToken = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_user}:{_password}"));

        // Aggiungi l'header Authorization nel formato "Basic [Token]"
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", authToken);

        // L'endpoint POST /api/tenants attende il tenant nel body
        request.Content = JsonContent.Create(emmaUser);

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

}
