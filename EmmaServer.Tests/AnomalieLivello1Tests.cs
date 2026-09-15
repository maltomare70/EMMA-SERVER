using EmmaServer.Entities;
using EmmaServer.Entities.Dtos;
using EmmaServer.Services.Anomalie;

namespace EmmaServer.Tests;

/// <summary>Test puri (niente database) delle regole di livello 1.</summary>
public class AnomalieLivello1Tests
{
    private static readonly DateOnly Oggi = new(2026, 9, 15);

    private static ArticoloBolla Riga(string id, string codice = "ART-1", decimal qta = 2, decimal imponibile = 0,
        decimal totale = 20, string iva = "22", string um = "PZ")
        => new()
        {
            Id_Riga = id, Codice = codice, Quantita = qta, Imponibile = imponibile,
            Totale = totale, Iva = iva, UnitaMisura = um, Descrizione = "test"
        };

    private static DatiBolla Bolla(List<ArticoloBolla> righe, double? totale = null, string tipo = "2",
        string data = "2026-09-10", string numero = "B-1")
        => new()
        {
            Id = "master",
            TipoDocumento = tipo,
            NumeroBolla = numero,
            DataBolla = data,
            Mittente = "Fornitore Srl",
            Articoli = righe,
            Totale = totale ?? (double)righe.Sum(r => r.Totale),
        };

    [Fact]
    public void DocumentoPulito_NessunAvviso()
        => Assert.Empty(ValutatoreLivello1.Valuta(Bolla([Riga("1"), Riga("2", "ART-2", totale: 30)]), Oggi));

    [Fact]
    public void TotaliCheNonQuadrano_AvvisoBloccanteConNumeri()
    {
        var avviso = Assert.Single(ValutatoreLivello1.Valuta(Bolla([Riga("1")], totale: 200), Oggi));

        Assert.Equal(TipoAnomalia.IncoerenzaTotali, avviso.tipo);
        Assert.Equal((short)3, avviso.severita);
        Assert.Contains(TipoAnomalia.IncoerenzaTotali, TipoAnomalia.BloccantiDocumento);
        Assert.Equal("Totali non quadrano: somma righe 20,00 €, totale documento 200,00 € (differenza -180,00 €). " +
                     "I prezzi di questo documento non vengono confrontati con lo storico", avviso.messaggio);
    }

    [Fact]
    public void DocumentoSenzaRighe_Bloccante()
    {
        var avviso = Assert.Single(ValutatoreLivello1.Valuta(Bolla([], totale: 0), Oggi));
        Assert.Equal(TipoAnomalia.IncoerenzaTotali, avviso.tipo);
    }

    [Theory]
    [InlineData("2026-09-20", 2)]   // futuro
    [InlineData("2025-01-10", 1)]   // oltre 18 mesi
    [InlineData("", 2)]             // mancante
    [InlineData("32/13/2026", 2)]   // illeggibile
    public void DataAnomala(string data, int severita)
    {
        var avviso = Assert.Single(ValutatoreLivello1.Valuta(Bolla([Riga("1")], data: data), Oggi));
        Assert.Equal(TipoAnomalia.DataAnomala, avviso.tipo);
        Assert.Equal(severita, (int)avviso.severita);
    }

    [Fact]
    public void DataDiDomani_TolleratoPerIFusiOrari()
        => Assert.Empty(ValutatoreLivello1.Valuta(Bolla([Riga("1")], data: "2026-09-16"), Oggi));

    [Fact]
    public void QuantitaZeroCodiceEUmVuoti_UnSoloAvvisoPerRiga()
    {
        var righe = new List<ArticoloBolla> { Riga("1", codice: "", qta: 0, um: "") };
        var avviso = Assert.Single(ValutatoreLivello1.Valuta(Bolla(righe), Oggi), a => a.tipo == TipoAnomalia.ValoreImpossibile);

        Assert.Equal((short)2, avviso.severita);
        Assert.Equal("1", avviso.id_riga);
        Assert.Equal("Riga 1 — quantità 0, codice articolo vuoto, unità di misura vuota", avviso.messaggio);
    }

    [Fact]
    public void DdtSenzaPrezzi_NonSegnalaIPrezziMancanti()
    {
        var righe = new List<ArticoloBolla> { Riga("1", totale: 0), Riga("2", "ART-2", totale: 0) };
        Assert.Empty(ValutatoreLivello1.Valuta(Bolla(righe, totale: 0), Oggi));
    }

    [Fact]
    public void PrezzoMancanteSuUnaSolaRiga_Segnalato()
    {
        var righe = new List<ArticoloBolla> { Riga("1"), Riga("2", "ART-2", totale: 0) };
        var avviso = Assert.Single(ValutatoreLivello1.Valuta(Bolla(righe), Oggi));

        Assert.Equal(TipoAnomalia.ValoreImpossibile, avviso.tipo);
        Assert.Equal("2", avviso.id_riga);
        Assert.Contains("prezzo mancante", avviso.messaggio);
    }

