using System.Collections.ObjectModel;
namespace EmmaServer.Entities;

public class MasterDocumento
{
    public string? Id { get; set; }
    public string? Fornitore { get; set; }
    public string? NumeroDocumento { get; set; }
    public string? DataDocumento { get; set; }
    public string? StatoDocumento { get; set; }
    public string TestoBottone => StatoDocumento == "Aperto" ? "Chiudi" : "Apri";
    public string? TipDocumento { get; set; }
    public byte[]? Allegato { get; set; }
    public ObservableCollection<RigheDocumento> Dettagli { get; set; } = new ObservableCollection<RigheDocumento>();
    
 
}

// Oggetto delle sotto-righe di dettaglio
public class RigheDocumento
{
    public string? IdRiga { get; set; }
    public string? IdMaster { get; set; }
    public string? CodiceArticolo { get; set; }
    
    public string? DescrizioneArticolo { get; set; }
    
    public string? UnitaMisura { get; set; }
    
    public double Qta { get; set; }
    
    public double Imponibile { get; set; }
    public string? IVA { get; set; }
    public double Totale { get; set; }
    
    public string TestoBottone => IdRiga == "" ? "Aggiungi" : "Elimina";
}