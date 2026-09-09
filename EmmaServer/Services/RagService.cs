using EmmaServer.Entities;
using EmmaServer.Entities.Dtos;
using EmmaServer.Helpers;
using EmmaServer.Repositories;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace EmmaServer.Services;

public interface IRagService
{
    /// <summary>Acquisisce un PDF, lo spezza in chunk, li vettorizza e li salva sul database vettoriale.</summary>
    Task<DocumentResponse> IndicizzaPdfAsync(IFormFile file, string tenant, CancellationToken ct = default);

    /// <summary>Ricerca semantica sui chunk del tenant.</summary>
    Task<List<RagSearchResult>> CercaAsync(RagSearchRequest richiesta, string tenant, CancellationToken ct = default);

    Task<List<RagDocument>> GetDocumentiAsync(string tenant);

    /// <summary>PDF originale di un documento indicizzato, null se non esiste o non ha allegato.</summary>
    Task<RagDocument?> GetAllegatoAsync(int idDoc, string tenant);

    /// <summary>Diagnostica: i chunk salvati per un documento, con la salute dei vettori.</summary>
    Task<List<RagChunkInfo>> GetChunksAsync(int idDoc, string tenant, int skip, int take);

    Task<bool> EliminaDocumentoAsync(int idDoc, string tenant);
}

/// <summary>
/// Pipeline di indicizzazione:
///   PDF -> testo per pagina (PdfPig)
///       -> chunk strutturali con il percorso della sezione in testa (Microsoft.ML.Tokenizers)
///       -> embedding (API Gemini, chiamata diretta)
///       -> rag_documents / rag_chunks su PostgreSQL con pgvector
/// </summary>
public class RagService : IRagService
{
    /// <summary>Valore di EmmaLog.modulo usato per le righe di log del database vettoriale.</summary>
    private const int MODULO_RAG = 2;

    private readonly IRagRepository _repo;
    private readonly ITokenChunker _chunker;
    private readonly IEmbeddingClient _embeddingClient;
    private readonly ILogService _logService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<RagService> _logger;

    public RagService(
        IRagRepository repo,
        ITokenChunker chunker,
        IEmbeddingClient embeddingClient,
        ILogService logService,
        IConfiguration configuration,
        ILogger<RagService> logger)
    {
        _repo = repo;
        _chunker = chunker;
        _embeddingClient = embeddingClient;
        _logService = logService;
        _configuration = configuration;
        _logger = logger;
    }

    private int ChunkSize => _configuration.GetValue<int?>("Rag:ChunkSize") ?? 512;
    private int ChunkOverlap => _configuration.GetValue<int?>("Rag:ChunkOverlap") ?? 64;
    private int EmbeddingDim => _configuration.GetValue<int?>("Rag:EmbeddingDim") ?? 768;
    private int TopKDefault => _configuration.GetValue<int?>("Rag:TopK") ?? 5;

    /// <summary>
    /// Ricerca ibrida: il ramo lessicale (full-text italiano) fuso con quello vettoriale.
    /// Richiede la colonna tsv creata da sql/rag_fulltext.sql: si mette a false se quella
    /// migrazione non e' ancora stata eseguita sul database.
    /// </summary>
    private bool RicercaIbrida => _configuration.GetValue<bool?>("Rag:HybridSearch") ?? true;

    // =================================================================
    //  Indicizzazione
    // =================================================================

