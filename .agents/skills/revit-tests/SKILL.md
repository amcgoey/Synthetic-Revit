---
name: revit-tests
description: Run NUnit unit and integration tests across multiple versions of Revit. Use when executing test commands or running TDD loops.
---

# Revit Testing Playbook (`revit-tests`)

Use this playbook to run tests efficiently and validate multi-version compatibility.

## 1. TDD Execution Sequence

When writing code under the `/tdd` loop, execute tests in the following order to maximize speed and feedback:

1. **Step 1 (Logic Loop):** Run headless Logic tests first. Iterate until they are Green.
   ```powershell
   python .agents/skills/revit-tests/scripts/test_executor.py --version Logic
   ```
2. **Step 2 (Latest Revit Loop):** Run integration tests for the version defined by `CURRENT_LATEST_VERSION` in `docs/agents/revit-config.md` (e.g. `2026`). Iterate until they are Green.
   ```powershell
   python .agents/skills/revit-tests/scripts/test_executor.py --version 2026
   ```
3. **Step 3 (Multi-Version Validation):** Once the feature is complete, run tests across all versions to ensure multi-version compatibility.
   ```powershell
   python .agents/skills/revit-tests/scripts/test_executor.py
   ```

Refer to the `revit-multi-versions` skill to resolve compilation or API discrepancies detected during Step 3.

## 2. Test Execution Commands

Use the unified Python test executor to build the solution and run tests:

* **Run logic tests:**
  ```powershell
  python .agents/skills/revit-tests/scripts/test_executor.py --version Logic
  ```
* **Run specific Revit version tests (e.g. latest supported):**
  ```powershell
  # Replace 2026 with the value of CURRENT_LATEST_VERSION from docs/agents/revit-config.md
  python .agents/skills/revit-tests/scripts/test_executor.py --version 2026
  ```
* **Filter to specific test classes or methods:**
  ```powershell
  python .agents/skills/revit-tests/scripts/test_executor.py --version 2026 --filter "ParameterEngineTests"
  ```

## 3. Publisher Verification Bypass

The executor script automatically runs `guid_whitelister.py` to bypass Autodesk's code-signing verification prompt. It:
1. Extracts ClientId/AddInId GUIDs from the local `.addin` files and test framework.
2. Writes them to the registry under `HKCU\Software\Autodesk\Revit\Autodesk Revit <Version>\CodeSigning` with a value of `1`.
3. Cleans up these temporary registry entries when the test run exits.
