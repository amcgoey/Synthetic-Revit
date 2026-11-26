---
name: revit-tests
description: Run Logic Tests, LegacyMockTests, and live Revit Tests via the Worker Pool or isolated cold workers. Use when executing single- or multi-version test runs, filtering by class, category, or framework, choosing shared Revit test fixtures, or running TDD loops.
---

# Revit Testing Playbook (`revit-tests`)

Run **Logic Tests**, `LegacyMock`, and live **Revit Tests** via [test_executor.py](scripts/test_executor.py). Concurrent runs stay isolated from each other and from interactive Revit sessions through per-target MSBuild isolation, **Add-In Bootstrap** ([ADR 0018](../../../docs/adrs/0018-universal-add-in-bootstrap-manifest-routing-and-studio-deployment.md)), the **Worker Pool** and **Clean-Launch Shim** ([ADR 0030](../../../docs/adrs/0030-clean-standby-worker-pool-and-clean-launch-shim.md)), and automatic `--assert-clean` journal gating ([revit-journal-telemetry](../revit-journal-telemetry/SKILL.md), [ADR 0026](../../../docs/adrs/0026-revit-journal-telemetry-strategy.md)).

## 1. Canonical 3-Step Execution Order

Under [tdd](../tdd/SKILL.md), execute tests from fastest headless feedback to full cross-version validation:

1. **Step 1 — Logic Tests:** Run headless unit and architecture checks (`Synthetic.LogicTests` across `net48`, `net8.0-windows8.0`, and `net10.0-windows8.0`, plus `Synthetic.LegacyMockTests`):
   ```powershell
   python .agents/skills/revit-tests/scripts/test_executor.py --version Logic
   ```
2. **Step 2 — Latest Revit Tests:** Run live **Revit Tests** for `CURRENT_LATEST_VERSION` in [revit-config.md](../../../docs/agents/revit-config.md) (`2027`):
   ```powershell
   python .agents/skills/revit-tests/scripts/test_executor.py --version 2027
   ```
3. **Step 3 — Multi-Version Revit Tests:** Omit `--version` (or pass a comma-separated list such as `--version 2025,2026,2027`) in a single command. `test_executor.py` runs headless targets first, then executes all selected **Revit Versions** concurrently in **Worker Pool** mode with isolated per-target builds, printing `[TESTS] Revit <Ver>: ...` lines as each version finishes and one combined Markdown summary. Consult [revit-multi-versions](../revit-multi-versions/SKILL.md) for cross-version API or `#if` fixes:
   ```powershell
   python .agents/skills/revit-tests/scripts/test_executor.py
   ```

**Completion criterion:** `test_executor.py` exits `0` after parsing `.trx` results with zero NUnit failures and passing `--assert-clean` verification on every worker **Revit Journal**.

## 2. Declaring What to Test & Typed Categories

Declare *what* to test using `--version`, `--filter`, `--category`, and `--framework` from the repository root (requires `.NET 10.0` SDK `10.0.100+`; `test_executor.py` runs a fail-fast SDK preflight). Run multi-version checks as one comma-separated or omitted-`--version` command so `test_executor.py` and the **Worker Pool** handle multi-version parallelism, fixture sharding, and detached worker lifecycle automatically:

```powershell
# Headless Logic Tests (all TFMs or one TFM) plus LegacyMockTests
python .agents/skills/revit-tests/scripts/test_executor.py --version Logic
python .agents/skills/revit-tests/scripts/test_executor.py --version Logic --framework net48

# Single or multi-version live Revit Tests (Worker Pool shards and runs versions concurrently)
python .agents/skills/revit-tests/scripts/test_executor.py --version 2027 --category RevitAdapter --filter StandardSerializationEngine
python .agents/skills/revit-tests/scripts/test_executor.py --version 2025,2026,2027 --category RevitSmoke
```

Run `python .agents/skills/revit-tests/scripts/test_executor.py --help` for the full CLI flag reference (including `--fail-fast`, `--shards`, `--no-shim`, `--no-pool`, and `--no-auto-upgrade`).

### Typed `[Category]` Attributes ([TypedCategoryAttributes.cs](../../../tests/Fixtures/Shared/TypedCategoryAttributes.cs))

