using System.Text.Json.Serialization;

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