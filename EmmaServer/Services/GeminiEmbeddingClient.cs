using EmmaServer.Entities.Dtos;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text.Json.Serialization;

namespace EmmaServer.Services;

/// <summary>Risultato di una vettorizzazione: i vettori piu' il modello che li ha prodotti.</summary>
public record EmbeddingBatch(List<float[]> Vettori, string Model, int Dimensione, Costs? Costs);

public interface IEmbeddingClient
{
    /// <summary>
    /// Vettorizza i chunk di un documento (taskType RETRIEVAL_DOCUMENT).
    /// L'ordine dei vettori segue l'ordine dei testi.
    /// </summary>
    Task<EmbeddingBatch> EmbedDocumentsAsync(IReadOnlyList<string> testi, CancellationToken ct = default);

    /// <summary>Vettorizza una domanda di ricerca (taskType RETRIEVAL_QUERY).</summary>
    Task<float[]> EmbedQueryAsync(string testo, CancellationToken ct = default);
}

/// <summary>
/// Chiamata diretta alle API di embedding di Google Gemini, senza passare da EMMA-AI:
/// un hop di rete in meno e nessun servizio intermedio da tenere in piedi.
///
///   POST https://generativelanguage.googleapis.com/v1beta/models/{model}:batchEmbedContents
///   header: x-goog-api-key
///
/// Due dettagli che contano per la qualita' della ricerca:
///  - taskType diverso fra documenti (RETRIEVAL_DOCUMENT) e domande (RETRIEVAL_QUERY):
///    Gemini produce vettori asimmetrici pensati proprio per il retrieval;
///  - gemini-embedding-001 restituisce vettori a 3072 dimensioni troncati via MRL quando
///    si chiede una dimensione minore, e i vettori troncati NON sono normalizzati:
///    la normalizzazione L2 va fatta qui (vedi Normalizza).
///
/// Sui limiti di frequenza: in batchEmbedContents Google conta OGNI TESTO come una
/// richiesta, non una per chiamata. Sul piano gratuito il limite e' 100 richieste al
/// minuto, quindi un solo batch da 100 chunk esaurisce la quota del minuto e il
/// successivo prende 429. Per questo il client ha un limitatore condiviso che conta i
/// testi su una finestra scorrevole di 60 secondi, e sul 429 aspetta esattamente il
/// tempo che Google indica nella risposta invece di un backoff a caso.
/// </summary>
public class GeminiEmbeddingClient : IEmbeddingClient
{
    private const string BASE_URL = "https://generativelanguage.googleapis.com/v1beta/models";
    private const string MODELLO_DEFAULT = "gemini-embedding-001";

    private const string TASK_DOCUMENT = "RETRIEVAL_DOCUMENT";
    private const string TASK_QUERY = "RETRIEVAL_QUERY";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Limitatore condiviso da tutte le istanze del client: la quota e' del progetto
    /// Google, non della singola richiesta HTTP di EmmaServer.
    /// </summary>
    private static readonly LimitatoreFrequenza Limitatore = new();

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<GeminiEmbeddingClient> _logger;

    public GeminiEmbeddingClient(IHttpClientFactory httpClientFactory, IConfiguration configuration, ILogger<GeminiEmbeddingClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    private string Modello => _configuration["Gemini:Model"] ?? MODELLO_DEFAULT;
    private int Dimensione => _configuration.GetValue<int?>("Rag:EmbeddingDim") ?? 768;
    /// <summary>Testi per chiamata. Mai piu' grande del limite al minuto, altrimenti un solo batch lo sfonda.</summary>
    private int BatchSize => Math.Clamp(_configuration.GetValue<int?>("Rag:BatchSize") ?? 25, 1, RichiestePerMinuto);

    /// <summary>Limite di richieste al minuto: 100 sul piano gratuito. 0 disattiva il limitatore.</summary>
    private int RichiestePerMinuto => Math.Max(_configuration.GetValue<int?>("Gemini:RequestsPerMinute") ?? 100, 1);

    /// <summary>Quante volte accettare un 429 aspettando il tempo indicato da Google.</summary>
    private int MaxRetry429 => _configuration.GetValue<int?>("Gemini:MaxRateLimitRetries") ?? 3;

    /// <summary>Oltre questa attesa non ha senso aspettare: e' quota giornaliera, non del minuto.</summary>
    private int MaxAttesaSecondi => _configuration.GetValue<int?>("Gemini:MaxRetryWaitSeconds") ?? 120;

    /// <summary>
    /// Forma della richiesta.
    ///
    /// Il default e' false: taskType e outputDimensionality vengono mandati al PRIMO
    /// LIVELLO della richiesta. Sono marcati come deprecati nella documentazione, ma
    /// sono gli unici che batchEmbedContents onora davvero: dentro embedContentConfig
    /// vengono ignorati in silenzio, e il risultato e' che Gemini restituisce i vettori
    /// nativi a 3072 dimensioni e ignora il taskType, senza alcun errore.
    ///
    /// Mettere true solo quando Google avra' rimosso i campi deprecati.
    /// </summary>
    private bool UsaEmbedContentConfig => _configuration.GetValue<bool?>("Gemini:UseEmbedContentConfig") ?? false;

    private string ApiKey
    {
        get
        {
            var key = _configuration["Gemini:ApiKey"];
            if (string.IsNullOrWhiteSpace(key))
                throw new ApplicationException(
                    "Chiave API di Gemini non configurata: valorizzare Gemini:ApiKey " +
                    "(o la variabile d'ambiente Gemini__ApiKey).");
            return key;
        }
    }

    public async Task<float[]> EmbedQueryAsync(string testo, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(testo))
            throw new ApplicationException("Testo da vettorizzare vuoto.");

        var batch = await EmbedAsync(new[] { testo }, TASK_QUERY, ct);
        if (batch.Vettori.Count == 0)
            throw new ApplicationException("Gemini non ha restituito alcun vettore.");

        return batch.Vettori[0];
    }

