using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;

namespace Emma.Services.Http;

/// <summary>
/// Fabbrica dell'eccezione da lanciare quando la risposta HTTP non ha esito positivo.
/// Riceve la response e il corpo gia' letto come stringa.
/// </summary>
public delegate Exception ApiErrorFactory(HttpResponseMessage response, string body);

/// <summary>
/// Classe base per tutti i client HTTP di Emma.
///
/// Centralizza cio' che prima era duplicato in ogni singolo metodo di ogni client:
/// composizione della URL, header <c>Authorization: Basic</c>, serializzazione JSON
/// del body, invio della richiesta, lettura della risposta e gestione dell'errore.
///
/// I metodi template <see cref="GetAsync{T}"/>, <see cref="PostAsync{T}"/>,
/// <see cref="PutAsync{T}"/> e <see cref="DeleteAsync{T}"/> lanciano un'eccezione
/// (per default <see cref="ApplicationException"/> con il corpo della risposta)
/// quando lo status non e' di successo; le varianti <c>Try*</c> non lanciano mai.
///
/// NOTA: l'header <c>x-tenant</c> viene inviato solo quando il singolo metodo lo
/// richiede esplicitamente tramite il parametro <c>tenant</c>. Il valore passato al
/// costruttore e' disponibile in <see cref="Tenant"/> ma non viene aggiunto in
/// automatico, per non alterare il comportamento degli endpoint esistenti.
/// </summary>
public abstract class ServiceClientBase
{
    /// <summary>
    /// Istanza condivisa usata quando il chiamante non ne fornisce una propria.
    /// Evita il socket exhaustion dovuto a un <c>new HttpClient()</c> per ogni client.
    /// </summary>
    private static readonly HttpClient SharedClient = new();

    protected HttpClient Client { get; }
    protected string BaseUrl { get; }
    protected string User { get; }
    protected string Password { get; }
    protected string Tenant { get; }

    protected ServiceClientBase(string url, string user, string password, string tenant = "")
        : this(null, url, user, password, tenant)
    {
    }

    /// <summary>
    /// Overload pensato per DI / IHttpClientFactory: se <paramref name="httpClient"/>
    /// e' null viene usata l'istanza condivisa.
    /// </summary>
    protected ServiceClientBase(HttpClient? httpClient, string url, string user, string password, string tenant = "")
    {
        Client = httpClient ?? SharedClient;
        BaseUrl = url ?? string.Empty;
        User = user;
        Password = password;
        Tenant = tenant ?? string.Empty;
    }

    // ------------------------------------------------------------------
    // Costruzione della richiesta
    // ------------------------------------------------------------------

    /// <summary>Concatena la base url con il path relativo (es. "/api/articoli").</summary>
    protected string BuildUrl(string path)
    {
        if (string.IsNullOrEmpty(path)) return BaseUrl;
        if (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return path;

        return $"{BaseUrl.TrimEnd('/')}/{path.TrimStart('/')}";
    }

    /// <summary>Header <c>Authorization: Basic base64(user:password)</c>.</summary>
    protected AuthenticationHeaderValue AuthHeader =>
        new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{User}:{Password}")));

    /// <summary>
    /// Crea una richiesta gia' autenticata. Utile quando serve un contenuto non JSON
    /// (es. multipart) o quando la richiesta va rieseguita da una retry policy.
    /// </summary>
    protected HttpRequestMessage CreateRequest(
        HttpMethod method,
        string path,
        HttpContent? content = null,
        string? tenant = null,
        bool authenticate = true)
    {
        var request = new HttpRequestMessage(method, BuildUrl(path));

        if (authenticate)
            request.Headers.Authorization = AuthHeader;

        if (!string.IsNullOrWhiteSpace(tenant))
            request.Headers.Add("x-tenant", tenant);

        if (content is not null)
            request.Content = content;

        return request;
    }

    /// <summary>
    /// Invia la richiesta serializzando <paramref name="body"/> in JSON (se presente).
    /// La response NON viene interpretata: e' compito del chiamante farlo/disporla.
    /// </summary>
    protected async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string path,
        object? body = null,
        string? tenant = null,
        bool authenticate = true,
        CancellationToken ct = default)
    {
        // inputType esplicito: senza di esso un body tipizzato come object
        // verrebbe serializzato come "{}".
        HttpContent? content = body is null ? null : JsonContent.Create(body, body.GetType());

        using var request = CreateRequest(method, path, content, tenant, authenticate);
        return await Client.SendAsync(request, ct).ConfigureAwait(false);
    }

    // ------------------------------------------------------------------
    // Template dei verbi: lanciano in caso di errore
    // ------------------------------------------------------------------

    protected Task<T?> GetAsync<T>(string path, string? tenant = null, ApiErrorFactory? error = null, CancellationToken ct = default)
        => SendForResultAsync<T>(HttpMethod.Get, path, null, tenant, error, ct);

