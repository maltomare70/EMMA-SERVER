using System.Net;
using System.Text.Json;
using EmmaServer.Entities.Dtos;
using EmmaServer.Tests.Infrastructure;

namespace EmmaServer.Tests;

/// <summary>
/// Test sui due punti d'ingresso "veri" da cui nascono le bolle:
/// <list type="bullet">
///   <item><c>ImportDocAsync</c>: chiamata al servizio EMMA-AI (qui sostituito da uno stub HTTP);</item>
///   <item><c>ImportFatturaElettronicaAsync</c>: parsing di un XML FatturaPA.</item>
/// </list>
/// </summary>
[Collection(NomeCollezioneDatabase.Nome)]
public class DocServiceImportTests
{
    private readonly DocServiceFixture _fixture;

    public DocServiceImportTests(DocServiceFixture fixture)
    {
        _fixture = fixture;
    }

    [IntegrationFact]
    public async Task ImportDocAsync_ConRispostaAiValida_CreaLaBollaSulDatabase()
    {
        // Tipo 4 (fattura): cosi' il test resta concentrato sulla creazione del documento
        // e non tira dentro l'aggiornamento delle anagrafiche, che scatta solo per i DDT.
        var ddtAtteso = BollaFactory.CreaBolla(
            mittente: "Fornitore AI SpA",
            numeroBolla: BollaFactory.NumeroBollaUnivoco("AI"),
            tipoDocumento: BollaFactory.TipoDocumentoFattura);

        _fixture.HttpStub.Reset();
        _fixture.HttpStub.Rispondi = _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(ddtAtteso, BollaFactory.OpzioniJson),
                System.Text.Encoding.UTF8,
                "application/json")
        };

        var file = FakeFormFile.DaTesto("contenuto finto del pdf", "bolla-ai.pdf", "application/pdf");

        var risposta = await _fixture.DocService.ImportDocAsync(file, _fixture.Tenant);

        Assert.True(risposta.DocId > 0, "il documento deve essere stato salvato sul database");
        Assert.NotNull(risposta.DdtResponse);
        Assert.Equal(ddtAtteso.Document.NumeroBolla, risposta.DdtResponse!.Document.NumeroBolla);

        // Il servizio ha chiamato l'endpoint giusto con gli header richiesti.
        Assert.Equal(1, _fixture.HttpStub.NumeroChiamate);
        Assert.EndsWith("/api/v1/doc/ddt", _fixture.HttpStub.UltimoUrl);
        Assert.True(_fixture.HttpStub.UltimiHeader.ContainsKey("x-model"));
        Assert.True(_fixture.HttpStub.UltimiHeader.ContainsKey("X-API-Key"));

        // E la bolla c'e' davvero.
        var trovato = Assert.Single(await _fixture.DocService.GetDocsAsync(BollaFactory.FiltriPer(ddtAtteso)));
        Assert.Equal(risposta.DocId, trovato!.id);
        Assert.Equal("Fornitore AI SpA", trovato.ToDoc()!.Mittente);
    }

    [IntegrationFact]
    public async Task ImportDocAsync_SeIlServizioAiFallisce_SollevaApplicationException()
    {
        _fixture.HttpStub.Reset();
        _fixture.HttpStub.Rispondi = _ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("errore simulato")
        };

        var file = FakeFormFile.DaTesto("contenuto finto del pdf", "bolla-ko.pdf", "application/pdf");

        var eccezione = await Assert.ThrowsAsync<ApplicationException>(
            () => _fixture.DocService.ImportDocAsync(file, _fixture.Tenant));

        Assert.Contains("Internal server error", eccezione.Message);
    }

    [IntegrationFact]
    public async Task ImportDocAsync_ConDdt_AggiornaAncheFornitoriEArticoli()
    {
        var mittente = $"Fornitore DDT {Guid.NewGuid().ToString("N")[..8]}";
        var ddtAtteso = BollaFactory.CreaBolla(
            mittente: mittente,
            numeroBolla: BollaFactory.NumeroBollaUnivoco("DDT"),
            tipoDocumento: BollaFactory.TipoDocumentoBolla);

        _fixture.HttpStub.Reset();
        _fixture.HttpStub.Rispondi = _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(ddtAtteso, BollaFactory.OpzioniJson),
                System.Text.Encoding.UTF8,
                "application/json")
        };

        var file = FakeFormFile.DaTesto("contenuto finto del pdf", "ddt-ai.pdf", "application/pdf");

        var risposta = await _fixture.DocService.ImportDocAsync(file, _fixture.Tenant);

        Assert.True(risposta.DocId > 0);

        // Per i DDT (tipo 2) DocService crea/aggiorna anche l'anagrafica fornitore.
        var fornitori = await _fixture.FornitoriService.GetAllTenantAsync();
        Assert.Contains(fornitori, fornitore =>
            string.Equals(fornitore?.descrizione, mittente, StringComparison.InvariantCultureIgnoreCase));
    }

    [IntegrationFact]
    public async Task ImportFatturaElettronicaAsync_CreaLaBollaDaXml()
    {
        var numeroFattura = BollaFactory.NumeroBollaUnivoco("FT");
        var dataFattura = DateTime.UtcNow.ToString("yyyy-MM-dd");
        const string denominazioneFornitore = "ACME FORNITURE SPA";

        var xml = CostruisciFatturaXml(numeroFattura, dataFattura, denominazioneFornitore);
        var file = FakeFormFile.DaTesto(xml, $"IT01234567890_{numeroFattura}.xml", "application/xml");

        var risposta = await _fixture.DocService.ImportFatturaElettronicaAsync(file, _fixture.Tenant);

        Assert.True(risposta.DocId > 0);
        Assert.NotNull(risposta.DdtResponse);

        var documento = risposta.DdtResponse!.Document;
        Assert.Equal(denominazioneFornitore, documento.Mittente);
        Assert.Equal(numeroFattura, documento.NumeroBolla);
        Assert.Equal(dataFattura, documento.DataBolla);
        Assert.Equal(BollaFactory.TipoDocumentoFattura, documento.TipoDocumento);
        Assert.Equal(122d, documento.Totale);

        var riga = Assert.Single(documento.Articoli);
        Assert.Equal("ART-001", riga.Codice);
        Assert.Equal("Articolo di prova 1", riga.Descrizione);
        Assert.Equal(2m, riga.Quantita);
        Assert.Equal("PZ", riga.UnitaMisura);
        Assert.Equal(100m, riga.Totale);

        // La fattura e' finita davvero sulla tabella docs.
        var filtri = new EmmaDocFilters
        {
            Fornitore = denominazioneFornitore,
            NumeroDoc = numeroFattura,
            DataDoc = dataFattura,
            TipoDoc = 4,
            Stato = -1
        };

        var trovato = Assert.Single(await _fixture.DocService.GetDocsAsync(filtri));
        Assert.Equal(risposta.DocId, trovato!.id);
    }

    /// <summary>XML FatturaPA (FPR12) minimo ma valido, usato dal test di import.</summary>
    private static string CostruisciFatturaXml(string numero, string data, string denominazione) =>
        $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <p:FatturaElettronica versione="FPR12" xmlns:p="http://ivaservizi.agenziaentrate.gov.it/docs/xsd/fatture/v1.2">
          <FatturaElettronicaHeader>
            <DatiTrasmissione>
              <IdTrasmittente>
                <IdPaese>IT</IdPaese>
                <IdCodice>01234567890</IdCodice>
              </IdTrasmittente>
              <ProgressivoInvio>00001</ProgressivoInvio>
              <FormatoTrasmissione>FPR12</FormatoTrasmissione>
              <CodiceDestinatario>0000000</CodiceDestinatario>
            </DatiTrasmissione>
            <CedentePrestatore>
              <DatiAnagrafici>
                <IdFiscaleIVA>
                  <IdPaese>IT</IdPaese>
                  <IdCodice>01234567890</IdCodice>
                </IdFiscaleIVA>
                <Anagrafica>
                  <Denominazione>{denominazione}</Denominazione>
                </Anagrafica>
                <RegimeFiscale>RF01</RegimeFiscale>
              </DatiAnagrafici>
              <Sede>
                <Indirizzo>VIA ROMA 1</Indirizzo>
                <CAP>20100</CAP>
                <Comune>MILANO</Comune>
                <Provincia>MI</Provincia>
                <Nazione>IT</Nazione>
              </Sede>
            </CedentePrestatore>
            <CessionarioCommittente>
              <DatiAnagrafici>
                <CodiceFiscale>RSSMRA80A01H501U</CodiceFiscale>
                <Anagrafica>
                  <Denominazione>CLIENTE DI PROVA SRL</Denominazione>
                </Anagrafica>
              </DatiAnagrafici>
              <Sede>
                <Indirizzo>VIA MILANO 2</Indirizzo>
                <CAP>00100</CAP>
                <Comune>ROMA</Comune>
                <Provincia>RM</Provincia>
                <Nazione>IT</Nazione>
              </Sede>
            </CessionarioCommittente>
          </FatturaElettronicaHeader>
          <FatturaElettronicaBody>
            <DatiGenerali>
              <DatiGeneraliDocumento>
                <TipoDocumento>TD01</TipoDocumento>
                <Divisa>EUR</Divisa>
                <Data>{data}</Data>
                <Numero>{numero}</Numero>
                <ImportoTotaleDocumento>122.00</ImportoTotaleDocumento>
              </DatiGeneraliDocumento>
            </DatiGenerali>
            <DatiBeniServizi>
              <DettaglioLinee>
                <NumeroLinea>1</NumeroLinea>
                <CodiceArticolo>
                  <CodiceTipo>FORNITORE</CodiceTipo>
                  <CodiceValore>ART-001</CodiceValore>
                </CodiceArticolo>
                <Descrizione>Articolo di prova 1</Descrizione>
                <Quantita>2.00</Quantita>
                <UnitaMisura>PZ</UnitaMisura>
                <PrezzoUnitario>50.00</PrezzoUnitario>
                <PrezzoTotale>100.00</PrezzoTotale>
                <AliquotaIVA>22.00</AliquotaIVA>
              </DettaglioLinee>
              <DatiRiepilogo>
                <AliquotaIVA>22.00</AliquotaIVA>
                <ImponibileImporto>100.00</ImponibileImporto>
                <Imposta>22.00</Imposta>
                <EsigibilitaIVA>I</EsigibilitaIVA>
              </DatiRiepilogo>
            </DatiBeniServizi>
          </FatturaElettronicaBody>
        </p:FatturaElettronica>
        """;
}
