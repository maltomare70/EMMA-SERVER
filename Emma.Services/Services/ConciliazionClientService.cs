using EmmaServer.Entities;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;


namespace EmmaClientAv.Services;

public interface IConciliazioneClientService
{
    Task<PayloadRiconciliazione> GetConciliazione(List<RigaConciliazione> bolle, List<RigaConciliazione> fatture);
}
public class ConciliazionClientService : IConciliazioneClientService
{
    private readonly HttpClient Client;

    private readonly string _url;
    private readonly string _user;
    private readonly string _password;
    private readonly string _tenant;

    public ConciliazionClientService(string url, string user, string password, string tenant = "")
    {
        _url = url;
        _user = user;
        _password = password;
        _tenant = tenant;

        Client = new HttpClient();
    }

    public async Task<PayloadRiconciliazione> GetConciliazione(List<RigaConciliazione> bolle, List<RigaConciliazione> fatture)
    {
        var urlApi = $"{_url}/api/v1/conciliazione";
        var request = new HttpRequestMessage(HttpMethod.Post, urlApi);
        var authToken = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_user}:{_password}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", authToken);
        request.Content = JsonContent.Create(new PayloadRiconciliazione()
        {
            bolle = bolle,
            fatture = fatture
        });
        HttpResponseMessage response = await Client.SendAsync(request);
        if (response.IsSuccessStatusCode)
        { 
            var fuzzyMatchResults = await response.Content.ReadFromJsonAsync<PayloadRiconciliazione>();
            return fuzzyMatchResults ?? new PayloadRiconciliazione();
        }
        else
        {
            string errorContent = await response.Content.ReadAsStringAsync();
            throw new ApplicationException(errorContent);
        }
    }
}