    public Task<EmbeddingBatch> EmbedDocumentsAsync(IReadOnlyList<string> testi, CancellationToken ct = default)
        => EmbedAsync(testi, TASK_DOCUMENT, ct);

    private async Task<EmbeddingBatch> EmbedAsync(IReadOnlyList<string> testi, string taskType, CancellationToken ct)
    {
        var vettori = new List<float[]>(testi.Count);
        int tokenTotali = 0;

        // Un PDF lungo produce centinaia di chunk: si va a blocchi, sequenziali,
        // per non superare i limiti di frequenza dell'API.
        for (int offset = 0; offset < testi.Count; offset += BatchSize)
        {
            ct.ThrowIfCancellationRequested();

            var blocco = testi.Skip(offset).Take(BatchSize).ToList();
            var risposta = await ChiamaBatchEmbedAsync(blocco, taskType, ct);

            if (risposta.Embeddings.Count != blocco.Count)
                throw new ApplicationException(
                    $"Gemini ha restituito {risposta.Embeddings.Count} vettori per {blocco.Count} testi.");

            foreach (var embedding in risposta.Embeddings)
            {
                var valori = embedding.Values ?? Array.Empty<float>();

                if (valori.Length != Dimensione)
                    throw new ApplicationException(
                        $"Dimensione dell'embedding non compatibile: attesa {Dimensione}, ricevuta {valori.Length}. " +
                        (valori.Length == 3072
                            ? "Gemini ha ignorato outputDimensionality e ha restituito il vettore nativo: " +
                              "verificare che Gemini:UseEmbedContentConfig sia false, cosi' taskType e " +
                              "outputDimensionality viaggiano al primo livello della richiesta."
                            : "Allineare Rag:EmbeddingDim e la colonna rag_chunks.embedding (vedi sql/rag.sql)."));

                vettori.Add(Normalizza(valori));
            }

            tokenTotali += risposta.UsageMetadata?.PromptTokenCount ?? 0;
        }

        var costi = new Costs
        {
            PromptTokens = tokenTotali,
            TotalTokens = tokenTotali
        };

        return new EmbeddingBatch(vettori, Modello, Dimensione, costi);
    }

