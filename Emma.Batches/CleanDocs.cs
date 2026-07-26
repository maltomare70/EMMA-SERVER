
using EmmaClientAv.Services;
using EmmaServer.Entities;

namespace Emma.Batches;

public interface ICleanDocs
{
    public Task ExecuteAsync();
}
public class CleanDocs : ICleanDocs
{
    private readonly EmailReaderOptions? _emailReaderOptions;

    public CleanDocs(EmailReaderOptions emailReaderOptions)
    {
        _emailReaderOptions = emailReaderOptions;
    }

    public async Task ExecuteAsync()
    {
  
            try
            {
                //Inizio Elabprazione
                string? emma_url = _emailReaderOptions?.ServerUrl;

                if (!string.IsNullOrWhiteSpace(emma_url))
                {
                    DocService docServiceClient = new DocService(emma_url, EmmaAdmin.ADMIN, _emailReaderOptions?.AdminPassword!);
                    await docServiceClient.CleanDocs();
                }

            }
            catch (Exception ex)
            {
                Console.WriteLine($"{ex.Message}");
            }
 
        
    }

}
