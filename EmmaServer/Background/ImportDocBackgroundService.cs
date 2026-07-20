using Emma.Batches;

namespace EmmaServer.Background;

public class ImportDocBackgroundService : BackgroundService
{
    private readonly IEmailReader _emailReader;
    private readonly IConfiguration _config;
    private readonly int _minutes = 10;
    public ImportDocBackgroundService(IConfiguration config, IEmailReader emailReader)
    {
        _config = config;
        _emailReader  = emailReader;
        var minutes = _config["ImportBatch:Minutes"] ?? "10";
        int.TryParse(minutes, out int _minutes);

    }

    private async Task<bool> IsReadyToRun()
    {
        var enabled = _config["ImportBatch:Enabled"].ToString();
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
                    await _emailReader.ExecuteAsync();
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
