using System.Globalization;
using System.Text;
using System.Text.Json;
using EmmaServer.Entities.Dtos;

namespace EmmaServer.DataSeed.Tests;

/// <summary>Le tre righe articolo, identiche su ordine, bolla e fattura.</summary>
/// <param name="Codice">Codice articolo.</param>
/// <param name="Descrizione">Descrizione articolo.</param>
/// <param name="Quantita">Quantita' di riga.</param>
/// <param name="UnitaMisura">Unita' di misura.</param>
/// <param name="PrezzoUnitario">Prezzo unitario netto.</param>
public readonly record struct RigaDemo(
    string Codice,
    string Descrizione,
    decimal Quantita,
    string UnitaMisura,
    decimal PrezzoUnitario)
{
    /// <summary>Importo netto della riga: quantita' x prezzo unitario.</summary>
    public decimal Imponibile => Math.Round(Quantita * PrezzoUnitario, 2, MidpointRounding.AwayFromZero);
}

/// <summary>
/// Costruisce i documenti di prova nello stesso formato che <c>DocService</c> si aspetta:
/// un <see cref="DdtResponse"/> serializzato che finisce nella colonna jsonb <c>docs.content</c>.
///
/// Convenzione sugli importi, la stessa di <c>ImportFatturaElettronicaAsync</c>:
/// <list type="bullet">
///   <item>riga: <c>imponibile = 0</c>, <c>totale</c> = importo NETTO della riga (qta x prezzo);</item>
///   <item>documento: <c>imponibile</c> = somma netta delle righe, <c>totale</c> = netto + IVA.</item>
/// </list>
/// Cosi' <c>RigheDocumento.PrezzoUnitario</c> ricava il prezzo unitario corretto e
/// <c>RigheDocumento.DocumentoQuadra</c> trova il documento quadrato.
/// </summary>
public static class DocumentoFactory
{
    /// <summary>Tipo documento 1 = ordine (vedi <see cref="TipoDocEnum"/>).</summary>
    public const string TipoOrdine = "1";

    /// <summary>Tipo documento 2 = DDT / bolla. E' l'unico tipo per cui vengono aggiornati gli articoli.</summary>
    public const string TipoBolla = "2";

    /// <summary>Tipo documento 4 = fattura.</summary>
    public const string TipoFattura = "4";

    /// <summary>Aliquota IVA usata su tutte le righe e sul documento.</summary>
    public const decimal AliquotaIva = 22m;

    /// <summary>L'unico mittente dell'esempio: ordine, bolla e fattura sono dello stesso fornitore.</summary>
    public const string Mittente = "Cartotecnica Demo Srl";

    /// <summary>Stessi option usati da DocService quando serializza/deserializza il content.</summary>
    public static readonly JsonSerializerOptions OpzioniJson = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// Le tre righe dell'esempio: le stesse quantita' e gli stessi importi su tutti e tre i documenti.
    /// Netto totale 189,00 - IVA 22% 41,58 - totale documento 230,58.
    /// </summary>
    public static readonly IReadOnlyList<RigaDemo> Righe =
    [
        new RigaDemo("ART-001", "Carta A4 80 g - risma 500 fogli", 10m, "PZ", 4.50m),
        new RigaDemo("ART-002", "Toner nero compatibile", 3m, "PZ", 38.00m),
        new RigaDemo("ART-003", "Cartone imballo 60x40x40", 25m, "PZ", 1.20m)
    ];

    /// <summary>
    /// Le prime <paramref name="quante"/> righe di <see cref="Righe"/>: serve a fare documenti
    /// "a scalare" (ordine 3 righe, bolla 2, fattura 1) mantenendo quantita' e importi identici
    /// sulle righe in comune.
    /// </summary>
    public static IReadOnlyList<RigaDemo> PrimeRighe(int quante)
    {
        if (quante < 1 || quante > Righe.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(quante),
                $"Le righe disponibili sono {Righe.Count}.");
        }