    public async Task<DocumentResponse> IndicizzaPdfAsync(IFormFile file, string tenant, CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();

        var bytes = await FileHelper.ConvertFormFileToByteArray(file);
        if (bytes.Length == 0)
            throw new ApplicationException("File vuoto.");

        string hash = CalcolaHash(bytes);

        // 1. Stesso file gia' indicizzato? Non lo rifacciamo (l'embedding costa).
        var esistente = await _repo.GetByHashAsync(tenant, hash);
        if (esistente is not null && esistente.stato == RagDocument.STATO_OK)
        {
            stopwatch.Stop();
            return new DocumentResponse
            {
                DocId = esistente.id,
                FileName = esistente.file_name,
                Pages = esistente.pagine,
                Chunks = esistente.chunk_count,
                Model = esistente.embedding_model,
                Dimension = esistente.embedding_dim,
                AlreadyIndexed = true,
                DurationMs = stopwatch.ElapsedMilliseconds
            };
        }

        // Tentativo precedente fallito o interrotto: si riparte pulito
        if (esistente is not null)
            await _repo.DeleteDocumentAsync(esistente.id, tenant);

        // 2. Estrazione del testo
        List<PdfPageText> pagine;
        int paginePdf;
        try
        {
            pagine = PdfTextExtractor.EstraiPagine(bytes, out paginePdf);
        }
        catch (Exception ex)
        {
            throw new ApplicationException($"PDF non leggibile: {ex.Message}", ex);
        }

        if (pagine.Count == 0)
            throw new ApplicationException(
                "Il PDF non contiene testo estraibile (probabilmente e' una scansione). Serve un passaggio di OCR.");

        // 3. Chunking strutturale: taglio su titoli e paragrafi, con il percorso
        //    della sezione in testa a ogni chunk.
        string titolo = TitoloDocumento(pagine, file.FileName);
        var chunks = _chunker.Chunk(pagine, ChunkSize, ChunkOverlap, titolo);
        if (chunks.Count == 0)
            throw new ApplicationException("Nessun chunk prodotto dal PDF.");

        // 4. Anagrafica del documento, in stato "in corso"
        var documento = new RagDocument
        {
            tenant = tenant,
            file_name = file.FileName,
            file_hash = hash,
            file_size = bytes.LongLength,
            pagine = paginePdf,
            chunk_count = 0,
            chunk_size = ChunkSize,
            chunk_overlap = ChunkOverlap,
            embedding_model = string.Empty,
            embedding_dim = EmbeddingDim,
            stato = RagDocument.STATO_IN_CORSO,
            allegato = bytes
        };

        int idDoc = await _repo.AddAsync(documento);

        try
        {
            // 5. Embedding di tutti i chunk (a blocchi, dentro il client)
            var testi = chunks.Select(c => c.Testo).ToList();
            var batch = await _embeddingClient.EmbedDocumentsAsync(testi, ct);

            if (batch.Vettori.Count != chunks.Count)
                throw new ApplicationException(
                    $"Vettori ricevuti ({batch.Vettori.Count}) diversi dai chunk inviati ({chunks.Count}).");

            // 6. Salvataggio dei vettori
            var righe = chunks.Select((c, i) => new RagChunk
            {
                id_doc = idDoc,
                tenant = tenant,
                chunk_index = c.Indice,
                pagina = c.Pagina,
                token_count = c.TokenCount,
                contenuto = c.Testo,
                embedding = batch.Vettori[i]
            });

            await _repo.InsertChunksAsync(righe);

            // 7. Chiusura dell'anagrafica
            await _repo.UpdateStatoAsync(idDoc, RagDocument.STATO_OK, chunks.Count,
                batch.Model, batch.Dimensione, null);

            stopwatch.Stop();

            int tokenTotali = chunks.Sum(c => c.TokenCount);

            await _logService.AddAsync(new EmmaLog
            {
                stato = 1,
                modulo = MODULO_RAG,
                tenant = tenant,
                token_input = batch.Costs?.TotalTokens ?? tokenTotali,
                token_output = 0,
                token_tot = batch.Costs?.TotalTokens ?? tokenTotali,
                cost = batch.Costs?.TotalCostEur ?? 0,
                message = $"RAG - {file.FileName} - {paginePdf} pagine - {chunks.Count} chunk",
                duration = stopwatch.ElapsedMilliseconds / 1000
            });

            return new DocumentResponse
            {
                DocId = idDoc,
                FileName = file.FileName,
                Pages = paginePdf,
                Chunks = chunks.Count,
                Tokens = tokenTotali,
                Model = batch.Model,
                Dimension = batch.Dimensione,
                AlreadyIndexed = false,
                DurationMs = stopwatch.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Indicizzazione fallita per {File}", file.FileName);

            // L'anagrafica resta con lo stato di errore: al prossimo upload dello stesso
            // file viene cancellata e si riparte da zero.
            try
            {
                await _repo.DeleteChunksAsync(idDoc);
                string messaggio = ex.Message.Length > 900 ? ex.Message[..900] : ex.Message;
                await _repo.UpdateStatoAsync(idDoc, RagDocument.STATO_ERRORE, 0, string.Empty, EmbeddingDim, messaggio);
            }
            catch (Exception exPulizia)
            {
                _logger.LogError(exPulizia, "Pulizia dopo errore fallita per il documento {IdDoc}", idDoc);
            }

            await _logService.AddAsync(new EmmaLog
            {
                stato = -1,
                modulo = MODULO_RAG,
                tenant = tenant,
                message = $"RAG - {file.FileName} - {ex.Message}",
                duration = stopwatch.ElapsedMilliseconds / 1000
            });

            throw;
        }
    }

    // =================================================================
    //  Ricerca
    // =================================================================

    public async Task<List<RagSearchResult>> CercaAsync(RagSearchRequest richiesta, string tenant, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(richiesta.Query))
            throw new ApplicationException("Query vuota.");

        int topK = richiesta.TopK > 0 ? Math.Min(richiesta.TopK, 100) : TopKDefault;

        // Stessa normalizzazione applicata ai documenti: le legature tipografiche del PDF
        // sono state sciolte in fase di estrazione, e la domanda deve passare per la
        // stessa forma perche' il confronto abbia senso.
        string domanda = richiesta.Query.Normalize(NormalizationForm.FormKC).Trim();

        // Stesso modello dei documenti ma taskType RETRIEVAL_QUERY:
        // Gemini produce vettori asimmetrici, pensati per far incontrare domanda e passaggio.
        var vettore = await _embeddingClient.EmbedQueryAsync(domanda, ct);

        var risultati = await _repo.SearchAsync(tenant, vettore, domanda, topK, richiesta.DocId, RicercaIbrida);

        if (richiesta.MinScore.HasValue)
            risultati = risultati.Where(r => r.Score >= richiesta.MinScore.Value).ToList();

        return risultati;
    }

