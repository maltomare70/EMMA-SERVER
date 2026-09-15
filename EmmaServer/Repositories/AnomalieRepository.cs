using Dapper;
using System.Text;
using EmmaServer.Entities;
using EmmaServer.Entities.Dtos;
using EmmaServer.Services.Anomalie;
using Npgsql;
using NpgsqlTypes;

namespace EmmaServer.Repositories;

/// <summary>Documento letto per il modulo anomalie: solo <c>content->'document'</c>, niente allegato.</summary>
public sealed class DocumentoAnomalie
{
    public int id { get; set; }
    public string tenant { get; set; } = string.Empty;
    public DateTime data_creazione { get; set; }
    public string? documento { get; set; }
}

/// <summary>Stato di un avviso di livello 1, per decidere cosa escludere da baseline e livello 2.</summary>
public sealed class AvvisoLivello1
{
    public int doc_id { get; set; }
    public string? id_riga { get; set; }
    public string tipo { get; set; } = string.Empty;
    public short stato { get; set; }
}

public interface IAnomalieRepository
{
    Task<IReadOnlyList<string>> GetTenantsConDocumentiAsync(CancellationToken ct = default);
    Task<IReadOnlyList<DocumentoAnomalie>> GetDocumentiAsync(string tenant, DateTime creatiDal, CancellationToken ct = default);
    Task<DocumentoAnomalie?> GetDocumentoAsync(int docId, CancellationToken ct = default);
    Task<IReadOnlyList<FornitoreRif>> GetFornitoriAsync(string tenant, CancellationToken ct = default);
    Task<IReadOnlyList<ArticoloRif>> GetArticoliAsync(string tenant, int? idFornitore, CancellationToken ct = default);

    /// <summary>Righe su cui l'operatore ha CONFERMATO un'anomalia: valori sbagliati, fuori dalla baseline.</summary>
    Task<HashSet<(int DocId, string IdRiga)>> GetRigheConfermateAsync(string tenant, CancellationToken ct = default);

    /// <summary>Avvisi di livello 1 del tenant (o di un solo documento), qualunque stato.</summary>
    Task<IReadOnlyList<AvvisoLivello1>> GetAvvisiLivello1Async(string tenant, int? docId = null, CancellationToken ct = default);

    /// <summary>
    /// Documenti dello stesso mittente e tipo, creati dopo <paramref name="creatiDal"/>, escluso <paramref name="escludiId"/>.
    /// Il filtro su lower(mittente) usa l'indice funzionale ix_bolle_mittente_lower.
    /// </summary>
    Task<IReadOnlyList<DocumentoAnomalie>> GetDocumentiStessoMittenteAsync(string tenant, string mittente, string tipoDocumento,
        int escludiId, DateTime creatiDal, CancellationToken ct = default);

    Task<int> SostituisciBaselineAsync(string tenant, IReadOnlyList<EmmaAnomaliaBaseline> righe, CancellationToken ct = default);
    Task<IReadOnlyList<EmmaAnomaliaBaseline>> GetBaselineAsync(string tenant, int idFornitore, short tipoDoc,
        IReadOnlyCollection<string> codici, CancellationToken ct = default);

    /// <summary>
    /// Riscrive gli avvisi APERTI di un livello per un documento. Quelli su cui l'operatore ha gia'
    /// dato un feedback restano, e non vengono duplicati.
    /// </summary>
    Task SostituisciAnomalieAsync(int docId, short livello, IReadOnlyList<EmmaAnomalia> anomalie, CancellationToken ct = default);

    Task<IReadOnlyList<EmmaAnomalia>> GetAnomalieDocumentoAsync(string tenant, int docId, CancellationToken ct = default);

    // ---- lista, feedback, statistiche ----

    Task<(int Totale, List<AnomaliaLista> Righe)> GetAnomalieAsync(string tenant, FiltriAnomalie filtri, CancellationToken ct = default);

    Task<EmmaAnomalia?> GetAnomaliaAsync(string tenant, long id, CancellationToken ct = default);

    /// <summary>Scrive il feedback; stato 0 lo cancella. False se l'avviso non esiste per il tenant.</summary>
    Task<bool> AggiornaFeedbackAsync(string tenant, long id, short stato, string? utente, string? note, CancellationToken ct = default);

