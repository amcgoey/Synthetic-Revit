---
name: revit-tests
description: Run Logic Tests, LegacyMockTests, and live Revit Tests via the Worker Pool or isolated cold workers. Use when executing test runs, warming or draining Standby Workers, filtering by class, category, or framework, choosing shared Revit test fixtures, or running TDD loops.
---

# Revit Testing Playbook (`revit-tests`)

Run **Logic Tests**, `LegacyMock`, and live **Revit Tests** via [test_executor.py](scripts/test_executor.py). Concurrent runs stay isolated from each other and from interactive Revit sessions through per-run MSBuild isolation, **Add-In Bootstrap** ([ADR 0018](../../../docs/adrs/0018-universal-add-in-bootstrap-manifest-routing-and-studio-deployment.md)), the **Worker Pool** and **Clean-Launch Shim** ([ADR 0030](../../../docs/adrs/0030-clean-standby-worker-pool-and-clean-launch-shim.md)), and post-run `--assert-clean` journal gating ([revit-journal-telemetry](../revit-journal-telemetry/SKILL.md), [ADR 0026](../../../docs/adrs/0026-revit-journal-telemetry-strategy.md)).

## 1. Canonical 3-Step Execution Order

Under [tdd](../tdd/SKILL.md), execute tests from fastest headless feedback to full cross-version validation:

1. **Step 1 — Logic Tests:** Run headless unit and architecture checks (`Synthetic.LogicTests` across `net48`, `net8.0-windows8.0`, and `net10.0-windows8.0`, plus `Synthetic.LegacyMockTests` on `net10.0-windows8.0` / `Synthetic2027`) directly via `dotnet test`:
   ```powershell
   python .agents/skills/revit-tests/scripts/test_executor.py --version Logic
   ```
2. **Step 2 — Latest Revit Tests:** Run live **Revit Tests** for `CURRENT_LATEST_VERSION` in [revit-config.md](../../../docs/agents/revit-config.md) (`2027`):
   ```powershell
   python .agents/skills/revit-tests/scripts/test_executor.py --version 2027
   ```
3. **Step 3 — Multi-Version Revit Tests:** Omit `--version` to run `Logic` (`LegacyMock` included) and all discovered **Revit Versions** (`2023–2027`), automatically pipelining `N -> N+1` **Standby Workers** across versions. Consult [revit-multi-versions](../revit-multi-versions/SKILL.md) for cross-version API or `#if` fixes:
   ```powershell
   python .agents/skills/revit-tests/scripts/test_executor.py
   ```

**Completion criterion:** `test_executor.py` exits `0` after parsing `.trx` results with zero NUnit failures and passing `--assert-clean` verification on every worker **Revit Journal**.

## 2. Test Executor CLI & Typed Categories

Run commands from the repository root (requires `.NET 10.0` SDK `10.0.100+`; `test_executor.py` runs a read-only fail-fast SDK preflight before building):

```powershell
# Headless Logic Tests (net48 + net8.0-windows8.0 + net10.0-windows8.0) plus LegacyMockTests
python .agents/skills/revit-tests/scripts/test_executor.py --version Logic
python .agents/skills/revit-tests/scripts/test_executor.py --version Logic --framework net48

# Live Revit Tests routed automatically through Worker Pool (ECMA-335 discovery + LPT Test Shards)
python .agents/skills/revit-tests/scripts/test_executor.py --version 2027 --category RevitAdapter --filter StandardSerializationEngine
python .agents/skills/revit-tests/scripts/test_executor.py --version 2027 --shards 2

# Unshimmed Worker Pool (Startup Gate) or isolated cold dotnet test fallback
python .agents/skills/revit-tests/scripts/test_executor.py --version 2027 --no-shim
python .agents/skills/revit-tests/scripts/test_executor.py --version 2027 --no-pool
```

