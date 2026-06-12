using Dapper.Contrib.Extensions;
namespace EmmaServer.Entities;


[Table("bolle_rows")] 
public record BolleRows: IEntity
{
    [Dapper.Contrib.Extensions.Key]
    public int id { get; init; }
    [Write(false)] 
    public DateTime data_creazione { get; init; }
    public int id_bolla { get; init; }
    public string codice { get; set; }
    public string descrizione { get; set; }
    public double qta { get; init; }
    public string um { get; set; }
    public double imponibile { get; init; }
    public string iva { get; set; }
    public double totale { get; init; }
}