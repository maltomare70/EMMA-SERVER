using Emma.Batches;

namespace EmmaServer.Background;

public class ImportDocumentsBackgroundService : BackgroundService
{
    private readonly IEmailReaderDoc _emailReaderDoc;
    private readonly IConfiguration _config;
    private readonly int _minutes = 10;
    public ImportDocumentsBackgroundService(IConfiguration config, IEmailReaderDoc emailReaderDoc)
    {
        _config = config;
        _emailReaderDoc = emailReaderDoc;
        var minutes = _config["ImportBatchDoc:Minutes"] ?? "10";
        int.TryParse(minutes, out int _minutes);

    }

    private async Task<bool> IsReadyToRun()
    {
        var enabled = _config["ImportBatchDOc:Enabled"]?.ToString();
        Boolean.TryParse(enabled, out bool bEnabled);

        return bEnabled;
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (await IsReadyToRun())
            {
                try
                {
                    await _emailReaderDoc.ExecuteAsync();
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }
                finally
                {
                    await Task.Delay(TimeSpan.FromMinutes(_minutes), stoppingToken);
                }
            }
        }
    }

}
