using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using EmmaServer.Entities;
using EmmaServer.Entities.Dtos;

namespace EmmaServer.Services.Anomalie;

/// <summary>Un documento dello stesso mittente, per il controllo dei duplicati di contenuto.</summary>
public sealed record DocumentoSimile(int Id, string NumeroBolla, string DataBolla, string Impronta);

/// <summary>
/// Livello 1 (docs/modulo-anomalie.md, par. 3): regole deterministiche sul documento appena importato,
/// senza storico. Oltre a segnalare, fa da guardia ai livelli successivi: un documento che non quadra
/// non genera avvisi di prezzo e non entra nella baseline.
///
/// Restituisce avvisi parziali: tenant, documento, fornitore e livello li completa il servizio.
/// </summary>
public static class ValutatoreLivello1
{
    public const string Versione = "regole-v1";
    public const short Livello = 1;

    public static readonly IReadOnlySet<decimal> AliquoteValide = new HashSet<decimal> { 0m, 4m, 5m, 10m, 22m };

    public static List<EmmaAnomalia> Valuta(DatiBolla bolla, DateOnly oggi, int mesiDataMassima = 18)
    {
        var avvisi = new List<EmmaAnomalia>();
        var righe = bolla.Articoli ?? [];
        var fattura = RigheDocumento.TipoDocumento(bolla) == RigheDocumento.TipoFattura;

        ControllaQuadratura(bolla, righe, avvisi);
        ControllaData(bolla, oggi, mesiDataMassima, avvisi);

        // DDT senza prezzi (caso frequente): se nessuna riga ha un importo, l'assenza di prezzo e' normale.
        bool documentoConPrezzi = righe.Any(r => r.Imponibile > 0 || r.Totale > 0);

        foreach (var r in righe)
        {
            ControllaValori(r, fattura, documentoConPrezzi, avvisi);
            ControllaRiga(r, avvisi);
            ControllaAliquota(r, avvisi);
        }

        ControllaRigheGemelle(righe, avvisi);
        return avvisi;
    }

    // ------------------------------------------------------------------ quadratura documento

    private static void ControllaQuadratura(DatiBolla bolla, List<ArticoloBolla> righe, List<EmmaAnomalia> avvisi)
    {
        if (righe.Count == 0)
        {
            avvisi.Add(new EmmaAnomalia
            {
                tipo = TipoAnomalia.IncoerenzaTotali,
                severita = 3,
                messaggio = "Documento senza righe articolo: l'estrazione va verificata",
            });
            return;
        }

        if (RigheDocumento.DocumentoQuadra(bolla)) return;

        var somma = righe.Sum(r => r.Totale);
        var totale = bolla.Totale > 0 ? bolla.Totale : bolla.Imponibile;
        var differenza = (double)somma - totale;

        avvisi.Add(new EmmaAnomalia
        {
            tipo = TipoAnomalia.IncoerenzaTotali,
            severita = 3,
            valore = somma,
            atteso = CostruttoreBaseline.ToDecimal(totale),
            delta_perc = totale != 0 ? CostruttoreBaseline.ToDecimal(differenza / totale * 100) : null,
            messaggio = $"Totali non quadrano: somma righe {FormatoIt.Importo((double)somma)} €, " +
                        $"totale documento {FormatoIt.Importo(totale)} € (differenza {FormatoIt.Importo(differenza)} €). " +
                        "I prezzi di questo documento non vengono confrontati con lo storico",
        });
    }

    // ------------------------------------------------------------------ data

    private static void ControllaData(DatiBolla bolla, DateOnly oggi, int mesiMassimi, List<EmmaAnomalia> avvisi)
    {
        var data = RigheDocumento.DataDocumento(bolla);

        if (data is null)
        {
            avvisi.Add(new EmmaAnomalia
            {
                tipo = TipoAnomalia.DataAnomala,
                severita = 2,
                messaggio = string.IsNullOrWhiteSpace(bolla.DataBolla)
                    ? "Data del documento mancante"
                    : $"Data del documento non leggibile: '{bolla.DataBolla}'",
            });
            return;
        }

        // Un giorno di tolleranza per i fusi orari.
        if (data.Value > oggi.AddDays(1))
        {
            avvisi.Add(new EmmaAnomalia
            {
                tipo = TipoAnomalia.DataAnomala,
                severita = 2,
                messaggio = $"Data del documento nel futuro: {Data(data.Value)}",
            });
        }
        else if (data.Value < oggi.AddMonths(-mesiMassimi))
        {
            avvisi.Add(new EmmaAnomalia
            {
                tipo = TipoAnomalia.DataAnomala,
                severita = 1,
                messaggio = $"Data del documento più vecchia di {mesiMassimi} mesi: {Data(data.Value)}",
            });
        }
    }

