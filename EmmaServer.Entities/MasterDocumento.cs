namespace EmmaServer.Entities;

public class MasterDocumento
{
    public string? Fornitore { get; set; }
    public string? NumeroDocumento { get; set; }
    public string? DataDocumento { get; set; }
    public string? StatoDocumento { get; set; }
    
    public string? TipDocumento { get; set; }
    public List<RigheDocumento> Dettagli { get; set; } = new List<RigheDocumento>();
    
 
}

// Oggetto delle sotto-righe di dettaglio
public class RigheDocumento
{
    public string? CodiceArticolo { get; set; }
    
    public string? DescrizioneArticolo { get; set; }
    
    public string? UnitaMisura { get; set; }
    
    public double Qta { get; set; }
    
    public double Imponibile { get; set; }
    public string? IVA { get; set; }
    public double Totale { get; set; }
}