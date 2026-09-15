using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Emma.Services.Http;
using EmmaServer.Entities;
using EmmaServer.Entities.Dtos;

namespace Emma.Services.Services;

public interface IAnomalieServiceClient
{
    /// <summary>GET /api/v1/anomalie: lista paginata con i filtri indicati.</summary>
    Task<PaginaAnomalie> GetAnomalieAsync(FiltriAnomalie filtri, CancellationToken ct = default);

    /// <summary>POST /api/v1/anomalie/{id}/feedback. Null se l'avviso non esiste piu'.</summary>
    Task<EmmaAnomalia?> InviaFeedbackAsync(long id, short stato, string? note = null, CancellationToken ct = default);

    /// <summary>GET /api/v1/anomalie/statistiche.</summary>
    Task<StatisticheAnomalie> GetStatisticheAsync(DateTime? da = null, DateTime? a = null, short? livello = null,
        CancellationToken ct = default);

    /// <summary>GET /api/v1/anomalie/doc/{docId}: avvisi di un documento (badge del dettaglio).</summary>
    Task<List<EmmaAnomalia>> GetAnomalieDocumentoAsync(int docId, CancellationToken ct = default);

    /// <summary>POST /api/v1/anomalie/doc/{docId}/analizza: rilancia i controlli sul documento.</summary>
    Task<List<EmmaAnomalia>> AnalizzaDocumentoAsync(int docId, CancellationToken ct = default);
}

/// <summary>
/// Client degli endpoint /api/v1/anomalie di EmmaServer.
/// Il tenant non viaggia nella richiesta: il server lo legge dalle claim dell'utente autenticato.
/// </summary>
public class AnomalieServiceClient : ServiceClientBase, IAnomalieServiceClient
{
    private const string Endpoint = "/api/v1/anomalie";

    public AnomalieServiceClient(string url, string user, string password)
        : base(url, user, password)
    {
    }

    public AnomalieServiceClient(HttpClient httpClient, string url, string user, string password)
        : base(httpClient, url, user, password)
    {
    }

    public async Task<PaginaAnomalie> GetAnomalieAsync(FiltriAnomalie filtri, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(filtri);

        var query = new Query()
            .Add("stato", filtri.Stato)
            .Add("severita", filtri.Severita)
            .Add("fornitore", filtri.Fornitore)
            .Add("da", filtri.Da)
            .Add("a", filtri.A)
            .Add("livello", filtri.Livello)
            .Add("tipo", filtri.Tipo)
            .Add("docId", filtri.DocId)
            .Add("limit", filtri.Limit)
            .Add("offset", filtri.Offset);

        return await GetAsync<PaginaAnomalie>(Endpoint + query, ct: ct).ConfigureAwait(false)
               ?? new PaginaAnomalie();
    }

    public async Task<EmmaAnomalia?> InviaFeedbackAsync(long id, short stato, string? note = null, CancellationToken ct = default)
    {
        using var response = await SendAsync(HttpMethod.Post, $"{Endpoint}/{id}/feedback",
            new FeedbackAnomalia { Stato = stato, Note = note }, ct: ct).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        if (!response.IsSuccessStatusCode) throw await BuildErrorAsync(response, null, ct).ConfigureAwait(false);

        return await response.Content.ReadFromJsonAsync<EmmaAnomalia>(ct).ConfigureAwait(false);
    }

    public async Task<StatisticheAnomalie> GetStatisticheAsync(DateTime? da = null, DateTime? a = null, short? livello = null,
        CancellationToken ct = default)
    {
        var query = new Query().Add("da", da).Add("a", a).Add("livello", livello);
        return await GetAsync<StatisticheAnomalie>($"{Endpoint}/statistiche{query}", ct: ct).ConfigureAwait(false)
               ?? new StatisticheAnomalie();
    }

    public async Task<List<EmmaAnomalia>> GetAnomalieDocumentoAsync(int docId, CancellationToken ct = default)
        => await GetAsync<List<EmmaAnomalia>>($"{Endpoint}/doc/{docId}", ct: ct).ConfigureAwait(false) ?? [];

    public async Task<List<EmmaAnomalia>> AnalizzaDocumentoAsync(int docId, CancellationToken ct = default)
        => await PostAsync<List<EmmaAnomalia>>($"{Endpoint}/doc/{docId}/analizza", ct: ct).ConfigureAwait(false) ?? [];

    /// <summary>
    /// Il server risponde agli errori di validazione con Results.BadRequest("testo"), cioe' una
    /// stringa JSON fra virgolette: la si scompatta per mostrare un messaggio leggibile.
    /// </summary>
    protected override Exception CreateError(HttpResponseMessage response, string body)
    {
        var testo = body;
        if (body.Length > 1 && body[0] == '"')
        {
            try { testo = JsonSerializer.Deserialize<string>(body) ?? body; }
            catch (JsonException) { /* corpo non JSON: si mostra cosi' com'e' */ }
        }

        return new ApplicationException(string.IsNullOrWhiteSpace(testo)
            ? $"Errore {(int)response.StatusCode} dal server"
            : testo);
    }

    /// <summary>Query string con i soli parametri valorizzati, formattati in modo indipendente dalla cultura.</summary>
    private sealed class Query
    {
        private readonly StringBuilder _sb = new();

        public Query Add(string nome, object? valore)
        {
            var testo = valore switch
            {
                null => null,
                string s => string.IsNullOrWhiteSpace(s) ? null : s.Trim(),
                DateTime d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
                _ => valore.ToString()
            };
            if (testo is null) return this;

            _sb.Append(_sb.Length == 0 ? '?' : '&')
               .Append(Uri.EscapeDataString(nome))
               .Append('=')
               .Append(Uri.EscapeDataString(testo));
            return this;
        }

        public override string ToString() => _sb.ToString();
    }
}
