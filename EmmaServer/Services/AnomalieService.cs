using System.Diagnostics;
using System.Text.Json;
using EmmaServer.Entities;
using EmmaServer.Entities.Dtos;
using EmmaServer.Repositories;
using EmmaServer.Services.Anomalie;
// Alias: esiste anche EmmaServer.Entities.Dtos.RigheDocumento
using RigheDoc = EmmaServer.Services.Anomalie.RigheDocumento;

namespace EmmaServer.Services;

/// <summary>Esito del ricalcolo della baseline di un tenant: serve a capire quanto storico e' stato scartato.</summary>
public sealed record RisultatoBaseline(
    string Tenant,
    int DocumentiLetti,
    int EsclusiIlleggibili,
    int EsclusiNonQuadrano,
    int EsclusiLivello1,
    int EsclusiSenzaFornitore,
    int RigheUsate,
    int RigheScartate,
    int ChiaviBaseline,
    long DurataMs);

public interface IAnomalieService
{
    /// <summary>
    /// Livelli 1 e 2 su un documento appena importato. Restituisce gli avvisi calcolati in questo giro
    /// (quelli gia' valutati dall'operatore restano sul DB e non vengono duplicati),
    /// oppure null se il documento non esiste o non appartiene a <paramref name="tenantAtteso"/>.
    /// </summary>
    Task<IReadOnlyList<EmmaAnomalia>?> AnalizzaDocumentoAsync(int docId, string? tenantAtteso = null, CancellationToken ct = default);

    Task<RisultatoBaseline> RicalcolaBaselineAsync(string tenant, CancellationToken ct = default);

    Task<IReadOnlyList<RisultatoBaseline>> RicalcolaTutteLeBaselineAsync(CancellationToken ct = default);

    Task<IReadOnlyList<EmmaAnomalia>> GetAnomalieDocumentoAsync(string tenant, int docId, CancellationToken ct = default);

    /// <summary>Lista paginata degli avvisi del tenant. Solleva ArgumentException su filtri non validi.</summary>
    Task<PaginaAnomalie> GetAnomalieAsync(string tenant, FiltriAnomalie filtri, CancellationToken ct = default);

    /// <summary>
    /// Registra il feedback dell'operatore e restituisce l'avviso aggiornato, oppure null se non esiste
    /// per il tenant. Solleva ArgumentException su valori non validi.
    /// </summary>
    Task<EmmaAnomalia?> RegistraFeedbackAsync(string tenant, long id, FeedbackAnomalia feedback, string? utente,
        CancellationToken ct = default);

    /// <summary>Precisione degli avvisi visibili, per tipo e complessiva.</summary>
    Task<StatisticheAnomalie> GetStatisticheAsync(string tenant, DateTime? da, DateTime? a, short? livello,
        CancellationToken ct = default);
}

/// <summary>
/// Modulo anomalie (docs/modulo-anomalie.md): livello 1 (regole sul documento), baseline notturna per
/// (tenant, fornitore, articolo, tipo documento) e livello 2 (z robusto su mediana/MAD) all'import.
///
/// Dipende solo da AnomalieRepository, che non legge il tenant dall'HttpContext:
/// il servizio funziona sia dentro una richiesta sia nel batch notturno.
/// </summary>
public class AnomalieService : IAnomalieService
{
    private static readonly JsonSerializerOptions OpzioniJson = new() { PropertyNameCaseInsensitive = true };

    private readonly IAnomalieRepository _repo;
    private readonly OpzioniAnomalie _opzioni;
    private readonly ILogger<AnomalieService> _logger;

    public AnomalieService(IAnomalieRepository repo, IConfiguration configuration, ILogger<AnomalieService> logger)
    {
        _repo = repo;
        _logger = logger;
        _opzioni = configuration.GetSection("Anomalie").Get<OpzioniAnomalie>() ?? new OpzioniAnomalie();
    }

    // ------------------------------------------------------------------
    // Baseline
    // ------------------------------------------------------------------

