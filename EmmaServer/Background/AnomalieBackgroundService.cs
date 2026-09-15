using EmmaServer.Services;
using EmmaServer.Services.Anomalie;

namespace EmmaServer.Background;

/// <summary>
/// Ricalcolo notturno della baseline anomalie per tutti i tenant.
/// A differenza degli altri batch non passa dalle API HTTP: il servizio non dipende dall'HttpContext
/// e gira direttamente in uno scope DI dedicato.
/// </summary>
public class AnomalieBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<AnomalieBackgroundService> _logger;

    public AnomalieBackgroundService(IServiceScopeFactory scopeFactory, IConfiguration config,
        ILogger<AnomalieBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var opzioni = _config.GetSection("Anomalie").Get<OpzioniAnomalie>() ?? new OpzioniAnomalie();

            try
            {
                if (!opzioni.BaselineNotturna)
                {
                    // Disattivato: si ricontrolla la configurazione ogni ora, senza girare a vuoto.
                    await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
                    continue;
                }

                var attesa = AttesaFinoAllaProssimaEsecuzione(DateTime.UtcNow, opzioni.OraUtc);
                _logger.LogInformation("Prossimo ricalcolo baseline anomalie fra {Attesa}", attesa);
                await Task.Delay(attesa, stoppingToken);

                using var scope = _scopeFactory.CreateScope();
                var servizio = scope.ServiceProvider.GetRequiredService<IAnomalieService>();
                var esiti = await servizio.RicalcolaTutteLeBaselineAsync(stoppingToken);

                _logger.LogInformation("Baseline anomalie ricalcolata per {Tenant} tenant, {Chiavi} chiavi totali",
                    esiti.Count, esiti.Sum(e => e.ChiaviBaseline));
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Errore nel ricalcolo notturno della baseline anomalie");
                // Evita un ciclo stretto se l'errore si ripete subito (es. database giu').
                try { await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    public static TimeSpan AttesaFinoAllaProssimaEsecuzione(DateTime adessoUtc, int oraUtc)
    {
        var ora = Math.Clamp(oraUtc, 0, 23);
        var prossima = adessoUtc.Date.AddHours(ora);
        if (prossima <= adessoUtc) prossima = prossima.AddDays(1);
        return prossima - adessoUtc;
    }
}