    /// <summary>Contatori per (tipo, livello) degli avvisi visibili.</summary>
    Task<List<StatisticaAnomalie>> GetStatisticheAsync(string tenant, DateTime? da, DateTime? a, short? livello, CancellationToken ct = default);
}

/// <summary>
/// Non deriva da RepositoryGenerico di proposito: quel costruttore chiama GetTenant(), che fuori da
/// una richiesta HTTP (batch notturno) solleva eccezione. Qui il tenant arriva sempre come parametro
/// e del provider si usa solo la stringa di connessione.
/// </summary>
public class AnomalieRepository : IAnomalieRepository
{
    private readonly IUserConnectionProvider _connectionProvider;

    public AnomalieRepository(IUserConnectionProvider connectionProvider)
    {
        _connectionProvider = connectionProvider;
    }

    private NpgsqlConnection Connessione() => new(_connectionProvider.GetEmmaConnectionString());

    public async Task<IReadOnlyList<string>> GetTenantsConDocumentiAsync(CancellationToken ct = default)
    {
        await using var db = Connessione();
        var r = await db.QueryAsync<string>(new CommandDefinition(
            "SELECT DISTINCT tenant FROM docs WHERE tenant IS NOT NULL AND tenant <> ''",
            cancellationToken: ct));
        return r.ToList();
    }

    // Seq scan con lettura del JSON: e' il motivo per cui la baseline si calcola di notte.
    public async Task<IReadOnlyList<DocumentoAnomalie>> GetDocumentiAsync(string tenant, DateTime creatiDal, CancellationToken ct = default)
    {
        const string sql = """
            SELECT id, tenant, data_creazione, (content->'document')::text AS documento
            FROM   docs
            WHERE  tenant = @Tenant
              AND  data_creazione >= @Dal
              AND  content->'document'->>'tipo_documento' IN ('2', '3', '4')
            ORDER  BY id
            """;
        await using var db = Connessione();
        var r = await db.QueryAsync<DocumentoAnomalie>(new CommandDefinition(
            sql, new { Tenant = tenant, Dal = creatiDal }, cancellationToken: ct, commandTimeout: 300));
        return r.ToList();
    }