    // ------------------------------------------------------------------ righe

    private static void ControllaValori(ArticoloBolla r, bool fattura, bool documentoConPrezzi, List<EmmaAnomalia> avvisi)
    {
        var problemi = new List<string>();
        short severita = 1;

        if (r.Quantita <= 0)
        {
            problemi.Add($"quantità {FormatoIt.Quantita((double)r.Quantita)}");
            severita = 2;
        }
        else if (documentoConPrezzi && RigheDocumento.PrezzoUnitario(r) is null)
        {
            problemi.Add("prezzo mancante o non positivo");
            // In fattura una riga a zero e' spesso un omaggio o una riga descrittiva.
            if (!fattura) severita = 2;
        }

        // In FatturaPA codice articolo e unita' di misura sono facoltativi (spese, righe descrittive).
        if (!fattura)
        {
            if (string.IsNullOrWhiteSpace(r.Codice)) problemi.Add("codice articolo vuoto");
            if (string.IsNullOrWhiteSpace(r.UnitaMisura)) problemi.Add("unità di misura vuota");
        }

        if (problemi.Count == 0) return;

        avvisi.Add(new EmmaAnomalia
        {
            tipo = TipoAnomalia.ValoreImpossibile,
            severita = severita,
            id_riga = r.Id_Riga,
            codice = NullSeVuoto(r.Codice),
            messaggio = $"{Etichetta(r)} — {string.Join(", ", problemi)}",
        });
    }

    /// <summary>
    /// Quadratura della riga. Il significato di "imponibile" nelle righe estratte non e' certo
    /// (importo di riga o prezzo unitario, con o senza IVA): la riga e' coerente se il totale torna
    /// con almeno una delle quattro letture. Se non torna con nessuna, l'estrazione e' sbagliata.
    /// </summary>
    private static void ControllaRiga(ArticoloBolla r, List<EmmaAnomalia> avvisi)
    {
        if (r.Imponibile <= 0 || r.Totale <= 0 || r.Quantita <= 0) return;

        var fattoreIva = 1 + RigheDocumento.LeggiAliquota(r.Iva) / 100m;
        var tolleranza = 0.01m + r.Totale * 0.005m;

        decimal[] candidati =
        [
            r.Imponibile,
            r.Imponibile * fattoreIva,
            r.Imponibile * r.Quantita,
            r.Imponibile * r.Quantita * fattoreIva,
        ];

        if (candidati.Any(c => Math.Abs(c - r.Totale) <= tolleranza)) return;

        var atteso = candidati.MinBy(c => Math.Abs(c - r.Totale));
        avvisi.Add(new EmmaAnomalia
        {
            tipo = TipoAnomalia.RigaIncoerente,
            severita = 2,
            id_riga = r.Id_Riga,
            codice = NullSeVuoto(r.Codice),
            valore = r.Totale,
            atteso = atteso,
            delta_perc = atteso != 0 ? Math.Round((r.Totale - atteso) / atteso * 100, 4) : null,
            messaggio = $"{Etichetta(r)} — totale riga {FormatoIt.Importo((double)r.Totale)} € non torna con " +
                        $"imponibile {FormatoIt.Importo((double)r.Imponibile)} € e quantità {FormatoIt.Quantita((double)r.Quantita)} " +
                        "(né con né senza IVA)",
        });
    }

