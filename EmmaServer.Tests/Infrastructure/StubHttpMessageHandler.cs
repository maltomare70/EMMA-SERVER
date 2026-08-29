using System.Net;

namespace EmmaServer.Tests.Infrastructure;

/// <summary>
/// Handler HTTP finto registrato come primary handler del client di default della fixture.
/// Serve a testare <c>DocService.ImportDocAsync</c> senza chiamare davvero il servizio EMMA-AI:
/// il test imposta <see cref="Rispondi"/> e decide cosa deve tornare indietro.
/// </summary>
public sealed class StubHttpMessageHandler : HttpMessageHandler
{
    /// <summary>Risposta di default: se un test non configura nulla, la chiamata fallisce in modo esplicito.</summary>
    public Func<HttpRequestMessage, HttpResponseMessage> Rispondi { get; set; } =
        _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            Content = new StringContent("StubHttpMessageHandler.Rispondi non configurato dal test")
        };

    /// <summary>URL dell'ultima richiesta ricevuta.</summary>
    public string? UltimoUrl { get; private set; }

    /// <summary>Header dell'ultima richiesta (copiati: la HttpRequestMessage viene poi rilasciata).</summary>
    public Dictionary<string, string> UltimiHeader { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Numero di chiamate ricevute.</summary>
    public int NumeroChiamate { get; private set; }

    public void Reset()
    {
        UltimoUrl = null;
        UltimiHeader.Clear();
        NumeroChiamate = 0;
        Rispondi = _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            Content = new StringContent("StubHttpMessageHandler.Rispondi non configurato dal test")
        };
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        NumeroChiamate++;
        UltimoUrl = request.RequestUri?.ToString();

        UltimiHeader.Clear();
        foreach (var header in request.Headers)
        {
            UltimiHeader[header.Key] = string.Join(",", header.Value);
        }

        return Task.FromResult(Rispondi(request));
    }
}
