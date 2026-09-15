using EmmaServer.Entities;
using EmmaServer.Entities.Dtos;
using EmmaServer.Tests.Infrastructure;
using Npgsql;

namespace EmmaServer.Tests;

/// <summary>
/// Test di integrazione del livello 2: storico vero sul database, baseline ricalcolata, analisi di un
/// documento nuovo. Fornitore e codice articolo sono univoci per esecuzione, cosi' la serie storica
/// non si mescola con quelle dei run precedenti (i dati restano sul DB, come negli altri test).
/// </summary>
[Collection(NomeCollezioneDatabase.Nome)]
public class AnomalieServiceTests
{
    private readonly DocServiceFixture _fixture;

    public AnomalieServiceTests(DocServiceFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>Le tabelle del modulo potrebbero non esserci ancora sul DB dei test: la migrazione e' idempotente.</summary>
    private async Task ApplicaMigrazioneAsync()
    {
        var percorso = Path.Combine(AppContext.BaseDirectory, "Migrations", "sql_003.sql");
        await using var connessione = new NpgsqlConnection(_fixture.StringaDiConnessione);
        await connessione.OpenAsync();
        await using var comando = new NpgsqlCommand(await File.ReadAllTextAsync(percorso), connessione);
        await comando.ExecuteNonQueryAsync();
    }

    private async Task<int> CreaDdtAsync(string mittente, string codice, decimal prezzo, decimal quantita = 5m, string um = "PZ",
        double? totaleDocumento = null)
    {
        var idMaster = Guid.NewGuid().ToString();
        var riga = new ArticoloBolla
        {
            Id_Master = idMaster,
            Id_Riga = "1",
            Codice = codice,
            Descrizione = "Articolo per test anomalie",
            Quantita = quantita,
            UnitaMisura = um,
            Imponibile = 0m,
            Iva = "22",
            Totale = prezzo * quantita,
        };

        var ddt = BollaFactory.CreaBolla(
            mittente: mittente,
            numeroBolla: BollaFactory.NumeroBollaUnivoco("ANOM"),
            idMaster: idMaster,
            articoli: [riga]);
        ddt.Document.Imponibile = totaleDocumento ?? (double)riga.Totale;
        ddt.Document.Totale = totaleDocumento ?? (double)riga.Totale;

        var documento = await _fixture.DocService.AddDocAsync(
            BollaFactory.FiltriPer(ddt), BollaFactory.ToJson(ddt), ddt.FileName!,
            BollaFactory.AllegatoDiProva(), _fixture.Tenant);
        Assert.NotNull(documento);

        // Come fa ImportDocAsync: senza anagrafica fornitore il livello 2 non si aggancia.
        await _fixture.DocService.AddOrUpdateFornitorieArticoli(documento!.id);
        return documento.id;
    }

    [IntegrationFact]
    public async Task PrezzoFuoriScala_GeneraAvvisoDiLivello2()
    {
        await ApplicaMigrazioneAsync();

        var suffisso = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var mittente = $"Fornitore Anomalie {suffisso} Srl";
        var codice = $"ANOM-{suffisso}";

        foreach (var prezzo in new[] { 10.00m, 10.20m, 9.90m, 10.10m, 10.00m, 9.80m })
            await CreaDdtAsync(mittente, codice, prezzo);

        var esito = await _fixture.AnomalieService.RicalcolaBaselineAsync(_fixture.Tenant);
        Assert.True(esito.ChiaviBaseline > 0);

        // Prezzo nella norma: nessun avviso
        var normale = await CreaDdtAsync(mittente, codice, 10.05m);
        var avvisiNormale = await _fixture.AnomalieService.AnalizzaDocumentoAsync(normale);
        Assert.NotNull(avvisiNormale);
        Assert.Empty(avvisiNormale!);

        // Prezzo +60%: avviso di prezzo alto con severita' massima, scritto sul DB
        var caro = await CreaDdtAsync(mittente, codice, 16.00m);
        var avvisi = await _fixture.AnomalieService.AnalizzaDocumentoAsync(caro);
        var avviso = Assert.Single(avvisi!);
        Assert.Equal(TipoAnomalia.PrezzoAlto, avviso.tipo);
        Assert.Equal((short)3, avviso.severita);
        Assert.Contains("mediana 10,00 €/PZ su 6 bolle", avviso.messaggio);

        var salvati = await _fixture.AnomalieService.GetAnomalieDocumentoAsync(_fixture.Tenant, caro);
        Assert.Equal(TipoAnomalia.PrezzoAlto, Assert.Single(salvati).tipo);

        // Rianalizzare non duplica
        await _fixture.AnomalieService.AnalizzaDocumentoAsync(caro);
        Assert.Single(await _fixture.AnomalieService.GetAnomalieDocumentoAsync(_fixture.Tenant, caro));

        // Cambio di unita' di misura: niente confronto di prezzo, solo avviso um_cambiata
        var cartone = await CreaDdtAsync(mittente, codice, 120m, 1m, "CT");
        var avvisiCt = await _fixture.AnomalieService.AnalizzaDocumentoAsync(cartone);
        Assert.Equal(TipoAnomalia.UmCambiata, Assert.Single(avvisiCt!).tipo);
    }

    [IntegrationFact]
    public async Task TotaliCheNonQuadrano_SoloLivello1ELivello2Saltato()
    {
        await ApplicaMigrazioneAsync();

        var suffisso = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var mittente = $"Fornitore Quadratura {suffisso} Srl";
        var codice = $"QUAD-{suffisso}";

        foreach (var prezzo in new[] { 10.00m, 10.20m, 9.90m, 10.10m, 10.00m, 9.80m })
            await CreaDdtAsync(mittente, codice, prezzo);
        await _fixture.AnomalieService.RicalcolaBaselineAsync(_fixture.Tenant);

        // Prezzo fuori scala MA totale documento sbagliato: l'estrazione non e' affidabile,
        // quindi solo l'avviso di quadratura e nessun avviso di prezzo.
        var id = await CreaDdtAsync(mittente, codice, 16.00m, totaleDocumento: 999);
        var avvisi = await _fixture.AnomalieService.AnalizzaDocumentoAsync(id);

        var avviso = Assert.Single(avvisi!);
        Assert.Equal(TipoAnomalia.IncoerenzaTotali, avviso.tipo);
        Assert.Equal((short)1, avviso.livello);

        var salvati = await _fixture.AnomalieService.GetAnomalieDocumentoAsync(_fixture.Tenant, id);
        Assert.DoesNotContain(salvati, a => a.livello == 2);
    }

    [IntegrationFact]
    public async Task StessoContenutoConNumeroDiverso_Duplicato()
    {
        await ApplicaMigrazioneAsync();

        var suffisso = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var mittente = $"Fornitore Duplicati {suffisso} Srl";
        var codice = $"DUP-{suffisso}";

        var primo = await CreaDdtAsync(mittente, codice, 7.50m);
        var secondo = await CreaDdtAsync(mittente, codice, 7.50m);

        var avvisi = await _fixture.AnomalieService.AnalizzaDocumentoAsync(secondo);
        var duplicato = Assert.Single(avvisi!, a => a.tipo == TipoAnomalia.Duplicato);
        Assert.Equal((decimal)primo, duplicato.atteso);
        Assert.Equal((short)3, duplicato.severita);
    }

    private async Task<(string Mittente, string Codice)> CreaStoricoAsync(string prefisso)
    {
        var suffisso = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var mittente = $"Fornitore {prefisso} {suffisso} Srl";
        var codice = $"{prefisso.ToUpperInvariant()}-{suffisso}";

        foreach (var prezzo in new[] { 10.00m, 10.20m, 9.90m, 10.10m, 10.00m, 9.80m })
            await CreaDdtAsync(mittente, codice, prezzo);
        await _fixture.AnomalieService.RicalcolaBaselineAsync(_fixture.Tenant);

        return (mittente, codice);
    }

    [IntegrationFact]
    public async Task Lista_Feedback_Statistiche()
    {
        await ApplicaMigrazioneAsync();
        var servizio = _fixture.AnomalieService;
        var (mittente, codice) = await CreaStoricoAsync("Feedback");

        var caro = await CreaDdtAsync(mittente, codice, 16.00m);
        await servizio.AnalizzaDocumentoAsync(caro);

        // Lista filtrata sul documento, con i dati del documento gia' agganciati
        var pagina = await servizio.GetAnomalieAsync(_fixture.Tenant, new FiltriAnomalie { DocId = caro });
        Assert.Equal(1, pagina.Totale);
        var riga = Assert.Single(pagina.Righe);
        Assert.Equal(TipoAnomalia.PrezzoAlto, riga.tipo);
        Assert.Equal(mittente, riga.mittente);
        Assert.Equal("2", riga.tipo_documento);
        Assert.False(string.IsNullOrEmpty(riga.numero_documento));

        // Feedback: confermata
        var aggiornata = await servizio.RegistraFeedbackAsync(_fixture.Tenant, riga.id,
            new FeedbackAnomalia { Stato = 1, Note = "  listino sbagliato  " }, "operatore.test");
        Assert.NotNull(aggiornata);
        Assert.Equal((short)1, aggiornata!.stato);
        Assert.Equal("operatore.test", aggiornata.utente_feedback);
        Assert.Equal("listino sbagliato", aggiornata.note);
        Assert.NotNull(aggiornata.data_feedback);

        // Filtri per stato
        Assert.Empty((await servizio.GetAnomalieAsync(_fixture.Tenant, new FiltriAnomalie { DocId = caro, Stato = 0 })).Righe);
        Assert.Single((await servizio.GetAnomalieAsync(_fixture.Tenant, new FiltriAnomalie { DocId = caro, Stato = 1 })).Righe);

        // Rianalizzare non cancella l'avviso gia' valutato e non lo duplica
        await servizio.AnalizzaDocumentoAsync(caro);
        var dopo = Assert.Single(await servizio.GetAnomalieDocumentoAsync(_fixture.Tenant, caro));
        Assert.Equal(riga.id, dopo.id);
        Assert.Equal((short)1, dopo.stato);

        // Statistiche: il tenant di test accumula dati fra un run e l'altro, quindi solo minimi
        var statistiche = await servizio.GetStatisticheAsync(_fixture.Tenant, DateTime.Today.AddDays(-1), DateTime.Today.AddDays(1), livello: 2);
        var prezzoAlto = Assert.Single(statistiche.PerTipo, s => s.Tipo == TipoAnomalia.PrezzoAlto);
        Assert.True(prezzoAlto.Confermate >= 1);
        Assert.NotNull(prezzoAlto.Precisione);
        Assert.True(statistiche.Complessivo.Totali >= prezzoAlto.Totali);

        // Riapertura: il feedback sparisce
        var riaperta = await servizio.RegistraFeedbackAsync(_fixture.Tenant, riga.id, new FeedbackAnomalia { Stato = 0 }, "operatore.test");
        Assert.Equal((short)0, riaperta!.stato);
        Assert.Null(riaperta.utente_feedback);
        Assert.Null(riaperta.data_feedback);

        // Altro tenant e valori non validi
        Assert.Null(await servizio.RegistraFeedbackAsync("tenant-che-non-esiste", riga.id, new FeedbackAnomalia { Stato = 1 }, "x"));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            servizio.RegistraFeedbackAsync(_fixture.Tenant, riga.id, new FeedbackAnomalia { Stato = 5 }, "x"));
    }

