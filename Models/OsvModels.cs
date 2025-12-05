using System.Text.Json.Serialization;

namespace CliApp.Models;

public class OsvVulnerability
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;
    
    [JsonPropertyName("summary")]
    public string Summary { get; set; } = string.Empty;
    
    [JsonPropertyName("affected")]
    public List<OsvAffected> Affected { get; set; } = new();
}

public class OsvAffected
{
    [JsonPropertyName("package")]
    public OsvPackage? Package { get; set; }
}

public class OsvPackage
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    
    [JsonPropertyName("ecosystem")]
    public string Ecosystem { get; set; } = string.Empty;
}

public class OsvBatchRequest
{
    [JsonPropertyName("queries")]
    public List<OsvQuery> Queries { get; set; } = new();
}

public class OsvQuery
{
    [JsonPropertyName("package")]
    public OsvQueryPackage Package { get; set; } = new();
}

public class OsvQueryPackage
{
    [JsonPropertyName("purl")]
    public string Purl { get; set; } = string.Empty;
}

public class OsvBatchResponse
{
    [JsonPropertyName("results")]
    public List<OsvBatchResult> Results { get; set; } = new();
}

public class OsvBatchResult
{
    [JsonPropertyName("vulns")]
    public List<OsvVulnerability> Vulns { get; set; } = new();
}
