using Dapper.Contrib.Extensions;
namespace EmmaServer.Entities;

[Table("fornitori")] 
public record EmmaFornitori: IEntity
{
    [Dapper.Contrib.Extensions.Key]
    public int id { get; init; }
    public string descrizione { get; init; } = string.Empty;
    [Write(false)] 
    public DateTime data_creazione { get; init; } = DateTime.UtcNow;
    public string riferimento{ get; init; } = string.Empty;
    public string tenant { get; set; } = string.Empty;
    public int score { get; set; } = 0;
}