    public Task<List<RagDocument>> GetDocumentiAsync(string tenant)
        => _repo.GetDocumentsAsync(tenant);

    public Task<RagDocument?> GetAllegatoAsync(int idDoc, string tenant)
        => _repo.GetAllegatoAsync(idDoc, tenant);

    public Task<List<RagChunkInfo>> GetChunksAsync(int idDoc, string tenant, int skip, int take)
        => _repo.GetChunksAsync(idDoc, tenant, Math.Max(skip, 0), Math.Clamp(take, 1, 100));

    public async Task<bool> EliminaDocumentoAsync(int idDoc, string tenant)
        => await _repo.DeleteDocumentAsync(idDoc, tenant) > 0;

    /// <summary>
    /// Titolo da mettere in testa a ogni chunk. La prima riga utile della prima pagina
    /// e' quasi sempre il titolo vero del documento; se non lo sembra si ripiega sul nome
    /// del file, che almeno dice di cosa si tratta.
    /// </summary>
    private static string TitoloDocumento(IReadOnlyList<PdfPageText> pagine, string fileName)
    {
        var prima = pagine.FirstOrDefault();

        if (prima is not null)
        {
            var riga = prima.Testo
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(r => r.Trim())
                .FirstOrDefault(r => r.Length > 0);

            if (riga is not null && riga.Length is > 3 and <= 100)
                return riga;
        }

        return Path.GetFileNameWithoutExtension(fileName);
    }

    private static string CalcolaHash(byte[] bytes)
        => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
