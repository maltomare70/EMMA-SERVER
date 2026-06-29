using System.Reflection.Metadata;
using System.Text.Json.Serialization;
using System.Text;
using System.Text.Json;
namespace EmmaServer.Entities;

public class DdtResponse
{
    [JsonPropertyName("model_name")]
    public string? ModelName { get; set; }
    
    [JsonPropertyName("file_name")]
    public string? FileName { get; set; }
    
    [JsonPropertyName("costs")]
    public Costs Costs { get; set; } = new();

    [JsonPropertyName("document")]
    public DatiBolla Document { get; set; } = new();
    

}


public class DocResponse
{
    [JsonPropertyName("doc_id")]
    public int DocId { get; set; }
    public DdtResponse?  DdtResponse { get; set; }
}