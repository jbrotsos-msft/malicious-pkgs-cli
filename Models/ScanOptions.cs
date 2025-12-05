namespace CliApp.Models;

public class ScanOptions
{
    public required string Target { get; set; }
    public required string SbomFormat { get; set; }
    public string? SbomOutputFile { get; set; }
    public bool FailOnMalicious { get; set; }
    public string? Type { get; set; }
}
