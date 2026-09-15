using EmmaServer.Entities.Dtos;
using EmmaServer.Services;

namespace EmmaServer.Tests;

/// <summary>Test puri (niente database) della logica dietro lista e statistiche.</summary>
public class AnomalieApiTests
{
    [Fact]
    public void Filtri_LimitEOffsetNormalizzati()
    {
        var f = new FiltriAnomalie { Limit = 0, Offset = -5 };
        AnomalieService.NormalizzaFiltri(f);
        Assert.Equal(100, f.Limit);
        Assert.Equal(0, f.Offset);

        f = new FiltriAnomalie { Limit = 10_000 };
        AnomalieService.NormalizzaFiltri(f);
        Assert.Equal(AnomalieService.LimiteMassimoPagina, f.Limit);
    }

    [Fact]
    public void Filtri_DataFinaleIncludeTuttoIlGiorno()
    {
        var f = new FiltriAnomalie { A = new DateTime(2026, 9, 15) };
        AnomalieService.NormalizzaFiltri(f);
        Assert.Equal(new DateTime(2026, 9, 16), f.A);

        // Con un orario esplicito il limite resta quello indicato
        f = new FiltriAnomalie { A = new DateTime(2026, 9, 15, 12, 30, 0) };
        AnomalieService.NormalizzaFiltri(f);
        Assert.Equal(new DateTime(2026, 9, 15, 12, 30, 0), f.A);
    }

    [Theory]
    [InlineData(3, null, null, null)]
    [InlineData(null, 4, null, null)]
    [InlineData(null, null, "2026-09-10", "2026-09-01")]
    public void Filtri_NonValidi(int? stato, int? severita, string? da, string? a)
    {
        var f = new FiltriAnomalie
        {
            Stato = (short?)stato,
            Severita = (short?)severita,
            Da = da is null ? null : DateTime.Parse(da),
            A = a is null ? null : DateTime.Parse(a),
        };
        Assert.Throws<ArgumentException>(() => AnomalieService.NormalizzaFiltri(f));
    }

    [Fact]
    public void Precisione_SoloSulleValutate()
    {
        var s = new StatisticaAnomalie { Totali = 20, Aperte = 16, Confermate = 3, Ignorate = 1 };
        AnomalieService.CalcolaPrecisione(s);
        Assert.Equal(0.75, s.Precisione);
        Assert.False(s.SottoSoglia);
    }

    [Fact]
    public void Precisione_NullSenzaFeedback()
    {
        var s = new StatisticaAnomalie { Totali = 5, Aperte = 5 };
        AnomalieService.CalcolaPrecisione(s);
        Assert.Null(s.Precisione);
        Assert.False(s.SottoSoglia);
    }

    [Fact]
    public void SottoSoglia_SoloConAbbastanzaFeedback()
    {
        // 2 su 10 = 20%: sotto il 30% con 10 feedback -> allarme
        var molti = new StatisticaAnomalie { Confermate = 2, Ignorate = 8 };
        AnomalieService.CalcolaPrecisione(molti);
        Assert.True(molti.SottoSoglia);

        // Stessa percentuale ma solo 5 feedback: troppo pochi per dirlo
        var pochi = new StatisticaAnomalie { Confermate = 1, Ignorate = 4 };
        AnomalieService.CalcolaPrecisione(pochi);
        Assert.False(pochi.SottoSoglia);
    }
}
