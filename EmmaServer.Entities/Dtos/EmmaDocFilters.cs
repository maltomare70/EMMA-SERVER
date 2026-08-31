namespace EmmaServer.Entities.Dtos;
public enum TipoDocEnum
{
    Ordine = 1,
    DDT = 2,
    FatturaAccompgnatario = 3,
    Fattura = 4,
    NotaDiAccredito = 5

}
public class EmmaDocFilters
{
    public string Fornitore { get; set; } = string.Empty;
    public string NumeroDoc { get; set; } = string.Empty;
    public string DataDoc { get; set; } = string.Empty;
    
    public int TipoDoc { get; set; } = 0;
    public int Stato { get; set; } = 0;

    public string Id { get; set; } = string.Empty;
}

public class InfoConciliazione
{
    public string Id { get; set; } = string.Empty;
    public string IdBolla { get; set; } = string.Empty;
    public string IdFattura { get; set; } = string.Empty;
}


public class CambioStato
{
    public string Id { get; set; } = string.Empty;
    public int Stato { get; set; } = 0;
}

public class CambioTipo
{
    public string Id { get; set; } = string.Empty;
    public int Tipo { get; set; } = 0;
}