using Emma.Batches;
using EmmaServer.Services;

namespace EmmaServer.Background;


public class CleanDataBackgroundService : BackgroundService
{
    private readonly ICleanDocs _docService;
    private readonly IConfiguration _config;
    private readonly int _minutes = 10;
    public CleanDataBackgroundService(IConfiguration config, ICleanDocs docService)
    {
        _config = config;
        _docService = docService;
        _minutes = 60;

    }

    private async Task<bool> IsReadyToRun()
    {
        var enabled = _config["ImportBatch:Enabled"]?.ToString();
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
                    await _docService.ExecuteAsync();
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
