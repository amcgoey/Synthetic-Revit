---
name: revit-tests
description: Run NUnit Logic, LegacyMock, and live Revit tests via isolated workers. Use when executing test runs, filtering by class, category, or framework, choosing shared Revit test fixtures, or running TDD loops.
---

# Revit Testing Playbook (`revit-tests`)

Run headless logic tests and live **Revit Tests** via [test_executor.py](scripts/test_executor.py). Concurrent agents and test runs stay isolated from each other and from interactive Revit sessions through per-run MSBuild outputs, Win32 bundle leases, persistent code-signing trust, and post-run journal gating ([revit-journal-telemetry](../revit-journal-telemetry/SKILL.md)).

## 1. Canonical 3-Step Execution Order

Under [tdd](../tdd/SKILL.md), execute tests from fastest feedback to full cross-version validation:

1. **Step 1 — Logic Tests:** Run headless unit and architecture tests until green (`Synthetic.LogicTests` on both `net48` and `net8.0-windows8.0`, plus `Synthetic.LegacyMockTests`):
   ```powershell
   python .agents/skills/revit-tests/scripts/test_executor.py --version Logic
   ```
2. **Step 2 — Latest Revit Tests:** Run live **Revit Tests** for `CURRENT_LATEST_VERSION` in [revit-config.md](../../../docs/agents/revit-config.md) (e.g. `2026`) until green:
   ```powershell
   python .agents/skills/revit-tests/scripts/test_executor.py --version 2026
   ```
3. **Step 3 — Multi-Version Revit Tests:** Omit `--version` to run `Logic` (including `LegacyMock`) and all discovered Revit versions (`2023–2026`) with automatic `--assert-clean` journal gating:
   ```powershell
   python .agents/skills/revit-tests/scripts/test_executor.py
   ```
   Consult [revit-multi-versions](../revit-multi-versions/SKILL.md) to resolve cross-version API or `#if` differences.

**Completion criterion:** A run passes when `test_executor.py` exits `0`—confirming all targeted NUnit assertions passed, TRX results parsed cleanly, and every Revit target cleared the automatic post-run `--assert-clean` journal gate.

## 2. CLI Flags & Category Selection

Run commands from the repository root:

```powershell
# Headless LogicTests (net48 + net8.0-windows8.0) plus LegacyMockTests
python .agents/skills/revit-tests/scripts/test_executor.py --version Logic

# Target only LegacyMockTests or a single TFM on Synthetic.LogicTests
python .agents/skills/revit-tests/scripts/test_executor.py --version LegacyMock
python .agents/skills/revit-tests/scripts/test_executor.py --version Logic --framework net48

# Filter a Revit target by typed [Category] and class/method fragment
python .agents/skills/revit-tests/scripts/test_executor.py --version 2026 --category RevitAdapter --filter StandardSerializationEngine

# Force isolated cold Revit workers (bypassing optional RevitWorkerPool)
python .agents/skills/revit-tests/scripts/test_executor.py --version 2026 --no-pool
```

| Flag | Default | Behavior |
| :--- | :--- | :--- |
| `--version <Target>` | All discovered targets | Targets `Logic` (`Synthetic.LogicTests` on `net48` + `net8.0-windows8.0` and `Synthetic.LegacyMockTests`), `LegacyMock` (`Synthetic.LegacyMockTests` alone), or a Revit year (`2023`–`2026`). |
| `--framework <TFM>` | All project TFMs | Targets a single framework on multi-TFM projects (e.g. `net48` or `net8.0-windows8.0` on `Synthetic.LogicTests`), skipping projects lacking that TFM. |
| `--filter <Fragment>` | None | Applies `FullyQualifiedName~<Fragment>` (combines with `--category` via `&`). |
| `--category <Name>` | None | Applies `TestCategory=<Name>` for `Logic`/`LegacyMock`; for Revit targets, scans `tests/Fixtures/`, `Revit/` partitions under `tests/{Features, RevitDOM, Platform}/`, and `tests/Projects/Synthetic.RevitTests.<ver>/` (resolving inherited class and method attributes, exiting `0` with `0 matching tests` when empty) to emit `FullyQualifiedName~...` filters compatible with `ricaun.RevitTest.TestAdapter`. |
| `--no-pool` | `False` | Skips optional `RevitWorkerPool` probing (`SYNTHETIC_WORKER_POOL_CMD` or `tools/RevitWorkerPool*`) and launches isolated cold Revit workers. |

### Typed `[Category]` Attributes ([TypedCategoryAttributes.cs](../../../tests/Fixtures/Shared/TypedCategoryAttributes.cs))

