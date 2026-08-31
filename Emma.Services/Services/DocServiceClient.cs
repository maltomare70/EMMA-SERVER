using Emma.Services.Http;
using EmmaServer.Entities;
using EmmaServer.Entities.Dtos;
using Polly;
using Polly.Retry;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace EmmaClientAv.Services;

public interface IDocServiceClient
{
    Task<List<EmmaDoc>> GetDocsAsync(EmmaDocFilters docFilters);
    Task CambioStato(MasterDocumento masterDocumento);
    Task CambioTipo(MasterDocumento masterDocumento);
    Task CancellaDocumento(MasterDocumento masterDocumento);
    Task<bool> InviaAddAllApi(RigheDocumento riga);
    Task InviaModificaAllApi(ArticoloBolla articoloBolla);
    Task<bool> InviaEliminazioneAllApi(RigheDocumento riga);
    Task<bool> PingAsync();
    Task<DatiBolla?> InviaFileAsync(Stream fileStream, string fileName, CancellationToken ct = default);
    Task CleanDocs();
}

public class DocServiceClient : ServiceClientBase, IDocServiceClient
{
    private const string EndpointDoc = "/api/v1/doc";
    private const string EndpointRiga = "/api/v1/doc/riga";
    private const string EndpointStato = "/api/v1/doc/stato";
    private const string EndpointTipo = "/api/v1/doc/tipo";
    private const string EndpointClean = "/api/v1/doc/clean";
    private const string EndpointHealth = "/api/health";

    public DocServiceClient(string url, string user, string password, string tenant = "")
        : base(url, user, password, tenant)
    {
    }

    public DocServiceClient(HttpClient httpClient, string url, string user, string password, string tenant = "")
        : base(httpClient, url, user, password, tenant)
    {
    }

    // Serve per forzare l'avvio del server nella versione free
    // poi questa chiamata va eliminata che non serve.
    // NB: e' l'unica chiamata senza autenticazione.
    public Task<bool> PingAsync()
        => TrySendAsync(HttpMethod.Get, EndpointHealth, authenticate: false);

    public async Task<List<EmmaDoc>> GetDocsAsync(EmmaDocFilters docFilters)
        => await PostAsync<List<EmmaDoc>>(EndpointDoc, docFilters).ConfigureAwait(false) ?? new List<EmmaDoc>();

    public Task CambioStato(MasterDocumento masterDocumento)
    {
        ArgumentNullException.ThrowIfNull(masterDocumento);

        var payload = new CambioStato
        {
            Id = masterDocumento.Id ?? string.Empty,
            Stato = string.Equals(masterDocumento.StatoDocumento, "Aperto", StringComparison.OrdinalIgnoreCase) ? 1 : 0
        };

        return PostAsync(EndpointStato, payload, error: (response, body) => new HttpRequestException(
            $"Errore durante il cambio stato del documento {masterDocumento.Id}. " +
            $"Status: {response.StatusCode}. Dettagli: {body}"));
    }

    public Task CambioTipo(MasterDocumento masterDocumento)
    {
        ArgumentNullException.ThrowIfNull(masterDocumento);

        var payload = new CambioTipo
        {
            Id = masterDocumento.Id ?? string.Empty,
            Tipo = int.Parse(masterDocumento.TipDocumento ?? string.Empty)
        };

        return PostAsync(EndpointTipo, payload, error: (response, body) => new HttpRequestException(
            $"Errore durante il cambio tipo del documento {masterDocumento.Id}. " +
            $"Status: {response.StatusCode}. Dettagli: {body}"));
    }

    public Task CancellaDocumento(MasterDocumento masterDocumento)
    {
        ArgumentNullException.ThrowIfNull(masterDocumento);

        EmmaDocFilters emmaDocFilters = new()
        {
            Fornitore = masterDocumento.Fornitore ?? string.Empty,
            NumeroDoc = masterDocumento.NumeroDocumento ?? string.Empty,
            DataDoc = masterDocumento.DataDocumento ?? string.Empty,
            TipoDoc = GetTipoDocumento(masterDocumento.TipDocumento ?? string.Empty),
            Stato = masterDocumento.StatoDocumento == "Aperto" ? 0 : 1
        };

        return DeleteAsync(EndpointDoc, emmaDocFilters);
    }

