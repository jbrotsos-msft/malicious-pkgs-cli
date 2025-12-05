#!/usr/bin/env python3
"""
Test Harness Generator for Defender SBOM CLI
Creates a compromised project context for testing malicious package detection.
"""

import os
import json
import shutil
import subprocess
import sys

def create_test_data():
    """Create a temporary directory with a package.json containing a known malicious package."""
    
    # Create temporary test directory
    test_dir = os.path.join(os.getcwd(), "test-malicious-project")
    
    # Clean up if it already exists
    if os.path.exists(test_dir):
        shutil.rmtree(test_dir)
    
    os.makedirs(test_dir)
    print(f"[+] Created test directory: {test_dir}")
    
    # Create package.json with a known test malicious package
    # Note: Using 'peacenotwar' version 9.1.1 which was flagged as malicious
    # This is a real historical example that is safe to test against
    package_json = {
        "name": "test-vulnerable-app",
        "version": "1.0.0",
        "description": "Test application with known malicious dependency",
        "dependencies": {
            "peacenotwar": "9.1.1"
        }
    }
    
    package_json_path = os.path.join(test_dir, "package.json")
    with open(package_json_path, "w") as f:
        json.dump(package_json, f, indent=2)
    
    print(f"[+] Created package.json with test malicious package")
    
    # Create a simple index.js file
    index_js = """
// Test application
console.log("Test application with malicious dependency");
"""
    
    index_js_path = os.path.join(test_dir, "index.js")
    with open(index_js_path, "w") as f:
        f.write(index_js)
    
    print(f"[+] Created index.js")
    
    return test_dir

def run_defender_scan(test_dir, defender_path="./bin/Debug/net9.0/defender"):
    """Run the Defender CLI against the test directory."""
    
    print(f"\n[+] Running Defender SBOM scan on {test_dir}...")
    
    # Construct the command
    # Note: Adjust the path to the defender executable as needed
    if os.name == 'nt':  # Windows
        defender_path = "./bin/Debug/net9.0/defender.exe"
    
    cmd = [
        "dotnet",
        "run",
        "--",
        "scan",
        "sbom",
        test_dir,
        "--sbom-format",
        "cyclonedx-json",
        "--fail-on-malicious"
    ]
    
    try:
        result = subprocess.run(
            cmd,
            cwd=os.path.dirname(os.path.dirname(test_dir)),
            capture_output=True,
            text=True
        )
        
        print("\n--- STDOUT ---")
        print(result.stdout)
        
        if result.stderr:
            print("\n--- STDERR ---")
            print(result.stderr)
        
        print(f"\n[+] Exit Code: {result.returncode}")
        
        # Assert that exit code is 1 (malicious package found)
        if result.returncode == 1:
            print("\n[✓] TEST PASSED: Malicious package detected correctly (exit code 1)")
            return True
        else:
            print("\n[✗] TEST FAILED: Expected exit code 1, got {result.returncode}")
            return False
            
    except Exception as e:
        print(f"\n[✗] Error running defender: {e}")
        return False

def cleanup(test_dir):
    """Clean up the test directory."""
    if os.path.exists(test_dir):
        shutil.rmtree(test_dir)
        print(f"\n[+] Cleaned up test directory: {test_dir}")

def main():
    """Main test harness execution."""
    print("=" * 60)
    print("Defender SBOM CLI - Test Harness")
    print("=" * 60)
    
    # Create test data
    test_dir = create_test_data()
    
    try:
        # Run the scan
        success = run_defender_scan(test_dir)
        
        # Return appropriate exit code
        sys.exit(0 if success else 1)
        
    finally:
        # Cleanup
        cleanup(test_dir)
        
        # Clean up generated SBOM files
        sbom_files = ["sbom-output.json", "sbom-output.xml"]
        for f in sbom_files:
            if os.path.exists(f):
                os.remove(f)
                print(f"[+] Removed {f}")

if __name__ == "__main__":
    main()
