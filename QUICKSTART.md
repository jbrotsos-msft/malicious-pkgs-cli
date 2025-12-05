# Defender CLI - Quick Start

## Running the CLI

After building, you can run the CLI using:

```bash
dotnet run -- scan sbom <target> --sbom-format <format>
```

## Example Commands

### 1. Scan a Node.js Project

```bash
dotnet run -- scan sbom ./my-node-app --sbom-format cyclonedx-json --fail-on-malicious
```

### 2. Scan a Python Project

```bash
dotnet run -- scan sbom ./my-python-app --sbom-format spdx-json
```

### 3. Scan a Docker Image

```bash
dotnet run -- scan sbom nginx:latest --sbom-format cyclonedx-json --fail-on-malicious
```

## Output

The CLI will:
1. Download Syft (first run only)
2. Generate the SBOM
3. Save it to `sbom-output.<format>`
4. Check for malicious packages (if `--fail-on-malicious` is set)
5. Exit with code 1 if malicious packages are found

## First Run

On the first run, you'll see:

```
[+] Scanner not found in cache. Downloading...
[+] Scanner downloaded and verified successfully.
[+] Generating SBOM for './my-app'...
[+] SBOM saved to: ./sbom-output.json
```

## Testing

Run the test harness:

```bash
python generate_test_data.py
```

This verifies the malicious package detection works correctly.
