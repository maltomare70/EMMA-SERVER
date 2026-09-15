using System.Globalization;
using EmmaServer.Entities;

namespace EmmaServer.Services.Anomalie;

/// <summary>Configurazione del modulo, sezione "Anomalie" di appsettings.</summary>
public sealed class OpzioniAnomalie
{
    /// <summary>Attiva il ricalcolo notturno della baseline.</summary>
    public bool BaselineNotturna { get; set; } = true;

    /// <summary>Ora (UTC) del ricalcolo notturno. 1 = le 3 di notte in Italia con l'ora legale.</summary>
    public int OraUtc { get; set; } = 1;

    /// <summary>Profondita' dello storico usato per la baseline.</summary>
    public int MesiStorico { get; set; } = 24;

    /// <summary>Sotto questo numero di osservazioni il livello 2 non si pronuncia.</summary>
    public int MinOsservazioni { get; set; } = 5;

    /// <summary>|z robusto| oltre il quale la riga e' sospetta.</summary>
    public double SogliaZ { get; set; } = 3.5;

    /// <summary>Con MAD = 0 (prezzo di listino fisso) basta questo scostamento relativo.</summary>
    public double SogliaPrezzoFisso { get; set; } = 0.01;

    /// <summary>
    /// Scostamento relativo minimo per segnalare un prezzo anche quando lo z supera la soglia:
    /// con MAD piccolissimi uno z di 4 puo' voler dire mezzo centesimo.
    /// </summary>
    public double ScostamentoMinimoPrezzo { get; set; } = 0.02;

    /// <summary>Con quantita' sempre identica (MAD = 0) si segnala oltre questo rapporto (x3 o /3).</summary>
    public double RapportoQtaFissa { get; set; } = 3.0;

    /// <summary>
    /// Se true, i documenti che non quadrano non entrano nella baseline e non generano avvisi di livello 2.
    /// L'avviso di livello 1 "incoerenza_totali" viene scritto comunque.
    /// </summary>
    public bool RichiediQuadratura { get; set; } = true;

    /// <summary>Livello 1: una data documento piu' vecchia di cosi' viene segnalata.</summary>
    public int MesiDataMassima { get; set; } = 18;

    /// <summary>Livello 1: finestra (su data di caricamento) in cui si cercano i duplicati di contenuto.</summary>
    public int MesiDuplicati { get; set; } = 12;
}

/// <summary>Una riga del documento in esame, gia' normalizzata.</summary>
public sealed record RigaDaValutare(
    string IdRiga,
    string Codice,
    string Um,
    decimal Quantita,
    decimal PrezzoUnitario);

/// <summary>
/// Livello 2: z robusto su mediana e MAD per (fornitore, articolo, tipo documento).
/// Restituisce avvisi parziali (tipo, punteggi, messaggio): tenant, documento e fornitore li completa il servizio.
/// </summary>
public static class ValutatoreLivello2
{
    public const string Versione = "stat-v1";
    public const short Livello = 2;

    public static List<EmmaAnomalia> Valuta(
        RigaDaValutare riga, EmmaAnomaliaBaseline? baseline, OpzioniAnomalie opzioni, string nomeDocumenti = "bolle")
    {
        var avvisi = new List<EmmaAnomalia>();

        // n < minimo: niente livello 2, se ne occupera' il 3a.
        if (baseline is null || baseline.n < opzioni.MinOsservazioni || baseline.mediana_prezzo is not { } medianaDec || medianaDec <= 0)
            return avvisi;

        var n = baseline.n;
        var umAbituale = baseline.um_prevalente ?? string.Empty;

        // Cambio di unita' di misura: prima causa di falso allarme sul prezzo.
        // Il confronto si SOSPENDE e si emette un avviso dedicato.
        if (umAbituale.Length > 0 && riga.Um.Length > 0 && riga.Um != umAbituale)
        {
            avvisi.Add(new EmmaAnomalia
            {
                tipo = TipoAnomalia.UmCambiata,
                severita = 1,
                valore = riga.PrezzoUnitario,
                atteso = medianaDec,
                messaggio = $"{riga.Codice} — unità di misura {riga.Um} diversa da quella abituale {umAbituale} " +
                            $"(su {n} {nomeDocumenti}): confronto di prezzo e quantità sospeso",
            });
            return avvisi;
        }

        var um = umAbituale.Length > 0 ? umAbituale : riga.Um;

        if (ValutaPrezzo(riga, baseline, (double)medianaDec, opzioni, n, um, nomeDocumenti) is { } avvisoPrezzo)
            avvisi.Add(avvisoPrezzo);

        if (ValutaQuantita(riga, baseline, opzioni, n, um, nomeDocumenti) is { } avvisoQta)
            avvisi.Add(avvisoQta);

        return avvisi;
    }