| Category / Attribute | Target Project | Scope & Lifecycle |
| :--- | :--- | :--- |
| `Unit` (`[UnitTest]`) | [Synthetic.LogicTests](../../../tests/Projects/Synthetic.LogicTests/Synthetic.LogicTests.csproj) | Pure `Core/` / `Logic/` tests (including STA WPF layout/theme tests) with zero Revit process dependency. |
| `Architecture` (`[ArchitectureTest]`) | [Synthetic.LogicTests](../../../tests/Projects/Synthetic.LogicTests/Synthetic.LogicTests.csproj) | Single-parse Roslyn and XML/JSON `FitnessRule` gates enforcing partitions, 4-band DAG, registry parity, and allowlist ratchets ([ArchitectureFitnessTests](../../../tests/Architecture/Logic/ArchitectureFitnessTests.cs), [ADR 0029](../../../docs/adrs/0029-isomorphic-test-mirror-and-architecture-fitness-guardrails.md)). |
| `RevitSmoke` (`[RevitSmokeTest]`) | `Synthetic.RevitTests.<Year>` | Minimal live Revit host checks (assembly load, ribbon wiring, `ExternalEvent`, BAML `Window.Show()`). |
| `RevitAdapter` (`[RevitAdapterTest]`) | `Synthetic.RevitTests.<Year>` | Live Revit API/adapter tests executing inside rolled-back `TransactionGroup`s against a cached baseline document. |
| `RevitIsolatedDoc` (`[RevitIsolatedDocTest]`) | `Synthetic.RevitTests.<Year>` | Live Revit tests opening temp copies of `.rvt`/`.rfa` fixtures, `EditFamily`/`LoadFamily`, or linked models. |
| `Quarantine` (`[QuarantineTest]`) | [Synthetic.LegacyMockTests](../../../tests/Projects/Synthetic.LegacyMockTests/Synthetic.LegacyMockTests.csproj) or any | Quarantined suites for legacy mock tests or confirmed upstream Autodesk defects (pair with `[Explicit]` in live Revit projects). |

## 3. Shared Fixtures & State Management

State-management design rationale lives in [ADR 0001](../../../docs/adrs/0001-hybrid-state-management-for-revit-tests.md). Inherit from the shared base class matching the test's isolation profile:

- **[`RevitTemplateRollbackFixture`](../../../tests/Fixtures/Revit/RevitTemplateRollbackFixture.cs) (`[RevitAdapterTest]`):**
  - Caches one baseline `Document` per `[TestFixture]` across `[OneTimeSetUp]` / `[OneTimeTearDown]` (`BaselineKind`: `MetricProject`, `ImperialProject`, `TestTemplateCopy`, or `None`—compatible with `ricaun.NUnit 1.5.0`, which injects `UIApplication` into `[OneTimeSetUp]` and omits `[SetUpFixture]`).
  - Invokes cold-worker initializers [`HostSettingsRegistrar.EnsureRegistered()`](../../../src/Platform/Host/Addin/HostSettingsRegistrar.cs) and [`TestWorkerJournalMarker.EnsureWritten(uiapp)`](../../../tests/Fixtures/Revit/TestWorkerJournalMarker.cs) in `[OneTimeSetUp]`.
  - Executes database writes inside `WithRolledBackDocument(...)` / `WithRolledBackDocumentPair(...)` via [`DocumentTransactionExtensions.ExecuteRolledBackGroup`](../../../src/Platform/Revit/Addin/DocumentTransactionExtensions.cs), recreates invalidated documents (`doc == null || !doc.IsValidObject || doc.IsModifiable`), and asserts `app.Documents.Size == initialDocCount` in `[OneTimeTearDown]`.