    public async Task<DocumentoAnomalie?> GetDocumentoAsync(int docId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT id, tenant, data_creazione, (content->'document')::text AS documento
            FROM   docs
            WHERE  id = @Id
            """;
        await using var db = Connessione();
        return await db.QueryFirstOrDefaultAsync<DocumentoAnomalie>(new CommandDefinition(
            sql, new { Id = docId }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<FornitoreRif>> GetFornitoriAsync(string tenant, CancellationToken ct = default)
    {
        await using var db = Connessione();
        var r = await db.QueryAsync<FornitoreRif>(new CommandDefinition(
            "SELECT id, descrizione FROM fornitori WHERE tenant = @Tenant ORDER BY id",
            new { Tenant = tenant }, cancellationToken: ct));
        return r.ToList();
    }

    public async Task<IReadOnlyList<ArticoloRif>> GetArticoliAsync(string tenant, int? idFornitore, CancellationToken ct = default)
    {
        var sql = """
            SELECT idfornitore, codice, rifcodice
            FROM   articoli
            WHERE  tenant = @Tenant
              AND  COALESCE(rifcodice, '') <> ''
            """;
        if (idFornitore is not null) sql += " AND idfornitore = @IdFornitore";

        await using var db = Connessione();
        var r = await db.QueryAsync<ArticoloRif>(new CommandDefinition(
            sql, new { Tenant = tenant, IdFornitore = idFornitore ?? 0 }, cancellationToken: ct));
        return r.ToList();
    }

    public async Task<HashSet<(int DocId, string IdRiga)>> GetRigheConfermateAsync(string tenant, CancellationToken ct = default)
    {
        const string sql = """
            SELECT doc_id AS DocId, id_riga AS IdRiga
            FROM   anomalie
            WHERE  tenant = @Tenant AND stato = 1 AND livello >= 2 AND id_riga IS NOT NULL
            """;
        await using var db = Connessione();
        var r = await db.QueryAsync<RigaConfermata>(new CommandDefinition(
            sql, new { Tenant = tenant }, cancellationToken: ct));
        return r.Select(x => (x.DocId, x.IdRiga)).ToHashSet();
    }

    public async Task<IReadOnlyList<AvvisoLivello1>> GetAvvisiLivello1Async(string tenant, int? docId = null, CancellationToken ct = default)
    {
        var sql = """
            SELECT doc_id, id_riga, tipo, stato
            FROM   anomalie
            WHERE  tenant = @Tenant AND livello = 1
            """;
        if (docId is not null) sql += " AND doc_id = @DocId";

        await using var db = Connessione();
        var r = await db.QueryAsync<AvvisoLivello1>(new CommandDefinition(
            sql, new { Tenant = tenant, DocId = docId ?? 0 }, cancellationToken: ct));
        return r.ToList();
    }

    public async Task<IReadOnlyList<DocumentoAnomalie>> GetDocumentiStessoMittenteAsync(string tenant, string mittente,
        string tipoDocumento, int escludiId, DateTime creatiDal, CancellationToken ct = default)
    {
        const string sql = """
            SELECT id, tenant, data_creazione, (content->'document')::text AS documento
            FROM   docs
            WHERE  tenant = @Tenant
              AND  lower(content->'document'->>'mittente') = lower(@Mittente)
              AND  content->'document'->>'tipo_documento' = @TipoDocumento
              AND  id <> @EscludiId
              AND  data_creazione >= @Dal
            """;
        await using var db = Connessione();
        var r = await db.QueryAsync<DocumentoAnomalie>(new CommandDefinition(sql, new
        {
            Tenant = tenant,
            Mittente = mittente,
            TipoDocumento = tipoDocumento,
            EscludiId = escludiId,
            Dal = creatiDal
        }, cancellationToken: ct));
        return r.ToList();
    }

    public async Task<int> SostituisciBaselineAsync(string tenant, IReadOnlyList<EmmaAnomaliaBaseline> righe, CancellationToken ct = default)
    {
        await using var db = Connessione();
        await db.OpenAsync(ct);
        await using var tx = await db.BeginTransactionAsync(ct);

        await db.ExecuteAsync(new CommandDefinition(
            "DELETE FROM anomalie_baseline WHERE tenant = @Tenant",
            new { Tenant = tenant }, transaction: tx, cancellationToken: ct));

        // COPY binario: migliaia di righe in un colpo solo, anche verso un database remoto.
        await using (var writer = await db.BeginBinaryImportAsync("""
            COPY anomalie_baseline (tenant, id_fornitore, codice, tipo_doc, n,
                                    mediana_prezzo, mad_prezzo, mediana_qta, mad_qta,
                                    um_prevalente, primo_visto, ultimo_visto)
            FROM STDIN (FORMAT BINARY)
            """, ct))
        {
            foreach (var b in righe)
            {
                await writer.StartRowAsync(ct);
                await writer.WriteAsync(tenant, NpgsqlDbType.Varchar, ct);
                await writer.WriteAsync(b.id_fornitore, NpgsqlDbType.Integer, ct);
                await writer.WriteAsync(b.codice, NpgsqlDbType.Varchar, ct);
                await writer.WriteAsync(b.tipo_doc, NpgsqlDbType.Smallint, ct);
                await writer.WriteAsync(b.n, NpgsqlDbType.Integer, ct);
                await Scrivi(writer, b.mediana_prezzo, NpgsqlDbType.Numeric, ct);
                await Scrivi(writer, b.mad_prezzo, NpgsqlDbType.Numeric, ct);
                await Scrivi(writer, b.mediana_qta, NpgsqlDbType.Numeric, ct);
                await Scrivi(writer, b.mad_qta, NpgsqlDbType.Numeric, ct);
                if (b.um_prevalente is null) await writer.WriteNullAsync(ct);
                else await writer.WriteAsync(b.um_prevalente, NpgsqlDbType.Varchar, ct);
                await Scrivi(writer, b.primo_visto, NpgsqlDbType.Date, ct);
                await Scrivi(writer, b.ultimo_visto, NpgsqlDbType.Date, ct);
            }

            await writer.CompleteAsync(ct);
        }

        await tx.CommitAsync(ct);
        return righe.Count;
    }

    private static Task Scrivi<T>(NpgsqlBinaryImporter writer, T? valore, NpgsqlDbType tipo, CancellationToken ct)
        where T : struct
        => valore.HasValue ? writer.WriteAsync(valore.Value, tipo, ct) : writer.WriteNullAsync(ct);

    public async Task<IReadOnlyList<EmmaAnomaliaBaseline>> GetBaselineAsync(string tenant, int idFornitore, short tipoDoc,
        IReadOnlyCollection<string> codici, CancellationToken ct = default)
    {
        if (codici.Count == 0) return [];

        const string sql = """
            SELECT *
            FROM   anomalie_baseline
            WHERE  tenant = @Tenant
              AND  id_fornitore = @IdFornitore
              AND  tipo_doc = @TipoDoc
              AND  codice = ANY(@Codici)
            """;
        await using var db = Connessione();
        var r = await db.QueryAsync<EmmaAnomaliaBaseline>(new CommandDefinition(sql, new
        {
            Tenant = tenant,
            IdFornitore = idFornitore,
            TipoDoc = tipoDoc,
            Codici = codici.ToArray()
        }, cancellationToken: ct));
        return r.ToList();
    }

    public async Task SostituisciAnomalieAsync(int docId, short livello, IReadOnlyList<EmmaAnomalia> anomalie, CancellationToken ct = default)
    {
        await using var db = Connessione();
        await db.OpenAsync(ct);
        await using var tx = await db.BeginTransactionAsync(ct);

        await db.ExecuteAsync(new CommandDefinition(
            "DELETE FROM anomalie WHERE doc_id = @DocId AND livello = @Livello AND stato = 0",
            new { DocId = docId, Livello = livello }, transaction: tx, cancellationToken: ct));

        if (anomalie.Count > 0)
        {
            const string insert = """
                INSERT INTO anomalie (tenant, doc_id, id_master, id_riga, id_fornitore, codice, tipo, livello,
                                      score, severita, valore, atteso, delta_perc, messaggio, modello_ver)
                SELECT @tenant, @doc_id, @id_master, @id_riga, @id_fornitore, @codice, @tipo, @livello,
                       @score, @severita, @valore, @atteso, @delta_perc, @messaggio, @modello_ver
                WHERE NOT EXISTS (
                    SELECT 1 FROM anomalie a
                    WHERE  a.doc_id  = @doc_id
                      AND  a.livello = @livello
                      AND  a.tipo    = @tipo
                      AND  a.id_riga IS NOT DISTINCT FROM @id_riga
                      AND  a.stato  <> 0)
                """;
            await db.ExecuteAsync(new CommandDefinition(insert, anomalie, transaction: tx, cancellationToken: ct));
        }

        await tx.CommitAsync(ct);
    }

    public async Task<IReadOnlyList<EmmaAnomalia>> GetAnomalieDocumentoAsync(string tenant, int docId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT *
            FROM   anomalie
            WHERE  tenant = @Tenant AND doc_id = @DocId
            ORDER  BY severita DESC, id
            """;
        await using var db = Connessione();
        var r = await db.QueryAsync<EmmaAnomalia>(new CommandDefinition(
            sql, new { Tenant = tenant, DocId = docId }, cancellationToken: ct));
        return r.ToList();
    }

