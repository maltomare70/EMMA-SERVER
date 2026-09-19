using EmmaServer.DataSeed.Tests.Infrastructure;
using EmmaServer.Entities.Dtos;
using Xunit.Abstractions;

namespace EmmaServer.DataSeed.Tests;

/// <summary>
/// DISATTIVATO (tutti i test hanno lo <c>Skip</c>): la terna con le stesse tre righe su ordine,
/// bolla e fattura e' sostituita da <see cref="GeneraDocumentiParzialiTests"/>, che fa 3 righe
/// sull'ordine, 2 sulla bolla e 1 sulla fattura. Per rigenerarla, togliere lo <c>Skip</c>.
///
/// Generatore di documenti di prova sul tenant di <see cref="SeedSettings.Tenant"/> (default "Test").
///
/// Questi test NON verificano il comportamento di EmmaServer: servono solo a riempire il database.
/// Lanciando la classe intera si ottiene una terna coerente - ordine, bolla e fattura dello stesso
/// mittente, con le stesse tre righe, stesse quantita' e stessi importi, e lo stesso suffisso nel
/// numero documento (vedi <see cref="SeedFixture.SuffissoEsecuzione"/>).
///
/// Ogni test e' anche lanciabile da solo:
/// <code>dotnet test --filter "FullyQualifiedName~GeneraDocumentiTests.Bolla_VieneGenerata"</code>
/// </summary>
[Collection(NomeCollezioneSeed.Nome)]
public class GeneraDocumentiTests
{
    private readonly SeedFixture _fixture;
    private readonly ITestOutputHelper _output;

    public GeneraDocumentiTests(SeedFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [SeedFact(Skip = "Disattivato: la terna a righe identiche e' sostituita da GeneraDocumentiParzialiTests. Togliere lo Skip per riattivarlo.")]
    public async Task Ordine_VieneGenerato()
        => await GeneraAsync(DocumentoFactory.TipoOrdine);

    [SeedFact(Skip = "Disattivato: la terna a righe identiche e' sostituita da GeneraDocumentiParzialiTests. Togliere lo Skip per riattivarlo.")]
    public async Task Bolla_VieneGenerata()
        => await GeneraAsync(DocumentoFactory.TipoBolla);

    [SeedFact(Skip = "Disattivato: la terna a righe identiche e' sostituita da GeneraDocumentiParzialiTests. Togliere lo Skip per riattivarlo.")]
    public async Task Fattura_VieneGenerata()
        => await GeneraAsync(DocumentoFactory.TipoFattura);

    /// <summary>
    /// Crea il documento del tipo richiesto e lo salva davvero sulla tabella <c>docs</c>.
    /// L'unica asserzione e' che il salvataggio sia andato a buon fine.
    /// </summary>
    private async Task<int> GeneraAsync(string tipoDocumento)
    {
        var documento = DocumentoFactory.CreaDocumento(tipoDocumento, _fixture.SuffissoEsecuzione);
        var id = await _fixture.SalvaDocumentoAsync(documento);

        StampaRiepilogo(documento, id);

        Assert.True(id > 0);
        return id;
    }

    private void StampaRiepilogo(DdtResponse documento, int id)
    {
        var dati = documento.Document;

        _output.WriteLine(
            $"docs.id={id}  tenant={_fixture.Tenant}  tipo={dati.TipoDocumento}  " +
            $"numero={dati.NumeroBolla}  data={dati.DataBolla}  mittente={dati.Mittente}");

        foreach (var riga in dati.Articoli)
        {
            _output.WriteLine(
                $"  riga {riga.Id_Riga}: {riga.Codice} {riga.Descrizione} - " +
                $"{riga.Quantita} {riga.UnitaMisura} - totale {riga.Totale}");
        }

        _output.WriteLine($"  imponibile {dati.Imponibile} - iva {dati.Iva}% - totale {dati.Totale}");
    }
}
