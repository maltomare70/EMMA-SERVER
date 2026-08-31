namespace EmmaServer.Entities.Dtos;

public class PayloadRiconciliazione
{
    public string codice { get; set; } = string.Empty;
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


public class RigaConciliazione
{
    public bool Selezionato { get; set; }
    public string? Note { get; set; }

    public string? Stato { get; set; }

    public string? IdMaster { get; set; }
    public string? IdRiga { get; set; }

    public string? Fornitore { get; set; }
    public string? NumeroDocumento { get; set; }
    public string? DataDocumento { get; set; }
    public string? TipoDocumento { get; set; }

    public string? CodiceArticolo { get; set; }
    public string? DescrizioneArticolo { get; set; }
    public string? UnitaMisura { get; set; }
    public decimal Qta { get; set; }
    public decimal Qta_Conc { get; set; } = 0;

    /// <summary>Data documento convertita, quando interpretabile.</summary>
    public DateTime? Data { get; set; }
}
