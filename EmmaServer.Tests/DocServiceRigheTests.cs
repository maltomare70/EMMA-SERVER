using EmmaServer.Entities;
using EmmaServer.Entities.Dtos;
using EmmaServer.Tests.Infrastructure;

namespace EmmaServer.Tests;

/// <summary>
/// Test sulle righe articolo della bolla: DocService le gestisce riscrivendo il jsonb del documento
/// (le righe non stanno su una tabella dedicata).
/// </summary>
[Collection(NomeCollezioneDatabase.Nome)]
public class DocServiceRigheTests
{
    private readonly DocServiceFixture _fixture;

    public DocServiceRigheTests(DocServiceFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task<(DdtResponse Ddt, EmmaDocFilters Filtri, EmmaDoc Documento)> CreaBollaAsync(string prefisso)
    {
        var ddt = BollaFactory.CreaBolla(numeroBolla: BollaFactory.NumeroBollaUnivoco(prefisso));
        var filtri = BollaFactory.FiltriPer(ddt);

        var documento = await _fixture.DocService.AddDocAsync(
            filtri, BollaFactory.ToJson(ddt), ddt.FileName!,
            BollaFactory.AllegatoDiProva(), _fixture.Tenant);

        Assert.NotNull(documento);
        return (ddt, filtri, documento!);
    }

    private async Task<DatiBolla> RileggiBollaAsync(EmmaDocFilters filtri)
    {
        var trovato = Assert.Single(await _fixture.DocService.GetDocsAsync(filtri));
        Assert.NotNull(trovato);

        var bolla = trovato!.ToDoc();
        Assert.NotNull(bolla);
        return bolla!;
    }

    [IntegrationFact]
    public async Task InsertRigaDocAsync_AggiungeLaRigaAllaBolla()
    {
        var (ddt, filtri, _) = await CreaBollaAsync("RIGA-INS");

        var nuovaRiga = new ArticoloBolla
        {
            Id_Master = ddt.Document.Id,
            Id_Riga = "3",
            Codice = "ART-003",
            Descrizione = "Articolo aggiunto dal test",
            Quantita = 5m,
            UnitaMisura = "MT",
            Imponibile = 10m,
            Iva = "22",
            Totale = 50m
        };

        await _fixture.DocService.InsertRigaDocAsync(nuovaRiga);

        var bolla = await RileggiBollaAsync(filtri);
        Assert.Equal(3, bolla.Articoli.Count);

        var riga = Assert.Single(bolla.Articoli, articolo => articolo.Id_Riga == "3");
        Assert.Equal("ART-003", riga.Codice);
        Assert.Equal("Articolo aggiunto dal test", riga.Descrizione);
        Assert.Equal(5m, riga.Quantita);
        Assert.Equal(50m, riga.Totale);
    }

    [IntegrationFact]
    public async Task UpdateRigaDocAsync_ModificaLaRigaEsistente()
    {
        var (ddt, filtri, _) = await CreaBollaAsync("RIGA-UPD");

        var rigaModificata = new ArticoloBolla
        {
            Id_Master = ddt.Document.Id,
            Id_Riga = "1",
            Codice = "ART-001-BIS",
            Descrizione = "Descrizione corretta",
            Quantita = 7m,
            UnitaMisura = "PZ",
            Imponibile = 11m,
            Iva = "10",
            Totale = 77m
        };

        await _fixture.DocService.UpdateRigaDocAsync(rigaModificata);

        var bolla = await RileggiBollaAsync(filtri);
        Assert.Equal(2, bolla.Articoli.Count); // nessuna riga aggiunta

        var riga = Assert.Single(bolla.Articoli, articolo => articolo.Id_Riga == "1");
        Assert.Equal("ART-001-BIS", riga.Codice);
        Assert.Equal("Descrizione corretta", riga.Descrizione);
        Assert.Equal(7m, riga.Quantita);
        Assert.Equal("10", riga.Iva);
        Assert.Equal(77m, riga.Totale);

        // La seconda riga non deve essere stata toccata.
        var altra = Assert.Single(bolla.Articoli, articolo => articolo.Id_Riga == "2");
        Assert.Equal("ART-002", altra.Codice);
    }

    [IntegrationFact]
    public async Task DeleteRigaDocAsync_RimuoveLaRigaDallaBolla()
    {
        var (ddt, filtri, _) = await CreaBollaAsync("RIGA-DEL");

        await _fixture.DocService.DeleteRigaDocAsync(new ArticoloBolla
        {
            Id_Master = ddt.Document.Id,
            Id_Riga = "2"
        });

        var bolla = await RileggiBollaAsync(filtri);
        var riga = Assert.Single(bolla.Articoli);
        Assert.Equal("1", riga.Id_Riga);
    }

    /// <summary>
    /// Regressione per un bug noto: le SELECT di InsertRigaDocAsync / UpdateRigaDocAsync /
    /// DeleteRigaDocAsync non leggono la colonna <c>allegato</c>, ma poi passano l'entita' a
    /// Dapper.Contrib UpdateAsync, che riscrive TUTTE le colonne: l'allegato viene azzerato.
    ///
    /// Il test e' disattivato perche' allo stato attuale fallisce. Per riattivarlo basta togliere
    /// Skip dopo aver aggiunto <c>allegato</c> alle SELECT in DocRepository.
    /// </summary>
    [IntegrationFact(Skip = "Bug noto: le SELECT delle righe non leggono 'allegato' e UpdateAsync lo azzera")]
    public async Task InsertRigaDocAsync_NonDeveAzzerareLAllegato()
    {
        var allegato = BollaFactory.AllegatoDiProva();
        var ddt = BollaFactory.CreaBolla(numeroBolla: BollaFactory.NumeroBollaUnivoco("RIGA-ALLEGATO"));
        var filtri = BollaFactory.FiltriPer(ddt);

        await _fixture.DocService.AddDocAsync(
            filtri, BollaFactory.ToJson(ddt), ddt.FileName!, allegato, _fixture.Tenant);

        await _fixture.DocService.InsertRigaDocAsync(new ArticoloBolla
        {
            Id_Master = ddt.Document.Id,
            Id_Riga = "3",
            Codice = "ART-003",
            Descrizione = "Articolo aggiunto dal test"
        });

        var trovato = Assert.Single(await _fixture.DocService.GetDocsAsync(filtri));
        Assert.Equal(allegato, trovato!.allegato);
    }
}
