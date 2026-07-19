using EmmaServer.Entities;
using System;
using System.Collections.Generic;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;

namespace Emma.Services.Services;

public interface IUserService
{
    Task<int> CambiaPasswordAsync(CambiaPasswordRequest cambiaPasswordRequest);
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

}
