# ADR 0001 - Hybrid State Management for Revit Tests

Status: Accepted

## Context
Original ADR 001 framed state management around legacy `"Tier 3 integration tests"`, calling for a `TransactionGroup` rolled back in `[TearDown]`, `NewProjectDocument` instances, and detached `.rvt` models. Under the partitioned test architecture ([ADR 0028](0028-hybrid-synthetic-core-assembly-and-colocated-partitions.md)) and live test taxonomy ([2026-11-21-revit-addin-test-categories-and-types.md](../research/2026-11-21-revit-addin-test-categories-and-types.md)), `"Tier 3 integration tests"` terminology is replaced by **Revit Tests** typed via [`TypedCategoryAttributes.cs`](../../tests/Fixtures/Shared/TypedCategoryAttributes.cs) (`[RevitSmokeTest]`, `[RevitAdapterTest]`, and `[RevitIsolatedDocTest]`).

Running **Revit Tests** across isolated concurrent workers introduces three operational requirements:
1. **`ricaun.NUnit 1.5.0` Lifecycle Constraints:** `ricaun.NUnit 1.5.0` (embedded in `ricaun.RevitTest.TestAdapter 1.11.1`) injects `UIApplication` into `[OneTimeSetUp]` and `[Test]` methods, omits `[SetUpFixture]`, and does not filter on `CategoryAttribute` natively. State caching and leak assertions therefore belong in per-`[TestFixture]` base classes carrying typed category attributes.
2. **Cold-Worker Payload Bypass (`SYNTHETIC_TEST_WORKER=1`):** Inside automated test workers, [`BootstrapApp.cs`](../../src/Synthetic.Bootstrap/BootstrapApp.cs) bypasses payload staging and `App.OnStartup` so `ricaun.NUnit` loads the isolated test assembly directly. Test fixtures must perform cold-worker initialization explicitly in `[OneTimeSetUp]` so synchronized settings registries are populated and **Revit Journal** run markers are recorded.
3. **Speed vs. File & Session Isolation:** Creating a new `Document` or opening `TestTemplate<Year>.rvt` before every `[RevitAdapterTest]` incurs seconds of repeated I/O, whereas `[RevitIsolatedDocTest]` workflows (`SaveAs`, `EditFamily`/`LoadFamily`, linked `.rvt` transforms, or detached workshared models) mutate disk files and session caches if run against shared repository assets.

## Decision
We retire `"Tier 3 integration tests"` terminology in favor of **Revit Tests** and codify hybrid state management across two base fixture classes in `tests/Fixtures/Revit/` (complementing standalone `[RevitSmokeTest]` host smoke fixtures) plus cold-worker setup:

1. **Cold-Worker Initializers in `[OneTimeSetUp]`:**
   - Both `RevitTemplateRollbackFixture` and `RevitIsolatedDocumentFixture` invoke two idempotent initializers in `[OneTimeSetUp]` once `UIApplication` is injected:
     - [`HostSettingsRegistrar.EnsureRegistered()`](../../src/Platform/Host/Addin/HostSettingsRegistrar.cs): registers all synchronized settings modules (`SettingsModuleRegistry`) and **Settings Dashboard** tabs (`SettingsTabRegistry`) normally populated by [`App.OnStartup`](../../src/Platform/Host/Addin/App.cs).
     - [`TestWorkerJournalMarker.EnsureWritten(uiapp)`](../../tests/Fixtures/Revit/TestWorkerJournalMarker.cs): emits a once-per-process `SYNTHETIC_TEST_RUN:<RunId>` comment into the active **Revit Journal** when `SYNTHETIC_TEST_RUN_ID` is set, enabling deterministic attribution under [ADR 0026](0026-revit-journal-telemetry-strategy.md).
   - Both fixtures record `initialDocCount = uiapp.Application.Documents.Size` in `[OneTimeSetUp]` and assert `app.Documents.Size == initialDocCount` in `[OneTimeTearDown]` so leaked open documents fail immediately at the offending fixture.

