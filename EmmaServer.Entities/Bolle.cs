using Dapper.Contrib.Extensions;
using System.Text.Json;
namespace EmmaServer.Entities;

[Table("bolle")] 
public record Bolle: IEntity
{
    [Dapper.Contrib.Extensions.Key]
    public int id { get; set; }
    public string file_name { get; init; } = string.Empty;
    [Write(false)] 
    public DateTime data_creazione { get; set; } = DateTime.UtcNow;
    public JsonDocument? data { get; init; }
    public byte[]? allegato { get; set; } 
}