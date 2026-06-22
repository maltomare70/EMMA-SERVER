using Dapper.Contrib.Extensions;
using System.Text.Json.Serialization;
namespace EmmaServer.Entities;

[Table("articoli")] 
public record EmmaArticoli: IEntity
{
    [Dapper.Contrib.Extensions.Key]
    public int id { get; init; }
    public string codice { get; set; } = string.Empty;
    public string descrizione { get; set; } = string.Empty;
    [Write(false)] 
    public DateTime data_creazione { get; init; } = DateTime.UtcNow;
    
    public string rifcodice{ get; set; } = string.Empty;
    
    public string rifdescrizione{ get; set; } = string.Empty;
    
    public int scorecodice { get; set; } = 0;
    
    public int scoredescrizione { get; set; } = 0;
    public string tenant { get; set; } = string.Empty;
    
    public int idfornitore { get; set; } = 0;
}