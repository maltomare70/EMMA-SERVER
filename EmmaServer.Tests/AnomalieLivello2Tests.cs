using EmmaServer.Background;
using EmmaServer.Entities;
using EmmaServer.Entities.Dtos;
using EmmaServer.Services.Anomalie;
using RigheDocumento = EmmaServer.Services.Anomalie.RigheDocumento;

namespace EmmaServer.Tests;

/// <summary>
/// Test puri (niente database) su statistica, costruzione della baseline e regole del livello 2.
/// Girano sempre, anche senza DB configurato.
/// </summary>
public class AnomalieLivello2Tests
{
    private static readonly OpzioniAnomalie Opzioni = new();

    // ---------------------------------------------------------------- statistica

    [Fact]
    public void Mediana_DispariEPari()
    {
        Assert.Equal(3, StatisticaRobusta.Mediana([5, 1, 3]));
        Assert.Equal(2.5, StatisticaRobusta.Mediana([4, 1, 3, 2]));
    }

    [Fact]
    public void Mad_IgnoraIlValoreAberrante()
    {
        double[] prezzi = [10, 10.2, 9.9, 10.1, 10, 9.8, 10000];
        var mediana = StatisticaRobusta.Mediana(prezzi);
        var mad = StatisticaRobusta.Mad(prezzi, mediana);

        Assert.Equal(10, mediana, 6);
        Assert.Equal(0.1, mad, 6);
    }

    // ---------------------------------------------------------------- estrazione righe

    [Fact]
    public void PrezzoUnitario_UsaImponibileSePresenteAltrimentiTotale()
    {
        Assert.Equal(5m, RigheDocumento.PrezzoUnitario(new ArticoloBolla { Quantita = 4, Imponibile = 20, Totale = 24.4m }));
        Assert.Equal(6.1m, RigheDocumento.PrezzoUnitario(new ArticoloBolla { Quantita = 4, Imponibile = 0, Totale = 24.4m }));
        Assert.Null(RigheDocumento.PrezzoUnitario(new ArticoloBolla { Quantita = 0, Totale = 10 }));
        Assert.Null(RigheDocumento.PrezzoUnitario(new ArticoloBolla { Quantita = 1, Totale = -3 }));
    }

    [Fact]
    public void DocumentoQuadra_AccettaTotaleConOSenzaIva()
    {
        var righe = new List<ArticoloBolla>
        {
            new() { Totale = 100, Iva = "22" },
            new() { Totale = 50, Iva = "10" },
        };

        Assert.True(RigheDocumento.DocumentoQuadra(new DatiBolla { Totale = 150, Articoli = righe }));
        Assert.True(RigheDocumento.DocumentoQuadra(new DatiBolla { Totale = 177, Articoli = righe }));      // 122 + 55
        Assert.True(RigheDocumento.DocumentoQuadra(new DatiBolla { Totale = 0, Imponibile = 0, Articoli = righe }));
        Assert.False(RigheDocumento.DocumentoQuadra(new DatiBolla { Totale = 1500, Articoli = righe }));
        Assert.False(RigheDocumento.DocumentoQuadra(new DatiBolla { Totale = 150, Articoli = [] }));
    }

    // ---------------------------------------------------------------- baseline

    [Fact]
    public void Baseline_UsaSoloLUmPrevalente()
    {
        var oggi = new DateOnly(2026, 9, 1);
        var storico = new List<OsservazioneStorico>();
        for (int i = 0; i < 6; i++)
            storico.Add(new OsservazioneStorico(7, "ART", 2, "PZ", 10, 2.00m + i * 0.01m, oggi.AddDays(-i)));
        storico.Add(new OsservazioneStorico(7, "ART", 2, "CT", 1, 24m, oggi)); // confezione: fuori dalla mediana

        var b = Assert.Single(CostruttoreBaseline.Costruisci("t", storico));

        Assert.Equal("PZ", b.um_prevalente);
        Assert.Equal(6, b.n);
        Assert.InRange(b.mediana_prezzo!.Value, 2.02m, 2.03m);
        Assert.Equal(Math.Round((decimal)Math.Log(10), 6), b.mediana_qta);
        Assert.Equal(0m, b.mad_qta);
        Assert.Equal(oggi.AddDays(-5).ToDateTime(TimeOnly.MinValue), b.primo_visto);
    }

    [Fact]
    public void Baseline_SeparaDdtEFatture()
    {
        var d = new DateOnly(2026, 9, 1);
        var storico = new[]
        {
            new OsservazioneStorico(1, "A", 2, "PZ", 1, 10, d),
            new OsservazioneStorico(1, "A", 4, "PZ", 1, 8, d),
        };
        Assert.Equal(2, CostruttoreBaseline.Costruisci("t", storico).Count);
    }

    // ---------------------------------------------------------------- livello 2

    private static EmmaAnomaliaBaseline Baseline(decimal mediana, decimal mad, int n = 14, string um = "PZ",
        double qtaAbituale = 10, double madLogQta = 0.2)
        => new()
        {
            n = n,
            mediana_prezzo = mediana,
            mad_prezzo = mad,
            mediana_qta = (decimal)Math.Log(qtaAbituale),
            mad_qta = (decimal)madLogQta,
            um_prevalente = um,
        };

    private static RigaDaValutare Riga(decimal prezzo, decimal qta = 10, string um = "PZ")
        => new("1", "ART-X", um, qta, prezzo);

    [Fact]
    public void PrezzoAlto_ProduceMessaggioConNumeri()
    {
        var avviso = Assert.Single(ValutatoreLivello2.Valuta(Riga(19.90m), Baseline(12.40m, 0.30m), Opzioni));

        Assert.Equal(TipoAnomalia.PrezzoAlto, avviso.tipo);
        Assert.Equal((short)3, avviso.severita);
        Assert.True(avviso.score > 3.5);
        Assert.Equal("ART-X — mediana 12,40 €/PZ su 14 bolle, questa 19,90 €/PZ (+60,5%)", avviso.messaggio);
    }