| Flag | Default | Behavior |
| :--- | :--- | :--- |
| `--version <Target>` | All discovered targets | Targets `Logic` (`Synthetic.LogicTests` `net48` + `net8.0-windows8.0` + `net10.0-windows8.0` and `Synthetic.LegacyMockTests`), `LegacyMock`, or a **Revit Version** (`2023`–`2027`). |
| `--framework <TFM>` | All project TFMs | Targets a single framework on multi-TFM projects (e.g. `net48`, `net8.0-windows8.0`, or `net10.0-windows8.0`), skipping projects lacking that TFM. |
| `--filter <Fragment>` | None | Applies `FullyQualifiedName~<Fragment>` (combines with `--category` via `&`). |
| `--category <Name>` | None | Applies `TestCategory=<Name>` for headless runs; in **Worker Pool** mode uses ECMA-335 assembly metadata (`PEReader`) across TFMs; in cold `--no-pool` mode scans C# sources under `tests/`. Exits `0` (`0 matching tests`) when empty. |
| `--shards <N>` | Auto (`<= 4`) | Requests `N >= 1` **Test Shards** in **Worker Pool** mode (clamped to fixture count and host cap `4`); ignored with notice in cold mode. |
| `--no-shim` | `False` | Disables the **Clean-Launch Shim** in **Worker Pool** mode (`StandardProcessLauncher` + **Startup Gate**; also set via `SYNTHETIC_CLEAN_SHIM=0`). Drain a running shimmed supervisor before switching modes. |
| `--no-pool` | `False` | Bypasses **Worker Pool** probing (`SYNTHETIC_WORKER_POOL_CMD` / `tools/bin/RevitWorkerPool/RevitWorkerPool.exe`) and runs isolated cold `dotnet test` workers under **Startup Gate**. |
| `--no-auto-upgrade` | `False` | Fail fast instead of invoking `RevitModelUpgrader` when `tests/Assets/test_models/TestTemplate<Year>.rvt` is missing. |

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

Design rationale and binary details live in [ADR 0030](../../../docs/adrs/0030-clean-standby-worker-pool-and-clean-launch-shim.md).

### Standalone Tooling Build & Routing
- **Build command:** Compile `tools/RevitWorkerPool.sln`, publish `tools/bin/RevitWorkerPool/RevitWorkerPool.exe`, and compile + Authenticode-sign (`CN=Synthetic`, SHA256) the native Detours **Clean-Launch Shim** binaries (`RevitCleanShim.dll`, `RevitCleanLauncher.dll`) via [tools/build.ps1](../../../tools/build.ps1):
  ```powershell
  powershell -NoProfile -ExecutionPolicy Bypass -File tools/build.ps1
  ```
- **Automatic routing & fallback:** `Logic` and `LegacyMock` always execute headlessly via `dotnet test`. For **Revit Tests**, `test_executor.py` checks `SYNTHETIC_WORKER_POOL_CMD` first and then `tools/bin/RevitWorkerPool/RevitWorkerPool.exe` (plus legacy `tools/RevitWorkerPool*` paths). When found (and `--no-pool` is omitted), **Revit Tests** route automatically through the **Worker Pool**; when unbuilt, `test_executor.py` logs a diagnostic notice and falls back automatically to isolated cold `dotnet test` workers.

### Lifecycle, Sharding, & Concurrency Limits
```powershell
tools/bin/RevitWorkerPool/RevitWorkerPool.exe warm    # Boot 2 Standby Workers for CURRENT_LATEST_VERSION (--version, --count, --no-shim, --json)
tools/bin/RevitWorkerPool/RevitWorkerPool.exe status  # Report booting, readyStandby, active, totalResident/4, and per-version counts (--json)
tools/bin/RevitWorkerPool/RevitWorkerPool.exe drain   # Terminate all workers, release bundle lease, and exit supervisor (--json)
```
- **On-demand lifecycle:** Pooled `run` commands lazy-start the supervisor (`1` standby target on `CURRENT_LATEST_VERSION`; `2` on explicit `warm`), enforce a `20–30 min` idle scale-to-zero TTL (`25 min` default, `--idle-ttl-minutes <20-30>`), and assign every worker to a Win32 Job Object (`JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`) prior to `ResumeThread`.
- **Single-use recycling & `4`-worker cap:** Active workers cap at `4` `Revit.exe` processes host-wide across all **Revit Versions** and queue concurrently in fair round-robin order. Every consumed **Standby Worker** terminates immediately after its **Test Shard** finishes so modified test assemblies always start in a fresh CLR context.
- **Duration-weighted LPT Test Shards:** Multi-fixture runs partition strictly at `[TestFixture]` boundaries using rolling EMA durations cached in `%LOCALAPPDATA%\Synthetic\WorkerPool\fixture_timings_<Year>.json` (falling back to ECMA-335 test method counts for unseen fixtures) and merge shard results into one `.trx`.
- **Cross-version pipelining & `+1` lookahead:** Multi-version sweeps (`test_executor.py` passing `--next-version`) pipeline `1` pre-warm of `N+1` inside cap `4` as version `N` enters its final shard. Under saturation, the supervisor boots at most `1` lookahead **Standby Worker** (`TotalResident <= 5`) strictly when all four guards hold: `Active == 4`, queued work exists, **Clean-Launch Shim** is enabled, and free physical RAM is `>= 12 GiB`.