    private async Task<GeminiBatchEmbedResponse> ChiamaBatchEmbedAsync(List<string> testi, string taskType, CancellationToken ct)
    {
        string modello = Modello;
        string url = $"{BASE_URL}/{modello}:batchEmbedContents";

        var richieste = testi.Select(t => new GeminiEmbedRequest
        {
            Model = $"models/{modello}",
            Content = new GeminiContent { Parts = new List<GeminiPart> { new() { Text = t } } },
            Config = UsaEmbedContentConfig ? new GeminiEmbedConfig { TaskType = taskType, OutputDimensionality = Dimensione } : null,
            TaskType = UsaEmbedContentConfig ? null : taskType,
            OutputDimensionality = UsaEmbedContentConfig ? null : Dimensione
        }).ToList();

        var payload = JsonSerializer.Serialize(new GeminiBatchEmbedRequest { Requests = richieste }, JsonOptions);

        int tentativi = 0;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            // Il limitatore conta i TESTI, non le chiamate: e' cosi' che Google conteggia la quota.
            await Limitatore.PrenotaAsync(testi.Count, RichiestePerMinuto, ct);

            // Il client "GeminiService" ritenta gli errori di rete e i 5xx.
            // I 429 li gestiamo qui, perche' solo leggendo il corpo si sa quanto aspettare.
            var client = _httpClientFactory.CreateClient("GeminiService");

            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
            // La chiave in header e non in querystring: non finisce nei log di accesso
            request.Headers.Add("x-goog-api-key", ApiKey);

            var response = await client.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                if (QuotaGiornalieraEsaurita(body))
                    throw new ApplicationException(
                        "Quota giornaliera di Gemini esaurita per gli embedding. " +
                        "L'indicizzazione riprendera' domani, oppure attivare la fatturazione sul progetto Google. " +
                        $"Risposta: {body}");

                var attesa = LeggiRetryDelay(body) ?? TimeSpan.FromSeconds(30);

                if (tentativi >= MaxRetry429 || attesa > TimeSpan.FromSeconds(MaxAttesaSecondi))
                    throw new ApplicationException(
                        $"Limite di frequenza di Gemini superato: Google chiede di riprovare fra {attesa.TotalSeconds:0}s " +
                        $"dopo {tentativi} tentativi. Abbassare Rag:BatchSize o Gemini:RequestsPerMinute, " +
                        $"oppure attivare la fatturazione. Risposta: {body}");

                tentativi++;
                _logger.LogWarning(
                    "Gemini 429 sul batch di {Testi} testi: attendo {Secondi:0}s (tentativo {Tentativo}/{Max}).",
                    testi.Count, attesa.TotalSeconds, tentativi, MaxRetry429);

                await Task.Delay(attesa + TimeSpan.FromSeconds(1), ct);

                // Trascorsa l'attesa richiesta da Google la finestra e' ripartita:
                // svuotando il limitatore si evita di aspettare due volte la stessa quota.
                await Limitatore.AzzeraAsync(ct);
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Embedding Gemini fallito: {Status} - {Body}", response.StatusCode, body);
                throw new ApplicationException($"Gemini {modello}:batchEmbedContents ha risposto {(int)response.StatusCode}: {body}");
            }

            var risultato = JsonSerializer.Deserialize<GeminiBatchEmbedResponse>(body, JsonOptions);
            if (risultato is null)
                throw new ApplicationException("Risposta di Gemini non deserializzabile.");

            return risultato;
        }
    }

    // Google indica quanto aspettare in due punti: nei details come RetryInfo
    // ("retryDelay": "46s") e in chiaro nel messaggio ("Please retry in 46.3727s").
    private static readonly Regex RegexRetryDelay =
        new(@"""retryDelay""\s*:\s*""([0-9.]+)s""", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex RegexRetryMessaggio =
        new(@"retry in\s+([0-9.]+)\s*s", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static TimeSpan? LeggiRetryDelay(string body)
    {
        if (string.IsNullOrEmpty(body)) return null;

        foreach (var regex in new[] { RegexRetryDelay, RegexRetryMessaggio })
        {
            var m = regex.Match(body);
            if (m.Success &&
                double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var secondi))
            {
                return TimeSpan.FromSeconds(secondi);
            }
        }

        return null;
    }

    /// <summary>
    /// Distingue la quota al minuto (si aspetta e si riparte) da quella giornaliera
    /// (aspettare non serve: la finestra si riapre il giorno dopo).
    /// </summary>
    private static bool QuotaGiornalieraEsaurita(string body)
        => body.Contains("per_day", StringComparison.OrdinalIgnoreCase)
        || body.Contains("PerDay", StringComparison.OrdinalIgnoreCase)
        || body.Contains("requests_per_day", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Normalizzazione L2. Serve perche' i vettori di gemini-embedding-001 troncati a una
    /// dimensione inferiore a 3072 perdono la norma unitaria. Con la distanza coseno di
    /// pgvector il risultato non cambierebbe, ma vettori unitari rendono confrontabili gli
    /// score e permettono in futuro di passare all'operatore di prodotto interno.
    /// </summary>
    private static float[] Normalizza(float[] vettore)
    {
        double sommaQuadrati = 0;
        for (int i = 0; i < vettore.Length; i++)
            sommaQuadrati += (double)vettore[i] * vettore[i];

        double norma = Math.Sqrt(sommaQuadrati);
        if (norma <= 0) return vettore;

        var risultato = new float[vettore.Length];
        for (int i = 0; i < vettore.Length; i++)
            risultato[i] = (float)(vettore[i] / norma);

        return risultato;
    }
}

// =====================================================================
//  Schema di trasporto delle API Gemini. Resta qui: e' un dettaglio del
//  client, non fa parte del dominio di EmmaServer.
// =====================================================================

internal class GeminiBatchEmbedRequest
{
    [JsonPropertyName("requests")]
    public List<GeminiEmbedRequest> Requests { get; set; } = new();
}

internal class GeminiEmbedRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public GeminiContent Content { get; set; } = new();

    [JsonPropertyName("embedContentConfig")]
    public GeminiEmbedConfig? Config { get; set; }

    // Campi deprecati, usati solo se Gemini:LegacyRequestFields = true
    [JsonPropertyName("taskType")]
    public string? TaskType { get; set; }

    [JsonPropertyName("outputDimensionality")]
    public int? OutputDimensionality { get; set; }
}

