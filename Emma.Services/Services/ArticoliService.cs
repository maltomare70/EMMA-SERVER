using EmmaServer.Entities;
using System.Net.Http.Headers;
using System.Text;
using System.Net.Http.Json;


namespace EmmaClientAv.Services;

public static class ArticoliServiceManager
{
    public static string[] GetTipodocs()
    {
        return new string[] { 
            "0. Tutti", 
            "1. Ordine",
            "2. DDT", 
            "3. Fattura Accompagnatoria",
            "4. Fattura",
            "5. Nota di Accredito"
        };
    }

    public static string[] GetStatodocs()
    {
        return new string[] { 
            "0. Aperto", 
            "1. Chiuso"
        };
    }
}

public interface IArticoliService
{
    Task<List<EmmaArticoli>> GetArticoliFornitore(string descrizione);
    Task AddArticolo(EmmaArticoli articolo);
    Task UpdateArticolo(EmmaArticoli articolo);
    Task DeleteArticolo(EmmaArticoli articolo);
}
public class ArticoliService : IArticoliService
{
    private readonly HttpClient Client;

    private readonly string _url;
    private readonly string _user;
    private readonly string _password;
    
    public ArticoliService(string url, string user, string password)
    {
        _url = url;
        _user = user;
        _password = password;
            
        Client = new HttpClient();
    }
    

    public async  Task<List<EmmaArticoli>> GetArticoliFornitore(string descrizione)
    {
        string urlApi = $"{_url}/api/articoli?fornitore={descrizione}";
        using var request = new HttpRequestMessage(HttpMethod.Get, urlApi);
        var authToken = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_user}:{_password}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", authToken);
        HttpResponseMessage response = await Client.SendAsync(request);
        if (response.IsSuccessStatusCode)
        {
            var emmaArticoliList = await response.Content.ReadFromJsonAsync<List<EmmaArticoli>>().ConfigureAwait(false);
            if (emmaArticoliList != null)
            {
                emmaArticoliList =  emmaArticoliList.OrderBy(x => x.descrizione).ToList();
            }

            return emmaArticoliList;
        }
        else
        {
            string errorContent = await response.Content.ReadAsStringAsync();
            throw new ApplicationException(errorContent); 
        }
    }
    
    public async Task AddArticolo(EmmaArticoli articolo)
    {
        string urlApi = $"{_url}/api/articoli";
        using var request = new HttpRequestMessage(HttpMethod.Post, urlApi);
        var authToken = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_user}:{_password}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", authToken);
        request.Content = JsonContent.Create(articolo);
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
    
    public async Task UpdateArticolo(EmmaArticoli articolo)
    {
        string urlApi = $"{_url}/api/articoli";
        using var request = new HttpRequestMessage(HttpMethod.Put, urlApi);
        var authToken = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_user}:{_password}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", authToken);
        request.Content = JsonContent.Create(articolo);
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
    
    public async Task DeleteArticolo(EmmaArticoli articolo)
    {
        string urlApi = $"{_url}/api/articoli";
        using var request = new HttpRequestMessage(HttpMethod.Delete, urlApi);
        var authToken = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_user}:{_password}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", authToken);
        request.Content = JsonContent.Create(articolo);
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