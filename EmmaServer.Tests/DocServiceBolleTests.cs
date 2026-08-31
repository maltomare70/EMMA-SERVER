using EmmaServer.Entities.Dtos;
using EmmaServer.Tests.Infrastructure;

namespace EmmaServer.Tests;

/// <summary>
/// Test di integrazione su DocService: creano davvero le bolle sulla tabella <c>docs</c>
/// sotto il tenant di test (vedi <c>Test:Tenant</c> in appsettings.Tests.json).
///
/// Le bolle NON vengono cancellate a fine test: servono anche a popolare il database.
/// Ogni test usa un numero bolla univoco, cosi' due esecuzioni consecutive non si disturbano.
/// </summary>
[Collection(NomeCollezioneDatabase.Nome)]
public class DocServiceBolleTests
{
    private readonly DocServiceFixture _fixture;

    public DocServiceBolleTests(DocServiceFixture fixture)
    {
        _fixture = fixture;
    }

    [IntegrationFact]
    public async Task AddDocAsync_CreaLaBollaSulDatabase()
    {
        var ddt = BollaFactory.CreaBolla(mittente: "ACME Forniture SpA");
        var filtri = BollaFactory.FiltriPer(ddt);

        var documento = await _fixture.DocService.AddDocAsync(
            filtri,
            BollaFactory.ToJson(ddt),
            ddt.FileName!,
            BollaFactory.AllegatoDiProva(),
            _fixture.Tenant);

        Assert.NotNull(documento);
        Assert.True(documento!.id > 0, "il documento deve avere l'id assegnato dal database");
        Assert.Equal(_fixture.Tenant, documento.tenant);
        Assert.Equal(ddt.FileName, documento.file_name);
        Assert.Equal(0, documento.stato); // 0 = aperto

        // Il contenuto jsonb deve essere rileggibile come DdtResponse.
        var bollaSalvata = documento.ToDoc();
        Assert.NotNull(bollaSalvata);
        Assert.Equal(ddt.Document.Mittente, bollaSalvata!.Mittente);
        Assert.Equal(ddt.Document.NumeroBolla, bollaSalvata.NumeroBolla);
        Assert.Equal(ddt.Document.DataBolla, bollaSalvata.DataBolla);
        Assert.Equal(2, bollaSalvata.Articoli.Count);

        // E deve essere ritrovabile con gli stessi filtri usati per inserirla.
        var trovati = await _fixture.DocService.GetDocsAsync(filtri);
        var trovato = Assert.Single(trovati);
        Assert.NotNull(trovato);
        Assert.Equal(documento.id, trovato!.id);
    }

    [IntegrationFact]
    public async Task AddDocAsync_SalvaAncheIlFileAllegato()
    {
        var allegato = BollaFactory.AllegatoDiProva();
        var ddt = BollaFactory.CreaBolla();

        var documento = await _fixture.DocService.AddDocAsync(
            BollaFactory.FiltriPer(ddt),
            BollaFactory.ToJson(ddt),
            ddt.FileName!,
            allegato,
            _fixture.Tenant);

        Assert.NotNull(documento);

        // GetDocsAsync e' l'unica query che rilegge la colonna allegato.
        var trovato = Assert.Single(await _fixture.DocService.GetDocsAsync(BollaFactory.FiltriPer(ddt)));
        Assert.NotNull(trovato);
        Assert.Equal(allegato, trovato!.allegato);
    }

    [IntegrationFact]
    public async Task AddDocAsync_CreaPiuBolleDistinte()
    {
        var conteggioIniziale = await _fixture.ContaDocumentiDelTenantAsync();

        var idCreati = new List<int>();
        for (var indice = 1; indice <= 3; indice++)
        {
            var ddt = BollaFactory.CreaBolla(
                mittente: $"Fornitore Test {indice}",
                numeroBolla: BollaFactory.NumeroBollaUnivoco($"MULTI{indice}"));

            var documento = await _fixture.DocService.AddDocAsync(
                BollaFactory.FiltriPer(ddt),
                BollaFactory.ToJson(ddt),
                ddt.FileName!,
                BollaFactory.AllegatoDiProva(),
                _fixture.Tenant);

            Assert.NotNull(documento);
            idCreati.Add(documento!.id);
        }

        Assert.Equal(3, idCreati.Distinct().Count());
        Assert.Equal(conteggioIniziale + 3, await _fixture.ContaDocumentiDelTenantAsync());
    }

