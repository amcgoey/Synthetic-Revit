---
name: revit-tests
description: Run C# NUnit unit and integration tests efficiently across multiple versions of Revit, managing code signing, environment isolation, and journal diagnostics.
---

# Revit Testing Playbook (`revit-tests`)

This skill provides guidelines and procedures for executing tests efficiently across all supported target Revit versions (2022–2027) using automated tooling.

## 1. Test Architecture & Directory Structure

The repository contains two levels of testing:
1. **Logic Tests (`SyntheticTests.Logic`):** Pure-C# logic tests that do not depend on the Revit API. These run in a headless NUnit runner instantly.
2. **Revit Integration Tests (`SyntheticTests202X`):** Version-specific tests that require a live Revit database. These compile against specific Revit API assemblies and run within a booted Revit process via the `ricaun.RevitTest` runner.

Test directories at the workspace root:
```
tests/
├── SyntheticTests.Logic/       ← Headless NUnit logic tests
├── SyntheticTests.Shared/      ← Shared test files referenced by versioned tests
├── SyntheticTests2023/         ← Revit 2023 integration tests
├── SyntheticTests2024/         ← Revit 2024 integration tests
├── SyntheticTests2025/         ← Revit 2025 integration tests
├── SyntheticTests2026/         ← Revit 2026 integration tests
└── TestResults/                ← Target folder for NUnit TRX reports and logs
```

## 2. Command Line Test Execution

Use the unified Python test executor located at `.agents/skills/revit-tests/scripts/test_executor.py` to automate solutions restoring, compilation, environment setup, execution, and telemetry collection.

### Run Headless Logic Tests
```powershell
python .agents/skills/revit-tests/scripts/test_executor.py --version Logic
```

### Run Revit Version Integration Tests
```powershell
# Run tests against a specific Revit version (e.g. 2026)
python .agents/skills/revit-tests/scripts/test_executor.py --version 2026
```

### Run Specific Test Classes or Methods
Use the `--filter` parameter to run targeted tests (uses standard NUnit filter patterns):
```powershell
python .agents/skills/revit-tests/scripts/test_executor.py --version 2026 --filter "ParameterEngineTests"
```

## 3. Automation and Telemetry Details

To run tests headlessly without human blockages, the executor script automates several environment adjustments:

### Code Signing & Registry Whitelisting
To prevent the Autodesk dialog prompt: *"The publisher of this add-in could not be verified. What do you want to do?"* (which halts headless execution), the script:
1. Scans `.addin` files in AppData/ProgramData and the `ricaun.RevitTest` bundle.
2. Extracts their ClientId and AddInId GUID constants.
3. Automatically writes them to `HKCU\Software\Autodesk\Revit\Autodesk Revit <Version>\CodeSigning` with a value of `1` (Approved).
4. Cleans up these temporary registry entries upon test completion.

### Revit Startup Isolation (Optional)
To speed up Revit booting, the executor includes helper methods in `addin_isolator.py` to temporarily rename third-party `.addin` files to `.bak` during the test run, restoring them upon completion.

### Journal Log Diagnostics
When integration tests run, `revit_journal_tool.py` parses the generated Revit session journal files under `%LOCALAPPDATA%\Autodesk\Revit\Autodesk Revit <Version>\Journals\` to:
- Capture and format managed stack traces / NUnit telemetry.
- Detect warning dialogs (`DBG_WARN`).
- Monitor RAM growth (`RAM Statistics`).
- Verify if the process shut down cleanly or crashed (detects matching `.dmp` files).

## 4. Test Code Modification Anchors

When writing temporary validation code or test-harness overrides in main source files, always wrap them in comments so they are easily identified and reverted:

* **Pure Additions:**
  ```csharp
  // [AG2_TEST_START: FeatureName]
  // REVERT_METHOD: To remove, safely delete this entire block.
  ...
  // [AG2_TEST_END: FeatureName]
  ```

* **Overrides:**
  ```csharp
  // [AG2_TEST_START: FeatureName]
  /* ORIGINAL_CODE:
  original_line_of_code;
  */
  override_line_of_code;
  // [AG2_TEST_END: FeatureName]
  ```
