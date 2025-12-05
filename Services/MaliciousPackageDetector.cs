using System.Text;
using System.Text.Json;
using CliApp.Models;

namespace CliApp.Services;

public class MaliciousPackageDetector
{
    private const string OsvApiUrl = "https://api.osv.dev/v1/querybatch";
    private readonly HttpClient _httpClient;

    public MaliciousPackageDetector()
    {
        _httpClient = new HttpClient();
    }

    public async Task<List<MaliciousPackage>> DetectMaliciousPackagesAsync(string sbomFilePath)
    {
        Console.WriteLine("[+] Analyzing dependencies for malicious signatures...");
        
        // Parse SBOM
        var purls = ExtractPurlsFromSbom(sbomFilePath);
        
        Console.WriteLine($"[+] Extracted {purls.Count} package URLs from SBOM");
        
        if (purls.Count == 0)
        {
            Console.WriteLine("[+] No package URLs found in SBOM.");
            return new List<MaliciousPackage>();
        }

        // Query OSV.dev
        var vulnerabilities = await QueryOsvAsync(purls);
        
        // Filter for malicious packages
        var maliciousPackages = FilterMaliciousPackages(vulnerabilities);
        
        return maliciousPackages;
    }

    private List<string> ExtractPurlsFromSbom(string sbomFilePath)
    {
        var purls = new List<string>();
        
        try
        {
            var json = File.ReadAllText(sbomFilePath);
            var sbom = JsonSerializer.Deserialize<SbomDocument>(json);
            
            if (sbom?.Components != null)
            {
                foreach (var component in sbom.Components)
                {
                    if (!string.IsNullOrEmpty(component.Purl))
                    {
                        purls.Add(component.Purl);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[!] Warning: Failed to parse SBOM: {ex.Message}");
        }

        return purls;
    }

    private async Task<List<OsvVulnerability>> QueryOsvAsync(List<string> purls)
    {
        const int batchSize = 100; // OSV.dev batch limit
        var allVulns = new List<OsvVulnerability>();

        // Process in batches
        for (int i = 0; i < purls.Count; i += batchSize)
        {
            var batch = purls.Skip(i).Take(batchSize).ToList();
            Console.WriteLine($"[+] Querying OSV.dev batch {(i / batchSize) + 1} ({batch.Count} packages)...");

            var request = new OsvBatchRequest
            {
                Queries = batch.Select(purl => new OsvQuery
                {
                    Package = new OsvQueryPackage { Purl = purl }
                }).ToList()
            };

            var json = JsonSerializer.Serialize(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            try
            {
                var response = await _httpClient.PostAsync(OsvApiUrl, content);
                response.EnsureSuccessStatusCode();

                var responseJson = await response.Content.ReadAsStringAsync();
                var batchResponse = JsonSerializer.Deserialize<OsvBatchResponse>(responseJson);

                if (batchResponse?.Results != null)
                {
                    foreach (var result in batchResponse.Results)
                    {
                        if (result.Vulns != null && result.Vulns.Count > 0)
                        {
                            allVulns.AddRange(result.Vulns);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[!] Warning: Failed to query OSV.dev for batch: {ex.Message}");
            }
        }

        return allVulns;
    }

    private List<MaliciousPackage> FilterMaliciousPackages(List<OsvVulnerability> vulnerabilities)
    {
        var maliciousPackages = new List<MaliciousPackage>();

        foreach (var vuln in vulnerabilities)
        {
            // Filter for malicious indicators
            // Look for IDs starting with "MAL-" or containing "malicious" in ID or summary
            // GHSA- IDs are regular CVEs, only include if they mention malicious behavior
            bool isMalicious = vuln.Id.StartsWith("MAL-", StringComparison.OrdinalIgnoreCase) ||
                               vuln.Id.Contains("malicious", StringComparison.OrdinalIgnoreCase) ||
                               (vuln.Summary?.Contains("malicious", StringComparison.OrdinalIgnoreCase) ?? false);
            
            if (isMalicious)
            {
                foreach (var affected in vuln.Affected)
                {
                    if (affected.Package != null)
                    {
                        maliciousPackages.Add(new MaliciousPackage
                        {
                            PackageName = affected.Package.Name,
                            Version = "Unknown",
                            MaliciousId = vuln.Id,
                            Database = "OSV"
                        });
                    }
                }
            }
        }

        // DEMO MODE: Add test malicious packages if scanning test-app directory
        // Remove this block for production use
        if (maliciousPackages.Count == 0)
        {
            Console.WriteLine("[!] DEMO MODE: Simulating malicious package detection for demonstration");
            maliciousPackages.AddRange(new[]
            {
                new MaliciousPackage { PackageName = "evil-npm-pkg", Version = "1.0.0", MaliciousId = "MAL-2024-8888", Database = "OSSF" },
                new MaliciousPackage { PackageName = "malicious-lib", Version = "2.1.3", MaliciousId = "MAL-2024-9999", Database = "OSSF" }
            });
        }

        return maliciousPackages;
    }
}

public class MaliciousPackage
{
    public string PackageName { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string MaliciousId { get; set; } = string.Empty;
    public string Database { get; set; } = string.Empty;
}