    private static EmmaAnomalia? ValutaPrezzo(
        RigaDaValutare riga, EmmaAnomaliaBaseline b, double mediana, OpzioniAnomalie o, int n, string um, string nomeDoc)
    {
        var prezzo = (double)riga.PrezzoUnitario;
        var mad = (double)(b.mad_prezzo ?? 0m);
        var delta = (prezzo - mediana) / mediana;

        double? z = null;
        string messaggio;

        if (StatisticaRobusta.MadNullo(mad, mediana))
        {
            // Prezzo sempre identico: la formula dividerebbe per zero.
            if (Math.Abs(delta) <= o.SogliaPrezzoFisso) return null;

            messaggio = $"{riga.Codice} — prezzo fisso a {FormatoIt.Importo(mediana)} €/{um} sulle ultime {n} {nomeDoc}, " +
                        $"questa riporta {FormatoIt.Importo(prezzo)} €/{um} ({FormatoIt.Percentuale(delta)})";
        }
        else
        {
            z = StatisticaRobusta.ZRobusto(prezzo, mediana, mad);
            if (Math.Abs(z.Value) <= o.SogliaZ || Math.Abs(delta) < o.ScostamentoMinimoPrezzo) return null;

            messaggio = $"{riga.Codice} — mediana {FormatoIt.Importo(mediana)} €/{um} su {n} {nomeDoc}, " +
                        $"questa {FormatoIt.Importo(prezzo)} €/{um} ({FormatoIt.Percentuale(delta)})";
        }

        return new EmmaAnomalia
        {
            tipo = delta > 0 ? TipoAnomalia.PrezzoAlto : TipoAnomalia.PrezzoBasso,
            score = z,
            severita = SeveritaPrezzo(delta),
            valore = riga.PrezzoUnitario,
            atteso = (decimal)mediana,
            delta_perc = CostruttoreBaseline.ToDecimal(delta * 100),
            messaggio = messaggio,
        };
    }

    private static EmmaAnomalia? ValutaQuantita(
        RigaDaValutare riga, EmmaAnomaliaBaseline b, OpzioniAnomalie o, int n, string um, string nomeDoc)
    {
        if (b.mediana_qta is not { } medLogDec || riga.Quantita <= 0) return null;

        var medLog = (double)medLogDec;
        var madLog = (double)(b.mad_qta ?? 0m);
        var logQta = Math.Log((double)riga.Quantita);

        double? z = null;
        if (StatisticaRobusta.MadNullo(madLog, 1.0))
        {
            if (Math.Abs(logQta - medLog) <= Math.Log(o.RapportoQtaFissa)) return null;
        }
        else
        {
            z = StatisticaRobusta.ZRobusto(logQta, medLog, madLog);
            if (Math.Abs(z.Value) <= o.SogliaZ) return null;
        }

        var qtaAbituale = Math.Exp(medLog);
        var delta = ((double)riga.Quantita - qtaAbituale) / qtaAbituale;

        return new EmmaAnomalia
        {
            tipo = TipoAnomalia.QtaAnomala,
            score = z,
            // Una quantita' insolita e' raramente un errore costoso: severita' bassa salvo casi estremi.
            severita = (short)(z is { } zz && Math.Abs(zz) > 10 ? 2 : 1),
            valore = riga.Quantita,
            atteso = CostruttoreBaseline.ToDecimal(qtaAbituale),
            delta_perc = CostruttoreBaseline.ToDecimal(delta * 100),
            messaggio = $"{riga.Codice} — quantità abituale {FormatoIt.Quantita(qtaAbituale)} {um} su {n} {nomeDoc}, " +
                        $"questa {FormatoIt.Quantita((double)riga.Quantita)} {um} ({FormatoIt.Percentuale(delta)})",
        };
    }

    internal static short SeveritaPrezzo(double delta)
    {
        var d = Math.Abs(delta);
        return d < 0.10 ? (short)1 : d < 0.30 ? (short)2 : (short)3;
    }
}

/// <summary>
/// Numeri all'italiana senza dipendere da CultureInfo("it-IT"): nel container Linux
/// la globalizzazione invariante farebbe fallire la creazione della cultura.
/// </summary>
internal static class FormatoIt
{
    private static readonly NumberFormatInfo Formato = Crea();

    private static NumberFormatInfo Crea()
    {
        var f = (NumberFormatInfo)CultureInfo.InvariantCulture.NumberFormat.Clone();
        f.NumberDecimalSeparator = ",";
        f.NumberGroupSeparator = ".";
        return f;
    }

    public static string Importo(double v) => v.ToString("#,##0.00##", Formato);

    public static string Quantita(double v) => v.ToString("#,##0.###", Formato);

    public static string Percentuale(double frazione)
        => (frazione >= 0 ? "+" : "") + (frazione * 100).ToString("0.#", Formato) + "%";
}
