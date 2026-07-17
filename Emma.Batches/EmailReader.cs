
using Emma.Services.Services;
using EmmaClientAv.Services;
using EmmaServer.Entities;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MimeKit;


namespace Emma.Batches;

public class EmailReaderOptions
{
    public string? ServerUrl { get; set; }
    public string? ImapServerUrl { get; set; }
    public int ImapServerPort { get; set; }
    public string? ImapUser { get; set; }
    public string? ImapPassword { get; set; }
    public string? AdminPassword { get; set; } 
}



public interface IEmailReader
{
    Task ExecuteAsync();
}

public class EmailReader : IEmailReader
{
    private readonly EmailReaderOptions? _emailReaderOptions;
    private readonly List<EmmaTenant>? _tenants;
    public EmailReader (EmailReaderOptions emailReaderOptions )
    {
        _emailReaderOptions = emailReaderOptions;
    }



    public async Task ExecuteAsync()
    {
        using (var client = new ImapClient())
        {
            try
            {
                string? emma_url = _emailReaderOptions?.ServerUrl;

                TenantServiceClient tenantServiceClient = new TenantServiceClient(emma_url, "admin", _emailReaderOptions.AdminPassword);
                var tenants = await tenantServiceClient.GetsAsync();
                

                // Configurazione dei dati di accesso
                string imapServer = _emailReaderOptions?.ImapServerUrl;
                int port = _emailReaderOptions.ImapServerPort; // Porta standard per IMAP su SSL
                string email = _emailReaderOptions.ImapUser;
                string password = _emailReaderOptions.ImapPassword; // NON la password normale

                client.Connect(imapServer, port, true);
                client.Authenticate(email, password);

                // 3. Apertura della cartella principale (In arrivo / Inbox)
                // Usiamo FolderAccess.ReadOnly se dobbiamo solo leggere, evita blocchi
                var inbox = client.Inbox;
                inbox.Open(FolderAccess.ReadWrite);

                var uids = inbox.Search(SearchQuery.NotSeen);

                // Console.WriteLine($"Totale messaggi in Inbox: {inbox.Count}");

                // 4. Cerchiamo gli ultimi 10 messaggi (o usiamo un filtro di ricerca)
                // In questo esempio prendiamo gli ul                                                                                                                                                                                                                                                                                                                                                                                           timi 10 partendo dalla fine
                //int startIndex = Math.Max(0, inbox.Count - 10);

                //for (int i = inbox.Count - 1; i >= startIndex; i--)
                foreach (var uid in uids)
                {
                    var message = inbox.GetMessage(uid);
                    if (await ProcessMessage(message, emma_url, tenants))
                    {
                        inbox.AddFlags(uid, MessageFlags.Seen, true);
                    }
                }

                client.Disconnect(true);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Errore durante la lettura della posta: {ex.Message}");
            }
        }
    }

    private async Task<bool> ProcessMessage(MimeMessage message, string emma_url, List<EmmaTenant> tenants)
    {        
        Console.WriteLine($"Data: {message.Date.UtcDateTime}");
        Console.WriteLine($"Da: {message.From}");
        Console.WriteLine($"Oggetto: {message.Subject}");

       
        //queste info le otteniamo dal message.From che devono essere collegate
        //in maniera univoca al tenant
        EmmaTenant? tenant = null;
        string mittenteEmail = string.Empty;
        if (message.From.Count > 0 && message.From[0] is MailboxAddress mailboxAddress)
        {
            // .Address restituisce solo "esempio@dominio.com"
            mittenteEmail = mailboxAddress.Address;
            tenant = tenants.FirstOrDefault(x => x.mail_from.Equals(mittenteEmail, StringComparison.InvariantCultureIgnoreCase));
        }

        if (tenant is null) return false;
        

        // Se vuoi leggere il testo del corpo del messaggio:
        // Console.WriteLine($"Testo: {message.TextBody}");
        IDocService docService = new DocService(emma_url, "admin", _emailReaderOptions.AdminPassword, tenant.codice);

        foreach (var attachment in message.Attachments)
        {
            if (attachment is MimePart mimePart)
            {
                string fileName = mimePart.FileName;

                using (var memoryStream = new MemoryStream())
                {
                    mimePart.Content.DecodeTo(memoryStream);
                    memoryStream.Position = 0;

                    Console.WriteLine($"Allegato '{mimePart.FileName}' caricato in memoria come Stream. Dimensione: {memoryStream.Length} byte");

                    DatiBolla? datiBolla = await docService.InviaFileAsync(memoryStream, fileName);
                    
                }
            }

        }

        return true;
    }
}
