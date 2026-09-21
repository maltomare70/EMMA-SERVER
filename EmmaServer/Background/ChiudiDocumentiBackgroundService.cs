using Emma.Batches;
using EmmaClientAv.Services;
using EmmaServer.Services;
using EmmaServer.Services.Anomalie;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;

namespace EmmaServer.Background;


public class ChiudiDocumentiBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<ChiudiDocumentiBackgroundService> _logger;


    public ChiudiDocumentiBackgroundService(IServiceScopeFactory scopeFactory, IConfiguration config,
        ILogger<ChiudiDocumentiBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {

            try
            {
                using var scope = _scopeFactory.CreateScope();

                // In background non esiste HttpContext: forziamo un HttpContext con claim "tenant"
                var httpContextAccessor = scope.ServiceProvider.GetService<IHttpContextAccessor>();
                if (httpContextAccessor != null && httpContextAccessor.HttpContext == null)
                {
                    var ctx = new DefaultHttpContext();
                    var tenant = _config["Seed:Tenant"] ?? _config["Admin:Tenant"] ?? _config["DefaultTenant"] ?? "test";
                    ctx.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("tenant", tenant) }));
                    httpContextAccessor.HttpContext = ctx;
                }

                var servizio = scope.ServiceProvider.GetRequiredService<IDocServiceExtension>();
               //await servizio.ChiusuraAutomaticaDocumenti();


                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
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
}
