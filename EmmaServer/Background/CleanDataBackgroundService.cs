using Emma.Batches;

namespace EmmaServer.Background;

/// <summary>
/// Pulizia periodica dei documenti chiusi (sezione "CleanData", intervallo di default 60 minuti).
/// </summary>
public class CleanDataBackgroundService : BatchPeriodicoBackgroundService
{
    private readonly ICleanDocs _cleanDocs;

    public CleanDataBackgroundService(IConfiguration config, ICleanDocs cleanDocs,
        ILogger<CleanDataBackgroundService> logger)
        : base(config, logger, sezione: "CleanData", minutiDefault: 60)
    {
        _cleanDocs = cleanDocs;
    }

    protected override Task EseguiAsync(CancellationToken stoppingToken) => _cleanDocs.ExecuteAsync();
}