| Category / Attribute | Target Project | Scope & Lifecycle |
| :--- | :--- | :--- |
| `Unit` (`[UnitTest]`) | [Synthetic.LogicTests](../../../tests/Projects/Synthetic.LogicTests/Synthetic.LogicTests.csproj) | Pure `Core/` / `Logic/` tests (including STA WPF layout/theme tests) with zero Revit process dependency. |
| `Architecture` (`[ArchitectureTest]`) | [Synthetic.LogicTests](../../../tests/Projects/Synthetic.LogicTests/Synthetic.LogicTests.csproj) | Single-parse Roslyn and XML/JSON `FitnessRule` gates enforcing partitions, 4-band DAG, registry parity, and allowlist ratchets ([ArchitectureFitnessTests](../../../tests/Architecture/Logic/ArchitectureFitnessTests.cs), [ADR 0029](../../../docs/adrs/0029-isomorphic-test-mirror-and-architecture-fitness-guardrails.md)). |
| `RevitSmoke` (`[RevitSmokeTest]`) | `Synthetic.RevitTests.<Year>` | Minimal live Revit host checks (assembly load, ribbon wiring, `ExternalEvent`, BAML `Window.Show()`). |
| `RevitAdapter` (`[RevitAdapterTest]`) | `Synthetic.RevitTests.<Year>` | Live Revit API/adapter tests executing inside rolled-back `TransactionGroup`s against a cached baseline document. |
| `RevitIsolatedDoc` (`[RevitIsolatedDocTest]`) | `Synthetic.RevitTests.<Year>` | Live Revit tests opening temp copies of `.rvt`/`.rfa` fixtures, `EditFamily`/`LoadFamily`, or linked models. |
| `Quarantine` (`[QuarantineTest]`) | [Synthetic.LegacyMockTests](../../../tests/Projects/Synthetic.LegacyMockTests/Synthetic.LegacyMockTests.csproj) or any | Quarantined suites for legacy mock tests or confirmed upstream Autodesk defects (pair with `[Explicit]` in live Revit projects). |

## 3. Worker Pool, Tooling Build, & Clean-Launch Guarantees

