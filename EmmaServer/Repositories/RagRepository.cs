using Dapper;
using EmmaServer.Entities;
using EmmaServer.Entities.Dtos;
using System.Globalization;
using System.Text;

namespace EmmaServer.Repositories;

public interface IRagRepository : IRepositoryGenerico<RagDocument>
{
    Task<RagDocument?> GetByHashAsync(string tenant, string fileHash);
    Task<List<RagDocument>> GetDocumentsAsync(string tenant);
    Task<RagDocument?> GetAllegatoAsync(int idDoc, string tenant);
    Task<List<RagChunkInfo>> GetChunksAsync(int idDoc, string tenant, int skip, int take);
    Task<int> InsertChunksAsync(IEnumerable<RagChunk> chunks);
    Task<int> DeleteChunksAsync(int idDoc);
    Task<int> UpdateStatoAsync(int idDoc, int stato, int chunkCount, string embeddingModel, int embeddingDim, string? messaggio);
    Task<int> DeleteDocumentAsync(int idDoc, string tenant);
    Task<List<RagSearchResult>> SearchAsync(string tenant, float[] queryEmbedding, string testoQuery, int topK, int? idDoc, bool ibrida);
}

/// <summary>
/// Accesso alle tabelle rag_documents / rag_chunks.
///
/// Nota sui vettori: il progetto crea le NpgsqlConnection a mano, senza
/// NpgsqlDataSource, quindi non e' disponibile la mappatura del tipo vector
/// di Pgvector.Npgsql. Il vettore viaggia quindi come letterale testuale
/// '[0.1,0.2,...]' con cast esplicito CAST(@x AS vector): stesso risultato,
/// nessuna dipendenza aggiuntiva e nessuna modifica all'architettura delle connessioni.
/// </summary>
public class RagRepository : RepositoryGenerico<RagDocument>, IRagRepository
{
    /// <summary>Righe per singola INSERT: tiene il numero di parametri ben sotto il limite di Postgres (65535).</summary>
    private const int RIGHE_PER_INSERT = 100;

    public RagRepository(IUserConnectionProvider connectionProvider) : base(connectionProvider)
    {
    }

    public async Task<RagDocument?> GetByHashAsync(string tenant, string fileHash)
    {
        const string sql = @"
            SELECT id, tenant, file_name, file_hash, file_size, pagine, chunk_count,
                   chunk_size, chunk_overlap, embedding_model, embedding_dim,
                   stato, messaggio, data_creazione
            FROM rag_documents
            WHERE tenant = @Tenant AND file_hash = @FileHash
            LIMIT 1;";

        using var db = await CreaConnessione();
        return await db.QuerySingleOrDefaultAsync<RagDocument>(sql, new { Tenant = tenant, FileHash = fileHash });
    }

    public async Task<List<RagDocument>> GetDocumentsAsync(string tenant)
    {
        // Senza allegato: la lista non deve trascinarsi dietro i PDF
        const string sql = @"
            SELECT id, tenant, file_name, file_hash, file_size, pagine, chunk_count,
                   chunk_size, chunk_overlap, embedding_model, embedding_dim,
                   stato, messaggio, data_creazione
            FROM rag_documents
            WHERE tenant = @Tenant
            ORDER BY data_creazione DESC;";

        using var db = await CreaConnessione();
        var righe = await db.QueryAsync<RagDocument>(sql, new { Tenant = tenant });
        return righe.ToList();
    }

    /// <summary>
    /// Recupera il PDF originale. E' l'unica query che legge la colonna allegato:
    /// tutte le altre la escludono di proposito per non trascinarsi dietro i binari.
    /// </summary>
    public async Task<RagDocument?> GetAllegatoAsync(int idDoc, string tenant)
    {
        const string sql = @"
            SELECT id, file_name, allegato
            FROM rag_documents
            WHERE id = @IdDoc AND tenant = @Tenant
            LIMIT 1;";

        using var db = await CreaConnessione();
        return await db.QuerySingleOrDefaultAsync<RagDocument>(sql, new { IdDoc = idDoc, Tenant = tenant });
    }

