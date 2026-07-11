using EmmaServer.Entities;
using System.Net.Http.Headers;
using System.Text;
using System.Net.Http.Json;


namespace EmmaClientAv.Services;

public interface IFornitoriService
{
    Task<List<EmmaFornitori>> GetFornitoriAsync();
   
    Task AddFornitore(EmmaFornitori fornitore);
    Task UpdateFornitore(EmmaFornitori fornitore);
    Task DeleteFornitore(EmmaFornitori fornitore);
}

public class FornitoriService : IFornitoriService
{
    private readonly HttpClient Client;
    private readonly string _url;
    private readonly string _user;
    private readonly string _password;
    public FornitoriService(string url, string user, string password)
    {
        _url = url;
        _user = user;
        _password = password;

        Client = new HttpClient();
    }
    public async Task<List<EmmaFornitori>> GetFornitoriAsync()
    {
        string urlApi = $"{_url}/api/fornitori";
        using var request = new HttpRequestMessage(HttpMethod.Get, urlApi);
        var authToken = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_user}:{_password}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", authToken);
        HttpResponseMessage response = await Client.SendAsync(request);
        if (response.IsSuccessStatusCode)
        {
            var emmaFornitoriList = await response.Content.ReadFromJsonAsync<List<EmmaFornitori>>().ConfigureAwait(false);
            return emmaFornitoriList?.ToList() ?? new List<EmmaFornitori>();
        }
        else
        {
            return new List<EmmaFornitori>();
        }
    }
    

    
    public async Task AddFornitore(EmmaFornitori fornitore)
    {
        string urlApi = $"{_url}/api/fornitori";
        using var request = new HttpRequestMessage(HttpMethod.Post, urlApi);
        var authToken = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_user}:{_password}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", authToken);
        request.Content = JsonContent.Create(fornitore);
        HttpResponseMessage response = await Client.SendAsync(request);
        if (response.IsSuccessStatusCode)
        {
            //
        }
        else
        {
            string errorContent = await response.Content.ReadAsStringAsync();
            throw new ApplicationException(errorContent); 
        }
    }
    
    public async Task UpdateFornitore(EmmaFornitori fornitore)
    {
        string urlApi = $"{_url}/api/fornitori";
        using var request = new HttpRequestMessage(HttpMethod.Put, urlApi);
        var authToken = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_user}:{_password}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", authToken);
        request.Content = JsonContent.Create(fornitore);
        HttpResponseMessage response = await Client.SendAsync(request);
        if (response.IsSuccessStatusCode)
        {
            //
        }
        else
        {
            string errorContent = await response.Content.ReadAsStringAsync();
            throw new ApplicationException(errorContent); 
        }
    }
    
    public async Task DeleteFornitore(EmmaFornitori fornitore)
    {
        string urlApi = $"{_url}/api/fornitori";
        using var request = new HttpRequestMessage(HttpMethod.Delete, urlApi);
        var authToken = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_user}:{_password}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", authToken);
        request.Content = JsonContent.Create(fornitore);
        HttpResponseMessage response = await Client.SendAsync(request);
        if (response.IsSuccessStatusCode)
        {
            //
        }
        else
        {
            string errorContent = await response.Content.ReadAsStringAsync();
            throw new ApplicationException(errorContent); 
        }
    }
}