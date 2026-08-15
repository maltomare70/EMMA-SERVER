using Emma.Services.Http;
using EmmaServer.Entities;

namespace EmmaClientAv.Services;

public interface IFornitoriServiceClient
{
    Task<List<EmmaFornitori>> GetFornitoriAsync();

    Task AddFornitore(EmmaFornitori fornitore);
    Task UpdateFornitore(EmmaFornitori fornitore);
    Task DeleteFornitore(EmmaFornitori fornitore);
}

public class FornitoriServiceClient : ServiceClientBase, IFornitoriServiceClient
{
    private const string Endpoint = "/api/fornitori";

    public FornitoriServiceClient(string url, string user, string password)
        : base(url, user, password)
    {
    }

    public FornitoriServiceClient(HttpClient httpClient, string url, string user, string password)
        : base(httpClient, url, user, password)
    {
    }

    /// <summary>In caso di errore restituisce una lista vuota, non lancia.</summary>
    public async Task<List<EmmaFornitori>> GetFornitoriAsync()
        => await TryGetAsync<List<EmmaFornitori>>(Endpoint).ConfigureAwait(false) ?? new List<EmmaFornitori>();

    public Task AddFornitore(EmmaFornitori fornitore) => PostAsync(Endpoint, fornitore);

    public Task UpdateFornitore(EmmaFornitori fornitore) => PutAsync(Endpoint, fornitore);

    public Task DeleteFornitore(EmmaFornitori fornitore) => DeleteAsync(Endpoint, fornitore);
}