    public async Task<(int Totale, List<AnomaliaLista> Righe)> GetAnomalieAsync(string tenant, FiltriAnomalie filtri,
        CancellationToken ct = default)
    {
        var where = new StringBuilder("WHERE a.tenant = @Tenant AND a.severita >= @Severita");
        var p = new DynamicParameters();
        p.Add("Tenant", tenant);
        p.Add("Severita", filtri.Severita ?? (short)1);

        if (filtri.Stato is { } stato) { where.Append(" AND a.stato = @Stato"); p.Add("Stato", stato); }
        if (filtri.Fornitore is { } forn) { where.Append(" AND a.id_fornitore = @Fornitore"); p.Add("Fornitore", forn); }
        if (filtri.Livello is { } liv) { where.Append(" AND a.livello = @Livello"); p.Add("Livello", liv); }
        if (!string.IsNullOrWhiteSpace(filtri.Tipo)) { where.Append(" AND a.tipo = @Tipo"); p.Add("Tipo", filtri.Tipo.Trim()); }
        if (filtri.DocId is { } doc) { where.Append(" AND a.doc_id = @DocId"); p.Add("DocId", doc); }
        if (filtri.Da is { } da) { where.Append(" AND a.data_creazione >= @Da"); p.Add("Da", da); }
        if (filtri.A is { } a) { where.Append(" AND a.data_creazione < @A"); p.Add("A", a); }

        p.Add("Limit", filtri.Limit);
        p.Add("Offset", filtri.Offset);

        var sqlConteggio = $"SELECT count(*)::int FROM anomalie a {where}";
        var sqlPagina = $"""
            SELECT a.*,
                   f.descrizione                              AS fornitore_descrizione,
                   d.content->'document'->>'mittente'         AS mittente,
                   d.content->'document'->>'numero_bolla'     AS numero_documento,
                   d.content->'document'->>'data_bolla'       AS data_documento,
                   d.content->'document'->>'tipo_documento'   AS tipo_documento
            FROM   anomalie a
            JOIN   docs d           ON d.id = a.doc_id
            LEFT   JOIN fornitori f ON f.id = a.id_fornitore
            {where}
            ORDER  BY a.stato, a.severita DESC, a.data_creazione DESC, a.id DESC
            LIMIT  @Limit OFFSET @Offset
            """;

        await using var db = Connessione();
        var totale = await db.ExecuteScalarAsync<int>(new CommandDefinition(sqlConteggio, p, cancellationToken: ct));
        var righe = await db.QueryAsync<AnomaliaLista>(new CommandDefinition(sqlPagina, p, cancellationToken: ct));
        return (totale, righe.ToList());
    }