    /// <summary>
    /// Diagnostica: i chunk salvati con il testo e la salute del vettore.
    ///
    /// La norma si calcola come sqrt(v . v): l'operatore &lt;#&gt; di pgvector restituisce
    /// il prodotto interno negato, quindi -(v &lt;#&gt; v) e' il prodotto scalare del vettore
    /// con se stesso. Cosi' funziona su qualunque versione dell'estensione, senza
    /// dipendere da l2_norm che e' arrivata solo dalla 0.7.
    /// </summary>
    public async Task<List<RagChunkInfo>> GetChunksAsync(int idDoc, string tenant, int skip, int take)
    {
        const string sql = @"
            SELECT c.chunk_index                       AS chunk_index,
                   c.pagina                            AS page,
                   c.token_count                       AS token_count,
                   c.contenuto                         AS content,
                   vector_dims(c.embedding)            AS dims,
                   sqrt(-1 * (c.embedding <#> c.embedding)) AS norm
            FROM rag_chunks c
            WHERE c.id_doc = @IdDoc AND c.tenant = @Tenant
            ORDER BY c.chunk_index
            OFFSET @Skip LIMIT @Take;";

        using var db = await CreaConnessione();
        var righe = await db.QueryAsync<RagChunkInfo>(sql, new { IdDoc = idDoc, Tenant = tenant, Skip = skip, Take = take });
        return righe.ToList();
    }

    public async Task<int> InsertChunksAsync(IEnumerable<RagChunk> chunks)
    {
        var lista = chunks.ToList();
        if (lista.Count == 0) return 0;

        using var db = await CreaConnessione();

        int inseriti = 0;

        for (int offset = 0; offset < lista.Count; offset += RIGHE_PER_INSERT)
        {
            var blocco = lista.Skip(offset).Take(RIGHE_PER_INSERT).ToList();

            var sql = new StringBuilder(@"
                INSERT INTO rag_chunks (id_doc, tenant, chunk_index, pagina, token_count, contenuto, embedding)
                VALUES ");

            var parametri = new DynamicParameters();

            for (int i = 0; i < blocco.Count; i++)
            {
                var chunk = blocco[i];

                if (i > 0) sql.Append(',');
                sql.Append($"(@d{i}, @t{i}, @i{i}, @p{i}, @k{i}, @c{i}, CAST(@e{i} AS vector))");

                parametri.Add($"d{i}", chunk.id_doc);
                parametri.Add($"t{i}", chunk.tenant);
                parametri.Add($"i{i}", chunk.chunk_index);
                parametri.Add($"p{i}", chunk.pagina);
                parametri.Add($"k{i}", chunk.token_count);
                parametri.Add($"c{i}", chunk.contenuto);
                parametri.Add($"e{i}", ToVectorLiteral(chunk.embedding));
            }

            sql.Append(';');

            inseriti += await db.ExecuteAsync(sql.ToString(), parametri);
        }

        return inseriti;
    }

    /// <summary>
    /// Chiude (o marca in errore) l'anagrafica del documento.
    /// UPDATE mirato invece di Dapper.Contrib: cosi' il PDF in allegato non
    /// viene rispedito al database a ogni cambio di stato.
    /// </summary>
    public async Task<int> UpdateStatoAsync(int idDoc, int stato, int chunkCount, string embeddingModel, int embeddingDim, string? messaggio)
    {
        const string sql = @"
            UPDATE rag_documents
            SET stato           = @Stato,
                chunk_count     = @ChunkCount,
                embedding_model = @EmbeddingModel,
                embedding_dim   = @EmbeddingDim,
                messaggio       = @Messaggio
            WHERE id = @IdDoc;";

        using var db = await CreaConnessione();
        return await db.ExecuteAsync(sql, new
        {
            IdDoc = idDoc,
            Stato = stato,
            ChunkCount = chunkCount,
            EmbeddingModel = embeddingModel,
            EmbeddingDim = embeddingDim,
            Messaggio = messaggio
        });
    }

    public async Task<int> DeleteChunksAsync(int idDoc)
    {
        const string sql = "DELETE FROM rag_chunks WHERE id_doc = @IdDoc;";

        using var db = await CreaConnessione();
        return await db.ExecuteAsync(sql, new { IdDoc = idDoc });
    }

    public async Task<int> DeleteDocumentAsync(int idDoc, string tenant)
    {
        // I chunk se ne vanno con la ON DELETE CASCADE della foreign key
        const string sql = "DELETE FROM rag_documents WHERE id = @IdDoc AND tenant = @Tenant;";

        using var db = await CreaConnessione();
        return await db.ExecuteAsync(sql, new { IdDoc = idDoc, Tenant = tenant });
    }

    /// <summary>
    /// Ricerca dei passaggi piu' pertinenti.
    ///
    /// Con <paramref name="ibrida"/> attiva si eseguono due ricerche in parallelo dentro
    /// la stessa query e si fondono i due ordinamenti con Reciprocal Rank Fusion:
    ///
    ///  - il ramo VETTORIALE trova cio' che somiglia per significato, anche con parole
    ///    diverse da quelle scritte nel documento;
    ///  - il ramo LESSICALE (full-text italiano) trova le parole esatte, e copre proprio
    ///    il caso in cui il vettore sbaglia su un termine preciso: un codice, un nome di
    ///    pulsante, una sigla.
    ///
    /// RRF somma 1/(k + posizione) dei due ranking con k = 60: e' la fusione standard,
    /// non richiede di normalizzare punteggi di natura diversa e premia i passaggi che
    /// stanno in alto in almeno uno dei due elenchi.
    ///
    /// Il campo score restituito resta la similarita' coseno, non il valore RRF: serve a
    /// mostrare all'utente quanto il passaggio somiglia alla domanda, mentre l'RRF governa
    /// solo l'ordine.
    /// </summary>
    public async Task<List<RagSearchResult>> SearchAsync(
        string tenant, float[] queryEmbedding, string testoQuery, int topK, int? idDoc, bool ibrida)
    {
        // Quanti candidati chiedere a ciascun ramo prima della fusione: servono piu'
        // dei risultati finali, altrimenti la fusione non ha nulla da fondere.
        int candidati = Math.Max(topK * 5, 30);

        string filtroDoc = idDoc.HasValue ? " AND c.id_doc = @IdDoc" : string.Empty;

        var parametri = new DynamicParameters();
        parametri.Add("Tenant", tenant);
        parametri.Add("Query", ToVectorLiteral(queryEmbedding));
        parametri.Add("TopK", topK);
        if (idDoc.HasValue) parametri.Add("IdDoc", idDoc.Value);

        string sql;

        if (ibrida)
        {
            parametri.Add("Testo", testoQuery);
            parametri.Add("Candidati", candidati);

            sql = $@"
                WITH vettoriale AS (
                    SELECT c.id,
                           row_number() OVER (ORDER BY c.embedding <=> CAST(@Query AS vector)) AS rk
                    FROM rag_chunks c
                    WHERE c.tenant = @Tenant{filtroDoc}
                    ORDER BY c.embedding <=> CAST(@Query AS vector)
                    LIMIT @Candidati
                ),
                lessicale AS (
                    SELECT c.id,
                           row_number() OVER (ORDER BY ts_rank_cd(c.tsv, q.query) DESC) AS rk
                    FROM rag_chunks c,
                         websearch_to_tsquery('italian', @Testo) AS q(query)
                    WHERE c.tenant = @Tenant{filtroDoc}
                      AND c.tsv @@ q.query
                    ORDER BY ts_rank_cd(c.tsv, q.query) DESC
                    LIMIT @Candidati
                ),
                fusi AS (
                    SELECT COALESCE(v.id, l.id) AS id,
                           COALESCE(1.0 / (60 + v.rk), 0) + COALESCE(1.0 / (60 + l.rk), 0) AS rrf
                    FROM vettoriale v
                    FULL OUTER JOIN lessicale l ON l.id = v.id
                )
                SELECT c.id_doc      AS doc_id,
                       d.file_name   AS file_name,
                       c.chunk_index AS chunk_index,
                       c.pagina      AS page,
                       c.contenuto   AS content,
                       1 - (c.embedding <=> CAST(@Query AS vector)) AS score
                FROM fusi f
                JOIN rag_chunks c    ON c.id = f.id
                JOIN rag_documents d ON d.id = c.id_doc
                ORDER BY f.rrf DESC
                LIMIT @TopK;";
        }
        else
        {
            // <=> e' la distanza coseno di pgvector (0 = identico): la similarita' e' 1 - distanza.
            // L'ORDER BY sulla stessa espressione e' cio' che permette l'uso dell'indice HNSW.
            sql = $@"
                SELECT c.id_doc      AS doc_id,
                       d.file_name   AS file_name,
                       c.chunk_index AS chunk_index,
                       c.pagina      AS page,
                       c.contenuto   AS content,
                       1 - (c.embedding <=> CAST(@Query AS vector)) AS score
                FROM rag_chunks c
                JOIN rag_documents d ON d.id = c.id_doc
                WHERE c.tenant = @Tenant{filtroDoc}
                ORDER BY c.embedding <=> CAST(@Query AS vector)
                LIMIT @TopK;";
        }

        using var db = await CreaConnessione();

        // Il default di HNSW e' ef_search = 40: con un grafo piccolo il richiamo ne
        // risente e capita che il passaggio giusto non entri fra i candidati esaminati.
        // La connessione e' nuova a ogni chiamata, quindi il SET vale solo per questa query.
        await db.ExecuteAsync("SET hnsw.ef_search = 100;");

        var righe = await db.QueryAsync<RagSearchResult>(sql, parametri);
        return righe.ToList();
    }

    /// <summary>Converte il vettore nel letterale accettato da pgvector: [0.1,0.2,...]</summary>
    private static string ToVectorLiteral(float[] vettore)
    {
        var sb = new StringBuilder(vettore.Length * 12 + 2);
        sb.Append('[');

        for (int i = 0; i < vettore.Length; i++)
        {
            if (i > 0) sb.Append(',');
            // "R" garantisce il round-trip; InvariantCulture evita la virgola decimale italiana
            sb.Append(vettore[i].ToString("R", CultureInfo.InvariantCulture));
        }

        sb.Append(']');
        return sb.ToString();
    }
}