- **[`RevitIsolatedDocumentFixture`](../../../tests/Fixtures/Revit/RevitIsolatedDocumentFixture.cs) (`[RevitIsolatedDocTest]`):**
  - Runs the same cold-worker initializers in `[OneTimeSetUp]` and creates a run root under `%TEMP%\SyntheticRevitTestDocs\<RunGuid>\`.
  - Copies `.rvt`/`.rfa` fixtures into per-test `%TEMP%\SyntheticRevitTestDocs\<RunGuid>\<TestId>\` via `OpenIsolatedModelCopy(...)` / `OpenIsolatedTestTemplateCopy(...)` (keeping tracked assets on disk unmutated), tracks opened or created documents with `TrackDocument(doc)` / `NewTrackedProjectDocument(...)`, closes tracked documents (`doc.Close(false)`) and deletes the per-test directory in `[TearDown]`, and asserts `app.Documents.Size == initialDocCount` in `[OneTimeTearDown]`.
- **Standalone `[RevitSmokeTest]` fixtures:** Call `HostSettingsRegistrar.EnsureRegistered()` and `TestWorkerJournalMarker.EnsureWritten(uiapp)` in `[OneTimeSetUp]` and assert `app.Documents.Size == initialDocCount` in `[OneTimeTearDown]`.

## 4. Isolated Concurrent Worker Architecture

### Per-Run MSBuild & Temp Isolation
- Each invocation generates `<RunId>` (`YYYYMMDD_HHMMSS_<pid>_<hex>`) and builds with `/p:SyntheticIsolatedBuildRoot=%TEMP%\SyntheticBuilds\<RunId>` and `/p:SyntheticRunId=<RunId>` ([tests/Directory.Build.props](../../../tests/Directory.Build.props), [src/Directory.Build.props](../../../src/Directory.Build.props)).
- Setting `SyntheticIsolatedBuildRoot` sets `SkipDeployAddin=true` and redirects `BaseIntermediateOutputPath` and `OutputPath` (`<SyntheticIsolatedBuildRoot>\bin\<Project>_<RunId>\`), keeping `output/Synthetic/` and `%APPDATA%\Autodesk\Revit\Addins\` intact.
- Logs and `.trx` reports write to `tests/TestResults/<RunId>/` (rotated to keep the latest 10 runs).
- Passing runs remove `<SyntheticIsolatedBuildRoot>` and `%TEMP%\RevitTest\*<RunId>*`; failing runs preserve `<SyntheticIsolatedBuildRoot>` for inspection.

### Worker Process Controls & Timeout Cleanup
- [tests/Directory.Build.targets](../../../tests/Directory.Build.targets) injects `NUnit.Open=true`, `NUnit.Close=true`, `NUnit.Timeout=10` (minutes), and `ricaun.RevitTest.Application.Tasks.Timeout=5`.
- `test_executor.py` launches Revit workers with `SYNTHETIC_PROJECT_ROOT=<workspace>`, `SYNTHETIC_TEST_WORKER=1` (directing [`BootstrapApp`](../../../src/Synthetic.Bootstrap/BootstrapApp.cs) to skip payload staging/loading at Revit startup so `ricaun.NUnit` loads the isolated test build), `SYNTHETIC_TEST_RUN_ID=<RunId>`, and `RICAUN_REVITTEST_CONSOLE_PROCESS_ARGUMENTS=/nosplash`.
- When `dotnet test` exceeds `660` s (`NUnit.Timeout` + 60 s boot buffer), `execute_revit_test_process` terminates only that worker's child PID tree with `taskkill /F /T /PID`.

### Shared Bundle Lease (`RicaunBundleLease`)
- Concurrent workers coordinate `%APPDATA%\Autodesk\ApplicationPlugins\ricaun.RevitTest.Application.bundle\PackageContents.xml` via the Win32 mutex `Local\Synthetic_RicaunBundleLease` and a shared-read handle (without `FILE_SHARE_DELETE`), keeping `ProgramData` free of bundle seeding.
- The first active worker extracts the bundle from `ricaun.RevitTest.TestAdapter.dll` into `%APPDATA%`; only the final exiting worker deletes the bundle once all shared-read handles close.

## 5. Code-Signing Whitelist & Add-In Safety

- **Persistent `CodeSigning` Registry Trust:** [guid_whitelister.py](scripts/guid_whitelister.py) recursively scans `.addin` (`ClientId`/`AddInId`) and `PackageContents.xml` (`ProductCode`/`ClientId`/`AddInId`) manifests across all Revit add-in roots (`%ProgramFiles%\Autodesk\Revit <ver>\AddIns`, `%ProgramFiles%\Autodesk\Revit\Addins\<ver>`, `%ProgramData%\Autodesk\Revit\Addins\<ver>`, `%AppData%\Autodesk\Revit\Addins\<ver>`) and `ApplicationPlugins` bundle roots (`%AppData%\Autodesk\ApplicationPlugins`, `%ProgramData%\Autodesk\ApplicationPlugins`), plus `KNOWN_FRAMEWORK_GUIDS`. It persists each GUID (lowercase and uppercase `REG_DWORD=1`) in `HKCU\Software\Autodesk\Revit\Autodesk Revit <ver>\CodeSigning` permanently to eliminate registry races between concurrent workers.
- **`AddInsSettings.json` Preservation:** Test runs leave `AddInsSettings.json` intact; on startup, `TempDisableThirdPartyAddins.restore_addins()` self-heals any orphaned `AddInsSettings.json.bak` left behind by legacy runs.

## 6. Post-Run Journal Cleanliness Gate

- Before starting each Revit worker, `test_executor.py` snapshots existing `journal*.txt` files to `tests/TestResults/<RunId>/known_journals_<ver>.json`.
- Inside Revit, [`BootstrapApp`](../../../src/Synthetic.Bootstrap/BootstrapApp.cs) and [`TestWorkerJournalMarker.EnsureWritten(uiapp)`](../../../tests/Fixtures/Revit/TestWorkerJournalMarker.cs) emit `SYNTHETIC_TEST_RUN:<RunId>` once per worker process into the active **Revit Journal**.
- Immediately after each Revit run finishes, `test_executor.py` invokes [revit_journal_tool.py](../revit-journal-telemetry/scripts/revit_journal_tool.py):
  ```powershell
  python .agents/skills/revit-journal-telemetry/scripts/revit_journal_tool.py --version <ver> --run-id <run_id> --known-journals-file <snapshot_path> --assert-clean
  ```
- Any matching `.dmp` **Crash Dump**, orphaned open transaction, or unhandled `Synthetic.*` / `SyntheticTests.*` exception fails the run even when NUnit reports passing tests. Use [revit-journal-telemetry](../revit-journal-telemetry/SKILL.md) to inspect dirty runs.