    /// <summary>Aggiunge una riga; restituisce false se il server risponde con errore.</summary>
    public Task<bool> InviaAddAllApi(RigheDocumento riga)
    {
        ArgumentNullException.ThrowIfNull(riga);

        // La riga nuova riceve un id generato dal client
        var articoloBolla = ToArticoloBolla(riga, Guid.NewGuid().ToString());

        return TrySendAsync(HttpMethod.Post, EndpointRiga, articoloBolla);
    }

    public Task InviaModificaAllApi(ArticoloBolla articoloBolla)
    {
        ArgumentNullException.ThrowIfNull(articoloBolla);

        return PutAsync(EndpointRiga, articoloBolla);
    }

    /// <summary>Elimina una riga; restituisce false su qualunque errore, incluse le eccezioni di rete.</summary>
    public async Task<bool> InviaEliminazioneAllApi(RigheDocumento riga)
    {
        try
        {
            var articoloBolla = ToArticoloBolla(riga, riga.IdRiga);
            return await TrySendAsync(HttpMethod.Delete, EndpointRiga, articoloBolla).ConfigureAwait(false);
        }
        catch
        {
            return false;
        }
    }

    public async Task<DatiBolla?> InviaFileAsync(Stream fileStream, string fileName, CancellationToken ct = default)
    {
        // Il contenuto multipart non è riusabile: bufferizzo il file una volta sola
        // così ogni tentativo ricostruisce la richiesta da zero.
        if (fileStream.CanSeek) fileStream.Position = 0;
        using var buffer = new MemoryStream();
        await fileStream.CopyToAsync(buffer, ct).ConfigureAwait(false);
        byte[] fileBytes = buffer.ToArray();

        using HttpResponseMessage response = await UploadRetryPipeline.ExecuteAsync(async token =>
        {
            using var content = new MultipartFormDataContent();
            var fileContent = new ByteArrayContent(fileBytes);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            content.Add(fileContent, "file", fileName);

            using var request = CreateRequest(HttpMethod.Post, EndpointDoc, content, Tenant);

            return await Client.SendAsync(request, token).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);

        if (response.IsSuccessStatusCode)
        {
            var ddt = await response.Content
                .ReadFromJsonAsync<DocResponse>(cancellationToken: ct)
                .ConfigureAwait(false);
            return ddt?.DdtResponse?.Document;
        }

        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        throw new HttpRequestException($"Errore durante l'invio: {(int)response.StatusCode} {response.ReasonPhrase} - {body}");
    }

    /// <summary>Best effort: l'esito della pulizia viene ignorato.</summary>
    public async Task CleanDocs()
        => await TrySendAsync(HttpMethod.Delete, EndpointClean).ConfigureAwait(false);

    private static int GetTipoDocumento(string tipodoc) => int.Parse(tipodoc);

    private static ArticoloBolla ToArticoloBolla(RigheDocumento riga, string? idRiga) => new()
    {
        Id_Master = riga.IdMaster ?? string.Empty,
        Id_Riga = idRiga ?? string.Empty,
        Quantita = riga.Qta,
        Descrizione = riga.DescrizioneArticolo ?? string.Empty,
        Codice = riga.CodiceArticolo ?? string.Empty,
        Imponibile = riga.Imponibile,
        Totale = riga.Totale,
        UnitaMisura = riga.UnitaMisura ?? string.Empty,
        Iva = riga.IVA ?? string.Empty
    };

    // Pipeline condivisa (thread-safe, va creata una sola volta)
    private static readonly ResiliencePipeline<HttpResponseMessage> UploadRetryPipeline =
        new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddRetry(new RetryStrategyOptions<HttpResponseMessage>
            {
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                    .Handle<HttpRequestException>()
                    .Handle<TaskCanceledException>()
                    .HandleResult(r => (int)r.StatusCode >= 500
                                    || r.StatusCode == HttpStatusCode.RequestTimeout
                                    || r.StatusCode == HttpStatusCode.TooManyRequests),
                MaxRetryAttempts = 3,               // 1 chiamata iniziale + 3 retry
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
