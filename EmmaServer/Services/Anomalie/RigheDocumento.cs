using System.Globalization;
using EmmaServer.Entities.Dtos;

namespace EmmaServer.Services.Anomalie;

/// <summary>
/// Lettura delle righe dal JSON di <c>docs</c>: prezzo unitario, normalizzazioni, guardia di quadratura.
/// Tutto statico e senza database, cosi' si testa da solo.
/// </summary>
public static class RigheDocumento
{
    public const short TipoDdt = 2;
    public const short TipoFattura = 4;

    public static short? TipoDocumento(DatiBolla bolla)
        => short.TryParse(bolla.TipoDocumento?.Trim(), out var t) && (t == TipoDdt || t == TipoFattura)
            ? t
            : null;

    /// <summary>
    /// Il prezzo unitario non e' memorizzato: si ricava come
    /// <c>COALESCE(NULLIF(imponibile,0), NULLIF(totale,0)) / NULLIF(qta,0)</c>.
    /// ImportFatturaElettronicaAsync scrive Imponibile = 0 e il prezzo di riga in Totale.
    /// Restituisce null per righe inutilizzabili (quantita' o importo non positivi).
    ///
    /// Se si scopre che EMMA-AI mette in "imponibile" il prezzo UNITARIO e non l'importo di riga,
    /// questo e' l'unico punto da correggere: baseline e analisi passano tutte da qui.
    /// </summary>
    public static decimal? PrezzoUnitario(ArticoloBolla riga)
    {
        if (riga.Quantita <= 0) return null;

        var importo = riga.Imponibile != 0 ? riga.Imponibile : riga.Totale;
        if (importo <= 0) return null;

        return importo / riga.Quantita;
    }

    public static string NormalizzaCodice(string? codice)
    {
        var c = (codice ?? string.Empty).Trim().ToUpperInvariant();
        return c.Length > 256 ? c[..256] : c;
    }

    public static string NormalizzaUm(string? um)
    {
        var u = (um ?? string.Empty).Trim().TrimEnd('.').ToUpperInvariant();
        return u.Length > 20 ? u[..20] : u;
    }

    public static DateOnly? DataDocumento(DatiBolla bolla)
    {
        var s = bolla.DataBolla?.Trim();
        if (string.IsNullOrEmpty(s)) return null;

        if (DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return d;
        if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            return DateOnly.FromDateTime(dt);
        return null;
    }

    /// <summary>
    /// Guardia PROVVISORIA di quadratura, in attesa del livello 1 completo (fase 1).
    /// Un documento che non quadra non deve generare avvisi ne' entrare nella baseline:
    /// un solo DDT letto male avvelena lo storico dell'articolo per mesi.
    ///
    /// Tollerante di proposito, perche' il totale del documento puo' essere con o senza IVA:
    /// la somma delle righe deve tornare con il totale, con il totale ricalcolato con l'IVA di riga,
    /// oppure con l'imponibile del documento. Se il documento non ha totali, non e' verificabile e passa.
    /// </summary>
    public static bool DocumentoQuadra(DatiBolla bolla)
    {
        if (bolla.Articoli is null || bolla.Articoli.Count == 0) return false;

        var totaleDoc = ToDecimal(bolla.Totale);
        var imponibileDoc = ToDecimal(bolla.Imponibile);
        if (totaleDoc <= 0 && imponibileDoc <= 0) return true;

        var riferimento = totaleDoc > 0 ? totaleDoc : imponibileDoc;
        var sconto = LeggiSconto(bolla.Sconto, riferimento);
        var tolleranza = Math.Max(0.01m + sconto, riferimento * 0.005m);

        decimal sommaRighe = 0, sommaConIva = 0;
        foreach (var r in bolla.Articoli)
        {
            sommaRighe += r.Totale;
            sommaConIva += r.Totale * (1 + LeggiAliquota(r.Iva) / 100m);
        }

        bool Vicino(decimal a, decimal b) => Math.Abs(a - b) <= tolleranza;

        return (totaleDoc > 0 && (Vicino(sommaRighe, totaleDoc) || Vicino(sommaConIva, totaleDoc)))
            || (imponibileDoc > 0 && Vicino(sommaRighe, imponibileDoc));
    }

    internal static decimal LeggiAliquota(string? iva)
        => LeggiNumero(iva) is { } v && v >= 0 && v < 100 ? v : 0m;

    /// <summary>"10" = importo, "10%" = percentuale del riferimento.</summary>
    internal static decimal LeggiSconto(string? sconto, decimal riferimento)
    {
        if (LeggiNumero(sconto) is not { } v) return 0m;
        v = Math.Abs(v);
        return sconto!.Contains('%') ? riferimento * v / 100m : v;
    }

    internal static decimal? LeggiNumero(string? testo)
    {
        if (string.IsNullOrWhiteSpace(testo)) return null;

        var pulito = testo.Replace("%", "").Replace("€", "").Replace(" ", "").Trim();
        // "1.234,50" -> "1234.50"; "12,5" -> "12.5"
        if (pulito.Contains(',')) pulito = pulito.Replace(".", "").Replace(',', '.');

        return decimal.TryParse(pulito, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    private static decimal ToDecimal(double v)
        => double.IsFinite(v) && Math.Abs(v) < 1e15 ? (decimal)v : 0m;
}
