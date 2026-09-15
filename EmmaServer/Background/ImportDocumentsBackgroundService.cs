using Emma.Batches;

namespace EmmaServer.Background;

/// <summary>
/// Import dei documenti dalla casella di posta (sezione "ImportBatchDoc", intervallo di default 10 minuti).
///
/// EmailReader (bolle) ed EmailReaderDoc (documenti RAG) leggono entrambi le mail NON LETTE della
/// Inbox e le segnano come lette. Sulla stessa casella si ruberebbero i messaggi a vicenda: una bolla
/// finirebbe nel database vettoriale, o un manuale verrebbe mandato a EMMA-AI come DDT.
/// Per questo, se i due import sono attivi sulla stessa casella, questo servizio non parte.
/// </summary>
public class ImportDocumentsBackgroundService : BatchPeriodicoBackgroundService
{
    private readonly IEmailReaderDoc _emailReaderDoc;
    private readonly IConfiguration _config;
    private readonly ILogger<ImportDocumentsBackgroundService> _logger;

    public ImportDocumentsBackgroundService(IConfiguration config, IEmailReaderDoc emailReaderDoc,
        ILogger<ImportDocumentsBackgroundService> logger)
        : base(config, logger, sezione: "ImportBatchDoc", minutiDefault: 10)
    {
        _emailReaderDoc = emailReaderDoc;
        _config = config;
        _logger = logger;
    }

    protected override Task EseguiAsync(CancellationToken stoppingToken)
    {
        if (StessaCasellaDelleBolle())
        {
            _logger.LogWarning(
                "Import documenti sospeso: ImportBatchDoc usa la stessa casella IMAP di ImportBatch ({Utente}). " +
                "Configurare una casella diversa oppure disattivare uno dei due import.",
                _config["ImportBatchDoc:ImapUser"]);
            return Task.CompletedTask;
        }

        return _emailReaderDoc.ExecuteAsync();
    }

    private bool StessaCasellaDelleBolle()
    {
        if (!bool.TryParse(_config["ImportBatch:Enabled"], out var bolleAttive) || !bolleAttive) return false;

        var utenteBolle = _config["ImportBatch:ImapUser"]?.Trim();
        var utenteDoc = _config["ImportBatchDoc:ImapUser"]?.Trim();
        if (string.IsNullOrEmpty(utenteBolle) || string.IsNullOrEmpty(utenteDoc)) return false;

        var serverBolle = _config["ImportBatch:ImapServer"]?.Trim() ?? string.Empty;
        var serverDoc = (_config["ImportBatchDoc:ImapServer"] ?? _config["ImportBatch:ImapServer"])?.Trim() ?? string.Empty;

        return string.Equals(utenteBolle, utenteDoc, StringComparison.OrdinalIgnoreCase)
            && string.Equals(serverBolle, serverDoc, StringComparison.OrdinalIgnoreCase);
    }
}