### Clean-Launch Shim, Safety, & Unshimmed Fallbacks
- **Fail-closed Clean-Launch Shim:** `CleanDetoursProcessLauncher` verifies `CN=Synthetic` Authenticode signatures on `RevitCleanShim.dll` and `RevitCleanLauncher.dll` and injects Detours hooks strictly into `Revit.exe` (preserving child processes such as `msedgewebview2.exe`), limiting `.addin` and `ApplicationPlugins` visibility to Autodesk built-ins, `Synthetic<Year>.addin`, and `ricaun.RevitTest.Application.bundle`. On signature, injection, or startup failure, `RevitWorkerPool.exe` exits `2` (`fail-closed`), while `test_executor.py` fails closed with exit `1` without downgrading or falling back to `--no-shim`/`--no-pool` (including when `shimEnabled=false` unexpectedly). Pass `--no-shim` explicitly when unshimmed execution is required.
- **Zero host mutation & zero focus steal:** Shimmed workers virtualize `AddInsSettings.json` and `AdminSettings.json` per worker PID under `%TEMP%\SyntheticCleanProfile\<PID>\`, keeping host `AddInsSettings.json` and interactive Revit sessions untouched (startup also self-heals orphaned `.bak` files). Out-of-process `SW_SHOWMINNOACTIVE` and `SetWinEventHook` place worker windows off-screen (`-32000, -32000`, `SWP_NOACTIVATE`), keeping WPF `IsVisible == true` without stealing desktop focus.
- **Readiness, `--no-shim` Startup Gate, & legacy preflight warning:** Workers report readiness only on named-pipe `ServerMessage.IsBusy == false`. Unshimmed runs (`--no-shim` or cold `--no-pool`) coordinate per **Revit Version** via Win32 **Startup Gate** (`Local\Synthetic_RevitStartupGate_<Year>`) plus a modal watchdog that kills and retries once on `TaskDialog_External_Tools_External_Tool_Failure`, and emit a read-only warning if any machine-wide `Synthetic*.addin` entry (`%ProgramFiles%\Autodesk\Revit\Addins\<Year>\` primary + `%ProgramData%` secondary for `2027+`; `%ProgramData%\Autodesk\Revit\Addins\<Year>\` for `2023–2026`) specifies `<FullClassName>` other than `Synthetic.Bootstrap.BootstrapApp`.

## 4. Shared Fixtures & State Management

State-management design rationale lives in [ADR 0001](../../../docs/adrs/0001-hybrid-state-management-for-revit-tests.md). Test workers set `SYNTHETIC_TEST_WORKER=1` so [`BootstrapApp`](../../../src/Synthetic.Bootstrap/BootstrapApp.cs) skips payload loading while `ricaun.NUnit` loads the isolated test build. Inherit from the shared base class matching the test's isolation profile:

- **[`RevitTemplateRollbackFixture`](../../../tests/Fixtures/Revit/RevitTemplateRollbackFixture.cs) (`[RevitAdapterTest]`):**
  - Caches one baseline `Document` per `[TestFixture]` across `[OneTimeSetUp]` / `[OneTimeTearDown]` (`BaselineKind`: `MetricProject`, `ImperialProject`, `TestTemplateCopy`, or `None`—compatible with `ricaun.NUnit 1.5.0`, which injects `UIApplication` into `[OneTimeSetUp]` and omits `[SetUpFixture]`).
  - Invokes worker initializers [`HostSettingsRegistrar.EnsureRegistered()`](../../../src/Platform/Host/Addin/HostSettingsRegistrar.cs) and [`TestWorkerJournalMarker.EnsureWritten(uiapp)`](../../../tests/Fixtures/Revit/TestWorkerJournalMarker.cs) in `[OneTimeSetUp]`.
  - Executes database writes inside `WithRolledBackDocument(...)` / `WithRolledBackDocumentPair(...)` via [`DocumentTransactionExtensions.ExecuteRolledBackGroup`](../../../src/Platform/Revit/Addin/DocumentTransactionExtensions.cs), recreates invalidated documents (`doc == null || !doc.IsValidObject || doc.IsModifiable`), and asserts `app.Documents.Size == initialDocCount` in `[OneTimeTearDown]`.
- **[`RevitIsolatedDocumentFixture`](../../../tests/Fixtures/Revit/RevitIsolatedDocumentFixture.cs) (`[RevitIsolatedDocTest]`):**
  - Runs the same worker initializers in `[OneTimeSetUp]` and creates a run root under `%TEMP%\SyntheticRevitTestDocs\<RunGuid>\`.
  - Copies `.rvt`/`.rfa` fixtures into per-test `%TEMP%\SyntheticRevitTestDocs\<RunGuid>\<TestId>\` via `OpenIsolatedModelCopy(...)` / `OpenIsolatedTestTemplateCopy(...)` (keeping tracked assets on disk unmutated), tracks opened or created documents with `TrackDocument(doc)` / `NewTrackedProjectDocument(...)`, closes tracked documents (`doc.Close(false)`) and deletes the per-test directory in `[TearDown]`, and asserts `app.Documents.Size == initialDocCount` in `[OneTimeTearDown]`.
- **Standalone `[RevitSmokeTest]` fixtures:** Call `HostSettingsRegistrar.EnsureRegistered()` and `TestWorkerJournalMarker.EnsureWritten(uiapp)` in `[OneTimeSetUp]` and assert `app.Documents.Size == initialDocCount` in `[OneTimeTearDown]`.

## 5. Build Isolation, Trust Leases, & Journal Gating

- **Per-run MSBuild & temp isolation:** Each invocation generates `<RunId>` (`YYYYMMDD_HHMMSS_<pid>_<hex>`) and builds with `/p:SyntheticIsolatedBuildRoot=%TEMP%\SyntheticBuilds\<RunId>` and `/p:SyntheticRunId=<RunId>` ([tests/Directory.Build.props](../../../tests/Directory.Build.props), [src/Directory.Build.props](../../../src/Directory.Build.props), [tests/Directory.Build.targets](../../../tests/Directory.Build.targets)), keeping `output/Synthetic/` and **Local Deployment** (`%APPDATA%\Autodesk\Revit\Addins\`) intact. Logs and `.trx` reports write to `tests/TestResults/<RunId>/` (latest 10 runs kept). Passing runs remove `<SyntheticIsolatedBuildRoot>` and `%TEMP%\RevitTest\*<RunId>*`; failing runs preserve `<SyntheticIsolatedBuildRoot>`.
- **Persistent `CodeSigning` & shared bundle lease:** [guid_whitelister.py](scripts/guid_whitelister.py) writes permanent `REG_DWORD=1` entries in `HKCU\Software\Autodesk\Revit\Autodesk Revit <ver>\CodeSigning` across add-in and `ApplicationPlugins` roots (`%APPDATA%`, `%ProgramData%`, and `%ProgramFiles%\Autodesk\ApplicationPlugins`). Active workers coordinate `%APPDATA%\Autodesk\ApplicationPlugins\ricaun.RevitTest.Application.bundle\PackageContents.xml` via `Local\Synthetic_RicaunBundleLease` (`FILE_SHARE_READ | FILE_SHARE_WRITE`, without `FILE_SHARE_DELETE`).
- **Pre-run `TestTemplate` upgrade trigger (`--no-auto-upgrade`):** Before dispatching live **Revit Tests**, `test_executor.py` checks `tests/Assets/test_models/TestTemplate<Year>.rvt` (`< 1 ms` no-op when present). If missing and an earlier `TestTemplate*.rvt` exists, it acquires a Win32 mutex, invokes `tools/RevitModelUpgrader/revit_model_upgrader.py` into temp staging, and verifies the upgraded template before promotion (never auto-upgrading `INC Standards` models). Pass `--no-auto-upgrade` to fail fast instead.
- **Automatic per-shard & cold journal gating:**
  - **Worker Pool runs:** **Add-In Bootstrap** ([BootstrapApp.cs](../../../src/Synthetic.Bootstrap/BootstrapApp.cs)) emits `SYNTHETIC_WORKER_ID:<WorkerId>` into each **Standby Worker**'s **Revit Journal** at boot (`TestWorkerJournalMarker` fallback). `test_executor.py` gates every returned `WorkerId` via [revit_journal_tool.py](../revit-journal-telemetry/scripts/revit_journal_tool.py):
    ```powershell
    python .agents/skills/revit-journal-telemetry/scripts/revit_journal_tool.py --version <ver> --worker-id <WorkerId> --assert-clean
    ```
  - **Cold `--no-pool` runs:** `test_executor.py` snapshots `tests/TestResults/<RunId>/known_journals_<ver>.json` pre-launch, sets `SYNTHETIC_TEST_RUN_ID=<RunId>`, terminates only that worker's PID tree on `660` s timeout, and gates via `--version <ver> --run-id <run_id> --known-journals-file <snapshot_path> --assert-clean`.
  - Any matching `.dmp` **Crash Dump**, orphaned open API transaction, or unhandled `Synthetic.*` / `SyntheticTests.*` exception fails the run even when NUnit passes. Use [revit-journal-telemetry](../revit-journal-telemetry/SKILL.md) to inspect dirty runs.