    public async Task<IReadOnlyList<RisultatoBaseline>> RicalcolaTutteLeBaselineAsync(CancellationToken ct = default)
    {
        var risultati = new List<RisultatoBaseline>();
        foreach (var tenant in await _repo.GetTenantsConDocumentiAsync(ct))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                risultati.Add(await RicalcolaBaselineAsync(tenant, ct));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Un tenant con dati rotti non deve fermare gli altri.
                _logger.LogError(ex, "Baseline anomalie non ricalcolata per il tenant {Tenant}", tenant);
            }
        }
        return risultati;
    }

    public async Task<RisultatoBaseline> RicalcolaBaselineAsync(string tenant, CancellationToken ct = default)
    {
        var cronometro = Stopwatch.StartNew();
        var dal = DateTime.UtcNow.AddMonths(-_opzioni.MesiStorico);
        var dataMinima = DateOnly.FromDateTime(dal);

        var documenti = await _repo.GetDocumentiAsync(tenant, dal, ct);
        var risolutore = new RisolutoreAnagrafiche(
            await _repo.GetFornitoriAsync(tenant, ct),
            await _repo.GetArticoliAsync(tenant, null, ct));
        var righeConfermate = await _repo.GetRigheConfermateAsync(tenant, ct);

        // Livello 1: documenti e righe inaffidabili restano fuori. Un avviso ignorato dall'operatore
        // (stato 2) vale come "il documento e' giusto".
        var avvisiL1 = await _repo.GetAvvisiLivello1Async(tenant, null, ct);
        var docLivello1 = avvisiL1
            .Where(a => a.stato != 2 && BloccaDocumento(a.tipo))
            .Select(a => a.doc_id)
            .ToHashSet();
        var quadraturaIgnorata = avvisiL1
            .Where(a => a.stato == 2 && a.tipo == TipoAnomalia.IncoerenzaTotali)
            .Select(a => a.doc_id)
            .ToHashSet();
        var righeLivello1 = avvisiL1
            .Where(a => a.stato != 2 && a.id_riga is not null && TipoAnomalia.BloccantiRiga.Contains(a.tipo))
            .Select(a => (a.doc_id, a.id_riga!))
            .ToHashSet();

        int illeggibili = 0, nonQuadrano = 0, livello1 = 0, senzaFornitore = 0, usate = 0, scartate = 0;
        var storico = new List<OsservazioneStorico>();

        foreach (var doc in documenti)
        {
            ct.ThrowIfCancellationRequested();

            var bolla = Leggi(doc);
            if (bolla is null || RigheDoc.TipoDocumento(bolla) is not { } tipo) { illeggibili++; continue; }
            if (docLivello1.Contains(doc.id)) { livello1++; continue; }
            // Anche i documenti mai passati dal livello 1 (storico precedente al modulo) devono quadrare.
            if (_opzioni.RichiediQuadratura && !quadraturaIgnorata.Contains(doc.id) && !RigheDoc.DocumentoQuadra(bolla))
            {
                nonQuadrano++;
                continue;
            }

            var idFornitore = risolutore.Fornitore(bolla.Mittente);
            if (idFornitore == 0) { senzaFornitore++; continue; }

            var data = RigheDoc.DataDocumento(bolla) ?? DateOnly.FromDateTime(doc.data_creazione);
            if (data < dataMinima) continue;

            foreach (var riga in bolla.Articoli ?? [])
            {
                var prezzo = RigheDoc.PrezzoUnitario(riga);
                var codice = risolutore.ChiaveArticolo(idFornitore, riga.Codice);

                if (prezzo is null || codice.Length == 0
                    || righeConfermate.Contains((doc.id, riga.Id_Riga))
                    || righeLivello1.Contains((doc.id, riga.Id_Riga)))
                {
                    scartate++;
                    continue;
                }

                storico.Add(new OsservazioneStorico(
                    idFornitore, codice, tipo, RigheDoc.NormalizzaUm(riga.UnitaMisura),
                    riga.Quantita, prezzo.Value, data));
                usate++;
            }
        }

        var baseline = CostruttoreBaseline.Costruisci(tenant, storico);
        await _repo.SostituisciBaselineAsync(tenant, baseline, ct);

        cronometro.Stop();
        var esito = new RisultatoBaseline(tenant, documenti.Count, illeggibili, nonQuadrano, livello1,
            senzaFornitore, usate, scartate, baseline.Count, cronometro.ElapsedMilliseconds);

        _logger.LogInformation("Baseline anomalie {@Esito}", esito);
        return esito;
    }

    // ------------------------------------------------------------------
    // Analisi del singolo documento (livello 1, poi livello 2)
    // ------------------------------------------------------------------

    public async Task<IReadOnlyList<EmmaAnomalia>?> AnalizzaDocumentoAsync(int docId, string? tenantAtteso = null, CancellationToken ct = default)
    {
        var doc = await _repo.GetDocumentoAsync(docId, ct);
        if (doc is null) return null;
        if (tenantAtteso is not null && !string.Equals(doc.tenant, tenantAtteso, StringComparison.Ordinal)) return null;

        var tenant = doc.tenant;
        var avvisi = new List<EmmaAnomalia>();

        var bolla = Leggi(doc);
        if (bolla is null || RigheDoc.TipoDocumento(bolla) is not { } tipo) return avvisi;

        var fornitori = await _repo.GetFornitoriAsync(tenant, ct);
        var idFornitore = new RisolutoreAnagrafiche(fornitori, []).Fornitore(bolla.Mittente);

        // ---------------- Livello 1: regole sul documento ----------------
        var livello1 = await AnalizzaLivello1Async(doc, bolla, idFornitore, ct);
        avvisi.AddRange(livello1);

        // Lo stato salvato conta piu' dell'esito appena calcolato: un avviso che l'operatore ha
        // ignorato non blocca, uno aperto o confermato si'.
        var statoL1 = await _repo.GetAvvisiLivello1Async(tenant, docId, ct);
        bool documentoBloccato = statoL1.Any(a => a.stato != 2 && BloccaDocumento(a.tipo));
        var righeBloccate = statoL1
            .Where(a => a.stato != 2 && a.id_riga is not null && TipoAnomalia.BloccantiRiga.Contains(a.tipo))
            .Select(a => a.id_riga!)
            .ToHashSet();

        // ---------------- Livello 2: confronto con lo storico ----------------
        if (documentoBloccato)
        {
            // Prima si corregge l'estrazione, poi si parla di prezzi.
            _logger.LogInformation("Documento {DocId}: bloccato dal livello 1, livello 2 saltato", docId);
            await _repo.SostituisciAnomalieAsync(docId, ValutatoreLivello2.Livello, [], ct);
            return avvisi;
        }

        if (idFornitore == 0)
        {
            _logger.LogInformation("Documento {DocId}: mittente '{Mittente}' senza anagrafica, livello 2 saltato",
                docId, bolla.Mittente);
            return avvisi;
        }

        var risolutore = new RisolutoreAnagrafiche(fornitori, await _repo.GetArticoliAsync(tenant, idFornitore, ct));

        var righe = new List<(ArticoloBolla Originale, RigaDaValutare Riga)>();
        foreach (var r in bolla.Articoli ?? [])
        {
            if (righeBloccate.Contains(r.Id_Riga)) continue;

            var prezzo = RigheDoc.PrezzoUnitario(r);
            var codice = risolutore.ChiaveArticolo(idFornitore, r.Codice);
            if (prezzo is null || codice.Length == 0) continue; // gia' segnalato dal livello 1

            righe.Add((r, new RigaDaValutare(r.Id_Riga, codice, RigheDoc.NormalizzaUm(r.UnitaMisura),
                r.Quantita, prezzo.Value)));
        }

        var baseline = (await _repo.GetBaselineAsync(tenant, idFornitore, tipo,
                righe.Select(x => x.Riga.Codice).Distinct().ToList(), ct))
            .ToDictionary(x => x.codice);

        var nomeDocumenti = tipo == RigheDoc.TipoFattura ? "fatture" : "bolle";
        var livello2 = new List<EmmaAnomalia>();

        foreach (var (originale, riga) in righe)
        {
            baseline.TryGetValue(riga.Codice, out var b);
            foreach (var a in ValutatoreLivello2.Valuta(riga, b, _opzioni, nomeDocumenti))
            {
                Completa(a, doc, bolla, originale.Id_Master, idFornitore, ValutatoreLivello2.Livello, ValutatoreLivello2.Versione);
                a.id_riga = originale.Id_Riga;
                a.codice = riga.Codice;
                livello2.Add(a);
            }
        }

        await _repo.SostituisciAnomalieAsync(docId, ValutatoreLivello2.Livello, livello2, ct);
        avvisi.AddRange(livello2);
        return avvisi;
    }

    private async Task<List<EmmaAnomalia>> AnalizzaLivello1Async(DocumentoAnomalie doc, DatiBolla bolla, int idFornitore,
        CancellationToken ct)
    {
        var avvisi = ValutatoreLivello1.Valuta(bolla, DateOnly.FromDateTime(DateTime.Now), _opzioni.MesiDataMassima);

        if (!string.IsNullOrWhiteSpace(bolla.Mittente))
        {
            var altri = await _repo.GetDocumentiStessoMittenteAsync(doc.tenant, bolla.Mittente, bolla.TipoDocumento,
                doc.id, DateTime.UtcNow.AddMonths(-_opzioni.MesiDuplicati), ct);

            var simili = altri
                .Select(a => (Id: a.id, Bolla: Leggi(a)))
                .Where(x => x.Bolla is not null)
                .Select(x => new DocumentoSimile(x.Id, x.Bolla!.NumeroBolla ?? string.Empty,
                    x.Bolla.DataBolla ?? string.Empty, ValutatoreLivello1.ImprontaContenuto(x.Bolla) ?? string.Empty))
                .OrderBy(x => x.Id);

            if (ValutatoreLivello1.Duplicato(bolla, simili) is { } duplicato)
                avvisi.Add(duplicato);
        }

        foreach (var a in avvisi)
            Completa(a, doc, bolla, idMasterRiga: null, idFornitore, ValutatoreLivello1.Livello, ValutatoreLivello1.Versione);

        await _repo.SostituisciAnomalieAsync(doc.id, ValutatoreLivello1.Livello, avvisi, ct);
        return avvisi;
    }

    private static void Completa(EmmaAnomalia a, DocumentoAnomalie doc, DatiBolla bolla, string? idMasterRiga,
        int idFornitore, short livello, string versione)
    {
        a.tenant = doc.tenant;
        a.doc_id = doc.id;
        a.id_master = string.IsNullOrEmpty(idMasterRiga) ? bolla.Id : idMasterRiga;
        a.id_fornitore = idFornitore == 0 ? null : idFornitore;
        a.livello = livello;
        a.modello_ver = versione;
    }

    /// <summary>Quadratura e duplicati bloccano il documento; la quadratura solo se richiesta in configurazione.</summary>
    private bool BloccaDocumento(string tipo)
        => TipoAnomalia.BloccantiDocumento.Contains(tipo)
           && (tipo != TipoAnomalia.IncoerenzaTotali || _opzioni.RichiediQuadratura);

    public Task<IReadOnlyList<EmmaAnomalia>> GetAnomalieDocumentoAsync(string tenant, int docId, CancellationToken ct = default)
        => _repo.GetAnomalieDocumentoAsync(tenant, docId, ct);

    // ------------------------------------------------------------------
    // Lista, feedback, statistiche
    // ------------------------------------------------------------------

    public const int LimiteMassimoPagina = 500;

    /// <summary>Sotto questa precisione l'operatore smette di guardare la lista (docs/modulo-anomalie.md, par. 8).</summary>
    public const double SogliaPrecisione = 0.30;

    /// <summary>Feedback minimi per considerare affidabile una precisione.</summary>
    public const int FeedbackMinimiPerSoglia = 10;

    public async Task<PaginaAnomalie> GetAnomalieAsync(string tenant, FiltriAnomalie filtri, CancellationToken ct = default)
    {
        NormalizzaFiltri(filtri);
        var (totale, righe) = await _repo.GetAnomalieAsync(tenant, filtri, ct);
        return new PaginaAnomalie { Totale = totale, Limit = filtri.Limit, Offset = filtri.Offset, Righe = righe };
    }

    public async Task<EmmaAnomalia?> RegistraFeedbackAsync(string tenant, long id, FeedbackAnomalia feedback, string? utente,
        CancellationToken ct = default)
    {
        if (feedback.Stato is < 0 or > 2)
            throw new ArgumentException("stato deve essere 1 (confermata), 2 (ignorata) oppure 0 (riapri).");

        var note = string.IsNullOrWhiteSpace(feedback.Note) ? null : feedback.Note.Trim();
        if (note is { Length: > 2000 })
            throw new ArgumentException("note: massimo 2000 caratteri.");

        var anomalia = await _repo.GetAnomaliaAsync(tenant, id, ct);
        if (anomalia is null) return null;

        var utenteFeedback = string.IsNullOrWhiteSpace(utente) ? null : utente.Trim();
        if (utenteFeedback is { Length: > 100 }) utenteFeedback = utenteFeedback[..100];

        if (!await _repo.AggiornaFeedbackAsync(tenant, id, feedback.Stato, utenteFeedback, note, ct)) return null;

        // Il feedback su un avviso bloccante di livello 1 cambia cosa passa al livello 2:
        // "ignorato" su una quadratura vuol dire che il documento e' buono e i prezzi vanno confrontati.
        if (anomalia.livello == ValutatoreLivello1.Livello && anomalia.stato != feedback.Stato &&
            (TipoAnomalia.BloccantiDocumento.Contains(anomalia.tipo) || TipoAnomalia.BloccantiRiga.Contains(anomalia.tipo)))
        {
            try
            {
                await AnalizzaDocumentoAsync(anomalia.doc_id, tenant, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Il feedback e' salvato comunque: la rianalisi si puo' rilanciare a mano.
                _logger.LogWarning(ex, "Rianalisi del documento {DocId} dopo il feedback non riuscita", anomalia.doc_id);
            }
        }

        // Se l'avviso e' stato riaperto (stato 0) la rianalisi lo riscrive con un nuovo id:
        // in quel caso si restituisce la copia aggiornata in memoria.
        return await _repo.GetAnomaliaAsync(tenant, id, ct) ?? ConFeedback(anomalia, feedback.Stato, utenteFeedback, note);
    }

    private static EmmaAnomalia ConFeedback(EmmaAnomalia a, short stato, string? utente, string? note)
    {
        a.stato = stato;
        a.utente_feedback = stato == 0 ? null : utente;
        a.data_feedback = stato == 0 ? null : DateTime.UtcNow;
        a.note = note;
        return a;
    }

    public async Task<StatisticheAnomalie> GetStatisticheAsync(string tenant, DateTime? da, DateTime? a, short? livello,
        CancellationToken ct = default)
    {
        var fine = FineGiornata(a);
        var perTipo = await _repo.GetStatisticheAsync(tenant, da, fine, livello, ct);

        var complessivo = new StatisticaAnomalie
        {
            Tipo = "totale",
            Livello = livello,
            Totali = perTipo.Sum(s => s.Totali),
            Aperte = perTipo.Sum(s => s.Aperte),
            Confermate = perTipo.Sum(s => s.Confermate),
            Ignorate = perTipo.Sum(s => s.Ignorate),
        };

        foreach (var s in perTipo.Append(complessivo)) CalcolaPrecisione(s);

        return new StatisticheAnomalie
        {
            Da = da,
            A = a,
            SogliaPrecisione = SogliaPrecisione,
            Complessivo = complessivo,
            PerTipo = perTipo,
        };
    }

    /// <summary>precisione = confermate / (confermate + ignorate).</summary>
    public static void CalcolaPrecisione(StatisticaAnomalie s)
    {
        var valutate = s.Confermate + s.Ignorate;
        s.Precisione = valutate == 0 ? null : Math.Round((double)s.Confermate / valutate, 4);
        s.SottoSoglia = valutate >= FeedbackMinimiPerSoglia && s.Precisione < SogliaPrecisione;
    }

    public static void NormalizzaFiltri(FiltriAnomalie filtri)
    {
        if (filtri.Stato is < 0 or > 2) throw new ArgumentException("stato deve essere 0, 1 o 2.");
        if (filtri.Severita is < 0 or > 3) throw new ArgumentException("severita deve essere fra 0 e 3.");
        if (filtri.Da is { } da && filtri.A is { } a && a < da) throw new ArgumentException("'a' precede 'da'.");

        filtri.Limit = filtri.Limit <= 0 ? 100 : Math.Min(filtri.Limit, LimiteMassimoPagina);
        filtri.Offset = Math.Max(filtri.Offset, 0);
        filtri.A = FineGiornata(filtri.A);
    }

    /// <summary>"a=2026-09-15" deve includere tutto il 15: si confronta con &lt; 16 settembre.</summary>
    internal static DateTime? FineGiornata(DateTime? a)
        => a is { } d && d.TimeOfDay == TimeSpan.Zero ? d.AddDays(1) : a;

    private DatiBolla? Leggi(DocumentoAnomalie doc)
    {
        if (string.IsNullOrWhiteSpace(doc.documento)) return null;
        try
        {
            return JsonSerializer.Deserialize<DatiBolla>(doc.documento, OpzioniJson);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning("Documento {DocId} non leggibile per le anomalie: {Errore}", doc.id, ex.Message);
            return null;
        }
    }
}
