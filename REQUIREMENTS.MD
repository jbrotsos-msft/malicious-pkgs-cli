
# Product Requirement Document: Defender for Cloud CLI (SBOM Integration)

| Metadata | Details |
| :--- | :--- |
| **Project Code** | DEF-SBOM-CLI |
| **Priority** | High (P0) |
| **Driver** | Supply Chain Security (Shai Hulud 2.0 Mitigation) |
| **Status** | Ready for Dev |

## 1. Executive Summary
To mitigate supply chain attacks (specifically **Shai Hulud 2.0** and compromised NPM/PyPI packages), the **Defender for Cloud CLI** will introduce a local SBOM generation capability. This allows developers to detect malicious dependencies early in the inner loop (pre-commit/pre-push) by integrating with the `osv.dev` database.

## 2. Architectural Design
The CLI acts as an **orchestrator**, managing the lifecycle of the underlying scanning engine without bundling it.

* **Engine:** We will wrap **Syft** (by Anchore) for SBOM generation.
* **Lazy Loading:** The Syft binary is **not** bundled. It is downloaded to a user-local cache (e.g., `~/.defender/cache/`) upon the first execution of `scan sbom`.
* **Cross-Platform Support:** The CLI must detect the host OS (Windows/Linux/MacOS) and Architecture (AMD64/ARM64) to download the matching Syft binary.

## 3. Scope & Priorities

| Priority | Feature | Description |
| :--- | :--- | :--- |
| **P0** | **Directory Scan** | Generate SBOM from local source (e.g., `node_modules`, `requirements.txt`). |
| **P0** | **Image Scan** | Generate SBOM from a container image (Docker daemon or OCI archive). |
| **P0** | **Malicious Detection** | Cross-reference generated SBOM artifacts with the OSSF Malicious Packages database via OSV API. |

## 4. Functional Requirements

### 4.1 Command Syntax
The CLI shall support the `sbom` subcommand under the `scan` verb.

```bash
./defender scan sbom <target> [flags]
````

### 4.2 Flags and Parameters

| Flag | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `<target>` | Argument | **Yes** | Local directory path OR Container Image tag. |
| `--sbom-format` | Flag | **Yes** | Output format. Enum: `cyclonedx-xml`, `cyclonedx-json`, `spdx-json`. |
| `--sbom-output-file` | Flag | No | Custom output path. Defaults to `./sbom-output.<ext>`. |
| `--fail-on-malicious` | Flag | No | If set, the CLI performs an API lookup against OSV.dev. If a malicious package is found, exit code is Non-Zero. |
| `--type` | Flag | No | Optional. Force scan type. Enum: `dir`, `image`. (Useful if a folder and image share a name). |

### 4.3 P0: Scanner Lifecycle Management (Lazy Loading)

**Logic:**

1.  Check `~/.defender/cache/` (or OS specific AppData) for `syft` binary.
2.  **If missing:**
      * Detect OS (Windows/Linux/Mac).
      * Detect Arch (AMD64/ARM64).
      * Construct download URL (e.g., `https://github.com/anchore/syft/releases/download/v1.11.0/syft_1.11.0_<OS>_<ARCH>.tar.gz`).
      * Download, extract, and `chmod +x` (if Unix).
3.  **Verification:** Compute SHA256 of the downloaded archive and compare against hardcoded known-good checksums (stored in CLI constants).

### 4.4 P0: Scanning Logic (Dir & Image)

**User Story:** As a developer, I want to scan my project or image.

**Algorithm:**

1.  **Target Resolution:**
      * If `--type` is provided, use it.
      * Else: Check if `<target>` exists as a local directory. If yes -\> `dir`.
      * Else: Assume `<target>` is a container image reference -\> `image`.
2.  **Invocation:** Spawn the `syft` process.
      * Dir: `syft dir:<target> -o <format>`
      * Image: `syft <target> -o <format>`
3.  **Output:** Stream `syft` logs to stderr (for progress). Write final JSON/XML to the specified output file.

### 4.5 P0: Malicious Package Verification (OSV Integration)

**User Story:** As a DevOps engineer, I want the build to fail if a known *malicious* package (not just buggy, but compromised) is found.

**Algorithm (if `--fail-on-malicious` is present):**

1.  **Parse SBOM:** Read the generated SBOM (JSON).
2.  **Extract PURLs:** Iterate through components and extract Package URLs (purl).
3.  **Batch Query:** Send a batch POST request to `https://api.osv.dev/v1/querybatch`.
      * *Payload:* `{"queries": [{"package": {"purl": "pkg:npm/evil-lib@1.0.0"}}... ]}`
4.  **Filter Results:**
      * Iterate through the response.
      * **CRITICAL:** Filter specifically for Malicious indicators. Look for IDs starting with `MAL-` or entries from the `ossf-malicious-packages` source. (Ignore standard CVEs unless they are critical supply chain compromises).
5.  **Action:**
      * If Malicious Package found: Print Table to Console (Package Name, Version, Malicious ID). **Return Exit Code 1.**
      * If None found: Print "No malicious packages detected." **Return Exit Code 0.**

## 5\. User Experience & Output

### 5.1 Success Output (Console)

```text
$ ./defender scan sbom ./my-app --sbom-format cyclonedx-json --fail-on-malicious

[+] Scanner Verification: Cached binary found.
[+] Generating SBOM for './my-app'...
[+] SBOM saved to: ./sbom-output.json
[+] Analyzing dependencies for malicious signatures...
[!] ALERT: Malicious Package Detected!

| Package       | Version | ID             | Database |
| :---          | :---    | :---           | :---     |
| nodetest-pkg  | 1.0.0   | MAL-2024-1234  | OSSF     |

[X] Scan failed: 1 malicious package(s) found.
```

## 6\. Security Requirements

1.  **Secure Download:** All downloads must occur over HTTPS.
2.  **Integrity Check:** The CLI must fail if the scanner binary checksum does not match the expected hash.
3.  **No Data Exfiltration:** The SBOM is processed locally. Only PURLs (Package URLs) are sent to the OSV API; no source code or proprietary logic is uploaded.

## 7\. Test Harness Generation

To verify this feature, the solution must include a script (`generate_test_data.py`) that creates a "compromised" project context.

**Script Requirements:**

1.  Create a temporary directory.
2.  Create a `package.json` file.
3.  Inject a known "test" malicious package (often used for verification, or a real package that has been retracted/flagged in OSSF but is safe for testing file existence, e.g., a specific version of `peacenotwar` or a dummy entry that matches a `MAL-` ID in the OSV database).
4.  The test should assert that `./defender scan sbom ... --fail-on-malicious` correctly returns **Exit Code 1**.