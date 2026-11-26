# Synthetic Coding Standards

Authoritative architecture, design, and review rules across `Synthetic.Core` (`net48;net8.0-windows8.0;net10.0-windows8.0`, C# 9.0), `Synthetic<Year>` (`2023–2027`), and `tests/` (see [GLOSSARY.md](GLOSSARY.md) for domain vocabulary and [revit-config.md](docs/agents/revit-config.md) for `CURRENT_LATEST_VERSION`).

## 1. Canonical Patterns (9)

### 1. Vertical Slice + Single-Seam Adapter
([ADR 0027](docs/adrs/0027-tiered-vertical-slice-architecture-and-seam-cqs.md), [ADR 0028](docs/adrs/0028-hybrid-synthetic-core-assembly-and-colocated-partitions.md), [revit-multi-versions](.agents/skills/revit-multi-versions/SKILL.md))

- Co-locate pure C# in `Core/` (`Synthetic.Core`) and Revit/XAML in `Addin/` (`Synthetic<Year>`) under `src/{Features/<Slice>, RevitDOM, Platform/<Submodule>}/`.
- Omit `.Core`/`.Addin` from slice namespaces (`Synthetic.<Root>.<Slice>`), use distinct cross-assembly static names (`CS0433` prevention), and keep `CURRENT_LATEST_VERSION` ([revit-config.md](docs/agents/revit-config.md)) in unconditioned `#else` blocks.
- **Direct Slices** (`Diagnostics`, `ViewManagement`, `Worksets`, `FamilyManagement`, `MaterialManagement`) execute in `Addin/Commands/` + `Addin/Utilities/` (plus slice settings tabs in `Addin/Settings/`) without pass-through adapters.
- **Interactive Slices** (`DetailItemFactory`, `MultiModelPdfExport`, `SheetIndex`, `SharedCoordinates`, `ShadowStudy`, migrating slices) isolate all Revit API access behind one narrow `I<Slice>Adapter` in `Core/Adapters/`, implemented by `Revit<Slice>Adapter` (`Addin/`) and `Fake<Slice>Adapter` (`tests/Fixtures/Shared/`).
- Enforce the 4-band acyclic DAG (`Features` -> `Domain` -> `RevitDOM` -> `Platform/{Settings, UI, Revit, IO}`), isolate `Platform/Host`, and graduate shared code to `src/Domain/` only when `>= 2` sub-modules each serve `>= 2` slices.

### 2. Harvest -> Pure Plan -> Atomic Apply
([ADR 0021](docs/adrs/0021-decouple-mergeanalysisengine-into-pure-poco-engine-and-duplicate-candidate-harvesting.md), [ADR 0023](docs/adrs/0023-transform-based-shared-site-link-repositioning-and-single-adapter-seam.md), [ADR 0025](docs/adrs/0025-non-mutating-shadow-study-export-and-base-ratio-multiply-compositing.md))

- Harvest live Revit state at `I<Slice>Adapter` into immutable `Core/` snapshots/POCOs (e.g., `SheetIndexSnapshot`, **Duplicate Candidate Harvesting**).
- Compute schedules, coordinate transforms, matrices, or diffs headlessly in `Core/` (**Logic Tests** across `net48`, `net8.0-windows8.0`, and `net10.0-windows8.0`).
- Apply the resulting plan atomically at the adapter seam.

### 3. Seam CQS (Command–Query Separation)
([ADR 0027](docs/adrs/0027-tiered-vertical-slice-architecture-and-seam-cqs.md))

- Keep `I<Slice>Adapter` queries side-effect-free, returning immutable DTOs or snapshots (`LoadProjectSnapshot`, `GetLoadedLinkedModels`, `GetEligibleViews`).
- Pass pure request objects/plans to adapter commands and mutate Revit solely inside guarded `DocumentTransactionExtensions` scopes.

### 4. Typed Outcome Object
([ADR 0027](docs/adrs/0027-tiered-vertical-slice-architecture-and-seam-cqs.md))

- Return structured outcome POCOs (`SheetIndexExportOutcome`, `SyncSharedSitesSummary`, `RepositionLinksSummary`, `RawFrameExportResult`, `RedirectionResultModel`, `FailureProcessingReport`) from workflows and adapters.
- Keep presentation decisions (`ExportCompletionWindow`, summary windows, NUnit assertions) in callers (`IExternalCommand` or ViewModels), never in adapters or handlers.

### 5. Pure DI at Entry Points
([ADR 0027](docs/adrs/0027-tiered-vertical-slice-architecture-and-seam-cqs.md), [revit-wpf-mvvm](.agents/skills/revit-wpf-mvvm/SKILL.md))

- Construct concrete adapters, `Platform/UI` prompt/dialog adapters, and ViewModels via plain constructors inside `IExternalCommand.Execute` and `HostSettingsRegistrar.EnsureRegistered()`.
- Use explicit `ViewModelBase` and `RelayCommand` (`src/Platform/UI/Core/`) across `net48` C# 9.0 and `net8.0+` without IoC containers or source-generator MVVM packages.

### 6. Transaction + Failure Guard
([revit-transactions](.agents/skills/revit-transactions/SKILL.md), [revit-failure-handling](.agents/skills/revit-failure-handling/SKILL.md))

- Elevate `DocumentTransactionExtensions.ExecuteCommittedBatchGroup` (supports `Func<bool>` / `Func<Document, bool>` returning `false` for clean rollback) or `ExecuteRolledBackGroup` (unconditional `finally` rollback for exports/probes) to `IExternalCommand`, `IExternalEventHandler`, or `Revit<Slice>Adapter`; keep helper modules transaction-agnostic.
- Pass an explicit non-null `IFailuresPreprocessor` (`FailurePipelines.ObserveOnly()`, `Purge()`, or `SuppressConstraints()`) to every `ExecuteTransaction` / `CreateTransaction`.
- Reserve `FailurePipelines.DeleteAllWarningsRecorded()` strictly for explicitly approved specs/ADRs, surfacing `FailureProcessingReport` in the **Typed Outcome Object**.

### 7. Window-Scoped Single-Instance `ExternalEvent` Contract
([ADR 0027](docs/adrs/0027-tiered-vertical-slice-architecture-and-seam-cqs.md), [revit-external-events](.agents/skills/revit-external-events/SKILL.md))

- Current implemented contract (`ICoalescedRevitDispatcher<TAdapter>` is deferred until Spec 5): for modeless windows (e.g., [CmdProjectStandards.cs](src/Features/StandardsManagement/Addin/Commands/CmdProjectStandards.cs)), `IExternalCommand.Execute` tracks one `_windowInstance` (`Focus()` if `IsLoaded`, cleared on `Closed`), creates one `IExternalEventHandler` + `ExternalEvent.Create(handler)` per session, and injects both into the window/ViewModel (`App.OnDocumentOpen` creates per-toast handlers for `SyncToastNotification`).
- Gate re-entrancy via `IsRunning` + UI busy state, clearing busy state immediately if `ExternalEvent.Raise()` returns `Denied` or `TimedOut`.
- Marshal UI updates back via WPF `Dispatcher.Invoke` with zero modal dialogs inside handlers or ViewModels; for modal workflows (`ShowDialog()`), run synchronously in `IExternalCommand.Execute` with `ProgressCoordinator` instead of `ExternalEvent`.

### 8. Explicit Descriptor Registry (`N >= 4`)
([ADR 0024](docs/adrs/0024-unified-internal-standardtyperegistry-and-composite-class-routing.md), [ADR 0028](docs/adrs/0028-hybrid-synthetic-core-assembly-and-colocated-partitions.md))

- Centralize `>= 4` peer dispatch, metadata, or filtering variants in explicit descriptor tables instead of parallel switches or reflection discovery:
  - **RevitDOM** type metadata & Revit bindings: `StandardTypeMetadataRegistry` (`Core`) + `StandardTypeRegistry` (`Addin`).
  - Ribbon & **Settings Dashboard** descriptors: `ribbon_config.json` + `RibbonItemFilter`, and `SettingsModuleRegistry` + `SettingsTabRegistry`.

### 9. Single-Field JSON Settings + Local User Prefs
([ADR 0025](docs/adrs/0025-non-mutating-shadow-study-export-and-base-ratio-multiply-compositing.md), [revit-extensible-storage](.agents/skills/revit-extensible-storage/SKILL.md))

- Persist project-stored **Firmwide Settings** (`ISettingModule`, using parameterless `IsValid()` on migrated slices such as `ShadowStudySettings` and `DetailItemFactorySettings`; remaining modules migrate from `IsValid(Document)` in Spec 6) through `SyntheticSettingsJsonSchema` (`JsonData` + `ModuleKey` string fields on `DataStorage`).
- Maintain 1-to-1 `ModuleKey` parity across concrete `ISettingModule` types, `HostSettingsRegistrar`, and `SyntheticSettings.template.json` (`SyncSettings` excluded from template).
- Store machine-local **Local User Preferences** (last folder, pixel widths, toggles) under `%APPDATA%\Synthetic\` (`ShadowStudyUserPrefsStore`) outside the model so normal runs leave Undo history clean.

## 2. Guardrail Anti-Patterns (12)

1. **Global Horizontal Layering:** Avoid repo-wide `ViewModels/`/`Repositories/` folders, sibling `Features.<A>` -> `Features.<B>` imports, or single-slice engines in `RevitDOM/`—keep features co-located in `Features/<Slice>/`.
2. **MediatR / Event-Sourced CQRS:** Avoid mediator buses or event stores—use direct Seam CQS interfaces.
3. **Runtime IoC Containers & `CommunityToolkit.Mvvm` on `net48`:** Avoid DI containers or source-generator MVVM packages—use Pure DI at entry points and `ViewModelBase`/`RelayCommand`.
4. **Repository-per-Element-Type:** Avoid per-type repositories (`IWallRepository`)—use one workflow-scoped `I<Slice>Adapter` per Interactive Slice.
5. **Speculative Category-1 Internal Seams:** Avoid `N = 1` pass-through interfaces around pure `Core/` classes—test deterministic modules directly in **Logic Tests**.
6. **Modal Dialogs Inside Logic/Seams:** Avoid `TaskDialog.Show`, `new TaskDialog`, `MessageBox.Show`, or `.ShowDialog()` in `ViewModels/`, `Views/`, `Adapters/`, `Handlers/`, `ExternalEvents/`, `Settings/`, or `RevitDOM/`—return **Typed Outcome Objects** or inject `Platform/UI` prompt ports at entry points.
7. **Live Revit SDK Types Across Interactive Seams:** Avoid `Autodesk.Revit.*` (`Element`, `Document`, `Parameter`, `ElementId`) in `Core/`, `ViewModels/`, `Views/`, `Models/`, or `Settings/`—exchange `string` `UniqueId`s and pure POCOs.
8. **Static or ViewModel-Created `ExternalEvent`s:** Avoid `static ExternalEvent` fields anywhere or `ExternalEvent.Create(...)` inside `ViewModels/`—create window-scoped instances in `IExternalCommand.Execute` (or `App`).
9. **One-Shot Post-Modal `ExternalEvent`s:** Avoid closing a modal dialog solely to loop via `ExternalEvent`—run synchronously inside `IExternalCommand.Execute` with `ProgressCoordinator`.
10. **Silent `catch {}`, Unguarded Transactions, or Blanket Warning Deletion:** Never swallow exceptions silently, instantiate raw `Transaction`s without `FailurePipelines`, call raw `FailuresAccessor.DeleteAllWarnings()`, or invoke `DeleteAllWarningsRecorded()` without explicit spec/ADR authorization.
11. **Reflection Assembly Scanning or `CURRENT_LATEST_VERSION` `#if` Guards:** Avoid `Assembly.GetTypes()` discovery, `#if REVIT<CURRENT_LATEST_VERSION>` directives, duplicate cross-assembly static names (`CS0433`), or relative `SyntheticTheme.xaml` URIs—place the latest **Revit Version** in `#else` and merge `<theme:SyntheticThemeDictionary />`.
12. **Ad-Hoc `SchemaBuilder`, Model-Polluting Prefs, or Mock Expansion:** Avoid `new SchemaBuilder(...)` outside `Platform/Settings/Addin/`, writing **Local User Preferences** to the model, or adding tests to `Synthetic.LegacyMockTests`—author **Logic Tests** or **Revit Tests** with typed `[Category]` attributes.

## 3. Architecture Fitness Gates (`12` `FitnessRule`s)

Enforced in **Logic Tests** (`python .agents/skills/revit-tests/scripts/test_executor.py --version Logic`) via single-parse Roslyn + XML/JSON **Architecture Fitness Tests** ([FitnessRule.cs](tests/Architecture/Logic/FitnessRule.cs), [ArchitectureFitnessRules.cs](tests/Architecture/Logic/ArchitectureFitnessRules.cs), [ArchitectureAllowlistRatchet.cs](tests/Architecture/Logic/ArchitectureAllowlistRatchet.cs)).

| # | `FitnessRule` | Scope | Enforced Invariant |
| :--- | :--- | :--- | :--- |
| 1 | `CoLocatedPartitionLayout` | Global `0%` | `Core/`/`Addin/` & `Logic/`/`Revit/` layout, dynamic `20\d{2}` heads, zero `.shproj`/`AssemblyInfo*`, `36` `SyntheticThemeDictionary` views. |
| 2 | `CorePurityAndZeroPreprocessor` | Global `0%` | Zero Revit API, XAML/BAML, `ProgressCoordinator`, Excel PIA DLL, or `#if REVIT*` in `Core/`/`Logic/`; zero `#if REVIT<CURRENT_LATEST_VERSION>` repo-wide. |
| 3 | `FourBandDependencyGraph` | Global `0%` | Acyclic 4-band DAG across C# & XAML `xmlns:`, `Platform.Host` isolation, zero sibling feature imports, `App.AppControlled`/retired namespace bans. |
| 4 | `DomainGraduationThreshold` | Global `0%` | Passes vacuously while `src/Domain/` is absent; otherwise requires `>= 2` sub-modules each used by `>= 2` slices, zero sibling `Domain` imports. |
| 5 | `RibbonConfigIntegrity` | Global `0%` | Every `ribbon_config.json` `class` and `availability_class` resolves via Roslyn in `src/**/Addin/**`, and all `.png` icons exist in `src/Assets/`. |
| 6 | `SettingsRegistryTemplateParity` | Global `0%` | 1-to-1 `ModuleKey` parity across concrete `ISettingModule` types (excluding `SyncSettings`), `HostSettingsRegistrar`, and `SyntheticSettings.template.json`. |
| 7 | `TestCategoryAndLegacyMockRatchet` | Global `0%` | Typed `[Category]` on every test class or 100% of test methods, zero `Tier1_*`/`Tier2_*` or retired symbols, exact `25`-file `Synthetic.LegacyMockTests` lock. |
| 8 | `InteractiveSliceSeamPurity` | Ratcheted | Zero `Autodesk.Revit.*` inside `{ViewModels, Views, Models, Settings}` across all `14` feature slices. |
| 9 | `TransactionPlacement` | Ratcheted | Raw `new Transaction` permitted only in `IExternalCommand`, `IExternalEventHandler`, `I*Adapter`, or `Platform/Revit/Addin`; raw `new TransactionGroup` permitted only in `Platform/Revit/Addin` (`DocumentTransactionExtensions`). |
| 10 | `TransactionFailureGuard` | Ratcheted | Every `new Transaction` outside `Platform/Revit/Addin` must attach `FailurePipelines` / `IFailuresPreprocessor` / `SetFailuresPreprocessor`. |
| 11 | `ExternalEventAndDialogPurity` | Ratcheted | Zero `static ExternalEvent`, zero raw `DeleteAllWarnings()`, zero `ExternalEvent.Create` in `ViewModels/`, zero modal dialogs in restricted scopes. |
| 12 | `SingleSchemaExtensibleStorage` | Ratcheted | `new SchemaBuilder(...)` permitted only in `Platform/Settings/Addin`. |

- **Global vs. Ratcheted Enforcement:** Rules `1–7` permit `0` debt repo-wide; rules `8–12` enforce exact `(Slice, FitnessRule, TypeName)` entries in `TransitionalSpecBacklog` and `DormantBoyScoutAllowlist` (`0` entries allowed in `DetailItemFactory`, `MultiModelPdfExport`, `Diagnostics`, `ShadowStudy`, `SharedCoordinates`, `SheetIndex`, and `Platform/Revit`), failing on both unlisted violations (**ceiling lock**) and stale entries (**one-way ratchet**).

## 4. 3-Tier Boy-Scout Trigger Rule

Applies to allowlisted legacy types in `TransitionalSpecBacklog` (`MergeDuplicates`, `SettingsDashboard`, `StandardsManagement`, `ViewManagement`, `Worksets`, `Platform/Settings`, `RevitDOM`; plus the Spec 6 prose note on `Diagnostics`/`StorageUtil.PurgeSchema` silent-catch hardening) and `DormantBoyScoutAllowlist` (`AutoTagger`, `FamilyManagement`, `MaterialManagement`):

- **Tier A — Mechanical Sweeps (Exempt):** Folder/namespace moves, unused `using` sweeps, Revit version/`#if` updates, or `Platform` helper renames require zero architectural rewrites.

- **Tier B — Surgical Bugfix (No New Violations):** Fix bugs locally without adding allowlist entries; if touching a bare Direct-Slice `Transaction`, attach `FailurePipelines` and delete its stale allowlist entry in `ArchitectureAllowlistRatchet.cs` without forcing a full `I<Slice>Adapter` rewrite.

- **Tier C — Behavioral Graduation (Mandatory Rewrite):** Adding or redesigning workflow behavior graduates the touched Direct-Slice command (`DocumentTransactionExtensions` + `FailurePipelines` + **Revit Tests**) or extracts an `I<Slice>Adapter` seam (`Core/` + **Logic Tests**), deleting its allowlist entries in the same change.

## 5. Cross-Cutting ADR Rules

- **`ADR 0020` (`ElementIdModel` 5-Step Value Equality):** Compare and dictionary-key headless BIM references via `ElementIdModel` / `IPocoIdentityService.AreSameIdentity()` (`UniqueId` -> `Id` -> `Class` guard -> `Name` + `Class`/`Category` -> `Aliases`), never reference equality or raw integers ([ADR 0020](docs/adrs/0020-5-step-identity-fallback-for-elementidmodel-value-equality.md)).

- **`ADR 0022` (6-Phase `AliasSwapEngine`):** Route cross-category reference redirection through **RevitDOM**'s 6-phase `AliasSwapEngine` rather than ad-hoc `ChangeTypeId` loops ([ADR 0022](docs/adrs/0022-unified-6-phase-alias-swap-and-reference-redirection-engine-architecture.md)).

- **`ADR 0025` (`BitmapCacheOption.OnLoad`):** Decode WPF images with `BitmapCacheOption.OnLoad` over closed `FileStream`s (`FileShare.Read`) and `Freeze()` cross-thread `BitmapSource`s so Windows/WIC never locks `.png` files ([ADR 0025](docs/adrs/0025-non-mutating-shadow-study-export-and-base-ratio-multiply-compositing.md)).

- **`ADR 0001` & `ADR 0026` (Fixture Isolation & Journal Cleanliness):** Inherit `RevitTemplateRollbackFixture` (`[RevitAdapterTest]`) or `RevitIsolatedDocumentFixture` (`[RevitIsolatedDocTest]`) so tracked `.rvt`/`.rfa` fixtures stay untouched and `app.Documents.Size` never leaks; enforce clean **Revit Journals** via `--assert-clean` ([ADR 0001](docs/adrs/0001-hybrid-state-management-for-revit-tests.md), [ADR 0026](docs/adrs/0026-revit-journal-telemetry-strategy.md)).

## 6. RevitDOM Serialization (Rung 3)

Read [revitdom-serialization.md](docs/agents/revitdom-serialization.md) when modifying `src/RevitDOM/`, translators, topological sorting, or JSON serialization schemas. It consolidates [ADR 0019](docs/adrs/0019-cross-slice-revitdom-scope-and-engine-eviction.md) and the `10` surviving serialization ADRs (`0003`, `0009`–`0015`, `0017`, `0024`), the `StandardTypeMetadataRegistry` / `StandardTypeRegistry` split, and cross-assembly extension naming.