internal class GeminiEmbedConfig
{
    [JsonPropertyName("taskType")]
    public string TaskType { get; set; } = string.Empty;

    [JsonPropertyName("outputDimensionality")]
    public int OutputDimensionality { get; set; }
}

internal class GeminiContent
{
    [JsonPropertyName("parts")]
    public List<GeminiPart> Parts { get; set; } = new();
}

internal class GeminiPart
{
    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;
}

internal class GeminiBatchEmbedResponse
{
    [JsonPropertyName("embeddings")]
    public List<GeminiEmbedding> Embeddings { get; set; } = new();

    [JsonPropertyName("usageMetadata")]
    public GeminiUsageMetadata? UsageMetadata { get; set; }
}

internal class GeminiEmbedding
{
    [JsonPropertyName("values")]
    public float[]? Values { get; set; }
}

internal class GeminiUsageMetadata
{
    [JsonPropertyName("promptTokenCount")]
    public int PromptTokenCount { get; set; }

    [JsonPropertyName("totalTokenCount")]
    public int TotalTokenCount { get; set; }
}

/// <summary>
/// Finestra scorrevole di 60 secondi sul numero di richieste consumate.
///
/// Serializza i chiamanti di proposito: e' un regolatore di ritmo, non un contatore.
/// Chi arriva quando la finestra e' piena aspetta che il piu' vecchio degli istanti
/// registrati esca dai 60 secondi, poi prenota la sua quota e prosegue.
/// </summary>
internal sealed class LimitatoreFrequenza
{
    private static readonly TimeSpan Finestra = TimeSpan.FromSeconds(60);

    private readonly SemaphoreSlim _accesso = new(1, 1);
    private readonly Queue<DateTime> _istanti = new();

    /// <summary>Attende quanto serve, poi registra <paramref name="richieste"/> richieste come consumate.</summary>
    public async Task PrenotaAsync(int richieste, int limitePerMinuto, CancellationToken ct)
    {
        if (richieste <= 0 || limitePerMinuto <= 0) return;

        await _accesso.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();

                var adesso = DateTime.UtcNow;
                Scarta(adesso);

                // C'e' posto nella finestra: si prosegue.
                if (_istanti.Count + richieste <= limitePerMinuto) break;

                // Blocco piu' grande dell'intero limite: aspettare non servirebbe a nulla,
                // si lascia passare e sara' il 429 a dire quanto attendere.
                if (_istanti.Count == 0) break;

                var attesa = Finestra - (adesso - _istanti.Peek());
                if (attesa <= TimeSpan.Zero) continue;

                await Task.Delay(attesa + TimeSpan.FromMilliseconds(250), ct).ConfigureAwait(false);
            }

            var istante = DateTime.UtcNow;
            for (int i = 0; i < richieste; i++) _istanti.Enqueue(istante);
        }
        finally
        {
            _accesso.Release();
        }
    }

    /// <summary>
    /// Svuota la finestra. Da chiamare dopo aver atteso il tempo che Google stesso ha
    /// indicato in un 429: a quel punto la quota e' ripartita e continuare a contare
    /// gli istanti vecchi farebbe aspettare due volte.
    /// </summary>
    public async Task AzzeraAsync(CancellationToken ct = default)
    {
        await _accesso.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _istanti.Clear();
        }
        finally
        {
            _accesso.Release();
        }
    }

    private void Scarta(DateTime adesso)
    {
        while (_istanti.Count > 0 && adesso - _istanti.Peek() >= Finestra)
            _istanti.Dequeue();
    }
}
