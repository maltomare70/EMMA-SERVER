using Emma.Services.Http;
using EmmaServer.Entities;

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

public interface IArticoliServiceClient
{
    Task<List<EmmaArticoli>> GetArticoliFornitore(string descrizione);
    Task AddArticolo(EmmaArticoli articolo);
    Task UpdateArticolo(EmmaArticoli articolo);
    Task DeleteArticolo(EmmaArticoli articolo);
}

public class ArticoliServiceClient : ServiceClientBase, IArticoliServiceClient
{
    private const string Endpoint = "/api/articoli";

    public ArticoliServiceClient(string url, string user, string password)
        : base(url, user, password)
    {
    }

    public ArticoliServiceClient(HttpClient httpClient, string url, string user, string password)
        : base(httpClient, url, user, password)
    {
    }

    public async Task<List<EmmaArticoli>> GetArticoliFornitore(string descrizione)
    {
        var articoli = await GetAsync<List<EmmaArticoli>>($"{Endpoint}?fornitore={descrizione}").ConfigureAwait(false);
        return articoli?.OrderBy(x => x.descrizione).ToList() ?? new List<EmmaArticoli>();
    }

    public Task AddArticolo(EmmaArticoli articolo) => PostAsync(Endpoint, articolo);

    public Task UpdateArticolo(EmmaArticoli articolo) => PutAsync(Endpoint, articolo);

    public Task DeleteArticolo(EmmaArticoli articolo) => DeleteAsync(Endpoint, articolo);
}