    [IntegrationFact]
    public async Task AddDocAsync_SeIlDocumentoEsisteEdEAperto_LoSostituisce()
    {
        var ddt = BollaFactory.CreaBolla(numeroBolla: BollaFactory.NumeroBollaUnivoco("DUP-APERTO"));
        var filtri = BollaFactory.FiltriPer(ddt);
        var json = BollaFactory.ToJson(ddt);

        var primo = await _fixture.DocService.AddDocAsync(
            filtri, json, ddt.FileName!, BollaFactory.AllegatoDiProva(), _fixture.Tenant);

        var secondo = await _fixture.DocService.AddDocAsync(
            filtri, json, ddt.FileName!, BollaFactory.AllegatoDiProva(), _fixture.Tenant);

        Assert.NotNull(primo);
        Assert.NotNull(secondo);
        Assert.NotEqual(primo!.id, secondo!.id);

        // Il vecchio documento aperto e' stato cancellato: ne resta uno solo.
        var trovati = await _fixture.DocService.GetDocsAsync(filtri);
        var rimasto = Assert.Single(trovati);
        Assert.Equal(secondo.id, rimasto!.id);
        Assert.Null(await _fixture.LeggiStatoAsync(primo.id));
    }

    [IntegrationFact]
    public async Task AddDocAsync_SeIlDocumentoEsisteEdEChiuso_SollevaEccezione()
    {
        var ddt = BollaFactory.CreaBolla(numeroBolla: BollaFactory.NumeroBollaUnivoco("DUP-CHIUSO"));
        var filtri = BollaFactory.FiltriPer(ddt);
        var json = BollaFactory.ToJson(ddt);

        var documento = await _fixture.DocService.AddDocAsync(
            filtri, json, ddt.FileName!, BollaFactory.AllegatoDiProva(), _fixture.Tenant);
        Assert.NotNull(documento);

        // Chiudo il documento (stato 1).
        await _fixture.DocService.CambiaStatoAsync(new CambioStato { Id = ddt.Document.Id, Stato = 1 });
        Assert.Equal(1, await _fixture.LeggiStatoAsync(documento!.id));

        var eccezione = await Assert.ThrowsAsync<Exception>(() => _fixture.DocService.AddDocAsync(
            filtri, json, ddt.FileName!, BollaFactory.AllegatoDiProva(), _fixture.Tenant));

        Assert.Contains("chiuso", eccezione.Message);
        Assert.Contains(ddt.Document.NumeroBolla, eccezione.Message);

        // Il documento chiuso e' ancora li': non e' stato sovrascritto.
        Assert.Single(await _fixture.DocService.GetDocsAsync(filtri));
    }

    [IntegrationFact]
    public async Task GetDocsAsync_ConStatoDefault_NonRestituisceIDocumentiChiusi()
    {
        var ddt = BollaFactory.CreaBolla(numeroBolla: BollaFactory.NumeroBollaUnivoco("STATO"));

        var documento = await _fixture.DocService.AddDocAsync(
            BollaFactory.FiltriPer(ddt), BollaFactory.ToJson(ddt), ddt.FileName!,
            BollaFactory.AllegatoDiProva(), _fixture.Tenant);
        Assert.NotNull(documento);

        // Con Stato = 0 (default dei filtri) il documento aperto si trova.
        Assert.Single(await _fixture.DocService.GetDocsAsync(BollaFactory.FiltriPer(ddt, stato: 0)));

        await _fixture.DocService.CambiaStatoAsync(new CambioStato { Id = ddt.Document.Id, Stato = 1 });

        // Ora non si trova piu' con Stato = 0, ma si trova con Stato = -1 ("tutti gli stati").
        Assert.Empty(await _fixture.DocService.GetDocsAsync(BollaFactory.FiltriPer(ddt, stato: 0)));
        Assert.Single(await _fixture.DocService.GetDocsAsync(BollaFactory.FiltriPer(ddt, stato: -1)));
    }

