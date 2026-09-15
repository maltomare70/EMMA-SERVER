namespace EmmaServer.Background;

/// <summary>
/// Base comune dei batch periodici (import mail, import documenti, pulizia).
///
/// Corregge i difetti che avevano le tre copie precedenti:
/// <list type="bullet">
///   <item>con il batch disattivato il ciclo girava senza pausa e teneva occupato un core;</item>
///   <item>l'intervallo letto dalla configurazione finiva in una variabile locale
///         (<c>out int _minutes</c>) e il campo restava sempre al valore di default;</item>
///   <item>allo spegnimento l'annullamento del Task.Delay usciva come eccezione non gestita;</item>
///   <item>gli errori finivano solo su Console.</item>
/// </list>
/// Abilitazione e intervallo si rileggono a ogni giro: una modifica ad appsettings vale senza riavvio.
/// </summary>
public abstract class BatchPeriodicoBackgroundService : BackgroundService
{
    /// <summary>Ogni quanto si ricontrolla la configurazione quando il batch e' disattivato.</summary>
    private static readonly TimeSpan AttesaSeDisattivato = TimeSpan.FromMinutes(1);

    private readonly IConfiguration _config;
    private readonly ILogger _logger;
    private readonly string _sezione;
    private readonly int _minutiDefault;

    /// <param name="sezione">Sezione di appsettings con le chiavi <c>Enabled</c> e <c>Minutes</c>.</param>
    /// <param name="minutiDefault">Intervallo se <c>Minutes</c> manca o non e' valido.</param>
    /// <param name="abilitatoDefault">Stato se <c>Enabled</c> manca o non e' valido.</param>
    protected BatchPeriodicoBackgroundService(IConfiguration config, ILogger logger,
        string sezione, int minutiDefault, bool abilitatoDefault = false)
    {
        _config = config;
        _logger = logger;
        _sezione = sezione;
        _minutiDefault = minutiDefault;
        AbilitatoDefault = abilitatoDefault;
    }

    private bool AbilitatoDefault { get; }

    /// <summary>Il lavoro vero e proprio di un giro.</summary>
    protected abstract Task EseguiAsync(CancellationToken stoppingToken);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Cede subito il controllo: l'avvio dell'host non aspetta il primo giro.
        await Task.Yield();

        while (!stoppingToken.IsCancellationRequested)
        {
            TimeSpan attesa;

            if (!Abilitato())
            {
                attesa = AttesaSeDisattivato;
            }
            else
            {
                try
                {
                    await EseguiAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // Un giro fallito non deve fermare il servizio: si riprova al prossimo intervallo.
                    _logger.LogError(ex, "Batch {Batch}: errore durante l'esecuzione", GetType().Name);
                }

                attesa = TimeSpan.FromMinutes(Minuti());
            }

            try
            {
                await Task.Delay(attesa, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private bool Abilitato()
        => bool.TryParse(_config[$"{_sezione}:Enabled"], out var abilitato) ? abilitato : AbilitatoDefault;

    private int Minuti()
        => int.TryParse(_config[$"{_sezione}:Minutes"], out var minuti) && minuti > 0 ? minuti : _minutiDefault;
}