    private static void ControllaAliquota(ArticoloBolla r, List<EmmaAnomalia> avvisi)
    {
        if (string.IsNullOrWhiteSpace(r.Iva)) return;

        var aliquota = RigheDocumento.LeggiNumero(r.Iva);
        if (aliquota is { } a && AliquoteValide.Contains(decimal.Round(a, 2))) return;

        avvisi.Add(new EmmaAnomalia
        {
            tipo = TipoAnomalia.AliquotaIva,
            severita = 1,
            id_riga = r.Id_Riga,
            codice = NullSeVuoto(r.Codice),
            valore = aliquota,
            messaggio = $"{Etichetta(r)} — aliquota IVA '{r.Iva.Trim()}' non tra quelle ammesse (0, 4, 5, 10, 22)",
        });
    }

    private static void ControllaRigheGemelle(List<ArticoloBolla> righe, List<EmmaAnomalia> avvisi)
    {
        var gruppi = righe
            .Where(r => !string.IsNullOrWhiteSpace(r.Codice))
            .GroupBy(r => RigheDocumento.NormalizzaCodice(r.Codice))
            .Where(g => g.Count() > 1 && g.Select(r => r.Quantita).Distinct().Count() > 1);

        foreach (var g in gruppi)
        {
            var quantita = string.Join(", ", g.Select(r => FormatoIt.Quantita((double)r.Quantita)));
            // Un avviso per ogni ripetizione, agganciato alla riga: la prima occorrenza e' il riferimento.
            foreach (var r in g.Skip(1))
            {
                avvisi.Add(new EmmaAnomalia
                {
                    tipo = TipoAnomalia.RigheGemelle,
                    severita = 1,
                    id_riga = r.Id_Riga,
                    codice = g.Key,
                    valore = r.Quantita,
                    messaggio = $"{g.Key} — compare {g.Count()} volte nel documento con quantità diverse ({quantita})",
                });
            }
        }
    }

    // ------------------------------------------------------------------ duplicati

    /// <summary>
    /// Impronta del contenuto: righe (codice, quantita', importi) ordinate. Due documenti con numero
    /// diverso e la stessa impronta sono lo stesso documento caricato due volte.
    /// Null se il documento non ha righe significative.
    /// </summary>
    public static string? ImprontaContenuto(DatiBolla bolla)
    {
        var righe = (bolla.Articoli ?? [])
            .Select(r => string.Join('|',
                RigheDocumento.NormalizzaCodice(r.Codice),
                Numero(r.Quantita),
                Numero(r.Imponibile),
                Numero(r.Totale)))
            .Order(StringComparer.Ordinal)
            .ToList();

        if (righe.Count == 0) return null;

        var testo = string.Join('\n', righe);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(testo)));
    }

    public static EmmaAnomalia? Duplicato(DatiBolla bolla, IEnumerable<DocumentoSimile> altri)
    {
        var impronta = ImprontaContenuto(bolla);
        if (impronta is null) return null;

        var numero = bolla.NumeroBolla?.Trim() ?? string.Empty;
        var gemello = altri.FirstOrDefault(a =>
            a.Impronta == impronta &&
            // Stesso numero = stesso documento reimportato: lo gestisce gia' AddDocAsync.
            !string.Equals(a.NumeroBolla.Trim(), numero, StringComparison.OrdinalIgnoreCase));

        if (gemello is null) return null;

        var dataGemello = DateOnly.TryParseExact(gemello.DataBolla, "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var d) ? Data(d) : gemello.DataBolla;

        return new EmmaAnomalia
        {
            tipo = TipoAnomalia.Duplicato,
            severita = 3,
            atteso = gemello.Id,
            messaggio = $"Stesso contenuto del documento n. {gemello.NumeroBolla} del {dataGemello} " +
                        $"(id {gemello.Id}): possibile doppio caricamento con numero diverso",
        };
    }

    // ------------------------------------------------------------------ utilita'

    private static string Etichetta(ArticoloBolla r)
        => string.IsNullOrWhiteSpace(r.Codice)
            ? $"Riga {r.Id_Riga}"
            : RigheDocumento.NormalizzaCodice(r.Codice);

    private static string? NullSeVuoto(string? codice)
    {
        var c = RigheDocumento.NormalizzaCodice(codice);
        return c.Length == 0 ? null : c;
    }

    private static string Numero(decimal v) => v.ToString("0.####", CultureInfo.InvariantCulture);

    private static string Data(DateOnly d) => d.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
}
