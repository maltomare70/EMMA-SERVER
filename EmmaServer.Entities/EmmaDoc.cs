using Dapper.Contrib.Extensions;
using System.Text.Json;
namespace EmmaServer.Entities;

[Table("docs")] 
public record EmmaDoc: IEntity
{
    [Dapper.Contrib.Extensions.Key]
    public int id { get; init; }
    public string file_name { get; init; } = string.Empty;
    [Write(false)] 
    public DateTime data_creazione { get; init; } = DateTime.UtcNow;
    public JsonDocument? content { get; set; }
    public byte[]? allegato { get; set; }
    public string tenant { get; set; } = string.Empty;
    public int stato { get; set; } = 0;
}