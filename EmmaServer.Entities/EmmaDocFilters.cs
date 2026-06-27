namespace EmmaServer.Entities;

public class EmmaDocFilters
{
    public string Fornitore { get; set; } = string.Empty;
    public string NumeroDoc { get; set; } = string.Empty;
    public string DataDoc { get; set; } = string.Empty;
    
    public int TipoDoc { get; set; } = 0;
    public int Stato { get; set; } = 0;
}