    [IntegrationFact]
    public async Task GetDocsAsync_FiltraMittenteENumeroInModoCaseInsensitive()
    {
        var numero = BollaFactory.NumeroBollaUnivoco("CaseTest");
        var ddt = BollaFactory.CreaBolla(mittente: "ACME Forniture SpA", numeroBolla: numero);

        var documento = await _fixture.DocService.AddDocAsync(
            BollaFactory.FiltriPer(ddt), BollaFactory.ToJson(ddt), ddt.FileName!,
            BollaFactory.AllegatoDiProva(), _fixture.Tenant);
        Assert.NotNull(documento);

        // Il case nel jsonb non e' normalizzato: la query deve applicare lower() su entrambi i lati.
        var filtriMinuscoli = new EmmaDocFilters
        {
            Fornitore = "acme forniture spa",
            NumeroDoc = numero.ToLowerInvariant(),
            DataDoc = ddt.Document.DataBolla,
            TipoDoc = int.Parse(ddt.Document.TipoDocumento),
            Stato = -1
        };

        var trovato = Assert.Single(await _fixture.DocService.GetDocsAsync(filtriMinuscoli));
        Assert.Equal(documento!.id, trovato!.id);

        var filtriMaiuscoli = new EmmaDocFilters
        {
            Fornitore = "ACME FORNITURE SPA",
            NumeroDoc = numero.ToUpperInvariant(),
            DataDoc = ddt.Document.DataBolla,
            TipoDoc = int.Parse(ddt.Document.TipoDocumento),
            Stato = -1
        };

        Assert.Single(await _fixture.DocService.GetDocsAsync(filtriMaiuscoli));
    }

    [IntegrationFact]
    public async Task CambiaTipoAsync_AggiornaIlTipoDocumentoNelJson()
    {
        var ddt = BollaFactory.CreaBolla(numeroBolla: BollaFactory.NumeroBollaUnivoco("TIPO"));

        var documento = await _fixture.DocService.AddDocAsync(
            BollaFactory.FiltriPer(ddt), BollaFactory.ToJson(ddt), ddt.FileName!,
            BollaFactory.AllegatoDiProva(), _fixture.Tenant);
        Assert.NotNull(documento);
        Assert.Equal(BollaFactory.TipoDocumentoBolla, await _fixture.LeggiCampoDocumentoAsync(documento!.id, "tipo_documento"));

        await _fixture.DocService.CambiaTipoAsync(new CambioTipo { Id = ddt.Document.Id, Tipo = 4 });

        Assert.Equal("4", await _fixture.LeggiCampoDocumentoAsync(documento.id, "tipo_documento"));

        // E ora si trova filtrando per tipo 4, non piu' per tipo 2.
        var filtriTipo4 = BollaFactory.FiltriPer(ddt);
        filtriTipo4.TipoDoc = 4;
        Assert.Single(await _fixture.DocService.GetDocsAsync(filtriTipo4));
        Assert.Empty(await _fixture.DocService.GetDocsAsync(BollaFactory.FiltriPer(ddt)));
    }

    [IntegrationFact]
    public async Task DeleteDocAsync_RimuoveLaBollaDalDatabase()
    {
        var ddt = BollaFactory.CreaBolla(numeroBolla: BollaFactory.NumeroBollaUnivoco("DELETE"));
        var filtri = BollaFactory.FiltriPer(ddt);

        var documento = await _fixture.DocService.AddDocAsync(
            filtri, BollaFactory.ToJson(ddt), ddt.FileName!,
            BollaFactory.AllegatoDiProva(), _fixture.Tenant);
        Assert.NotNull(documento);

        await _fixture.DocService.DeleteDocAsync(filtri);

        Assert.Empty(await _fixture.DocService.GetDocsAsync(filtri));
        Assert.Null(await _fixture.LeggiStatoAsync(documento!.id));
    }
}
