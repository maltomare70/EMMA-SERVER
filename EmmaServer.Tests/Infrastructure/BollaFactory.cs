using System.Text.Json;
using EmmaServer.Entities;
using EmmaServer.Entities.Dtos;

namespace EmmaServer.Tests.Infrastructure;

/// <summary>
/// Costruisce le bolle di prova nello stesso identico formato che DocService si aspetta:
/// un <see cref="DdtResponse"/> serializzato che finisce nella colonna jsonb <c>docs.content</c>.
/// </summary>
public static class BollaFactory
{
    /// <summary>Tipo documento 2 = DDT (bolla). E' l'unico tipo per cui DocService aggiorna le anagrafiche.</summary>
    public const string TipoDocumentoBolla = "2";

    /// <summary>Tipo documento 4 = fattura.</summary>
    public const string TipoDocumentoFattura = "4";

    /// <summary>Stessi option usati da DocService quando serializza/deserializza il content.</summary>
    public static readonly JsonSerializerOptions OpzioniJson = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// Numero bolla univoco per ogni esecuzione: i documenti restano sul database e cosi' due run
    /// consecutivi non si sovrappongono (in particolare quelli che chiudono il documento).
    /// </summary>
    public static string NumeroBollaUnivoco(string prefisso = "TEST")
        => $"{prefisso}-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6]}";

    public static DdtResponse CreaBolla(
        string mittente = "ACME Forniture SpA",
        string? numeroBolla = null,
        string? dataBolla = null,
        string tipoDocumento = TipoDocumentoBolla,
        string? idMaster = null,
        IEnumerable<ArticoloBolla>? articoli = null,
        string? nomeFile = null)
    {
        var id = idMaster ?? Guid.NewGuid().ToString();
        var numero = numeroBolla ?? NumeroBollaUnivoco();
        var righe = (articoli ?? CreaArticoliDiProva(id)).ToList();

        return new DdtResponse
        {
            ModelName = "test",
            FileName = nomeFile ?? $"{numero}.pdf",
            Costs = new Costs
            {
                Id = id,
                PromptTokens = 100,
                OutputTokens = 50,
                TotalTokens = 150,
                TotalCostEur = 0.0012
            },
            Document = new DatiBolla
            {
                Id = id,
                TipoDocumento = tipoDocumento,
                NumeroBolla = numero,
                DataBolla = dataBolla ?? DateTime.UtcNow.ToString("yyyy-MM-dd"),
                Mittente = mittente,
                Imponibile = 100,
                Iva = "22",
                Sconto = "0",
                Totale = 122,
                Articoli = righe
            }
        };
    }

    public static List<ArticoloBolla> CreaArticoliDiProva(string idMaster) =>
    [
        new ArticoloBolla
        {
            Id_Master = idMaster,
            Id_Riga = "1",
            Codice = "ART-001",
            Descrizione = "Articolo di prova 1",
            Quantita = 2m,
            UnitaMisura = "PZ",
            Imponibile = 50m,
            Iva = "22",
            Totale = 100m
        },
        new ArticoloBolla
        {
            Id_Master = idMaster,
            Id_Riga = "2",
            Codice = "ART-002",
            Descrizione = "Articolo di prova 2",
            Quantita = 1m,
            UnitaMisura = "KG",
            Imponibile = 22m,
            Iva = "22",
            Totale = 22m
        }
    ];

    /// <summary>Serializza la bolla come fa DocService prima di salvarla.</summary>
    public static string ToJson(DdtResponse ddt) => JsonSerializer.Serialize(ddt, OpzioniJson);

    /// <summary>
    /// Filtri che identificano univocamente la bolla.
    /// <c>Stato = -1</c> significa "qualunque stato": e' la convenzione usata da DocService.AddDocAsync,
    /// col default 0 si troverebbero solo i documenti ancora aperti.
    /// </summary>
    public static EmmaDocFilters FiltriPer(DdtResponse ddt, int stato = -1) => new()
    {
        Fornitore = ddt.Document.Mittente,
        NumeroDoc = ddt.Document.NumeroBolla,
        DataDoc = ddt.Document.DataBolla,
        TipoDoc = int.Parse(ddt.Document.TipoDocumento),
        Stato = stato
    };

    /// <summary>Finto PDF allegato: bastano dei byte riconoscibili per verificare che vengano salvati.</summary>
    public static byte[] AllegatoDiProva() => "%PDF-1.4 bolla di test"u8.ToArray();
}
