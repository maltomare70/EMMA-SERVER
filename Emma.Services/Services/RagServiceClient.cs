using Emma.Services.Http;
using EmmaServer.Entities;
using EmmaServer.Entities.Dtos;
using Polly;
using Polly.Retry;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;

namespace Emma.Services.Services;

public interface IRagServiceClient
{
    /// <summary>Carica un PDF su /api/v1/document: viene spezzato, vettorizzato e indicizzato.</summary>
    Task<DocumentResponse?> IndicizzaPdfAsync(Stream fileStream, string fileName, CancellationToken ct = default);

    /// <summary>Come sopra, partendo da un file su disco.</summary>
    Task<DocumentResponse?> IndicizzaPdfAsync(string percorsoFile, CancellationToken ct = default);

    /// <summary>Ricerca semantica sui documenti indicizzati del tenant.</summary>
    Task<List<RagSearchResult>> CercaAsync(RagSearchRequest richiesta, CancellationToken ct = default);

    /// <summary>Ricerca semantica con i parametri piu' comuni.</summary>
    Task<List<RagSearchResult>> CercaAsync(string query, int topK = 5, int? docId = null, double? minScore = null, CancellationToken ct = default);

    /// <summary>
    /// Ricerca e restituzione dei passaggi gia' concatenati, con i riferimenti a
    /// documento e pagina: il testo e' pronto da mettere nel prompt di un LLM.
    /// Stringa vuota se non c'e' nessun risultato sopra la soglia.
    /// </summary>
    Task<string> CercaContestoAsync(string query, int topK = 5, double? minScore = null, CancellationToken ct = default);

    /// <summary>Elenco dei documenti indicizzati del tenant (senza il PDF).</summary>
    Task<List<RagDocument>> GetDocumentiAsync(CancellationToken ct = default);

    /// <summary>Diagnostica: i chunk salvati per un documento, con testo e salute del vettore.</summary>
    Task<List<RagChunkInfo>> GetChunksAsync(int idDoc, int skip = 0, int take = 10, CancellationToken ct = default);

    /// <summary>Scarica il PDF originale di un documento indicizzato. Null se non c'e'.</summary>
    Task<byte[]?> ScaricaPdfAsync(int idDoc, CancellationToken ct = default);

    /// <summary>Cancella un documento e, in cascata, i suoi chunk. False se non esiste.</summary>
    Task<bool> EliminaDocumentoAsync(int idDoc, CancellationToken ct = default);
}

/// <summary>
/// Client del database vettoriale (endpoint /api/v1/document di EmmaServer).
///
/// Il tenant non viaggia nel body: il server lo legge dalle claim dell'utente
/// autenticato in Basic, esattamente come per gli altri endpoint.
/// </summary>
public class RagServiceClient : ServiceClientBase, IRagServiceClient
{
    private const string EndpointDocument = "/api/v1/document";
    private const string EndpointSearch = "/api/v1/document/search";

    public RagServiceClient(string url, string user, string password, string tenant = "")
        : base(url, user, password, tenant)
    {
    }

    public RagServiceClient(HttpClient httpClient, string url, string user, string password, string tenant = "")
        : base(httpClient, url, user, password, tenant)
    {
    }

    // ------------------------------------------------------------------
    // Indicizzazione
    // ------------------------------------------------------------------

    public async Task<DocumentResponse?> IndicizzaPdfAsync(string percorsoFile, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(percorsoFile))
            throw new ArgumentException("Percorso del file non valorizzato.", nameof(percorsoFile));

        if (!File.Exists(percorsoFile))
            throw new FileNotFoundException("File non trovato.", percorsoFile);