2. **`RevitTemplateRollbackFixture` (`[RevitAdapterTest]`) — Per-Fixture Baseline Caching & Rolled-Back Groups:**
   - [`RevitTemplateRollbackFixture.cs`](../../tests/Fixtures/Revit/RevitTemplateRollbackFixture.cs) applies `[RevitAdapterTest]` and caches a reusable baseline `Document` per `[TestFixture]` across `[OneTimeSetUp]` / `[OneTimeTearDown]` (compatible with `ricaun.NUnit 1.5.0`, which injects `UIApplication` into `[OneTimeSetUp]` and omits `[SetUpFixture]`).
   - Subclasses choose the baseline via `BaselineKind` (`RevitBaselineDocumentKind`, defaulting to `MetricProject`):
     - `MetricProject`: creates `App.NewProjectDocument(UnitSystem.Metric)`.
     - `ImperialProject`: creates `App.NewProjectDocument(UnitSystem.Imperial)`.
     - `TestTemplateCopy`: opens an isolated detached copy of `tests/Assets/test_models/TestTemplate<Year>.rvt` in a fixture temp folder without touching the tracked template on disk.
     - `None`: skips baseline document creation for pure Revit API struct/value tests that still require cold-worker setup.
   - Database-mutating tests run via `WithRolledBackDocument(...)` or `WithRolledBackDocumentPair(...)`, wrapping execution inside [`DocumentTransactionExtensions.ExecuteRolledBackGroup(...)`](../../src/Platform/Revit/Addin/DocumentTransactionExtensions.cs) so inner transactions roll back unconditionally in `finally` without masking exceptions.
   - **Invalidated Document Recreation:** Both `WithRolledBackDocument` / `WithRolledBackDocumentPair` (`finally`) and `[TearDown]` check `doc == null || !doc.IsValidObject || doc.IsModifiable`. Invalidated or left-modifiable baseline documents are closed via `doc.Close(false)` and transparently recreated before the next test.
   - `[OneTimeTearDown]` closes cached baseline documents, deletes any `TestTemplateCopy` fixture temp directory, and asserts `app.Documents.Size == initialDocCount`.

3. **`RevitIsolatedDocumentFixture` (`[RevitIsolatedDocTest]`) — Per-Test Temp Directory & File Copy Isolation:**
   - [`RevitIsolatedDocumentFixture.cs`](../../tests/Fixtures/Revit/RevitIsolatedDocumentFixture.cs) applies `[RevitIsolatedDocTest]` for stateful or multi-document suites (`SaveAs`, `EditFamily`/`LoadFamily`, linked `.rvt` models, or template modifications).
   - `[OneTimeSetUp]` creates `%TEMP%\SyntheticRevitTestDocs\<RunGuid>\`; `[SetUp]` creates a fresh per-test directory `%TEMP%\SyntheticRevitTestDocs\<RunGuid>\<TestId>\` (`CurrentTestTempDirectory`).
   - `OpenIsolatedModelCopy(...)` and `OpenIsolatedTestTemplateCopy(...)` copy `.rvt`/`.rfa` fixtures into the per-test directory (with optional `DetachFromCentralOption.DetachAndDiscardWorksets`) and register every opened `Document` via `TrackDocument(doc)` so tracked `.rvt` files on disk are never mutated. (`CopyAssetToTestTemp`, `GetTempFilePath`, and `NewTrackedProjectDocument` provide matching temp isolation and tracking for sidecars and newly created documents.)
   - `[TearDown]` closes all tracked documents (`doc.Close(false)`) in reverse order and recursively deletes the per-test temp directory.
   - `[OneTimeTearDown]` closes any remaining tracked documents, removes the fixture run directory, and asserts `app.Documents.Size == initialDocCount`.

## Alternatives Considered
1. **Assembly-Wide `[SetUpFixture]` Document Cache:** Rejected because `ricaun.NUnit 1.5.0` omits `[SetUpFixture]` execution, making per-`[TestFixture]` `[OneTimeSetUp]`/`[OneTimeTearDown]` the reliable shared baseline boundary.
2. **Opening Tracked `.rvt` Assets Directly In-Place:** Rejected because `SaveAs`, backup/recovery files, and concurrent Revit worker sessions mutate tracked repository files and trigger OS file-lock failures.
3. **Spawning Fresh Documents Per `[RevitAdapterTest]`:** Rejected because repeating `NewProjectDocument` or template opens on every test slows adapter loops unnecessarily when `DocumentTransactionExtensions.ExecuteRolledBackGroup(...)` already guarantees clean rollback between tests.

## Consequences
* **Pros:** Maximizes `[RevitAdapterTest]` throughput via per-fixture baseline caching and `ExecuteRolledBackGroup(...)`; protects tracked `.rvt`/`.rfa` fixtures on disk via `%TEMP%\SyntheticRevitTestDocs\<RunGuid>\<TestId>\` copies and deterministic `[TearDown]` cleanup; guarantees cold-worker settings registration and **Revit Journal** attribution; and isolates document handle leaks at the exact offending fixture via `app.Documents.Size == initialDocCount`.
* **Cons:** Baseline documents are created once per `[TestFixture]` rather than once per assembly because `ricaun.NUnit 1.5.0` omits `[SetUpFixture]`; tests that invalidate or leave a cached document modifiable incur baseline recreation on the subsequent test; and tests requiring persistent model writes, `EditFamily`/`LoadFamily`, or linked files must inherit `RevitIsolatedDocumentFixture` (`[RevitIsolatedDocTest]`) rather than `RevitTemplateRollbackFixture`.
