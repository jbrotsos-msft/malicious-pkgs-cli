using System.Text.Json.Serialization;

namespace CliApp.Models;

public class SbomDocument
{
    [JsonPropertyName("components")]
    public List<SbomComponent> Components { get; set; } = new();
}

public class SbomComponent
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    
    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;
    
    [JsonPropertyName("purl")]
    public string? Purl { get; set; }
}
