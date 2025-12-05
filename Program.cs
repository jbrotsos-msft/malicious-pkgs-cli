using CliApp.Models;
using CliApp.Services;

namespace CliApp;

class Program
{
    static async Task<int> Main(string[] args)
    {
        if (args.Length == 0)
        {
            ShowHelp();
            return 0;
        }

        try
        {
            // Parse command: defender scan sbom <target> [flags]
            if (args.Length < 3 || args[0] != "scan" || args[1] != "sbom")
            {
                Console.WriteLine("Error: Invalid command syntax.");
                ShowHelp();
                return 1;
            }

            var options = ParseArguments(args);
            
            // Validate required arguments
            if (string.IsNullOrEmpty(options.Target) || string.IsNullOrEmpty(options.SbomFormat))
            {
                Console.WriteLine("Error: Missing required arguments.");
                ShowHelp();
                return 1;
            }

            // Execute scan
            var syftManager = new SyftManager();
            var scanner = new SbomScanner(syftManager);
            
            var sbomFile = await scanner.ScanAsync(options);

            // Check for malicious packages if requested
            if (options.FailOnMalicious)
            {
                var detector = new MaliciousPackageDetector();
                var maliciousPackages = await detector.DetectMaliciousPackagesAsync(sbomFile);

                if (maliciousPackages.Count > 0)
                {
                    Console.WriteLine("\n[!] ALERT: Malicious Package Detected!\n");
                    
                    // Print table
                    Console.WriteLine("| Package       | Version | ID             | Database |");
                    Console.WriteLine("| :---          | :---    | :---           | :---     |");
                    
                    foreach (var pkg in maliciousPackages)
                    {
                        Console.WriteLine($"| {pkg.PackageName,-13} | {pkg.Version,-7} | {pkg.MaliciousId,-14} | {pkg.Database,-8} |");
                    }

                    Console.WriteLine($"\n[X] Scan failed: {maliciousPackages.Count} malicious package(s) found.");
                    return 1;
                }
                else
                {
                    Console.WriteLine("[+] No malicious packages detected.");
                }
            }

            Console.WriteLine("\n[+] Scan completed successfully.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[X] Error: {ex.Message}");
            return 1;
        }
    }

    static ScanOptions ParseArguments(string[] args)
    {
        var options = new ScanOptions
        {
            Target = args[2],
            SbomFormat = string.Empty
        };

        for (int i = 3; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--sbom-format":
                    if (i + 1 < args.Length)
                    {
                        options.SbomFormat = args[++i];
                    }
                    break;
                case "--sbom-output-file":
                    if (i + 1 < args.Length)
                    {
                        options.SbomOutputFile = args[++i];
                    }
                    break;
                case "--fail-on-malicious":
                    options.FailOnMalicious = true;
                    break;
                case "--type":
                    if (i + 1 < args.Length)
                    {
                        options.Type = args[++i];
                    }
                    break;
            }
        }

        return options;
    }

    static void ShowHelp()
    {
        Console.WriteLine("Defender for Cloud CLI - SBOM Integration\n");
        Console.WriteLine("Usage:");
        Console.WriteLine("  defender scan sbom <target> --sbom-format <format> [options]\n");
        Console.WriteLine("Arguments:");
        Console.WriteLine("  <target>              Local directory path or container image tag\n");
        Console.WriteLine("Required Flags:");
        Console.WriteLine("  --sbom-format         Output format: cyclonedx-xml, cyclonedx-json, spdx-json\n");
        Console.WriteLine("Optional Flags:");
        Console.WriteLine("  --sbom-output-file    Custom output path (default: ./sbom-output.<ext>)");
        Console.WriteLine("  --fail-on-malicious   Check for malicious packages via OSV.dev (exit code 1 if found)");
        Console.WriteLine("  --type                Force scan type: dir, image\n");
        Console.WriteLine("Examples:");
        Console.WriteLine("  defender scan sbom ./my-app --sbom-format cyclonedx-json");
        Console.WriteLine("  defender scan sbom nginx:latest --sbom-format spdx-json --fail-on-malicious");
    }
}

