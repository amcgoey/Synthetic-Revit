# Project Atlas: Synthetic Revit Addon

> Last updated: 2026-07-24

This document serves as the high-level map of the **Synthetic Revit Addon** codebase, detailing the folder structure, multi-version configuration, active Revit API namespaces, core class architecture, dynamic ribbon setup, developer telemetry, and the registry of existing commands.

---

## 1. High-Level Folder Structure

The repository is structured to support multi-version compilation for Revit 2022 through 2026. Core business logic, pure domain models, view models, views, and utilities reside within the shared project (`SyntheticShared`), while version-specific project folders compile targeting specific Revit API DLL versions and runtime configurations.

```
Revit API Synthetic v2/
├── .agents/                        # Antigravity agent configuration & skills
├── docs/                           # Documentation folder
│   ├── project_atlas.md             # This file (high-level codebase map)
│   ├── change_log.md               # Development phase change logs
│   ├── SyntheticShared_API_Documentation.md  # Compiled C# API documentation
│   ├── Synthetic_UI_Theme_Specification.md   # UI Theme Specification & Design System tokens
│   ├── adrs/                       # Architectural Decision Records (ADRs 001 - 021)
│   ├── prds/                       # Product Requirement Documents (PRDs 001 - 015)
│   └── source_code/                 # Aggregated bundle source code directory
├── src/                            # Source code folder
│   ├── Synthetic.sln               # Visual Studio Solution compiling all versions
│   ├── SyntheticShared/            # Shared compilation project (core codebase)
│   │   ├── Assets/                 # Ribbon icon PNGs, fallback graphics & ribbon_config.json
│   │   ├── Core/                   # Application lifecycle, App.cs & dynamic RibbonManager
│   │   ├── Infrastructure/         # System helpers (IO, Persistence, Extensible Storage)
│   │   ├── Modules/                # Cohesive presentation modules (AutoTagger, DetailItemFactory, MergeDuplicates, StandardsManagement, SettingsDashboard, Worksets, ViewManagement, MaterialManagement, BatchPrint, FamilyManagement)
│   │   ├── RevitDOM/               # Elevated domain foundation (Models/, Operations/, Translation/)
│   │   ├── Settings/               # Common settings definitions
│   │   └── Shared/                 # Reusable WPF controls, converters, viewmodels & Revit API wrappers
│   ├── Synthetic2022/              # Revit 2022 specific project (.NET Framework 4.8)
│   ├── Synthetic2023/              # Revit 2023 specific project (.NET Framework 4.8)
│   ├── Synthetic2024/              # Revit 2024 specific project (.NET Framework 4.8)
│   ├── Synthetic2025/              # Revit 2025 specific project (.NET 8.0-windows)
│   └── Synthetic2026/              # Revit 2026 specific project (.NET 8.0-windows)
├── build/                          # Build configurations and deployment scripts
└── tests/                          # Headless NUnit unit/logic tests and ricaun.RevitTest integration test projects
    ├── RevitAPIMock/               # Standalone mock Revit API assembly for headless testing
    ├── SyntheticTests.Logic/       # Fast Tier 1 unit and logic tests (100% headless)
    └── SyntheticTests.Shared/      # Tier 2 integration tests (Revit-connected / mock harness)
```

---

## 2. Multi-Version Compatibility & Compilation

* **Target Frameworks:**
  * **Revit 2022 – 2024:** Targets `.NET Framework 4.8` (Legacy MSBuild/CSProj structure).
  * **Revit 2025 – 2026:** Targets `.NET 8.0-windows8.0` (Modern SDK-style CSProj structure).
* **WPF & Modern UI Integration:**
  * Legacy `.csproj` files (`Synthetic2022` through `Synthetic2024`) incorporate `ProjectTypeGuids` for legacy WPF support, while modern ones (`Synthetic2025` and `Synthetic2026`) target `.NET 8.0-windows8.0` directly enabling WPF. All UI dialogs follow modern WPF/MVVM patterns.
* **Conditional Compilation:**
  * Each version defines a custom compile constant (e.g. `REVIT2022`, `REVIT2024`, `REVIT2026`) used in code blocks requiring version-specific Revit API overrides.
