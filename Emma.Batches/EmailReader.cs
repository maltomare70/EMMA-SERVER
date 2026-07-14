
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

public class InfoTenant
{
    public string? Name { get; set; }
    public string? User { get; set; }
    public string? Password { get; set; }
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
                    await ProcessMessage(message, emma_url);
                    inbox.AddFlags(uid, MessageFlags.Seen, true);
                }

                client.Disconnect(true);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Errore durante la lettura della posta: {ex.Message}");
            }
        }
    }

    private async Task ProcessMessage(MimeMessage message, string emma_url)
    {        
        Console.WriteLine($"Data: {message.Date.UtcDateTime}");
        Console.WriteLine($"Da: {message.From}");
        Console.WriteLine($"Oggetto: {message.Subject}");

        //queste info le otteniamo dal message.From che devono essere collegate
        //in maniera univoca al tenant
        InfoTenant tenantInfo = new();
        string mittenteEmail = string.Empty;
        if (message.From.Count > 0 && message.From[0] is MailboxAddress mailboxAddress)
        {
            // .Address restituisce solo "esempio@dominio.com"
            mittenteEmail = mailboxAddress.Address;
            tenantInfo = await GetInfoTenant(mittenteEmail);
        }

        if (tenantInfo is null) return;

        string emma_user = tenantInfo.User;
        string emma_pwd = tenantInfo.Password;

        // Se vuoi leggere il testo del corpo del messaggio:
        // Console.WriteLine($"Testo: {message.TextBody}");
        IDocService docService = new DocService(emma_url, emma_user, emma_pwd);

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

        
    }

    private async Task<InfoTenant> GetInfoTenant(string emailFrom)
    {
        //TODO
        return new InfoTenant()
        {
            Name = "002",
            User = "marco.altomare.1970@gmail.com",
            Password = "nocafla"
        };
    }
}
