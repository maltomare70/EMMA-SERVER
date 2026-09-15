using FuzzySharp;

namespace EmmaServer.Services.Anomalie;

public sealed class FornitoreRif
{
    public int id { get; set; }
    public string descrizione { get; set; } = string.Empty;
}

public sealed class ArticoloRif
{
    public int idfornitore { get; set; }
    public string codice { get; set; } = string.Empty;
    public string? rifcodice { get; set; }
}

/// <summary>
/// Aggancia un documento al fornitore e le righe all'articolo, in SOLA LETTURA.
///
/// Stessa regola di FornitoriService.AddOrUpdateFornitoriByDocIdAsync (uguaglianza case-insensitive,
/// poi FuzzySharp con punteggio >= 90), cosi' baseline e analisi finiscono sullo stesso id.
/// Non crea mai anagrafiche: quello resta compito dell'import.
/// </summary>
public sealed class RisolutoreAnagrafiche
{
    private const int PunteggioMinimo = 90;

    private readonly List<FornitoreRif> _fornitori;
    private readonly List<string> _nomi;
    private readonly Dictionary<string, int> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(int, string), string> _rifCodici = new();

    public RisolutoreAnagrafiche(IEnumerable<FornitoreRif> fornitori, IEnumerable<ArticoloRif> articoli)
    {
        _fornitori = fornitori.Where(f => !string.IsNullOrWhiteSpace(f.descrizione)).ToList();
        _nomi = _fornitori.Select(f => f.descrizione).ToList();

        foreach (var a in articoli)
        {
            var rif = RigheDocumento.NormalizzaCodice(a.rifcodice);
            if (rif.Length == 0) continue;
            _rifCodici[(a.idfornitore, RigheDocumento.NormalizzaCodice(a.codice))] = rif;
        }
    }

    /// <summary>Id del fornitore, oppure 0 se il mittente non si aggancia a nessuna anagrafica.</summary>
    public int Fornitore(string? mittente)
    {
        var nome = mittente?.Trim();
        if (string.IsNullOrEmpty(nome) || _fornitori.Count == 0) return 0;
        if (_cache.TryGetValue(nome, out var inCache)) return inCache;

        var id = _fornitori
            .FirstOrDefault(f => f.descrizione.Trim().Equals(nome, StringComparison.InvariantCultureIgnoreCase))?.id ?? 0;

        if (id == 0)
        {
            var migliore = Process.ExtractOne(nome, _nomi);
            if (migliore is not null && migliore.Score >= PunteggioMinimo)
                id = _fornitori[migliore.Index].id;
        }

        _cache[nome] = id;
        return id;
    }

    /// <summary>
    /// Chiave dell'articolo nello storico: <c>articoli.rifcodice</c> se l'operatore l'ha valorizzato,
    /// altrimenti il codice del fornitore. Cosi' i codici che l'operatore ha gia' ricondotto
    /// allo stesso articolo confluiscono in una serie sola.
    /// </summary>
    public string ChiaveArticolo(int idFornitore, string? codice)
    {
        var c = RigheDocumento.NormalizzaCodice(codice);
        if (c.Length == 0) return c;
        return _rifCodici.TryGetValue((idFornitore, c), out var rif) ? rif : c;
    }
}
