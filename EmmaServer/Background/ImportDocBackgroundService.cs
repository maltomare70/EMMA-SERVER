using Emma.Batches;

namespace EmmaServer.Background;

/// <summary>
/// Import dei DDT dalla casella di posta (sezione "ImportBatch", intervallo di default 10 minuti).
/// </summary>
public class ImportDocBackgroundService : BatchPeriodicoBackgroundService
{
    private readonly IEmailReader _emailReader;

    public ImportDocBackgroundService(IConfiguration config, IEmailReader emailReader,
        ILogger<ImportDocBackgroundService> logger)
        : base(config, logger, sezione: "ImportBatch", minutiDefault: 10)
    {
        _emailReader = emailReader;
    }

    protected override Task EseguiAsync(CancellationToken stoppingToken) => _emailReader.ExecuteAsync();
}