    [Fact]
    public void Fattura_CodiceEUmFacoltativi()
    {
        var righe = new List<ArticoloBolla> { Riga("1", codice: "", um: "") };
        Assert.Empty(ValutatoreLivello1.Valuta(Bolla(righe, tipo: "4"), Oggi));
    }

    [Theory]
    [InlineData(10.0, 20.0, 2.0)]       // imponibile = prezzo unitario, totale = imp x qta
    [InlineData(20.0, 20.0, 2.0)]       // imponibile = importo di riga
    [InlineData(20.0, 24.40, 2.0)]    // importo di riga, totale con IVA 22%
    [InlineData(10.0, 24.40, 2.0)]    // prezzo unitario, totale con IVA
    public void RigaCoerente_InTutteLeLetture(double imponibile, double totale, double qta)
    {
        var riga = Riga("1", qta: (decimal)qta, imponibile: (decimal)imponibile, totale: (decimal)totale);
        Assert.DoesNotContain(ValutatoreLivello1.Valuta(Bolla([riga]), Oggi), a => a.tipo == TipoAnomalia.RigaIncoerente);
    }

    [Fact]
    public void RigaIncoerente_Segnalata()
    {
        var riga = Riga("1", qta: 2, imponibile: 10, totale: 55);
        var avviso = Assert.Single(ValutatoreLivello1.Valuta(Bolla([riga]), Oggi));

        Assert.Equal(TipoAnomalia.RigaIncoerente, avviso.tipo);
        Assert.Contains(TipoAnomalia.RigaIncoerente, TipoAnomalia.BloccantiRiga);
        Assert.Equal(24.4m, avviso.atteso);   // la lettura piu' vicina: 10 x 2 con IVA
    }

    [Theory]
    [InlineData("22", false)]
    [InlineData("22,00", false)]
    [InlineData("10%", false)]
    [InlineData("0", false)]
    [InlineData("", false)]
    [InlineData("21", true)]
    [InlineData("esente", true)]
    public void AliquotaIva(string iva, bool segnalata)
    {
        var avvisi = ValutatoreLivello1.Valuta(Bolla([Riga("1", iva: iva)]), Oggi);
        Assert.Equal(segnalata, avvisi.Any(a => a.tipo == TipoAnomalia.AliquotaIva));
    }

    [Fact]
    public void RigheGemelleConQuantitaDiverse()
    {
        var righe = new List<ArticoloBolla>
        {
            Riga("1", "ART-1", qta: 2, totale: 20),
            Riga("2", "art-1 ", qta: 5, totale: 50),
            Riga("3", "ART-2", qta: 1, totale: 10),
            Riga("4", "ART-2", qta: 1, totale: 10),   // stessa quantita': non e' un errore evidente
        };

        var avviso = Assert.Single(ValutatoreLivello1.Valuta(Bolla(righe), Oggi));
        Assert.Equal(TipoAnomalia.RigheGemelle, avviso.tipo);
        Assert.Equal("2", avviso.id_riga);
        Assert.Equal("ART-1 — compare 2 volte nel documento con quantità diverse (2, 5)", avviso.messaggio);
    }

    [Fact]
    public void Duplicato_StessoContenutoNumeroDiverso()
    {
        var originale = Bolla([Riga("1"), Riga("2", "ART-2", totale: 30)], numero: "B-1", data: "2026-09-01");
        // Stesse righe in ordine diverso e con id diversi: stesso contenuto.
        var copia = Bolla([Riga("9", "ART-2", totale: 30), Riga("8")], numero: "B-2");

        var altri = new[]
        {
            new DocumentoSimile(41, "B-1", "2026-09-01", ValutatoreLivello1.ImprontaContenuto(originale)!),
        };

        var avviso = ValutatoreLivello1.Duplicato(copia, altri);
        Assert.NotNull(avviso);
        Assert.Equal(TipoAnomalia.Duplicato, avviso!.tipo);
        Assert.Equal(41m, avviso.atteso);
        Assert.StartsWith("Stesso contenuto del documento n. B-1 del 01/09/2026 (id 41)", avviso.messaggio);
    }

    [Fact]
    public void Duplicato_StessoNumeroOContenutoDiverso_NonSegnalato()
    {
        var originale = Bolla([Riga("1")], numero: "B-1");
        var impronta = ValutatoreLivello1.ImprontaContenuto(originale)!;

        Assert.Null(ValutatoreLivello1.Duplicato(Bolla([Riga("1")], numero: "b-1"),
            [new DocumentoSimile(1, "B-1", "2026-09-10", impronta)]));

        Assert.Null(ValutatoreLivello1.Duplicato(Bolla([Riga("1", totale: 21)], numero: "B-2"),
            [new DocumentoSimile(1, "B-1", "2026-09-10", impronta)]));
    }
}