* **Shared Reference System:**
  * Clean sharing of code is facilitated via [SyntheticShared.projitems](../src/SyntheticShared/SyntheticShared.projitems), which is imported by each version-specific `.csproj` file.
* **Local AppData Deployment Policy ([ADR 018](adrs/Synthetic_ADRs - 18 - ADR 018 - AppData Local Deployment for Debug Builds.md)):**
  * Debug compilation targets local user directory `%APPDATA%\Autodesk\Revit\Addins\<Year>\` to allow unprivileged developer compilation and hot-reloading, reserving administrative `%PROGRAMDATA%` paths for production installer deployments.

---

## 3. Active Revit API Namespaces

The project relies extensively on the following Revit namespaces:

* `Autodesk.Revit.ApplicationServices` — Application lifecycle and global settings.
* `Autodesk.Revit.Attributes` — Transaction attributes (`[Transaction(TransactionMode.Manual)]`).
* `Autodesk.Revit.DB` — Core database elements, geometry, parameters, and materials.
* `Autodesk.Revit.DB.Events` — Event triggers like document opening and closing.
* `Autodesk.Revit.DB.ExtensibleStorage` — Custom schemas to embed JSON and settings inside `.rvt` files.
* `Autodesk.Revit.UI` — UI controlled applications, ribbons, task dialogs, and external event handling.
* `Autodesk.Revit.UI.Selection` — User picking, selection filters, and active canvas operations.

---

## 4. Key Classes & Architecture Map

### Core Application Entry Point & Dynamic Ribbon Engine
* **[App.cs](../src/SyntheticShared/Core/App.cs)**: Implements `IExternalApplication`. Handles application startup, invokes `RibbonManager` layout construction, and registers event triggers (`DocumentOpened`, `DocumentClosing`).
* **[RibbonManager.cs](../src/SyntheticShared/Core/RibbonManager.cs)**: Polymorphic ribbon factory that reads dynamic ribbon configurations from [ribbon_config.json](../src/SyntheticShared/Assets/ribbon_config.json). Replaces hardcoded UI creation with declarative JSON layout parsing:
  * **Polymorphic Item Builders**: Houses `PushButtonBuilder`, `StackedGroupBuilder`, and `SplitButtonBuilder` concrete implementations of `IRibbonItemBuilder`.
  * **Version & Environment Filtering**: Evaluates `minVersion`, `maxVersion`, and `debugOnly` flags against the active Revit release (e.g. restricting `CmdDetailItemFactory` to Revit 2024+).
  * **Command Validation**: `ValidateCommandClass` verifies command class existence in the executing assembly at startup, gracefully disabling missing buttons and displaying warning tooltips rather than crashing startup.
  * **Icon Fallback**: Automatically substitutes missing 16px/32px icons with `placeholder_16.png` and `placeholder_32.png`.

### Elevated RevitDOM Foundation Subsystem
Centralized domain model framework ([ADR 019](adrs/Synthetic_ADRs - 19 - ADR 019 - Elevate RevitDOM Foundation and Decouple Backend Operations.md)) mapping Revit elements to pure C# POCO state containers and hosting decoupled backend execution engines under `src/SyntheticShared/RevitDOM/`:

* **Domain Models (`RevitDOM/Models/`)**:
  * **[ElementIdModel.cs](../src/SyntheticShared/RevitDOM/Models/ElementIdModel.cs)**: Foundation POCO reference container. Implements `IEquatable<ElementIdModel>` and value equality operators (`Equals`, `operator==`, `GetHashCode`) enforcing the **5-Step Identity Fallback Strategy** ([ADR 020](adrs/Synthetic_ADRs - 20 - ADR 020 - 5-Step Identity Fallback for ElementIdModel Value Equality.md)):
    1. UniqueId match (+ Class type guard)
    2. Id integer match (+ Class type guard, preserving built-in negative IDs)
    3. Type/Class guard verification
    4. Name + Class/Category match (case-insensitive)
    5. Aliases match / Name vs Aliases cross-match
  * **[ParameterDefinitionSpec.cs](../src/SyntheticShared/RevitDOM/Models/ParameterDefinitionSpec.cs)**: Immutable value object encapsulating parameter group, parameter type, spec identifier, display name, and unit spec metadata.
  * **Pure POCO Models**: Serialization state containers including `ElementModel`, `CategoryModel`, `MaterialModel`, `HostObjTypeModel`, `ViewPlanModel`, `ViewSheetModel`, `ViewScheduleModel`, `ParameterFilterElementModel`, `FilterRuleModel`, `ViewFilterOverrideModel`, `PlanViewRangeModel`, `XYZModel`, `UVModel`, and `BoundingBoxXYZModel` (with native-free spatial methods `GetSize()`, `GetCenter()`, `IsValid()`).

* **Translation & Identity (`RevitDOM/Translation/`)**:
  * **[StandardSerializationEngine.cs](../src/SyntheticShared/RevitDOM/Translation/StandardSerializationEngine.cs)**: Central model dispatcher and serialization engine. Orchestrates type-specific translators and performs batch imports/exports and analysis.
  * **[ModelDispatcher.cs](../src/SyntheticShared/RevitDOM/Translation/ModelDispatcher.cs)**: Maps and dispatches type-specific translator registrations.
  * **[IPocoIdentityService.cs](../src/SyntheticShared/RevitDOM/Translation/IPocoIdentityService.cs)** / **[PocoIdentityService.cs](../src/SyntheticShared/RevitDOM/Translation/PocoIdentityService.cs)**: Pure C# service executing 5-step fallback identity resolution across disconnected in-memory POCO object graph pools.
  * **[IIdentityService.cs](../src/SyntheticShared/RevitDOM/Translation/IIdentityService.cs)** / **[RevitIdentityService.cs](../src/SyntheticShared/RevitDOM/Translation/RevitIdentityService.cs)**: Live Revit database identity resolution service supporting bulk element queries (`GetElementsByElementIdModels`).

* **Operations Engine (`RevitDOM/Operations/`)**:
  * **[RevitDomDependencyScanner.cs](../src/SyntheticShared/RevitDOM/Operations/RevitDomDependencyScanner.cs)**: Pure C# reflection scanner that recursively sweeps POCO graphs to locate nested `ElementIdModel` references without Revit API context.
  * **[ImportExecutionRunner.cs](../src/SyntheticShared/RevitDOM/Operations/ImportExecutionRunner.cs)**: Graph orchestrator that constructs a Directed Acyclic Graph (DAG) of elements using their internal references and sorts them bottom-up to prevent import ordering exceptions.
  * **[MergeAnalysisEngine.cs](../src/SyntheticShared/RevitDOM/Operations/Merge/MergeAnalysisEngine.cs)**: Pure static POCO duplicate analysis engine ([ADR 021](adrs/Synthetic_ADRs - 21 - Decouple MergeAnalysisEngine into Pure POCO Engine and Revit Data Collector.md)) executing 100% headless cluster detection, deep parameter comparisons, and recommendation generation.
  * **[IStandardsExecutionPipeline.cs](../src/SyntheticShared/RevitDOM/Operations/Standards/IStandardsExecutionPipeline.cs)** / **[StandardsExecutionPipeline.cs](../src/SyntheticShared/RevitDOM/Operations/Standards/StandardsExecutionPipeline.cs)**: Central backend pipeline executing project standards enforcement, coordinating database writes, family editing cycles, and audit report generation.
  * **[IFamilyEnforcer.cs](../src/SyntheticShared/RevitDOM/Operations/Standards/IFamilyEnforcer.cs)** / **[RevitFamilyEnforcer.cs](../src/SyntheticShared/RevitDOM/Operations/Standards/RevitFamilyEnforcer.cs)**: Specialized service managing background family document processing, style injection, and type purging using `PurgeFailuresPreprocessor` to suppress native warning dialogs.
  * **Execution DTOs**: Strongly typed data containers (`StandardsExecutionItem`, `StandardsExecutionOptions`, `StandardsExecutionResult`, `ImportLogItem`, `StandardsReportGenerator`).

### Extensible Storage & Data Persistence
* **[TemplateStorageRepository.cs](../src/SyntheticShared/Modules/AutoTagger/Repositories/TemplateStorageRepository.cs)**: Implements storage and retrieval of Universal Auto-Tagger templates using Revit Extensible Storage schemas.
* **[SettingsManager.cs](../src/SyntheticShared/Infrastructure/Persistence/SettingsManager.cs)**: Unified manager handling lazy-loading, caching, persistence, and default fallback mechanisms for in-document settings modules.
* **[SyntheticSettingsJsonSchema.cs](../src/SyntheticShared/Infrastructure/Persistence/SyntheticSettingsJsonSchema.cs)**: Schema definition storing settings modules as serialized JSON payloads inside Revit extensible storage.
* **[LegacySettingsMigration.cs](../src/SyntheticShared/Infrastructure/Persistence/LegacySettingsMigration.cs)**: Utility to identify and safely remove obsolete schemas and legacy setting storage from the Revit document database.

### Project Standards Consolidation Dashboard Subsystem
Centralized modeless project standards consolidation with staged execution queues, interactive find & replace editing, diff inspection panels, and post-execution reports:
* **[ProjectStandardsDashboardWindow.xaml](../src/SyntheticShared/Modules/StandardsManagement/Views/ProjectStandardsDashboardWindow.xaml)** / **[ProjectStandardsDashboardViewModel.cs](../src/SyntheticShared/Modules/StandardsManagement/ViewModels/ProjectStandardsDashboardViewModel.cs)**: Root modeless dashboard UI coordinator implementing `IProjectStandardsDashboard`.
* **[StandardsSourceTreeViewModel.cs](../src/SyntheticShared/Modules/StandardsManagement/ViewModels/StandardsSourceTreeViewModel.cs)**: Child ViewModel governing live Revit model and offline JSON file source tree expansion, selection filtering, and category tab organization.
* **[StagingQueueViewModel.cs](../src/SyntheticShared/Modules/StandardsManagement/ViewModels/StagingQueueViewModel.cs)**: Child ViewModel managing staged execution queue items, baseline cloning, item selection, Find/Replace service calls, and parameter diff reviewing.
* **[StandardsExecutionPipelineViewModel.cs](../src/SyntheticShared/Modules/StandardsManagement/ViewModels/StandardsExecutionPipelineViewModel.cs)**: Child ViewModel orchestrating execution options, pipeline invocation, progress reporting, and cancellation tokens.
* **[QueueItemModel.cs](../src/SyntheticShared/Modules/StandardsManagement/ViewModels/QueueItemModel.cs)**: Model representing staged items in the run queue, capturing source POCO model, target properties, and execution intents (`WillEnforce`, `WillSave`).
* **[FindReplaceService.cs](../src/SyntheticShared/Modules/StandardsManagement/Utilities/FindReplaceService.cs)** / **[IFindReplaceService.cs](../src/SyntheticShared/Modules/StandardsManagement/Utilities/IFindReplaceService.cs)**: Dedicated service executing batch find-and-replace text modifications across staged element parameters.
* **[StandardsExtractionOrchestrator.cs](../src/SyntheticShared/Modules/StandardsManagement/Engine/StandardsExtractionOrchestrator.cs)**: Recursive DAG extraction service harvesting standards and tagging nested child dependencies.

### Modeless Merge Duplicates Consolidation Subsystem
Enables automated type merging, instance swapping, redundant type deletion, and family parameter schema synchronization:
* **[MergeDuplicatesWindow.xaml](../src/SyntheticShared/Modules/MergeDuplicates/Views/MergeDuplicatesWindow.xaml)** / **[MergeDuplicatesViewModel.cs](../src/SyntheticShared/Modules/MergeDuplicates/ViewModels/MergeDuplicatesViewModel.cs)**: Modeless staging workspace to scan, group duplicate family/type clusters, and queue merge actions.
* **[RevitMergeDataCollector.cs](../src/SyntheticShared/Modules/MergeDuplicates/Services/RevitMergeDataCollector.cs)**: Revit document harvester service ([ADR 021](adrs/Synthetic_ADRs - 21 - Decouple MergeAnalysisEngine into Pure POCO Engine and Revit Data Collector.md)) executing `FilteredElementCollector` queries (`RunFastScan`, `RunTargetedScan`, `CollectStragglers`) and converting native elements into POCO models for `MergeAnalysisEngine`.
* **[MergeDetailedReviewWindow.xaml](../src/SyntheticShared/Modules/MergeDuplicates/Views/MergeDetailedReviewWindow.xaml)** / **[MergeDetailedReviewViewModel.cs](../src/SyntheticShared/Modules/MergeDuplicates/ViewModels/MergeDetailedReviewViewModel.cs)**: Conflict mapping interface with side-by-side parameter value comparisons (`ParameterDiffRowModel`), primary naming overrides, and schema parameter mapping controls.
* **[ProcessMergeEventHandler.cs](../src/SyntheticShared/Modules/MergeDuplicates/Handlers/ProcessMergeEventHandler.cs)**: Modeless transaction pipeline executing database changes on the Revit API thread (injecting missing parameters -> duplicating types -> remapping instances/groups/assemblies -> purging redundant duplicate elements).

### Developer Telemetry & Debugging Subsystem
Tools and scripts enabling automated journal session telemetry and interactive Visual Studio debugging:
* **[revit_journal_tool.py](../.agents/skills/revit-journal-telemetry/scripts/revit_journal_tool.py)**: Python telemetry engine ([ADR 0003](adrs/local/0003-revit-journal-telemetry-strategy.md)) parsing active Revit session journal logs. Features version folder isolation, process-gated OS check for force-killed sessions, transaction boundary monitoring, and rolling UI command sequence tracing.
* **[start_debugging.ps1](../.agents/skills/revit-debug-by-user/scripts/start_debugging.ps1)**: PowerShell automation script launching Visual Studio with the appropriate version-specific target project for interactive F5 debugging.

---

## 5. Command Registry

Below is the complete active registry of `IExternalCommand` classes defined in `SyntheticShared/Modules/` and configured in [ribbon_config.json](../src/SyntheticShared/Assets/ribbon_config.json):

| Command Class | Ribbon Panel | Version Filter | Core Functionality |
| :--- | :--- | :--- | :--- |
| **[ViewsAutoNumber](../src/SyntheticShared/Modules/ViewManagement/Commands/ViewsAutoNumber.cs)** | Views & Tags | All Versions | Automatically renumbers views placed on sheets. |
| **[CmdBatchTag](../src/SyntheticShared/Modules/AutoTagger/Commands/CmdBatchTag.cs)** | Views & Tags | All Versions | Iteratively tags selected family elements or active view using template mapping rules. |
| **[CmdManageTemplates](../src/SyntheticShared/Modules/AutoTagger/Commands/CmdManageTemplates.cs)** | Views & Tags | All Versions | Control panel to manage, delete, import, or export tag template files. |
| **[ViewAutoNumberConfig](../src/SyntheticShared/Modules/ViewManagement/Commands/ViewAutoNumberConfig.cs)** | Views & Tags | All Versions | Displays configuration details for view auto-numbering. |
| **[CmdSetTemplate](../src/SyntheticShared/Modules/AutoTagger/Commands/CmdSetTemplate.cs)** | *WPF Dialog context* | All Versions | Launches coordinate picker to calculate coordinate offsets and save them as templates. |
| **[CmdDetailItemFactory](../src/SyntheticShared/Modules/DetailItemFactory/Commands/CmdDetailItemFactory.cs)** | Views & Tags | `minVersion: 2024` | Batch-processes selected 3D model elements into 2D Detail Item families via temporary DWG projection and tracing. |
| **[ConvertDraftingToLegend](../src/SyntheticShared/Modules/ViewManagement/Commands/ConvertDraftingToLegend.cs)** | Legends | All Versions | Batch transfers details and symbols from Drafting Views into Legends. |
| **[ConvertLegendToDrafting](../src/SyntheticShared/Modules/ViewManagement/Commands/ConvertLegendToDrafting.cs)** | Legends | All Versions | Batch transfers details and symbols from Legends into Drafting Views. |
| **[CmdMergeDuplicates](../src/SyntheticShared/Modules/MergeDuplicates/Commands/CmdMergeDuplicates.cs)** | Model Management | All Versions | Launches modeless workspace to merge duplicate family types, groups, and assemblies. |
| **[MaterialsRepathAll](../src/SyntheticShared/Modules/MaterialManagement/Commands/MaterialsRepathAll.cs)** | Model Management | All Versions | Mass-repaths render assets of materials from specified search directories. |
| **[MaterialImagesPackage](../src/SyntheticShared/Modules/MaterialManagement/Commands/MaterialImagesPackage.cs)** | Model Management | All Versions | Gathers and packages render images mapped inside materials to an export folder. |
| **[PaintElements](../src/SyntheticShared/Modules/MaterialManagement/Commands/PaintElements.cs)** | Model Management | All Versions | Standardizes face painting of select geometries with a set material. |
| **[AuditPurgeAllFamilies](../src/SyntheticShared/Modules/FamilyManagement/Commands/AuditPurgeAllFamilies.cs)** | Model Management | All Versions | Iteratively opens, logs errors, clears legacy schemas, and closes all family components. |
| **[FamiliesForceReinsert](../src/SyntheticShared/Modules/FamilyManagement/Commands/FamiliesForceReinsert.cs)** | Model Management | All Versions | Re-imports/overwrites families to fix post-upgrade text sizing discrepancies. |
| **[CmdProjectStandards](../src/SyntheticShared/Modules/StandardsManagement/Commands/CmdProjectStandards.cs)** | Model Management | All Versions | Displays modeless Project Standards Dashboard enabling enqueued style execution, guardrails, and markdown logging. |
| **[WorksetsImport](../src/SyntheticShared/Modules/Worksets/Commands/WorksetsImport.cs)** | Worksets & Setup | All Versions | Automates new workset generation using definitions stored in Excel. |
| **[ScopeBoxesMoveToWorkset](../src/SyntheticShared/Modules/Worksets/Commands/ScopeBoxesMoveToWorkset.cs)** | Worksets & Setup | All Versions | Mass relocates project scope boxes to a designated workset. |
| **[WorksetSetFile](../src/SyntheticShared/Modules/Worksets/Commands/WorksetSetFile.cs)** | Worksets & Setup | All Versions | Assigns the template spreadsheet path utilized by the workset importer. |
| **[WorksetSettingsShow](../src/SyntheticShared/Modules/Worksets/Commands/WorksetSettingsShow.cs)** | Worksets & Setup | All Versions | Displays the registered project workset Excel file mappings. |
| **[WorksetStartView](../src/SyntheticShared/Modules/Worksets/Commands/WorksetStartView.cs)** | Worksets & Setup | All Versions | Synchronizes initial workspace settings (e.g. startup view details) from Excel. |
| **[ElementsOnWorksetRecord](../src/SyntheticShared/Modules/Worksets/Commands/ElementsOnWorksetRecord.cs)** | Worksets & Setup | All Versions | Exports unique element-to-workset associations to a text recovery file. |
| **[ElementsOnWorksetReload](../src/SyntheticShared/Modules/Worksets/Commands/ElementsOnWorksetReload.cs)** | Worksets & Setup | All Versions | Resolves and reassigns elements back to their recorded worksets from file. |
| **[PrintBatchMultiDoc](../src/SyntheticShared/Modules/BatchPrint/Commands/PrintBatchMultiDoc.cs)** | Publish | All Versions | Multi-document batch printer processing sheets across loaded models. |
| **[SettingsDashboardCommand](../src/SyntheticShared/Modules/SettingsDashboard/Commands/SettingsDashboardCommand.cs)** | Admin & Settings | All Versions | Displays unified settings dashboard to manage configuration overrides and drift resolution. |
| **[StorageQuery](../src/SyntheticShared/Infrastructure/Diagnostics/StorageQuery.cs)** | Admin & Settings | All Versions | Queries document metadata for registered extensible storage schemas. |
| **[StorageDelete](../src/SyntheticShared/Infrastructure/Diagnostics/StorageDelete.cs)** | Admin & Settings | All Versions | Deletes custom extensible storage schemas from the project database. |
