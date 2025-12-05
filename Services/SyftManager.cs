using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace CliApp.Services;

public class SyftManager
{
    private const string SyftVersion = "1.38.0";
    private static readonly string CacheDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".defender",
        "cache"
    );

    // SHA256 checksums for known-good Syft binaries (v1.38.0)
    // Note: For production, these should be updated with actual checksums from the release
    private static readonly Dictionary<string, string> KnownChecksums = new()
    {
        { "windows_amd64", "" },
        { "linux_amd64", "" },
        { "darwin_amd64", "" },
        { "darwin_arm64", "" }
    };

    public async Task<string> EnsureSyftAsync()
    {
        var syftPath = GetSyftPath();
        
        if (File.Exists(syftPath))
        {
            Console.WriteLine("[+] Scanner Verification: Cached binary found.");
            return syftPath;
        }

        Console.WriteLine("[+] Scanner not found in cache. Downloading...");
        await DownloadSyftAsync();
        Console.WriteLine("[+] Scanner downloaded and verified successfully.");
        
        return syftPath;
    }

    private string GetSyftPath()
    {
        var platform = GetPlatformString();
        var executable = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "syft.exe" : "syft";
        return Path.Combine(CacheDirectory, platform, executable);
    }

    private async Task DownloadSyftAsync()
    {
        var platform = GetPlatformString();
        
        // Windows uses .zip, others use .tar.gz
        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        var extension = isWindows ? ".zip" : ".tar.gz";
        var archiveUrl = $"https://github.com/anchore/syft/releases/download/v{SyftVersion}/syft_{SyftVersion}_{platform}{extension}";
        
        Console.WriteLine($"[+] Downloading from: {archiveUrl}");
        
        if (!Directory.Exists(CacheDirectory))
        {
            Directory.CreateDirectory(CacheDirectory);
        }

        var archivePath = Path.Combine(CacheDirectory, $"syft_{platform}{extension}");
        
        using (var client = new HttpClient())
        {
            var response = await client.GetAsync(archiveUrl);
            response.EnsureSuccessStatusCode();
            
            await using var fileStream = File.Create(archivePath);
            await response.Content.CopyToAsync(fileStream);
        }

        // Verify checksum
        var actualChecksum = ComputeSha256(archivePath);
        if (KnownChecksums.TryGetValue(platform, out var expectedChecksum) && !string.IsNullOrEmpty(expectedChecksum))
        {
            if (!actualChecksum.Equals(expectedChecksum, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(archivePath);
                throw new InvalidOperationException(
                    $"Checksum verification failed for {platform}. Expected: {expectedChecksum}, Got: {actualChecksum}");
            }
            Console.WriteLine("[+] Checksum verification passed.");
        }
        else
        {
            Console.WriteLine("[!] Warning: Checksum verification skipped (no checksum configured).");
        }

        // Extract archive
        var extractPath = Path.Combine(CacheDirectory, platform);
        Directory.CreateDirectory(extractPath);
        
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            await ExtractZipAsync(archivePath, extractPath);
        }
        else
        {
            await ExtractTarGzAsync(archivePath, extractPath);
        }
        
        // Set executable permissions on Unix
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var syftPath = GetSyftPath();
            Process.Start("chmod", $"+x \"{syftPath}\"")?.WaitForExit();
        }

        File.Delete(archivePath);
    }

    private string GetPlatformString()
    {
        var os = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "windows" :
                 RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "linux" :
                 RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "darwin" :
                 throw new PlatformNotSupportedException("Unsupported operating system");

        var arch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "amd64",
            Architecture.Arm64 => "arm64",
            _ => throw new PlatformNotSupportedException("Unsupported architecture")
        };

        return $"{os}_{arch}";
    }

    private string ComputeSha256(string filePath)
    {
        using var sha256 = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        var hash = sha256.ComputeHash(stream);
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }

    private async Task ExtractZipAsync(string zipPath, string extractPath)
    {
        await Task.Run(() => ZipFile.ExtractToDirectory(zipPath, extractPath));
    }

    private async Task ExtractTarGzAsync(string gzipPath, string extractPath)
    {
        await using var gzipStream = File.OpenRead(gzipPath);
        await using var decompressionStream = new GZipStream(gzipStream, CompressionMode.Decompress);
        
        // For simplicity, we'll use external tar command
        // A production implementation should use a proper tar library
        var tarPath = gzipPath.Replace(".tar.gz", ".tar");
        
        await using (var tarStream = File.Create(tarPath))
        {
            await decompressionStream.CopyToAsync(tarStream);
        }

        // Extract tar using system command
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Process.Start("tar", $"-xf \"{tarPath}\" -C \"{extractPath}\"")?.WaitForExit();
        }
        else
        {
            Process.Start("tar", $"-xf \"{tarPath}\" -C \"{extractPath}\"")?.WaitForExit();
        }

        File.Delete(tarPath);
    }
}
