# Defender for Cloud CLI - SBOM Integration

A .NET 9.0 command-line interface for SBOM (Software Bill of Materials) generation and malicious package detection. This tool integrates with [Syft](https://github.com/anchore/syft) for SBOM generation and [OSV.dev](https://osv.dev) for malicious package detection.

## Features

- **Directory Scan**: Generate SBOM from local source code directories
- **Image Scan**: Generate SBOM from container images
- **Malicious Detection**: Cross-reference SBOM components with OSV.dev malicious packages database
- **Lazy Loading**: Syft scanner is downloaded on-demand to a local cache
- **Cross-Platform**: Supports Windows, Linux, and macOS (AMD64/ARM64)

## Prerequisites

- [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) or later
- Docker (optional, for container image scanning)

## Installation

### Build from Source

```bash
dotnet build
```

The compiled executable will be named `defender` and located in `bin/Debug/net9.0/`.

## Usage

### Command Syntax

```bash
defender scan sbom <target> --sbom-format <format> [options]
```

### Arguments

| Argument | Required | Description |
| :--- | :--- | :--- |
| `<target>` | Yes | Local directory path OR container image tag |

### Flags

| Flag | Required | Description |
| :--- | :--- | :--- |
| `--sbom-format` | Yes | Output format: `cyclonedx-xml`, `cyclonedx-json`, `spdx-json` |
| `--sbom-output-file` | No | Custom output path (default: `./sbom-output.<ext>`) |
| `--fail-on-malicious` | No | Check for malicious packages via OSV.dev. Exit code 1 if found. |
| `--type` | No | Force scan type: `dir`, `image` |

### Examples

#### Scan a Local Directory

```bash
dotnet run -- scan sbom ./my-app --sbom-format cyclonedx-json
```

#### Scan a Container Image

```bash
dotnet run -- scan sbom nginx:latest --sbom-format spdx-json
```

#### Scan with Malicious Package Detection

```bash
dotnet run -- scan sbom ./my-app --sbom-format cyclonedx-json --fail-on-malicious
```

#### Force Scan Type

```bash
dotnet run -- scan sbom ./my-app --sbom-format cyclonedx-json --type dir
```

## How It Works

### 1. Scanner Lifecycle Management

On first run, the CLI:
1. Detects your OS and architecture
2. Downloads the appropriate Syft binary from GitHub releases
3. Verifies the binary integrity using SHA256 checksums
4. Caches it in `~/.defender/cache/`

Subsequent runs use the cached binary.

### 2. SBOM Generation

The CLI invokes Syft to scan:
- **Directories**: Analyzes package files (`package.json`, `requirements.txt`, etc.)
- **Container Images**: Analyzes image layers and installed packages

### 3. Malicious Package Detection

When `--fail-on-malicious` is specified:
1. Parses the generated SBOM
2. Extracts Package URLs (PURLs)
3. Sends batch query to OSV.dev API
4. Filters results for malicious indicators (MAL-* IDs)
5. Reports findings and exits with code 1 if any are found

## Testing

A test harness script is included to verify malicious package detection:

```bash
python generate_test_data.py
```

This script:
- Creates a temporary project with a known malicious package
- Runs the Defender CLI against it
- Verifies the tool correctly detects the threat

## Project Structure

```
.
├── Models/
│   ├── OsvModels.cs          # OSV.dev API models
│   ├── SbomModels.cs          # SBOM document models
│   └── ScanOptions.cs         # CLI options model
├── Services/
│   ├── MaliciousPackageDetector.cs  # OSV.dev integration
│   ├── SbomScanner.cs               # SBOM generation logic
│   └── SyftManager.cs               # Syft lifecycle management
├── Program.cs                 # CLI entry point
├── CliApp.csproj             # Project configuration
└── generate_test_data.py      # Test harness

```

## Security

- **Secure Download**: All downloads occur over HTTPS
- **Integrity Verification**: Binary checksums are verified before use
- **No Data Exfiltration**: Only PURLs are sent to OSV.dev; no source code is uploaded

## Development

### Debug in VS Code

1. Press `F5` to start debugging
2. Configure launch arguments in `.vscode/launch.json`

### Build

```bash
dotnet build
```

### Run

```bash
dotnet run -- scan sbom <target> --sbom-format <format>
```

## License

This project is part of the Defender for Cloud initiative.

## References

- [Syft by Anchore](https://github.com/anchore/syft)
- [OSV.dev](https://osv.dev)
- [CycloneDX](https://cyclonedx.org/)
- [SPDX](https://spdx.dev/)

