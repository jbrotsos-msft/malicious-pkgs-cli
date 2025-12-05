# Defender for Cloud CLI - Architecture & Implementation Guide

## Table of Contents
- [Overview](#overview)
- [High-Level Architecture](#high-level-architecture)
- [Component Details](#component-details)
- [Data Flow](#data-flow)
- [Key Implementation Details](#key-implementation-details)
- [API Integrations](#api-integrations)
- [Error Handling](#error-handling)
- [Extension Points](#extension-points)

---

## Overview

The Defender for Cloud CLI is a command-line tool designed to generate Software Bill of Materials (SBOM) and detect malicious packages in software projects. It acts as an orchestrator, managing the lifecycle of the Syft scanning engine without bundling it directly.

### Core Capabilities
1. **SBOM Generation**: Creates comprehensive software inventories from directories and container images
2. **Malicious Package Detection**: Cross-references SBOM components with OSV.dev malicious packages database
3. **Lazy Loading**: Downloads and caches the Syft scanner on first use
4. **Cross-Platform Support**: Works on Windows, Linux, and macOS with both AMD64 and ARM64 architectures

---

## High-Level Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                      Defender CLI (defender)                     │
│                                                                   │
│  ┌──────────────┐  ┌──────────────┐  ┌────────────────────────┐ │
│  │   Program.cs │─▶│ ScanOptions  │  │ MaliciousPackage       │ │
│  │  (Entry Point)│  │   (Models)   │  │     (Models)           │ │
│  └──────┬───────┘  └──────────────┘  └────────────────────────┘ │
│         │                                                         │
│         ▼                                                         │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │                    Service Layer                          │   │
│  │                                                            │   │
│  │  ┌────────────────┐  ┌─────────────────┐  ┌────────────┐│   │
│  │  │ SyftManager    │  │  SbomScanner    │  │  Malicious ││   │
│  │  │                │  │                 │  │  Package   ││   │
│  │  │ - Download     │  │ - Scan Dir      │  │  Detector  ││   │
│  │  │ - Verify       │  │ - Scan Image    │  │            ││   │
│  │  │ - Cache        │  │ - Format Output │  │ - Query OSV││   │
│  │  └────────────────┘  └─────────────────┘  └────────────┘│   │
│  └──────────────────────────────────────────────────────────┘   │
└───────────────────────────────────────────────────────────────┘
         │                        │                        │
         ▼                        ▼                        ▼
┌────────────────┐      ┌──────────────┐       ┌──────────────────┐
│ ~/.defender/   │      │    Syft      │       │  OSV.dev API     │
│    cache/      │      │   Scanner    │       │ (v1/querybatch)  │
│                │      │              │       │                  │
│ - syft binary  │      │ - SBOM Gen   │       │ - Malicious DB   │
│ - checksums    │      │ - Multiple   │       │ - Vulnerability  │
│                │      │   formats    │       │   Data           │
└────────────────┘      └──────────────┘       └──────────────────┘
```

---

## Component Details

### 1. Entry Point: `Program.cs`

**Responsibilities:**
- Command-line argument parsing
- Validation of required flags
- Orchestration of scan workflow
- Output formatting and error reporting
- Exit code management

**Key Functions:**

```csharp
Main(string[] args)
├─ Parse arguments → ScanOptions
├─ Validate required fields (target, sbom-format)
├─ Initialize services
│  ├─ SyftManager
│  ├─ SbomScanner
│  └─ MaliciousPackageDetector (if --fail-on-malicious)
├─ Execute scan
│  └─ SbomScanner.ScanAsync(options)
├─ Optional: Check for malicious packages
│  └─ MaliciousPackageDetector.DetectMaliciousPackagesAsync(sbomFile)
└─ Return exit code (0 = success, 1 = failure)
```

**Command Syntax:**
```bash
defender scan sbom <target> --sbom-format <format> [options]
```

---

### 2. Service: `SyftManager.cs`

**Responsibilities:**
- Manages Syft scanner lifecycle
- Downloads scanner binary on first use
- Verifies integrity via SHA256 checksums
- Handles platform-specific binary selection

**Architecture:**

```csharp
EnsureSyftAsync()
├─ Check if binary exists in cache
│  └─ Path: ~/.defender/cache/{platform}/syft[.exe]
├─ If missing:
│  ├─ Detect OS (Windows/Linux/macOS)
│  ├─ Detect Architecture (AMD64/ARM64)
│  ├─ Construct download URL
│  │  └─ https://github.com/anchore/syft/releases/download/v{version}/syft_{version}_{platform}.{ext}
│  ├─ Download archive (.zip for Windows, .tar.gz for Unix)
│  ├─ Verify SHA256 checksum (if configured)
│  ├─ Extract binary
│  └─ Set executable permissions (Unix only)
└─ Return binary path
```

**Platform String Format:**
- Windows AMD64: `windows_amd64`
- Linux AMD64: `linux_amd64`
- macOS AMD64: `darwin_amd64`
- macOS ARM64: `darwin_arm64`

**Security Features:**
- HTTPS-only downloads
- Checksum verification (SHA256)
- Fails if checksum mismatch detected

---

### 3. Service: `SbomScanner.cs`

**Responsibilities:**
- Orchestrates Syft execution
- Determines scan type (directory vs. image)
- Manages output formatting
- Streams Syft progress to stderr

**Scan Type Detection:**

```csharp
DetermineScanType(options)
├─ If --type flag provided
│  └─ Use specified type (dir/image)
└─ Else
   ├─ Check if target is local directory
   │  └─ Return "dir"
   └─ Else
      └─ Assume container image → "image"
```

**Syft Invocation:**

```bash
# Directory Scan
syft dir:<target> -o <format>=<output-file>

# Image Scan
syft <image-name> -o <format>=<output-file>
```

**Supported Formats:**
- `cyclonedx-json` → `.json`
- `cyclonedx-xml` → `.xml`
- `spdx-json` → `.json`

**Process Management:**
- Redirects stdout and stderr
- Streams errors to console in real-time
- Waits for process completion
- Returns output file path on success

---

### 4. Service: `MaliciousPackageDetector.cs`

**Responsibilities:**
- Parses SBOM files
- Extracts Package URLs (PURLs)
- Queries OSV.dev API in batches
- Filters for malicious indicators
- Reports findings

**Detection Workflow:**

```csharp
DetectMaliciousPackagesAsync(sbomFilePath)
├─ Parse SBOM (JSON deserialization)
├─ Extract PURLs from components
│  └─ Filter: components with non-empty purl field
├─ Query OSV.dev in batches (100 PURLs per batch)
│  └─ POST to https://api.osv.dev/v1/querybatch
├─ Deserialize vulnerability responses
├─ Filter for malicious indicators
│  ├─ ID starts with "MAL-"
│  ├─ ID contains "malicious"
│  └─ Summary contains "malicious"
└─ Return list of MaliciousPackage objects
```

**Batch Processing:**
- **Batch Size**: 100 packages per request
- **Reason**: OSV.dev API limits
- **Strategy**: Sequential batching to avoid rate limits

**OSV.dev API Request Format:**

```json
{
  "queries": [
    {
      "package": {
        "purl": "pkg:npm/lodash@4.17.21"
      }
    },
    {
      "package": {
        "purl": "pkg:deb/ubuntu/bash@4.3-14ubuntu1.2?arch=amd64&distro=ubuntu-16.04"
      }
    }
  ]
}
```

**OSV.dev API Response Format:**

```json
{
  "results": [
    {
      "vulns": [
        {
          "id": "MAL-2024-1234",
          "summary": "Malicious package detected",
          "affected": [
            {
              "package": {
                "name": "evil-package",
                "ecosystem": "npm"
              }
            }
          ]
        }
      ]
    }
  ]
}
```

---

## Data Flow

### Standard SBOM Generation Flow

```
┌──────────┐
│   User   │
└────┬─────┘
     │ defender scan sbom ./my-app --sbom-format cyclonedx-json
     ▼
┌─────────────────┐
│   Program.cs    │
└────┬────────────┘
     │ 1. Parse args → ScanOptions
     ▼
┌─────────────────┐
│  SyftManager    │
└────┬────────────┘
     │ 2. Ensure Syft binary exists
     │    - Download if missing
     │    - Verify checksum
     ▼
┌─────────────────┐
│  SbomScanner    │
└────┬────────────┘
     │ 3. Determine scan type (dir/image)
     │ 4. Execute Syft
     │    syft dir:./my-app -o cyclonedx-json=sbom-output.json
     ▼
┌─────────────────┐
│  Syft Process   │
└────┬────────────┘
     │ 5. Scan target
     │    - Parse package files
     │    - Extract metadata
     │    - Generate SBOM
     ▼
┌─────────────────┐
│ SBOM File (.json)│
└─────────────────┘
```

### Malicious Package Detection Flow

```
┌──────────┐
│   User   │
└────┬─────┘
     │ defender scan sbom ./my-app --sbom-format cyclonedx-json --fail-on-malicious
     ▼
┌────────────────────────┐
│ [Standard SBOM Flow]   │
│ (see above)            │
└────┬───────────────────┘
     │ SBOM generated
     ▼
┌────────────────────────┐
│ MaliciousPackageDetector│
└────┬───────────────────┘
     │ 1. Read SBOM file
     │ 2. Extract PURLs
     │    ["pkg:npm/express@4.18.0", "pkg:npm/lodash@4.17.21", ...]
     ▼
┌────────────────────────┐
│  Batch PURLs (100 max) │
└────┬───────────────────┘
     │ 3. Query OSV.dev API
     │    POST /v1/querybatch
     ▼
┌────────────────────────┐
│    OSV.dev Response    │
└────┬───────────────────┘
     │ 4. Deserialize vulnerabilities
     │ 5. Filter for malicious indicators
     │    - MAL-* prefix
     │    - "malicious" in ID/summary
     ▼
┌────────────────────────┐
│ Malicious packages?    │
└────┬───────────────────┘
     │
     ├─ YES ──────────────────────┐
     │                             ▼
     │                   ┌─────────────────────┐
     │                   │  Print Alert Table  │
     │                   │  Exit Code: 1       │
     │                   └─────────────────────┘
     │
     └─ NO ───────────────────────┐
                                   ▼
                         ┌─────────────────────┐
                         │  Success Message    │
                         │  Exit Code: 0       │
                         └─────────────────────┘
```

---

## Key Implementation Details

### 1. Cross-Platform Binary Management

**Challenge**: Different platforms require different Syft binaries and archive formats.

**Solution**:
```csharp
// Detect platform
var os = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "windows" :
         RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "linux" :
         RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "darwin" :
         throw new PlatformNotSupportedException();

var arch = RuntimeInformation.ProcessArchitecture switch
{
    Architecture.X64 => "amd64",
    Architecture.Arm64 => "arm64",
    _ => throw new PlatformNotSupportedException()
};

// Windows uses .zip, others use .tar.gz
var extension = isWindows ? ".zip" : ".tar.gz";
```

### 2. SBOM Parsing Strategy

**Challenge**: Parse large SBOM files efficiently.

**Solution**: Use `System.Text.Json` with selective deserialization:
```csharp
public class SbomDocument
{
    [JsonPropertyName("components")]
    public List<SbomComponent> Components { get; set; } = new();
}

public class SbomComponent
{
    [JsonPropertyName("purl")]
    public string? Purl { get; set; }
}
```

Only deserialize necessary fields to minimize memory usage.

### 3. OSV.dev API Integration

**Challenge**: Query potentially hundreds of packages efficiently.

**Solution**: Batch processing with proper JSON serialization:
```csharp
// Use JsonPropertyName attributes for correct API format
public class OsvBatchRequest
{
    [JsonPropertyName("queries")]
    public List<OsvQuery> Queries { get; set; } = new();
}

// Process in batches of 100
const int batchSize = 100;
for (int i = 0; i < purls.Count; i += batchSize)
{
    var batch = purls.Skip(i).Take(batchSize).ToList();
    // Query OSV.dev
}
```

### 4. Malicious Package Filtering Logic

**Criteria for "Malicious" Classification:**

1. **MAL- Prefix**: ID starts with "MAL-" (OSSF malicious packages database)
2. **Keyword Match**: ID or summary contains "malicious"
3. **Future**: Could be extended to check specific database sources

**Example Filter:**
```csharp
bool isMalicious = vuln.Id.StartsWith("MAL-", StringComparison.OrdinalIgnoreCase) ||
                   vuln.Id.Contains("malicious", StringComparison.OrdinalIgnoreCase) ||
                   (vuln.Summary?.Contains("malicious", StringComparison.OrdinalIgnoreCase) ?? false);
```

**Note**: Regular CVEs (GHSA-*, CVE-*) are NOT considered malicious unless they explicitly mention malicious behavior.

---

## API Integrations

### OSV.dev API v1

**Base URL**: `https://api.osv.dev/v1`

**Endpoint**: `POST /querybatch`

**Rate Limits**: 
- Recommended batch size: 100 queries
- No explicit rate limit documented, but use reasonable batching

**Authentication**: None required (public API)

**Request Headers**:
```
Content-Type: application/json
```

**Supported Ecosystems**:
- npm (Node.js)
- PyPI (Python)
- Maven (Java)
- Go
- Debian/Ubuntu (deb)
- Alpine (apk)
- And many more...

**PURL Format Examples**:
```
pkg:npm/lodash@4.17.21
pkg:pypi/requests@2.28.0
pkg:deb/ubuntu/bash@4.3-14ubuntu1.2?arch=amd64&distro=ubuntu-16.04
```

---

## Error Handling

### Exit Codes

| Code | Meaning | Scenario |
|------|---------|----------|
| 0 | Success | Scan completed, no malicious packages (or flag not set) |
| 1 | Failure | Malicious packages detected, invalid arguments, or scan error |

### Error Categories

1. **Argument Validation Errors**
   - Missing required flags
   - Invalid format specification
   - Triggers: Help message + exit code 1

2. **Download/Verification Errors**
   - Network failure during Syft download
   - Checksum mismatch
   - Triggers: Exception with error message

3. **Scan Errors**
   - Syft process failure
   - Invalid target (non-existent directory/image)
   - Triggers: Exception with error message

4. **OSV.dev API Errors**
   - Network timeout
   - API unavailable
   - Triggers: Warning message, continues with available data

5. **SBOM Parsing Errors**
   - Malformed JSON
   - Missing expected fields
   - Triggers: Warning message, continues with partial data

### Graceful Degradation

- OSV.dev failures log warnings but don't halt execution
- Checksum verification can be skipped if checksums not configured
- Partial SBOM data is still processed if some components fail

---

## Extension Points

### 1. Adding New SBOM Formats

**Location**: `SbomScanner.cs`

```csharp
private string MapSbomFormat(string format)
{
    return format switch
    {
        "cyclonedx-json" => "cyclonedx-json",
        "cyclonedx-xml" => "cyclonedx-xml",
        "spdx-json" => "spdx-json",
        // Add new format here
        "spdx-xml" => "spdx-xml",
        _ => throw new ArgumentException($"Unsupported SBOM format: {format}")
    };
}
```

### 2. Adding New Vulnerability Databases

**Location**: `MaliciousPackageDetector.cs`

Create new detector class implementing similar pattern:
```csharp
public class CustomVulnDetector
{
    private const string ApiUrl = "https://custom-api.com/v1/check";
    
    public async Task<List<Vulnerability>> QueryAsync(List<string> purls)
    {
        // Implementation
    }
}
```

Integrate in `Program.cs`:
```csharp
var osvDetector = new MaliciousPackageDetector();
var customDetector = new CustomVulnDetector();

var maliciousPackages = await osvDetector.DetectMaliciousPackagesAsync(sbomFile);
var customVulns = await customDetector.QueryAsync(purls);
```

### 3. Adding Custom Filters

**Location**: `MaliciousPackageDetector.FilterMaliciousPackages()`

Extend filtering logic:
```csharp
bool isMalicious = vuln.Id.StartsWith("MAL-", StringComparison.OrdinalIgnoreCase) ||
                   vuln.Id.Contains("malicious", StringComparison.OrdinalIgnoreCase) ||
                   // Add custom filter
                   vuln.Severity == "CRITICAL" && vuln.Type == "supply-chain";
```

### 4. Custom Output Formats

**Location**: `Program.cs`

Add output formatters:
```csharp
if (options.OutputFormat == "json")
{
    var json = JsonSerializer.Serialize(maliciousPackages);
    File.WriteAllText("malicious-report.json", json);
}
else if (options.OutputFormat == "table")
{
    // Current table implementation
}
```

---

## Performance Considerations

### 1. SBOM Generation
- **Time**: Depends on project size (seconds to minutes)
- **Memory**: Syft streams data, minimal CLI memory usage
- **Disk**: SBOM files typically < 1MB for small projects, 5-10MB for large containers

### 2. OSV.dev Queries
- **Batch Size**: 100 packages per request (optimal)
- **Network**: ~1-2 seconds per batch
- **Total Time**: For 500 packages ≈ 5-10 seconds

### 3. Cache Benefits
- **First Run**: Downloads Syft (~10-20MB), takes 5-30 seconds depending on network
- **Subsequent Runs**: Uses cached binary, instant startup

---

## Security Considerations

### 1. Supply Chain Security
- Syft binary downloaded from official GitHub releases
- SHA256 checksum verification (when configured)
- HTTPS-only downloads

### 2. Data Privacy
- Only PURLs (package identifiers) sent to OSV.dev
- No source code or proprietary data transmitted
- SBOM files stay local unless explicitly shared

### 3. Execution Safety
- Syft runs in isolated process
- No shell command injection vulnerabilities
- Proper argument escaping

---

## Testing Strategy

### Unit Tests
- Model serialization/deserialization
- Platform string generation
- PURL extraction logic
- Malicious package filtering

### Integration Tests
- OSV.dev API responses
- Syft process execution
- SBOM file parsing

### End-to-End Tests
- Full scan workflows (directory + image)
- Malicious package detection pipeline
- Error scenarios

### Test Harness
Use `generate_test_data.py` to create reproducible test scenarios.

---

## Future Enhancements

### Planned Features
1. **Configurable Checksum Database**: Auto-update checksums from releases
2. **Multiple Vulnerability Sources**: Integrate GitHub Advisory Database, NVD
3. **Offline Mode**: Bundle Syft binary for air-gapped environments
4. **Incremental Scanning**: Only scan changed dependencies
5. **Report Generation**: HTML/PDF vulnerability reports
6. **CI/CD Integration**: Native GitHub Actions, Azure DevOps tasks
7. **Policy Engine**: Define custom rules for package acceptance

### Architecture Improvements
1. **Plugin System**: Load custom scanners/detectors
2. **Configuration File**: Support `.defenderrc` for project-specific settings
3. **Telemetry**: Optional usage metrics for improvement
4. **Caching Layer**: Cache OSV.dev responses to reduce API calls

---

## Troubleshooting Guide

### Common Issues

**Issue**: "Scanner not found in cache"
- **Cause**: First run or cache corruption
- **Solution**: CLI automatically downloads, ensure internet connectivity

**Issue**: "Checksum verification failed"
- **Cause**: Corrupted download or wrong version
- **Solution**: Delete `~/.defender/cache/` and retry

**Issue**: "No vulnerabilities found" for vulnerable packages
- **Cause**: PURLs not extracted or OSV.dev doesn't have data
- **Solution**: Check SBOM manually, verify OSV.dev has ecosystem coverage

**Issue**: "Failed to query OSV.dev"
- **Cause**: Network issues or API unavailable
- **Solution**: Check internet connection, retry later

---

## Conclusion

The Defender for Cloud CLI is architected for:
- **Modularity**: Clear separation of concerns (scanning, detection, reporting)
- **Extensibility**: Easy to add new formats, databases, filters
- **Performance**: Batching, caching, streaming for efficiency
- **Security**: Checksums, HTTPS, no data exfiltration
- **Usability**: Simple CLI, clear output, sensible defaults

Developers can extend any component by following the patterns established in the existing codebase.
