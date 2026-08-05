using EmmaServer.Entities;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;


namespace EmmaClientAv.Services;

public interface IConciliazioneClientService
{
    Task<List<ConciliazioneResponse>> GetConciliazione(List<RigaConciliazione> bolle, List<RigaConciliazione> fatture);
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

    public async Task<List<ConciliazioneResponse>> GetConciliazione(
        List<RigaConciliazione> bolle,
        List<RigaConciliazione> fatture)
    {
        var results = new List<ConciliazioneResponse>();

        List<string?> fornitoriValidi = bolle.Select(b => b.Fornitore)
               .Union(fatture.Select(f => f.Fornitore))
               .Where(f => !string.IsNullOrEmpty(f))
               .Distinct()
               .ToList();

        foreach (var fornitore in fornitoriValidi)
        {
            if (string.IsNullOrWhiteSpace(fornitore)) continue;

            List<DettaglioDocumento> bolleInput = bolle
                .Where(b => b.Fornitore == fornitore)
                .Select(b => new DettaglioDocumento
                {
                    Id = b.IdRiga ?? string.Empty,
                    Codice = b.CodiceArticolo ?? string.Empty,
                    Qta = b.Qta
                }).ToList();

            List<DettaglioDocumento> fattureInput = fatture
                .Where(f => f.Fornitore == fornitore)
                .Select(f => new DettaglioDocumento
                {
                    Id = f.IdRiga ?? string.Empty,
                    Codice = f.CodiceArticolo ?? string.Empty,
                    Qta = f.Qta
                }).ToList();

            if (bolleInput.Count == 0 && fattureInput.Count == 0)
            {
                continue; // Skip this fornitore if there are no bolle or fatture
            }

            var inputConciliazione = new InputConciliazione
            {
                Fornitore = fornitore,
                Bolle = bolleInput,
                Fatture = fattureInput,
            };

            var urlApi = $"{_url}/api/v1/conciliazione";
            var request = new HttpRequestMessage(HttpMethod.Post, urlApi);
            var authToken = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_user}:{_password}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", authToken);
            request.Content = JsonContent.Create(inputConciliazione);

            HttpResponseMessage response = await Client.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                var fuzzyMatchResult = await response.Content.ReadFromJsonAsync<ConciliazioneResponse>();
                if (fuzzyMatchResult != null)
                    results.Add(fuzzyMatchResult);
            }
            else
            {
                string errorContent = await response.Content.ReadAsStringAsync();
                throw new ApplicationException(errorContent);
            }
        }

           return results;
    }
}