        await using var stream = File.OpenRead(percorsoFile);
        return await IndicizzaPdfAsync(stream, Path.GetFileName(percorsoFile), ct).ConfigureAwait(false);
    }

    public async Task<DocumentResponse?> IndicizzaPdfAsync(Stream fileStream, string fileName, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(fileStream);

        // Il contenuto multipart non e' riusabile: bufferizzo il file una volta sola
        // cosi' ogni tentativo ricostruisce la richiesta da zero.
        if (fileStream.CanSeek) fileStream.Position = 0;
        using var buffer = new MemoryStream();
        await fileStream.CopyToAsync(buffer, ct).ConfigureAwait(false);
        byte[] fileBytes = buffer.ToArray();

        using HttpResponseMessage response = await UploadRetryPipeline.ExecuteAsync(async token =>
        {
            using var content = new MultipartFormDataContent();
            var fileContent = new ByteArrayContent(fileBytes);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            content.Add(fileContent, "file", fileName);

            using var request = CreateRequest(HttpMethod.Post, EndpointDocument, content, Tenant);

            return await Client.SendAsync(request, token).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);

        if (response.IsSuccessStatusCode)
        {
            return await response.Content
                .ReadFromJsonAsync<DocumentResponse>(cancellationToken: ct)
                .ConfigureAwait(false);
        }

        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        throw new HttpRequestException(
            $"Errore durante l'indicizzazione di {fileName}: {(int)response.StatusCode} {response.ReasonPhrase} - {body}");
    }

    // ------------------------------------------------------------------
    // Ricerca
    // ------------------------------------------------------------------

    public async Task<List<RagSearchResult>> CercaAsync(RagSearchRequest richiesta, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(richiesta);

        if (string.IsNullOrWhiteSpace(richiesta.Query))
            return new List<RagSearchResult>();

        var risultati = await PostAsync<List<RagSearchResult>>(
            EndpointSearch,
            richiesta,
            error: (response, body) => new HttpRequestException(
                $"Errore durante la ricerca: {(int)response.StatusCode} {response.ReasonPhrase} - {body}"),
            ct: ct).ConfigureAwait(false);

        return risultati ?? new List<RagSearchResult>();
    }

    public Task<List<RagSearchResult>> CercaAsync(string query, int topK = 5, int? docId = null, double? minScore = null, CancellationToken ct = default)
        => CercaAsync(new RagSearchRequest
        {
            Query = query,
            TopK = topK,
            DocId = docId,
            MinScore = minScore
        }, ct);

    public async Task<string> CercaContestoAsync(string query, int topK = 5, double? minScore = null, CancellationToken ct = default)
    {
        var risultati = await CercaAsync(query, topK, null, minScore, ct).ConfigureAwait(false);
        if (risultati.Count == 0) return string.Empty;

        var sb = new StringBuilder();

        for (int i = 0; i < risultati.Count; i++)
        {
            var r = risultati[i];

            // I riferimenti servono per far citare la fonte al modello
            sb.Append('[').Append(i + 1).Append("] ")
              .Append(r.FileName)
              .Append(", pag. ").Append(r.Page)
              .Append(" (rilevanza ").Append(r.Score.ToString("0.00")).AppendLine(")");

            sb.AppendLine(r.Content);

            if (i < risultati.Count - 1) sb.AppendLine("---");
        }

        return sb.ToString();
    }

    // ------------------------------------------------------------------
    // Gestione dell'archivio
    // ------------------------------------------------------------------

    public async Task<List<RagDocument>> GetDocumentiAsync(CancellationToken ct = default)
        => await GetAsync<List<RagDocument>>(EndpointDocument, ct: ct).ConfigureAwait(false)
           ?? new List<RagDocument>();

    public async Task<List<RagChunkInfo>> GetChunksAsync(int idDoc, int skip = 0, int take = 10, CancellationToken ct = default)
        => await GetAsync<List<RagChunkInfo>>(
               $"{EndpointDocument}/{idDoc}/chunks?skip={skip}&take={take}", ct: ct).ConfigureAwait(false)
           ?? new List<RagChunkInfo>();

    public async Task<byte[]?> ScaricaPdfAsync(int idDoc, CancellationToken ct = default)
    {
        using HttpResponseMessage response = await SendAsync(
            HttpMethod.Get, $"{EndpointDocument}/{idDoc}/file", ct: ct).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound) return null;

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new HttpRequestException(
                $"Errore durante il download del documento {idDoc}: {(int)response.StatusCode} {response.ReasonPhrase} - {body}");
        }

        return await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// La DELETE risponde senza corpo, quindi interessa solo lo status:
    /// TrySendAsync evita di provare a deserializzare una risposta vuota.
    /// </summary>
    public Task<bool> EliminaDocumentoAsync(int idDoc, CancellationToken ct = default)
        => TrySendAsync(HttpMethod.Delete, $"{EndpointDocument}/{idDoc}", ct: ct);

    // ------------------------------------------------------------------
    // Resilienza
    // ------------------------------------------------------------------

    // Pipeline condivisa (thread-safe, va creata una sola volta).
    // Si ritenta solo su errori transitori, mai su un 400 dovuto al file:
    // un PDF senza testo non migliora ritentando.
    //
    // A differenza di DocServiceClient qui NON si ritenta su TaskCanceledException:
    // l'indicizzazione e' sincrona e puo' durare a lungo, quindi un timeout del client
    // non vuol dire che il server abbia smesso di lavorare. Ritentare vorrebbe dire
    // far ripartire da zero un'indicizzazione ancora in corso. Se i timeout capitano,
    // la soluzione e' passare al costruttore un HttpClient con Timeout piu' alto,
    // non ritentare.
    private static readonly ResiliencePipeline<HttpResponseMessage> UploadRetryPipeline =
        new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddRetry(new RetryStrategyOptions<HttpResponseMessage>
            {
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                    .Handle<HttpRequestException>()
                    .HandleResult(r => (int)r.StatusCode >= 500
                                    || r.StatusCode == HttpStatusCode.RequestTimeout
                                    || r.StatusCode == HttpStatusCode.TooManyRequests),
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromSeconds(2),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                OnRetry = args =>
                {
                    args.Outcome.Result?.Dispose(); // libera la response scartata
                    return default;
                }
            })
            .Build();
}
