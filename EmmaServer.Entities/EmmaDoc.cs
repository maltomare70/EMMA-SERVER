using Dapper.Contrib.Extensions;
using System.Text.Json;
namespace EmmaServer.Entities;

using EmmaServer.Entities.Dtos;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

[Dapper.Contrib.Extensions.Table("docs")] 
public record EmmaDoc: IEntity
{
    [Dapper.Contrib.Extensions.Key]
    public int id { get; set; }
    [Column(TypeName = "varchar(256)")]
    public string file_name { get; init; } = string.Empty;
    [Write(false)] 
    public DateTime data_creazione { get; set; } = DateTime.UtcNow;
    public JsonDocument? content { get; set; }
    public byte[]? allegato { get; set; }
    [Column(TypeName = "varchar(100)")]
    public string tenant { get; set; } = string.Empty;
    public int stato { get; set; } = 0;

    public DatiBolla? ToDoc()
    {
        // Fix: Use RootElement to properly deserialize a JsonDocument
        var ddtResponse = content?.RootElement.Deserialize<DdtResponse>();
        return ddtResponse?.Document;
    }
    
}