    [Fact]
    public void PrezzoBasso_Segnalato()
    {
        var avviso = Assert.Single(ValutatoreLivello2.Valuta(Riga(9m), Baseline(12.40m, 0.30m), Opzioni));
        Assert.Equal(TipoAnomalia.PrezzoBasso, avviso.tipo);
        Assert.Equal((short)2, avviso.severita);
    }

    [Fact]
    public void PrezzoNellaNorma_NessunAvviso()
        => Assert.Empty(ValutatoreLivello2.Valuta(Riga(12.60m), Baseline(12.40m, 0.30m), Opzioni));

    [Fact]
    public void ZAltoMaScostamentoMinimo_NessunAvviso()
    {
        // MAD di mezzo centesimo: 12,45 da' z ~ 6.7 ma e' +0,4%
        Assert.Empty(ValutatoreLivello2.Valuta(Riga(12.45m), Baseline(12.40m, 0.005m), Opzioni));
    }

    [Fact]
    public void MadZero_PrezzoFisso_SegnalaOltreL1PerCento()
    {
        var avviso = Assert.Single(ValutatoreLivello2.Valuta(Riga(13.10m), Baseline(12.40m, 0m), Opzioni));

        Assert.Equal(TipoAnomalia.PrezzoAlto, avviso.tipo);
        Assert.Null(avviso.score);
        Assert.StartsWith("ART-X — prezzo fisso a 12,40 €/PZ sulle ultime 14 bolle, questa riporta 13,10 €/PZ", avviso.messaggio);

        Assert.Empty(ValutatoreLivello2.Valuta(Riga(12.45m), Baseline(12.40m, 0m), Opzioni));
    }

    [Fact]
    public void PocheOsservazioni_NessunAvviso()
        => Assert.Empty(ValutatoreLivello2.Valuta(Riga(99m), Baseline(12.40m, 0.30m, n: 4), Opzioni));

    [Fact]
    public void SenzaBaseline_NessunAvviso()
        => Assert.Empty(ValutatoreLivello2.Valuta(Riga(99m), null, Opzioni));

    [Fact]
    public void UmCambiata_SospendeIlConfrontoDiPrezzo()
    {
        var avviso = Assert.Single(ValutatoreLivello2.Valuta(Riga(148m, 1, "CT"), Baseline(12.40m, 0.30m), Opzioni));
        Assert.Equal(TipoAnomalia.UmCambiata, avviso.tipo);
        Assert.Equal((short)1, avviso.severita);
    }

    [Fact]
    public void QuantitaAnomala_SuScalaLogaritmica()
    {
        // Abituale 10 con MAD(ln) 0.2: 100 pezzi -> z = 0.6745 * ln(10) / 0.2 ~ 7.8
        var avviso = Assert.Single(ValutatoreLivello2.Valuta(Riga(12.40m, 100), Baseline(12.40m, 0.30m), Opzioni));
        Assert.Equal(TipoAnomalia.QtaAnomala, avviso.tipo);
        Assert.Equal((short)1, avviso.severita);

        // 20 pezzi: z ~ 2.3, normale
        Assert.Empty(ValutatoreLivello2.Valuta(Riga(12.40m, 20), Baseline(12.40m, 0.30m), Opzioni));
    }

    [Fact]
    public void QuantitaFissa_SegnalaSoloOltreIlTriplo()
    {
        var b = Baseline(12.40m, 0.30m, madLogQta: 0);
        Assert.Empty(ValutatoreLivello2.Valuta(Riga(12.40m, 25), b, Opzioni));
        Assert.Single(ValutatoreLivello2.Valuta(Riga(12.40m, 40), b, Opzioni));
    }

    [Fact]
    public void FattureHannoIlLoroNomeNelMessaggio()
    {
        var avviso = Assert.Single(ValutatoreLivello2.Valuta(Riga(19.90m), Baseline(12.40m, 0.30m), Opzioni, "fatture"));
        Assert.Contains("su 14 fatture", avviso.messaggio);
    }

    // ---------------------------------------------------------------- anagrafiche

    [Fact]
    public void Risolutore_UsaRifcodiceEMatchCaseInsensitive()
    {
        var r = new RisolutoreAnagrafiche(
            [new FornitoreRif { id = 3, descrizione = "Rossi Forniture Srl" }],
            [new ArticoloRif { idfornitore = 3, codice = "ab-01", rifcodice = "INT-9" }]);

        Assert.Equal(3, r.Fornitore("ROSSI FORNITURE SRL "));
        Assert.Equal(0, r.Fornitore("Bianchi Spa"));
        Assert.Equal("INT-9", r.ChiaveArticolo(3, " AB-01"));
        Assert.Equal("AB-02", r.ChiaveArticolo(3, "ab-02"));
    }

    // ---------------------------------------------------------------- batch

    [Fact]
    public void Batch_AttesaFinoAllOraConfigurata()
    {
        var adesso = new DateTime(2026, 9, 15, 23, 30, 0, DateTimeKind.Utc);
        Assert.Equal(TimeSpan.FromMinutes(90), AnomalieBackgroundService.AttesaFinoAllaProssimaEsecuzione(adesso, 1));

        var mattina = new DateTime(2026, 9, 15, 0, 30, 0, DateTimeKind.Utc);
        Assert.Equal(TimeSpan.FromMinutes(30), AnomalieBackgroundService.AttesaFinoAllaProssimaEsecuzione(mattina, 1));
    }
}