    protected Task<T?> PostAsync<T>(string path, object? body = null, string? tenant = null, ApiErrorFactory? error = null, CancellationToken ct = default)
        => SendForResultAsync<T>(HttpMethod.Post, path, body, tenant, error, ct);

    protected Task<T?> PutAsync<T>(string path, object? body = null, string? tenant = null, ApiErrorFactory? error = null, CancellationToken ct = default)
        => SendForResultAsync<T>(HttpMethod.Put, path, body, tenant, error, ct);

    protected Task<T?> DeleteAsync<T>(string path, object? body = null, string? tenant = null, ApiErrorFactory? error = null, CancellationToken ct = default)
        => SendForResultAsync<T>(HttpMethod.Delete, path, body, tenant, error, ct);

    // Varianti senza corpo di risposta: interessa solo l'esito.

    protected Task PostAsync(string path, object? body = null, string? tenant = null, ApiErrorFactory? error = null, CancellationToken ct = default)
        => EnsureSuccessAsync(HttpMethod.Post, path, body, tenant, error, ct);

    protected Task PutAsync(string path, object? body = null, string? tenant = null, ApiErrorFactory? error = null, CancellationToken ct = default)
        => EnsureSuccessAsync(HttpMethod.Put, path, body, tenant, error, ct);

    protected Task DeleteAsync(string path, object? body = null, string? tenant = null, ApiErrorFactory? error = null, CancellationToken ct = default)
        => EnsureSuccessAsync(HttpMethod.Delete, path, body, tenant, error, ct);

    // ------------------------------------------------------------------
    // Varianti "silenziose": non lanciano su status di errore
    // ------------------------------------------------------------------

    /// <summary>GET che restituisce <c>default</c> invece di lanciare quando l'esito non e' positivo.</summary>
    protected async Task<T?> TryGetAsync<T>(string path, string? tenant = null, CancellationToken ct = default)
    {
        using HttpResponseMessage response = await SendAsync(HttpMethod.Get, path, null, tenant, true, ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode) return default;

        return await response.Content.ReadFromJsonAsync<T>(ct).ConfigureAwait(false);
    }

    /// <summary>Esegue la richiesta e restituisce solo <c>true</c>/<c>false</c> in base allo status.</summary>
    protected async Task<bool> TrySendAsync(
        HttpMethod method,
        string path,
        object? body = null,
        string? tenant = null,
        bool authenticate = true,
        CancellationToken ct = default)
    {
        using HttpResponseMessage response = await SendAsync(method, path, body, tenant, authenticate, ct).ConfigureAwait(false);
        return response.IsSuccessStatusCode;
    }

    // ------------------------------------------------------------------
    // Gestione errori
    // ------------------------------------------------------------------

    /// <summary>
    /// Eccezione lanciata per default quando lo status non e' di successo.
    /// I client che usano un formato diverso ne fanno l'override.
    /// </summary>
    protected virtual Exception CreateError(HttpResponseMessage response, string body)
        => new ApplicationException(body);

    /// <summary>Formato usato dai client di amministrazione: status + corpo della risposta.</summary>
    protected static readonly ApiErrorFactory InvioError =
        (response, body) => new Exception($"Errore durante l'invio: {response.StatusCode} {body}");

    /// <summary>
    /// Variante storica che interpola l'oggetto <c>HttpContent</c> invece del suo corpo
    /// (produce il nome del tipo, non il testo dell'errore). Mantenuta per non alterare
    /// i messaggi gia' in produzione: da preferire <see cref="InvioError"/> nel codice nuovo.
    /// </summary>
    protected static readonly ApiErrorFactory InvioErrorLegacy =
        (response, _) => new Exception($"Errore durante l'invio: {response.StatusCode} {response.Content}");

    /// <summary>Legge il corpo della response e costruisce l'eccezione da lanciare.</summary>
    protected async Task<Exception> BuildErrorAsync(HttpResponseMessage response, ApiErrorFactory? error, CancellationToken ct = default)
    {
        string body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return error is not null ? error(response, body) : CreateError(response, body);
    }

    // ------------------------------------------------------------------
    // Implementazione condivisa
    // ------------------------------------------------------------------

    private async Task<T?> SendForResultAsync<T>(
        HttpMethod method,
        string path,
        object? body,
        string? tenant,
        ApiErrorFactory? error,
        CancellationToken ct)
    {
        using HttpResponseMessage response = await SendAsync(method, path, body, tenant, true, ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            throw await BuildErrorAsync(response, error, ct).ConfigureAwait(false);

        return await response.Content.ReadFromJsonAsync<T>(ct).ConfigureAwait(false);
    }

    private async Task EnsureSuccessAsync(
        HttpMethod method,
        string path,
        object? body,
        string? tenant,
        ApiErrorFactory? error,
        CancellationToken ct)
    {
        using HttpResponseMessage response = await SendAsync(method, path, body, tenant, true, ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            throw await BuildErrorAsync(response, error, ct).ConfigureAwait(false);
    }
}