Pool architecture, worker scaling, RAM admission, makespan-aware sharding, and detached supervisor lifecycle are specified in [ADR 0030](../../../docs/adrs/0030-clean-standby-worker-pool-and-clean-launch-shim.md) (including its Spec #256 Addendum); inspect `tools/bin/RevitWorkerPool/RevitWorkerPool.exe --help` for standalone CLI options.

- **Automatic routing & cold fallback:** `Logic` and `LegacyMock` always execute headlessly via `dotnet test`. For **Revit Tests**, `test_executor.py` checks `SYNTHETIC_WORKER_POOL_CMD` and `tools/bin/RevitWorkerPool/RevitWorkerPool.exe`, lazy-starting a detached supervisor that survives caller command exit while isolating child workers, builds, and per-worktree `ProjectRoot` paths. When the pool binary is absent (or `--no-pool` is passed), `test_executor.py` logs a notice and runs sequential isolated cold `dotnet test` workers under **Startup Gate**.
- **Standalone tooling build:** Compile `tools/RevitWorkerPool.sln`, publish `tools/bin/RevitWorkerPool/RevitWorkerPool.exe`, and compile + Authenticode-sign (`CN=Synthetic`) the native Detours **Clean-Launch Shim** binaries via [tools/build.ps1](../../../tools/build.ps1) (drains an idle supervisor safely before cleaning output binaries):
  ```powershell
  powershell -NoProfile -ExecutionPolicy Bypass -File tools/build.ps1
  ```
- **Fail-closed Clean-Launch Shim & unshimmed mode:** Shimmed workers isolate `.addin` / `ApplicationPlugins` visibility, virtualize `AddInsSettings.json` / `AdminSettings.json` per worker PID, suppress focus steal off-screen, and fail closed (`exit 2` in `RevitWorkerPool.exe`, `exit 1` in `test_executor.py`) on injection, signature, or mode mismatch rather than silently downgrading. Pass `--no-shim` or `--no-pool` explicitly when unshimmed execution under **Startup Gate** is required (preflight also emits a read-only warning for legacy machine-wide `Synthetic*.addin` entries whose `<FullClassName>` differs from `Synthetic.Bootstrap.BootstrapApp`).

## 4. Shared Fixtures & State Management

State-management design rationale lives in [ADR 0001](../../../docs/adrs/0001-hybrid-state-management-for-revit-tests.md). Test workers set `SYNTHETIC_TEST_WORKER=1` so [`BootstrapApp`](../../../src/Synthetic.Bootstrap/BootstrapApp.cs) skips payload loading while `ricaun.NUnit` loads the isolated test build. Inherit from the shared base class matching the test's isolation profile:

- **[`RevitTemplateRollbackFixture`](../../../tests/Fixtures/Revit/RevitTemplateRollbackFixture.cs) (`[RevitAdapterTest]`):**
  - Caches one baseline `Document` per `[TestFixture]` across `[OneTimeSetUp]` / `[OneTimeTearDown]` (`BaselineKind`: `MetricProject`, `ImperialProject`, `TestTemplateCopy`, or `None`—compatible with `ricaun.NUnit 1.5.0`, which injects `UIApplication` into `[OneTimeSetUp]` and omits `[SetUpFixture]`).
  - Invokes worker initializers [`HostSettingsRegistrar.EnsureRegistered()`](../../../src/Platform/Host/Addin/HostSettingsRegistrar.cs) and [`TestWorkerJournalMarker.EnsureWritten(uiapp)`](../../../tests/Fixtures/Revit/TestWorkerJournalMarker.cs) in `[OneTimeSetUp]`.
  - Executes database writes inside `WithRolledBackDocument(...)` / `WithRolledBackDocumentPair(...)` via [`DocumentTransactionExtensions.ExecuteRolledBackGroup`](../../../src/Platform/Revit/Addin/DocumentTransactionExtensions.cs), recreates invalidated documents (`doc == null || !doc.IsValidObject || doc.IsModifiable`), and asserts `app.Documents.Size == initialDocCount` in `[OneTimeTearDown]`.
- **[`RevitIsolatedDocumentFixture`](../../../tests/Fixtures/Revit/RevitIsolatedDocumentFixture.cs) (`[RevitIsolatedDocTest]`):**
  - Runs the same worker initializers in `[OneTimeSetUp]` and creates a run root under `%TEMP%\SyntheticRevitTestDocs\<RunGuid>\`.
  - Copies `.rvt`/`.rfa` fixtures into per-test `%TEMP%\SyntheticRevitTestDocs\<RunGuid>\<TestId>\` via `OpenIsolatedModelCopy(...)` / `OpenIsolatedTestTemplateCopy(...)` (preserving tracked assets on disk), tracks opened or created documents with `TrackDocument(doc)` / `NewTrackedProjectDocument(...)`, closes tracked documents (`doc.Close(false)`) and deletes the per-test directory in `[TearDown]`, and asserts `app.Documents.Size == initialDocCount` in `[OneTimeTearDown]`.
- **Standalone `[RevitSmokeTest]` fixtures:** Call `HostSettingsRegistrar.EnsureRegistered()` and `TestWorkerJournalMarker.EnsureWritten(uiapp)` in `[OneTimeSetUp]` and assert `app.Documents.Size == initialDocCount` in `[OneTimeTearDown]`.

## 5. Build Isolation, Trust Leases, & Journal Gating

- **Per-target MSBuild & temp isolation:** Each invocation generates `<RunId>` (`YYYYMMDD_HHMMSS_<pid>_<hex>`) and builds each target inside its own `<RunBuildRoot>\<TargetKey>` subfolder (`%TEMP%\SyntheticBuilds\<RunId>\<TargetKey>`, passed via `/p:SyntheticIsolatedBuildRoot=...` and `/p:SyntheticRunId=<RunId>`; [tests/Directory.Build.props](../../../tests/Directory.Build.props), [src/Directory.Build.props](../../../src/Directory.Build.props), [tests/Directory.Build.targets](../../../tests/Directory.Build.targets)), preserving `output/Synthetic/` and **Local Deployment** (`%APPDATA%\Autodesk\Revit\Addins\`). Logs and `.trx` reports write to `tests/TestResults/<RunId>/` (latest 10 runs kept). Passing runs remove `<RunBuildRoot>` and `%TEMP%\RevitTest\*<RunId>*`; failing runs preserve `<RunBuildRoot>` for inspection.
- **Persistent `CodeSigning` & shared bundle lease:** [guid_whitelister.py](scripts/guid_whitelister.py) writes permanent `REG_DWORD=1` entries in `HKCU\Software\Autodesk\Revit\Autodesk Revit <ver>\CodeSigning` across add-in and `ApplicationPlugins` roots (`%APPDATA%`, `%ProgramData%`, and `%ProgramFiles%\Autodesk\ApplicationPlugins`). Active workers coordinate `%APPDATA%\Autodesk\ApplicationPlugins\ricaun.RevitTest.Application.bundle\PackageContents.xml` via `Local\Synthetic_RicaunBundleLease`.
- **Ascending `TestTemplate` preflight (`--no-auto-upgrade`):** Before building live **Revit Tests**, `test_executor.py` checks `tests/Assets/test_models/TestTemplate<Year>.rvt` sequentially in ascending **Revit Version** order and upgrades missing templates via `tools/RevitModelUpgrader/revit_model_upgrader.py` under a Win32 mutex (leaving `INC Standards` models untouched). Pass `--no-auto-upgrade` to fail fast instead.
- **Automatic per-shard & cold journal gating:**
  - **Worker Pool runs:** **Add-In Bootstrap** ([BootstrapApp.cs](../../../src/Synthetic.Bootstrap/BootstrapApp.cs)) emits `SYNTHETIC_WORKER_ID:<WorkerId>` into each **Standby Worker**'s **Revit Journal** (`TestWorkerJournalMarker` fallback). `test_executor.py` gates every returned `WorkerId` via [revit_journal_tool.py](../revit-journal-telemetry/scripts/revit_journal_tool.py) (`--version <ver> --worker-id <WorkerId> --assert-clean`).
  - **Cold `--no-pool` runs:** `test_executor.py` snapshots `tests/TestResults/<RunId>/known_journals_<ver>.json` pre-launch, sets `SYNTHETIC_TEST_RUN_ID=<RunId>`, terminates only that worker's PID tree on `660` s timeout, and gates via `--version <ver> --run-id <run_id> --known-journals-file <snapshot_path> --assert-clean`.
  - Any matching `.dmp` **Crash Dump**, orphaned open API transaction, or unhandled `Synthetic.*` / `SyntheticTests.*` exception fails the run even when NUnit passes. Use [revit-journal-telemetry](../revit-journal-telemetry/SKILL.md) to inspect dirty runs.
