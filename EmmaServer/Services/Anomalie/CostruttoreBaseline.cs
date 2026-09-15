using EmmaServer.Entities;

namespace EmmaServer.Services.Anomalie;

/// <summary>Una riga dello storico gia' ripulita e agganciata a fornitore e articolo.</summary>
public sealed record OsservazioneStorico(
    int IdFornitore,
    string Codice,
    short TipoDoc,
    string Um,
    decimal Quantita,
    decimal PrezzoUnitario,
    DateOnly Data);

/// <summary>
/// Calcola le righe di <c>anomalie_baseline</c> a partire dallo storico. Nessun accesso al database.
/// </summary>
public static class CostruttoreBaseline
{
    public static List<EmmaAnomaliaBaseline> Costruisci(string tenant, IEnumerable<OsservazioneStorico> storico)
    {
        var risultato = new List<EmmaAnomaliaBaseline>();

        foreach (var gruppo in storico.GroupBy(o => (o.IdFornitore, o.Codice, o.TipoDoc)))
        {
            // UM prevalente; a parita' vince quella usata piu' di recente.
            var umPrevalente = gruppo
                .GroupBy(o => o.Um)
                .OrderByDescending(g => g.Count())
                .ThenByDescending(g => g.Max(o => o.Data))
                .First().Key;

            // Il prezzo si confronta solo nella stessa unita' di misura:
            // le righe in PZ non entrano nella mediana di un articolo che di solito arriva in CT.
            var valide = gruppo.Where(o => o.Um == umPrevalente).ToList();

            var prezzi = valide.Select(o => (double)o.PrezzoUnitario).ToArray();
            var medianaPrezzo = StatisticaRobusta.Mediana(prezzi);
            var madPrezzo = StatisticaRobusta.Mad(prezzi, medianaPrezzo);

            // Quantita' su scala logaritmica: tanti ordini piccoli e pochi grandi.
            var logQta = valide.Select(o => Math.Log((double)o.Quantita)).ToArray();
            var medianaLogQta = StatisticaRobusta.Mediana(logQta);
            var madLogQta = StatisticaRobusta.Mad(logQta, medianaLogQta);

            risultato.Add(new EmmaAnomaliaBaseline
            {
                tenant = tenant,
                id_fornitore = gruppo.Key.IdFornitore,
                codice = gruppo.Key.Codice,
                tipo_doc = gruppo.Key.TipoDoc,
                n = valide.Count,
                mediana_prezzo = ToDecimal(medianaPrezzo),
                mad_prezzo = ToDecimal(madPrezzo),
                mediana_qta = ToDecimal(medianaLogQta),
                mad_qta = ToDecimal(madLogQta),
                um_prevalente = umPrevalente,
                primo_visto = gruppo.Min(o => o.Data).ToDateTime(TimeOnly.MinValue),
                ultimo_visto = gruppo.Max(o => o.Data).ToDateTime(TimeOnly.MinValue),
            });
        }

        return risultato;
    }

    internal static decimal? ToDecimal(double v)
        => double.IsFinite(v) && Math.Abs(v) < 1e15 ? (decimal)Math.Round(v, 6) : null;
}