        return Righe.Take(quante).ToList();
    }

    /// <summary>
    /// Suffisso comune a ordine, bolla e fattura di una stessa esecuzione: tiene insieme la terna
    /// e evita che due run consecutivi si sovrappongano sullo stesso numero documento.
    /// </summary>
    public static string SuffissoUnivoco()
        => $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6]}";

    /// <summary>Prefisso del numero documento per tipo: ORD, DDT, FT.</summary>
    public static string PrefissoNumero(string tipoDocumento) => tipoDocumento switch
    {
        TipoOrdine => "ORD",
        TipoBolla => "DDT",
        TipoFattura => "FT",
        _ => "DOC"
    };

    /// <summary>
    /// Costruisce un documento con il mittente e le tre righe dell'esempio.
    /// </summary>
    /// <param name="tipoDocumento">"1" ordine, "2" bolla, "4" fattura.</param>
    /// <param name="suffissoNumero">Suffisso del numero documento; se null ne genera uno nuovo.</param>
    /// <param name="data">Data del documento; se null usa oggi.</param>
    /// <param name="mittente">Mittente; se null usa <see cref="Mittente"/>.</param>
    /// <param name="righe">Righe; se null usa <see cref="Righe"/>.</param>
    public static DdtResponse CreaDocumento(
        string tipoDocumento,
        string? suffissoNumero = null,
        DateOnly? data = null,
        string? mittente = null,
        IEnumerable<RigaDemo>? righe = null)
    {
        var idMaster = Guid.NewGuid().ToString();
        var numero = $"{PrefissoNumero(tipoDocumento)}-{suffissoNumero ?? SuffissoUnivoco()}";
        var dataDocumento = data ?? DateOnly.FromDateTime(DateTime.Today);
        var elenco = (righe ?? Righe).ToList();

        var articoli = new List<ArticoloBolla>(elenco.Count);
        for (var i = 0; i < elenco.Count; i++)
        {
            var riga = elenco[i];
            articoli.Add(new ArticoloBolla
            {
                Id_Master = idMaster,
                Id_Riga = (i + 1).ToString(CultureInfo.InvariantCulture),
                Codice = riga.Codice,
                Descrizione = riga.Descrizione,
                Quantita = riga.Quantita,
                UnitaMisura = riga.UnitaMisura,
                // Convenzione dell'import reale: l'importo di riga sta in "totale", "imponibile" resta 0.
                Imponibile = 0m,
                Iva = AliquotaIva.ToString(CultureInfo.InvariantCulture),
                Totale = riga.Imponibile
            });
        }

        var imponibile = articoli.Sum(a => a.Totale);
        var totale = Math.Round(imponibile * (1 + AliquotaIva / 100m), 2, MidpointRounding.AwayFromZero);

        return new DdtResponse
        {
            ModelName = "data-seed",
            FileName = $"{numero}.pdf",
            Costs = new Costs { Id = idMaster },
            Document = new DatiBolla
            {
                Id = idMaster,
                TipoDocumento = tipoDocumento,
                NumeroBolla = numero,
                DataBolla = dataDocumento.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Mittente = mittente ?? Mittente,
                Imponibile = (double)imponibile,
                Iva = AliquotaIva.ToString(CultureInfo.InvariantCulture),
                Sconto = "0",
                Totale = (double)totale,
                Articoli = articoli
            }
        };
    }

    /// <summary>Serializza il documento come fa DocService prima di salvarlo.</summary>
    public static string ToJson(DdtResponse documento) => JsonSerializer.Serialize(documento, OpzioniJson);

    /// <summary>
    /// Filtri che identificano univocamente il documento.
    /// <c>Stato = -1</c> significa "qualunque stato": e' la convenzione di <c>DocService.AddDocAsync</c>.
    /// </summary>
    public static EmmaDocFilters FiltriPer(DdtResponse documento, int stato = -1) => new()
    {
        Fornitore = documento.Document.Mittente,
        NumeroDoc = documento.Document.NumeroBolla,
        DataDoc = documento.Document.DataBolla,
        TipoDoc = int.Parse(documento.Document.TipoDocumento, CultureInfo.InvariantCulture),
        Stato = stato
    };

    /// <summary>Finto PDF allegato: bastano dei byte riconoscibili.</summary>
    public static byte[] AllegatoFinto(DdtResponse documento)
        => Encoding.UTF8.GetBytes($"%PDF-1.4 documento di prova {documento.Document.NumeroBolla}");
}