    [IntegrationFact]
    public async Task IgnorareLaQuadratura_SbloccaIlLivello2()
    {
        await ApplicaMigrazioneAsync();
        var servizio = _fixture.AnomalieService;
        var (mittente, codice) = await CreaStoricoAsync("Sblocco");

        var id = await CreaDdtAsync(mittente, codice, 16.00m, totaleDocumento: 999);
        var quadratura = Assert.Single((await servizio.AnalizzaDocumentoAsync(id))!);
        Assert.Equal(TipoAnomalia.IncoerenzaTotali, quadratura.tipo);

        var salvata = Assert.Single(await servizio.GetAnomalieDocumentoAsync(_fixture.Tenant, id));

        // L'operatore dice che il documento e' giusto: i prezzi ora vanno confrontati con lo storico.
        await servizio.RegistraFeedbackAsync(_fixture.Tenant, salvata.id, new FeedbackAnomalia { Stato = 2 }, "operatore.test");

        var avvisi = await servizio.GetAnomalieDocumentoAsync(_fixture.Tenant, id);
        Assert.Contains(avvisi, a => a.tipo == TipoAnomalia.IncoerenzaTotali && a.stato == 2);
        Assert.Contains(avvisi, a => a.tipo == TipoAnomalia.PrezzoAlto && a.livello == 2);
    }

    [IntegrationFact]
    public async Task DocumentoDiAltroTenant_NonVieneAnalizzato()
    {
        await ApplicaMigrazioneAsync();

        var suffisso = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var id = await CreaDdtAsync($"Fornitore Tenant {suffisso} Srl", $"T-{suffisso}", 1m);

        Assert.Null(await _fixture.AnomalieService.AnalizzaDocumentoAsync(id, tenantAtteso: "tenant-che-non-esiste"));
    }
}
