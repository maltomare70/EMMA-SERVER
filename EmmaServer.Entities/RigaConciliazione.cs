
namespace EmmaServer.Entities;

public class PayloadRiconciliazione
{
    public List<RigaConciliazione> bolle { get; set; } = new();
    public List<RigaConciliazione> fatture { get; set; } = new();
}


/// <summary>DTO risultato fuzzy matching da Python.</summary>
public class FuzzyMatchResult
{
    public string? bolla_id { get; set; }
    public string? fattura_id { get; set; }
    public double confidence { get; set; }
}

public class PayloadRiconciliazioneFuzzy
{
    public List<RigaConciliazione> bolle { get; set; } = new();
    public List<RigaConciliazione> fatture { get; set; } = new();
}
public class RigaConciliazione
{
    public bool Selezionato { get; set; }

    public string? IdMaster { get; set; }
    public string? IdRiga { get; set; }

    public string? Fornitore { get; set; }
    public string? NumeroDocumento { get; set; }
    public string? DataDocumento { get; set; }
    public string? TipoDocumento { get; set; }

    public string? CodiceArticolo { get; set; }
    public string? DescrizioneArticolo { get; set; }
    public string? UnitaMisura { get; set; }
    public double Qta { get; set; }

    /// <summary>Data documento convertita, quando interpretabile.</summary>
    public DateTime? Data { get; set; }
}