    public async Task<EmmaAnomalia?> GetAnomaliaAsync(string tenant, long id, CancellationToken ct = default)
    {
        await using var db = Connessione();
        return await db.QueryFirstOrDefaultAsync<EmmaAnomalia>(new CommandDefinition(
            "SELECT * FROM anomalie WHERE id = @Id AND tenant = @Tenant",
            new { Id = id, Tenant = tenant }, cancellationToken: ct));
    }

    public async Task<bool> AggiornaFeedbackAsync(string tenant, long id, short stato, string? utente, string? note,
        CancellationToken ct = default)
    {
        // Stato 0 = riapertura: il feedback precedente non vale piu' come etichetta.
        const string sql = """
            UPDATE anomalie
            SET    stato           = @Stato,
                   utente_feedback = CASE WHEN @Stato = 0 THEN NULL ELSE @Utente END,
                   data_feedback   = CASE WHEN @Stato = 0 THEN NULL ELSE now() END,
                   note            = @Note
            WHERE  id = @Id AND tenant = @Tenant
            """;
        await using var db = Connessione();
        var righe = await db.ExecuteAsync(new CommandDefinition(sql, new
        {
            Id = id,
            Tenant = tenant,
            Stato = stato,
            Utente = utente,
            Note = note
        }, cancellationToken: ct));
        return righe > 0;
    }

    public async Task<List<StatisticaAnomalie>> GetStatisticheAsync(string tenant, DateTime? da, DateTime? a, short? livello,
        CancellationToken ct = default)
    {
        // Solo avvisi visibili: la metrica e' la precisione di cio' che l'operatore vede davvero.
        var sql = new StringBuilder("""
            SELECT tipo                                      AS Tipo,
                   livello                                   AS Livello,
                   count(*)::int                             AS Totali,
                   (count(*) FILTER (WHERE stato = 0))::int  AS Aperte,
                   (count(*) FILTER (WHERE stato = 1))::int  AS Confermate,
                   (count(*) FILTER (WHERE stato = 2))::int  AS Ignorate
            FROM   anomalie
            WHERE  tenant = @Tenant AND severita > 0
            """);
        var p = new DynamicParameters();
        p.Add("Tenant", tenant);

        if (da is { } dal) { sql.Append(" AND data_creazione >= @Da"); p.Add("Da", dal); }
        if (a is { } al) { sql.Append(" AND data_creazione < @A"); p.Add("A", al); }
        if (livello is { } liv) { sql.Append(" AND livello = @Livello"); p.Add("Livello", liv); }
        sql.Append(" GROUP BY tipo, livello ORDER BY livello, tipo");

        await using var db = Connessione();
        var r = await db.QueryAsync<StatisticaAnomalie>(new CommandDefinition(sql.ToString(), p, cancellationToken: ct));
        return r.ToList();
    }

    private sealed class RigaConfermata
    {
        public int DocId { get; set; }
        public string IdRiga { get; set; } = string.Empty;
    }
}
