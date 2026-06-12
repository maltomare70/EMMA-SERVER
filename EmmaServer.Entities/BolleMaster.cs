using Dapper.Contrib.Extensions;
namespace EmmaServer.Entities;

[Table("bolle_master")]
public record BolleMaster : IEntity
{
    [Dapper.Contrib.Extensions.Key]
    public int id { get; init; }
    [Write(false)] 
    public DateTime data_creazione { get; init; }
    public int id_bolla { get; init; }
    public string fornitore { get; set; }
    public string numero_bolla { get; set; }
    public string data_bolla { get; set; }
    public int tipo_doc { get; init; }
    public double imponibile { get; init; }
    public double totale { get; init; }
}


