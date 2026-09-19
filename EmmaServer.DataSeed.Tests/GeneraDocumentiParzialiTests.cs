using EmmaServer.DataSeed.Tests.Infrastructure;
using EmmaServer.Entities.Dtos;
using Xunit.Abstractions;

namespace EmmaServer.DataSeed.Tests;

/// <summary>
/// Generatore di documenti "a scalare" sul tenant di <see cref="SeedSettings.Tenant"/> (default "Test"):
/// stesso mittente, righe che si riducono passando da un documento all'altro.
///
/// <list type="bullet">
///   <item>ordine (tipo 1): 3 righe - ART-001, ART-002, ART-003;</item>
///   <item>bolla (tipo 2): 2 righe - ART-001, ART-002 (consegna parziale);</item>
///   <item>fattura (tipo 4): 1 riga - ART-001 (fatturazione parziale).</item>
/// </list>
///
/// Quantita' e prezzi delle righe in comune sono identici in tutti e tre i documenti: cambia solo
/// QUANTE righe ci sono, cosi' la conciliazione ordine/bolla/fattura ha righe che matchano e righe
/// che restano scoperte.
///
/// I test NON verificano il comportamento di EmmaServer: servono solo a riempire il database, e
/// sono pensati per essere lanciati UNO ALLA VOLTA:
/// <code>dotnet test --filter "FullyQualifiedName~Ordine_3Righe"</code>
///
/// Perche' i tre documenti restino legati dallo stesso numero anche lanciandoli in momenti diversi,
/// fissare il suffisso prima di lanciarli:
/// <code>$env:EMMA_Seed__Suffisso = "0001"</code>
/// Senza, ogni esecuzione usa un suffisso nuovo (e i documenti non si sovrappongono mai).
/// </summary>
[Collection(NomeCollezioneSeed.Nome)]
public class GeneraDocumentiParzialiTests
{
    private readonly SeedFixture _fixture;
    private readonly ITestOutputHelper _output;

    public GeneraDocumentiParzialiTests(SeedFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    /// <summary>Ordine con tutte e 3 le righe: imponibile 189,00 - totale 230,58.</summary>
    [SeedFact]
    public async Task Ordine_3Righe()
        => await GeneraAsync(DocumentoFactory.TipoOrdine, righe: 3);

    /// <summary>Bolla con le prime 2 righe dell'ordine: imponibile 159,00 - totale 193,98.</summary>
    [SeedFact]
    public async Task Bolla_2Righe()
        => await GeneraAsync(DocumentoFactory.TipoBolla, righe: 2);

    /// <summary>Fattura con la prima riga soltanto: imponibile 45,00 - totale 54,90.</summary>
    [SeedFact]
    public async Task Fattura_1Riga()
        => await GeneraAsync(DocumentoFactory.TipoFattura, righe: 1);

    /// <summary>
    /// Crea il documento del tipo richiesto con le prime <paramref name="righe"/> righe e lo salva
    /// davvero sulla tabella <c>docs</c>. L'unica asserzione e' che il salvataggio sia riuscito.
    /// </summary>
    private async Task<int> GeneraAsync(string tipoDocumento, int righe)
    {
        var documento = DocumentoFactory.CreaDocumento(
            tipoDocumento,
            _fixture.SuffissoEsecuzione,
            righe: DocumentoFactory.PrimeRighe(righe));

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
            $"numero={dati.NumeroBolla}  data={dati.DataBolla}  mittente={dati.Mittente}  " +
            $"righe={dati.Articoli.Count}");

        foreach (var riga in dati.Articoli)
        {
            _output.WriteLine(
                $"  riga {riga.Id_Riga}: {riga.Codice} {riga.Descrizione} - " +
                $"{riga.Quantita} {riga.UnitaMisura} - totale {riga.Totale}");
        }

        _output.WriteLine($"  imponibile {dati.Imponibile} - iva {dati.Iva}% - totale {dati.Totale}");
    }
}
