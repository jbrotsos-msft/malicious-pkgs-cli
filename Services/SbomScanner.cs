using System.Diagnostics;
using CliApp.Models;

namespace CliApp.Services;

public class SbomScanner
{
    private readonly SyftManager _syftManager;

    public SbomScanner(SyftManager syftManager)
    {
        _syftManager = syftManager;
    }

    public async Task<string> ScanAsync(ScanOptions options)
    {
        var syftPath = await _syftManager.EnsureSyftAsync();
        
        // Determine scan type
        var scanType = DetermineScanType(options);
        
        // Determine output file
        var outputFile = options.SbomOutputFile ?? GetDefaultOutputFile(options.SbomFormat);
        
        Console.WriteLine($"[+] Generating SBOM for '{options.Target}'...");
        
        // Build syft command
        var target = scanType == "dir" ? $"dir:{options.Target}" : options.Target;
        var format = MapSbomFormat(options.SbomFormat);
        
        var startInfo = new ProcessStartInfo
        {
            FileName = syftPath,
            Arguments = $"{target} -o {format}={outputFile}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo };
        
        process.ErrorDataReceived += (sender, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
            {
                Console.Error.WriteLine(e.Data);
            }
        };

        process.Start();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Syft scan failed with exit code {process.ExitCode}");
        }

        Console.WriteLine($"[+] SBOM saved to: {outputFile}");
        
        return outputFile;
    }

    private string DetermineScanType(ScanOptions options)
    {
        if (!string.IsNullOrEmpty(options.Type))
        {
            return options.Type;
        }

        // Check if target is a local directory
        if (Directory.Exists(options.Target))
        {
            return "dir";
        }

        // Assume it's an image
        return "image";
    }

    private string GetDefaultOutputFile(string format)
    {
        var extension = format switch
        {
            "cyclonedx-json" => "json",
            "cyclonedx-xml" => "xml",
            "spdx-json" => "json",
            _ => "json"
        };

        return $"./sbom-output.{extension}";
    }

    private string MapSbomFormat(string format)
    {
        return format switch
        {
            "cyclonedx-json" => "cyclonedx-json",
            "cyclonedx-xml" => "cyclonedx-xml",
            "spdx-json" => "spdx-json",
            _ => throw new ArgumentException($"Unsupported SBOM format: {format}")
        };
    }
}
