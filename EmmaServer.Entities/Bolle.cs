using Dapper.Contrib.Extensions;
using System.Text.Json;
namespace EmmaServer.Entities;

[Table("bolle")] 
public record Bolle: IEntity
{
    [Dapper.Contrib.Extensions.Key]
    public int id { get; init; }
    public string file_name { get; init; } = string.Empty;
    [Write(false)] 
    public DateTime created_at { get; init; }
    public JsonDocument data { get; init; }
    public byte[] allegato { get; set; }
}