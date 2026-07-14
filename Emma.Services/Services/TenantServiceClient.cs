using EmmaServer.Entities;
using System;
using System.Collections.Generic;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;

namespace Emma.Services.Services;

public interface ITenantServiceClient
{
    Task<List<EmmaTenant>> GetsAsync();
}
public class TenantServiceClient: ITenantServiceClient
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
            return await response.Content.ReadFromJsonAsync<List<EmmaTenant>>();

        }
        else
        {
            throw new Exception($"Errore durante l'invio: {response.StatusCode} {response.Content}");
        }
    }
}
