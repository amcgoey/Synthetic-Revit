# Change Log

## 2026-07-24 - MergeAnalysisEngine Decoupling & Project Standards ViewModel Decomposition (ADR 021 - Issues #62, #64, #66, #67, #69, #84, #85, #86, #87)
**Reason for Change:** Decoupled duplicate analysis from `Autodesk.Revit.DB` into pure POCO execution, extracted document querying into `RevitMergeDataCollector`, refactored data clumps into `ParameterDefinitionSpec`, eliminated single-letter variables across duplicate matching algorithms, and decomposed `ProjectStandardsDashboardViewModel` into modular child ViewModels.
**Key Enhancements:**
* **Pure POCO MergeAnalysisEngine (ADR 021, Issue #64)**: Refactored `MergeAnalysisEngine` into a pure, static POCO analysis engine in `Synthetic.RevitDOM.Operations.Merge` with zero dependencies on Revit API assemblies. Public methods include `GetBaseName`, `IsIdentityParameter`, `BuildClustersFromModels`, `RunDeepScan`, and `GenerateRecommendations`.
* **RevitMergeDataCollector Extraction (Issue #62)**: Isolated all live Revit document queries (`RunFastScan`, `RunTargetedScan`, `CollectStragglers`, `ConvertToPoco`) into `RevitMergeDataCollector` in `Synthetic.Modules.MergeDuplicates.Services`.
* **ParameterDefinitionSpec Value Object (Issue #84)**: Introduced `ParameterDefinitionSpec` in `Synthetic.RevitDOM.Models` to encapsulate parameter group, type, spec, display name, and unit metadata, resolving data clump smells.
* **POCO Bounding Box & Spatial Displacement (Issue #85)**: Added `GetSize()`, `GetCenter()`, and `IsValid()` to `BoundingBoxXYZModel`, and updated `XYZModel.IsOffsetEqual` to evaluate component-wise spatial displacement.
* **100% Descriptive Variable Renaming (Issue #86)**: Replaced single-letter variables and cryptic abbreviations across `MergeAnalysisEngine` with self-documenting domain identifiers.
* **Project Standards ViewModel Decomposition (Issues #47-#50, #53, #56)**: Decomposed `ProjectStandardsDashboardViewModel` into modular child ViewModels (`StandardsSourceTreeViewModel`, `StagingQueueViewModel`, `StandardsExecutionPipelineViewModel`) bound by the `IProjectStandardsDashboard` interface contract.
* **Tier 2 Headless NUnit Suite (Issue #87)**: Created `Tier2_MergeDuplicatesHeadlessTests.cs` testing cluster detection, deep scanning, base name extraction, and recommendation generation headlessly from JSON snapshots.
**Files Added:**
* [RevitMergeDataCollector.cs](../src/SyntheticShared/Modules/MergeDuplicates/Services/RevitMergeDataCollector.cs)
* [ParameterDefinitionSpec.cs](../src/SyntheticShared/RevitDOM/Models/ParameterDefinitionSpec.cs)
* [ParameterDefinitionSpecTests.cs](../tests/SyntheticTests.Logic/Modules/RevitDOM/ParameterDefinitionSpecTests.cs)
* [IProjectStandardsDashboard.cs](../src/SyntheticShared/Modules/StandardsManagement/ViewModels/IProjectStandardsDashboard.cs)
* [StandardsSourceTreeViewModel.cs](../src/SyntheticShared/Modules/StandardsManagement/ViewModels/StandardsSourceTreeViewModel.cs)
* [StagingQueueViewModel.cs](../src/SyntheticShared/Modules/StandardsManagement/ViewModels/StagingQueueViewModel.cs)
* [StandardsExecutionPipelineViewModel.cs](../src/SyntheticShared/Modules/StandardsManagement/ViewModels/StandardsExecutionPipelineViewModel.cs)
* [Tier2_MergeDuplicatesHeadlessTests.cs](../tests/SyntheticTests.Shared/Modules/MergeDuplicates/Tier2_MergeDuplicatesHeadlessTests.cs)
* [ADR 021](adrs/Synthetic_ADRs - 21 - Decouple MergeAnalysisEngine into Pure POCO Engine and Revit Data Collector.md)
**Files Modified:**
* [MergeAnalysisEngine.cs](../src/SyntheticShared/RevitDOM/Operations/Merge/MergeAnalysisEngine.cs)
* [MergeDuplicatesViewModel.cs](../src/SyntheticShared/Modules/MergeDuplicates/ViewModels/MergeDuplicatesViewModel.cs)
* [ProjectStandardsDashboardViewModel.cs](../src/SyntheticShared/Modules/StandardsManagement/ViewModels/ProjectStandardsDashboardViewModel.cs)
* [BoundingBoxXYZModel.cs](../src/SyntheticShared/RevitDOM/Models/BoundingBoxXYZModel.cs)
* [XYZModel.cs](../src/SyntheticShared/RevitDOM/Models/XYZModel.cs)
* [SyntheticShared.projitems](../src/SyntheticShared/SyntheticShared.projitems)
* [CONTEXT.md](../CONTEXT.md)

## 2026-07-22 - 5-Step Identity Value Equality & Standards Execution Pipeline (ADR 020 - Issues #78, #79, #80, #82, #83)
**Reason for Change:** Enforced universal 5-step fallback value equality on `ElementIdModel` to resolve WPF binding selection states and dictionary lookup failures, and extracted the project standards enforcement execution into a dedicated backend operation pipeline within RevitDOM.
**Key Enhancements:**
* **5-Step ElementIdModel Identity Equality (ADR 020, Issue #79)**: Implemented `IEquatable<ElementIdModel>` on `ElementIdModel`. Overrode `Equals`, `operator==`, `operator!=`, and `GetHashCode()` using the 5-step fallback identity strategy:
  1. UniqueId match (+ Class type guard)
  2. Id integer match (+ Class type guard, preserving built-in negative IDs)
  3. Type/Class guard verification
  4. Name + Class/Category match (case-insensitive)
  5. Aliases match / Name vs Aliases cross-match
* **POCO Identity Resolution Service (Issue #80)**: Introduced `IPocoIdentityService` and `PocoIdentityService` in `Synthetic.RevitDOM.Translation` for offline POCO identity comparison across `ElementIdModel`, `ElementModel`, and mixed pairs without Revit API context.
* **Bulk Element Identity Resolution (Issue #82)**: Extended `IIdentityService` and `RevitIdentityService` with `GetElementsByElementIdModels` for efficient single-pass native element collection.
* **ParameterDiffRowModel Encapsulation (Issue #83)**: Encapsulated `WinningValue` selection logic and fixed radio button unselection behavior and dictionary lookups in `ParameterDiffRowModel`.
* **IStandardsExecutionPipeline Extraction**: Relocated `IStandardsExecutionPipeline` and `StandardsExecutionPipeline` into `Synthetic.RevitDOM.Operations.Standards`. Introduced DTOs (`StandardsExecutionItem`, `StandardsExecutionOptions`, `StandardsExecutionResult`, `ImportLogItem`) and `StandardsReportGenerator`.
* **IFamilyEnforcer & Failure Suppression**: Abstracted family enforcement under `IFamilyEnforcer` and implemented `RevitFamilyEnforcer` utilizing `PurgeFailuresPreprocessor` to suppress native Revit warning dialogs during batch operations.
**Files Added:**
* [IPocoIdentityService.cs](../src/SyntheticShared/RevitDOM/Translation/IPocoIdentityService.cs)
* [PocoIdentityService.cs](../src/SyntheticShared/RevitDOM/Translation/PocoIdentityService.cs)
* [PocoIdentityServiceTests.cs](../tests/SyntheticTests.Logic/Modules/RevitDOM/PocoIdentityServiceTests.cs)
* [ElementIdModelTests.cs](../tests/SyntheticTests.Logic/Modules/RevitDOM/ElementIdModelTests.cs)
* [IStandardsExecutionPipeline.cs](../src/SyntheticShared/RevitDOM/Operations/Standards/IStandardsExecutionPipeline.cs)
* [StandardsExecutionPipeline.cs](../src/SyntheticShared/RevitDOM/Operations/Standards/StandardsExecutionPipeline.cs)
* [IFamilyEnforcer.cs](../src/SyntheticShared/RevitDOM/Operations/Standards/IFamilyEnforcer.cs)
* [RevitFamilyEnforcer.cs](../src/SyntheticShared/RevitDOM/Operations/Standards/RevitFamilyEnforcer.cs)
* [StandardsExecutionItem.cs](../src/SyntheticShared/RevitDOM/Operations/Standards/StandardsExecutionItem.cs)
* [StandardsExecutionOptions.cs](../src/SyntheticShared/RevitDOM/Operations/Standards/StandardsExecutionOptions.cs)
* [StandardsExecutionResult.cs](../src/SyntheticShared/RevitDOM/Operations/Standards/StandardsExecutionResult.cs)
* [ImportLogItem.cs](../src/SyntheticShared/RevitDOM/Operations/Standards/ImportLogItem.cs)
* [StandardsReportGenerator.cs](../src/SyntheticShared/RevitDOM/Operations/Standards/StandardsReportGenerator.cs)
* [RevitFamilyEnforcerTests.cs](../tests/SyntheticTests.Logic/Modules/StandardsManagement/RevitFamilyEnforcerTests.cs)
* [ADR 020](adrs/Synthetic_ADRs - 20 - ADR 020 - 5-Step Identity Fallback for ElementIdModel Value Equality.md)
**Files Modified:**
* [ElementIdModel.cs](../src/SyntheticShared/RevitDOM/Models/ElementIdModel.cs)
* [ElementModel.cs](../src/SyntheticShared/RevitDOM/Models/ElementModel.cs)
* [IIdentityService.cs](../src/SyntheticShared/RevitDOM/Translation/IIdentityService.cs)
* [RevitIdentityService.cs](../src/SyntheticShared/RevitDOM/Translation/RevitIdentityService.cs)
* [ParameterDiffRowModel.cs](../src/SyntheticShared/Modules/MergeDuplicates/Models/ParameterDiffRowModel.cs)
* [SyntheticShared.projitems](../src/SyntheticShared/SyntheticShared.projitems)

## 2026-07-18 - RevitDOM Architecture Elevation & Infrastructure Seam Decoupling (ADR 018, ADR 019)
**Reason for Change:** Promoted `RevitDOM` to a top-level architectural foundation (`src/SyntheticShared/RevitDOM/`), relocated duplicate domain models out of UI modules, and reconfigured debug build manifest deployment to user AppData folders.
**Key Enhancements:**
* **RevitDOM Elevation (ADR 019, Issue #61)**: Reorganized folder layout to establish `Synthetic.RevitDOM` as the core domain root housing `Models/`, `Operations/`, and `Translation/`. Updated all namespace references globally.
* **Domain Model Relocations (Issues #62, #64)**: Relocated `DiffEngine` to `Synthetic.RevitDOM.Operations.Diffing`, and decoupled `DuplicateItemModel`, `DuplicateTypeModel`, and `DuplicateClusterModel` from Revit API types by replacing raw `ElementId` references with `ElementIdModel` POCO containers.
* **AppData Debug Deployment (ADR 018)**: Refactored debug manifest deployment to write `.addin` manifests to `%APPDATA%\Autodesk\Revit\Addins\<Year>\` during developer compilation, avoiding administrative permission escalations while reserving `%PROGRAMDATA%` for Release installers.
* **Glossary & Domain Terminology Alignment**: Updated `CONTEXT.md` to formally record definitions for `Debug Manifest`, `Local Deployment`, `RevitDOM`, `Identity Comparison`, `Deep Diff Comparison`, `Revit Merge Data Collector`, and `Parameter Definition Spec`.
**Files Added:**
* [ADR 018](adrs/Synthetic_ADRs - 18 - ADR 018 - AppData Local Deployment for Debug Builds.md)
* [ADR 019](adrs/Synthetic_ADRs - 19 - ADR 019 - Elevate RevitDOM Foundation and Decouple Backend Operations.md)
**Files Modified:**
* [CONTEXT.md](../CONTEXT.md)
* [SyntheticShared.projitems](../src/SyntheticShared/SyntheticShared.projitems)
* [Synthetic.sln](../src/Synthetic.sln)

## 2026-07-16 - Dynamic Ribbon Framework, Telemetry Engine & Developer Skills (ADR 0003 - Issues #22, #23, #25, #30, #32, #33)
**Reason for Change:** Replaced hardcoded static C# ribbon initialization with a dynamic, JSON-configured ribbon framework supporting declarative UI layout, version-gated commands, debug-only filtering, automated command class validation, and integrated developer telemetry & Visual Studio launch automation.
**Key Enhancements:**
* **Dynamic Ribbon Manager (Issues #22, #23)**: Created `RibbonManager` and `IRibbonItemBuilder` polymorphic builders (`PushButtonBuilder`, `StackedGroupBuilder`, `SplitButtonBuilder`) reading ribbon specifications from `ribbon_config.json`.
* **Version & Environment Filtering (Issues #25, #30)**: Added `minVersion`, `maxVersion`, and `debugOnly` properties to ribbon schema items. `CmdDetailItemFactory` is conditionally loaded starting in Revit 2024 via `IsVersionMatch`.
* **Command Validation & Fallbacks (Issue #32)**: Implemented `ValidateCommandClass` to verify command class existence at startup, disabling non-existent buttons and applying warning tooltips without crashing app initialization. Added icon fallback to `placeholder_16.png` and `placeholder_32.png`.
* **Slideout Optimization (Issue #33)**: Optimized panel rendering so `AddSlideOut()` executes strictly when valid slideout buttons remain after version filtering.
* **Revit Journal Telemetry Engine (ADR 0003)**: Built `revit_journal_tool.py` script supporting version folder isolation, process-gated OS checks to detect force-killed sessions, transaction boundary monitoring, and rolling UI command sequence tracing.
* **Visual Studio Debug Launcher**: Created `start_debugging.ps1` PowerShell automation script to select Visual Studio project by target year and launch interactive F5 debug sessions.
* **Command Demolition**: Permanently removed obsolete test command `CmdTestAuditPurgeJournal.cs` and `CmdTestAuditPurgeJournalAvailability`.
* **Developer Skills Integration**: Added developer skills under `.agents/skills/` (`revit-ui-ribbon`, `revit-journal-telemetry`, `revit-debug-by-user`, `revit-build-deploy`, `docs-aggregate`, `docs-push-google`, `docs-pull-google`, `ask-matt`, `codebase-design`, `domain-modeling`, `tdd`, `diagnosing-bugs`, `prototype`, `grilling`).
**Files Added:**
* [RibbonManager.cs](../src/SyntheticShared/Core/RibbonManager.cs)
* [ribbon_config.json](../src/SyntheticShared/Assets/ribbon_config.json)
* [RibbonManagerTests.cs](../tests/SyntheticTests.Logic/Infrastructure/UI/RibbonManagerTests.cs)
* [RibbonTests.cs](../tests/SyntheticTests.Logic/Infrastructure/UI/RibbonTests.cs)
* [start_debugging.ps1](../.agents/skills/revit-debug-by-user/scripts/start_debugging.ps1)
* [revit_journal_tool.py](../.agents/skills/revit-journal-telemetry/scripts/revit_journal_tool.py)
* [0003-revit-journal-telemetry-strategy.md](adrs/local/0003-revit-journal-telemetry-strategy.md)
* [revit-ui-ribbon SKILL.md](../.agents/skills/revit-ui-ribbon/SKILL.md)
* [revit-journal-telemetry SKILL.md](../.agents/skills/revit-journal-telemetry/SKILL.md)
* [revit-debug-by-user SKILL.md](../.agents/skills/revit-debug-by-user/SKILL.md)
* [revit-build-deploy SKILL.md](../.agents/skills/revit-build-deploy/SKILL.md)
**Files Modified:**
* [App.cs](../src/SyntheticShared/Core/App.cs)
* [SyntheticShared.projitems](../src/SyntheticShared/SyntheticShared.projitems)
* [.agents/AGENTS.md](../.agents/AGENTS.md)
**Files Deleted:**
* [CmdTestAuditPurgeJournal.cs](../src/SyntheticShared/Modules/FamilyManagement/Commands/CmdTestAuditPurgeJournal.cs)



## 2026-07-12 - Legacy Code Demolition, ViewModel Decoupling & Find/Replace Refactoring (PRD 003, PRD 014, etc.)
**Reason for Change:** Cleaned up legacy/obsolete codebase artifacts (commands, views, viewmodels, tests) to improve build speed and maintainability, decoupled presentation logic from native OS dialogs/prompts, and extracted batch Find & Replace execution logic into a dedicated service.
**Key Enhancements:**
* **IUserPromptService Integration**: Introduced `IUserPromptService` and implemented `WindowsUserPromptService` (WPF implementation with fully qualified namespaces to prevent Revit namespace clashes) and `FakeUserPromptService` (unit testing implementation).
* **ViewModel Decoupling**: Decoupled `ParameterWrapperVM`, `ProjectStandardsDashboardViewModel`, `ManageTemplatesViewModel`, `StandardsSettingsViewModel`, `SyncSettingsViewModel`, `SyncWizardViewModel`, and `WorksetWizardViewModel` from native OS file dialogs and `MessageBox` calls using `IFileDialogService` and `IUserPromptService`.
* **RevitDOM Extensions Decoupling**: Refactored `RevitDomExtensions.cs` to delegate segment extraction to `LinePatternTranslator.ExtractSpecifics` and decoupled `IIdentityService` inside ToModel translation.
* **Legacy Component Demolition**:
  - Deleted obsolete command files (`CmdEnforceStandards.cs`, `CmdExportStandards.cs`, `StandardsEditorShow.cs`).
  - Deleted obsolete views (`EnforceStandardsView.xaml`, `ExportStylesView.xaml`, `JsonEditorWindow.xaml`, `FindReplaceWindow.xaml`).
  - Deleted obsolete ViewModels (`EnforceStandardsViewModel.cs`, `ExportStylesViewModel.cs`, `JsonEditorMainViewModel.cs`, `FindReplaceViewModel.cs`).
  - Deleted obsolete tests (`ExportStylesViewModelTests.cs`, `FindReplaceViewModelTests.cs`, `StandardsViewModelTests.cs`, `Tier2_ExportHarvesterTests.cs`, `Tier2_JsonEditorEventHandlerTests.cs`).
* **Find & Replace Extraction**: Extracted batch Find & Replace execution logic from `ProjectStandardsDashboardViewModel` into a dedicated `FindReplaceService` class and registered it in `SyntheticShared.projitems`. Added `FindReplaceServiceTests` NUnit suite.
* **Test Suite Relocation & Expansion**: Relocated `UVModelTests` to the correct folder and updated its namespace. Expanded `RevitDomExtensionsTests` in `SyntheticTests.Shared` to test `Material` and `PlanViewRange` translations under a live Revit context.
* **Git History Merge**: Integrated branch `refactor/legacy-cleanup-and-merge-fixes` resolving all modify/delete merge conflicts in favor of the clean current branch.
**Files Added:**
* [IUserPromptService.cs](../src/SyntheticShared/Shared/UI/IUserPromptService.cs)
* [WindowsUserPromptService.cs](../src/SyntheticShared/Shared/UI/WindowsUserPromptService.cs)
* [FakeUserPromptService.cs](../tests/SyntheticTests.Logic/Modules/RevitDOM/FakeUserPromptService.cs)
* [FindReplaceService.cs](../src/SyntheticShared/Modules/StandardsManagement/Utilities/FindReplaceService.cs)
* [IFindReplaceService.cs](../src/SyntheticShared/Modules/StandardsManagement/Utilities/IFindReplaceService.cs)
* [FindReplaceServiceTests.cs](../tests/SyntheticTests.Logic/Modules/StandardsManagement/FindReplaceServiceTests.cs)
* [RevitDomExtensionsTests.cs](../tests/SyntheticTests.Shared/Modules/RevitDOM/RevitDomExtensionsTests.cs)
**Files Deleted:**
* [CmdEnforceStandards.cs](../src/SyntheticShared/Modules/StandardsManagement/Commands/CmdEnforceStandards.cs)
* [CmdExportStandards.cs](../src/SyntheticShared/Modules/StandardsManagement/Commands/CmdExportStandards.cs)
* [StandardsEditorShow.cs](../src/SyntheticShared/Modules/StandardsManagement/Commands/StandardsEditorShow.cs)
* [EnforceStandardsViewModel.cs](../src/SyntheticShared/Modules/StandardsManagement/ViewModels/EnforceStandardsViewModel.cs)
* [ExportStylesViewModel.cs](../src/SyntheticShared/Modules/StandardsManagement/ViewModels/ExportStylesViewModel.cs)
* [JsonEditorMainViewModel.cs](../src/SyntheticShared/Modules/StandardsManagement/ViewModels/JsonEditorMainViewModel.cs)
* [FindReplaceViewModel.cs](../src/SyntheticShared/Modules/StandardsManagement/ViewModels/FindReplaceViewModel.cs)
* [EnforceStandardsView.xaml](../src/SyntheticShared/Modules/StandardsManagement/Views/EnforceStandardsView.xaml)
* [EnforceStandardsView.xaml.cs](../src/SyntheticShared/Modules/StandardsManagement/Views/EnforceStandardsView.xaml.cs)
* [ExportStylesView.xaml](../src/SyntheticShared/Modules/StandardsManagement/Views/ExportStylesView.xaml)
* [ExportStylesView.xaml.cs](../src/SyntheticShared/Modules/StandardsManagement/Views/ExportStylesView.xaml.cs)
* [JsonEditorWindow.xaml](../src/SyntheticShared/Modules/StandardsManagement/Views/JsonEditorWindow.xaml)
* [JsonEditorWindow.xaml.cs](../src/SyntheticShared/Modules/StandardsManagement/Views/JsonEditorWindow.xaml.cs)
* [FindReplaceWindow.xaml](../src/SyntheticShared/Modules/StandardsManagement/Views/FindReplaceWindow.xaml)
* [FindReplaceWindow.xaml.cs](../src/SyntheticShared/Modules/StandardsManagement/Views/FindReplaceWindow.xaml.cs)
* [ExportStylesViewModelTests.cs](../tests/SyntheticTests.Logic/Modules/StandardsManagement/ExportStylesViewModelTests.cs)
* [FindReplaceViewModelTests.cs](../tests/SyntheticTests.Logic/Modules/StandardsManagement/FindReplaceViewModelTests.cs)
* [StandardsViewModelTests.cs](../tests/SyntheticTests.Logic/Modules/StandardsManagement/StandardsViewModelTests.cs)
* [Tier2_ExportHarvesterTests.cs](../tests/SyntheticTests.Shared/Modules/StandardsManagement/Tier2_ExportHarvesterTests.cs)
* [Tier2_JsonEditorEventHandlerTests.cs](../tests/SyntheticTests.Shared/Modules/StandardsManagement/Tier2_JsonEditorEventHandlerTests.cs)
* [JsonEditorExternalEventHandler.cs](../src/SyntheticShared/Modules/StandardsManagement/Handlers/JsonEditorExternalEventHandler.cs)

## 2026-07-13 - Nested Dependency Harvesting & POCO-Driven Staging (PRD 015 - DB 015-1 to DB 015-5, Staging Queue Fix)
**Reason for Change:** Shifted nested dependency harvesting from native Revit database queries to a fast, recursive, disconnected POCO-driven Directed Acyclic Graph (DAG) lookup, enabling unified staging for both live Revit model sources and offline JSON file sources, and integrated it into the main UI extraction and staging queue workflows.
**Key Enhancements:**
* **RevitDomDependencyScanner (Issue 015-1)**: Implemented a pure C# reflection scanner inside `Synthetic.Modules.RevitDOM` namespace that recursively sweeps POCO object graphs for nested `ElementIdModel` references while filtering out external namespaces and avoiding cyclic reference stack overflows.
* **Refactored Style Group Preselection**: Updated `EnforceStandardsViewModel` style preselection to utilize the new `RevitDomDependencyScanner` rather than private internal scanners.
* **Bulk Identity Resolution (Issue 015-2)**: Added `GetElementsByElementIdModels` to `IIdentityService` interface and `RevitIdentityService` implementation, supporting bulk resolving of elements in a single loop and recording telemetry warning logs for unresolved references.
* **StandardsExtractionOrchestrator (Issue 015-3)**: Implemented a recursive DAG extraction orchestrator in `Synthetic.Modules.StandardsManagement.Engine` namespace. It runs a loop that extracts elements via `StandardSerializationEngine`, scans them for dependencies using the POCO scanner, resolves missing native elements in bulk, and tags child dependencies with a `DependencyOrigin` name badge.
* **POCO-Driven Staging Queue (Issue 015-4)**: Overhauled the "Add to Queue" execution pipeline in `ProjectStandardsDashboardViewModel`. It flattens the selected source tree into a lookup pool and uses the POCO scanner to recursively harvest and stage dependencies in-memory, making staging a 100% disconnected, offline operation.
* **Extraction Pipeline Integration (Issue 015-5)**: Integrated the new `StandardsExtractionOrchestrator` into `ProjectStandardsDashboardViewModel`'s live Revit extraction pipeline. Replaced legacy recursive family scanning (`ScanFamilyRecursively`) with the orchestrator, and refactored the native element harvesting into a clean `GatherRootElements` method.
* **POCO-Layer 5-Step Fallback Identity Resolution**: Introduced `IPocoIdentityService` and `PocoIdentityService` in `Synthetic.Modules.RevitDOM` implementing a 5-step fallback identity resolution strategy (UniqueId -> Id -> Type Guard -> Name -> Aliases) at the POCO layer to harvest and stage dependencies in the dashboard's Queue Pane reliably.
* **Global IsTemplate Default Fix**: Changed the default value of `IsTemplate` to `false` in `ElementModel` and `ElementIdModel` constructors, and refactored utility classes (`ParameterEngine`, `GraphicOverrideUtility`, `ElementModel`) to accept dependency-injected `IIdentityService` instances to resolve JIT compilation mismatches in test harnesses.
* **NUnit Verification Suite**: 
  - Wrote logic tests in `RevitDomDependencyScannerTests.cs`, `PocoIdentityServiceTests.cs`, and `ProjectStandardsDashboardTests.cs` (verifying offline/JSON dependency staging, orchestrator extraction, and POCO fallback resolution).
  - Wrote integration tests in `RevitIdentityServiceIntegrationTests.cs`, `Tier2_StandardsExtractionOrchestratorTests.cs`, and `Tier2_DashboardIntegrationTests.cs` (verifying deep WallType-to-Material dependency retrieval, tagging, and VM extraction).
  - All unit/integration tests compile and pass successfully across all supported Revit versions (2022-2026).
**Files Added:**
* [RevitDomDependencyScanner.cs](../src/SyntheticShared/Modules/RevitDOM/RevitDomDependencyScanner.cs)
* [RevitDomDependencyScannerTests.cs](../tests/SyntheticTests.Logic/Modules/RevitDOM/RevitDomDependencyScannerTests.cs)
* [IStandardsExtractionOrchestrator.cs](../src/SyntheticShared/Modules/StandardsManagement/Engine/IStandardsExtractionOrchestrator.cs)
* [StandardsExtractionOrchestrator.cs](../src/SyntheticShared/Modules/StandardsManagement/Engine/StandardsExtractionOrchestrator.cs)
* [Tier2_StandardsExtractionOrchestratorTests.cs](../tests/SyntheticTests.Shared/Modules/RevitDOM/Tier2_StandardsExtractionOrchestratorTests.cs)
* [IPocoIdentityService.cs](../src/SyntheticShared/Modules/RevitDOM/IPocoIdentityService.cs)
* [PocoIdentityService.cs](../src/SyntheticShared/Modules/RevitDOM/PocoIdentityService.cs)
* [PocoIdentityServiceTests.cs](../tests/SyntheticTests.Logic/Modules/RevitDOM/PocoIdentityServiceTests.cs)
**Files Modified:**
* [SyntheticShared.projitems](../src/SyntheticShared/SyntheticShared.projitems)
* [EnforceStandardsViewModel.cs](../src/SyntheticShared/Modules/StandardsManagement/ViewModels/EnforceStandardsViewModel.cs)
* [IIdentityService.cs](../src/SyntheticShared/Modules/RevitDOM/IIdentityService.cs)
* [RevitIdentityService.cs](../src/SyntheticShared/Modules/RevitDOM/RevitIdentityService.cs)
* [FakeIdentityService.cs](../tests/SyntheticTests.Shared/Modules/RevitDOM/FakeIdentityService.cs)
* [MockRevitAPI.cs](../tests/RevitAPIMock/MockRevitAPI.cs)
* [Tier2_AliasAssimilationTests.cs](../tests/SyntheticTests.Shared/Modules/RevitDOM/Tier2_AliasAssimilationTests.cs)
* [RevitIdentityServiceIntegrationTests.cs](../tests/SyntheticTests.Shared/Modules/RevitDOM/RevitIdentityServiceIntegrationTests.cs)
* [ElementModel.cs](../src/SyntheticShared/Modules/RevitDOM/ElementModel.cs)
* [ElementIdModel.cs](../src/SyntheticShared/Modules/RevitDOM/ElementIdModel.cs)
* [ParameterEngine.cs](../src/SyntheticShared/Modules/RevitDOM/ParameterEngine.cs)
* [GraphicOverrideUtility.cs](../src/SyntheticShared/Modules/RevitDOM/GraphicOverrideUtility.cs)
* [ProjectStandardsDashboardViewModel.cs](../src/SyntheticShared/Modules/StandardsManagement/ViewModels/ProjectStandardsDashboardViewModel.cs)
* [DependencyOriginTests.cs](../tests/SyntheticTests.Logic/Modules/StandardsManagement/DependencyOriginTests.cs)
* [ProjectStandardsDashboardTests.cs](../tests/SyntheticTests.Logic/Modules/StandardsManagement/ProjectStandardsDashboardTests.cs)
* [RevitDomExtensionsTests.cs](../tests/SyntheticTests.Logic/Modules/RevitDOM/RevitDomExtensionsTests.cs)
* [Tier2_DashboardIntegrationTests.cs](../tests/SyntheticTests.Shared/Modules/StandardsManagement/Tier2_DashboardIntegrationTests.cs)
* [SyntheticTests.Shared.projitems](../tests/SyntheticTests.Shared/SyntheticTests.Shared.projitems)

## 2026-07-11 - Legacy Code Cleanup, Mock API Correction & Merge Action Enhancements (PRDs 009 - 014)
**Reason for Change:** Cleaned up deprecated classes and constructor parameters left behind during recent refactors, resolved test compilation locks and test harness API mismatches, and enhanced standard element merging behavior to combine active execution intent tags.
**Key Enhancements:**
* **Mock Revit API Document.Delete (Commit 1)**: Implemented `Delete(ElementId)` in mock `Document` class to restore integration test capability, which was broken due to live element purging requirements added in PRD 013.
* **QueueIntent Demolition (Commit 2 & 3)**: Eliminated the obsolete `QueueIntent` enum and its constructor in `QueueItemModel.cs` as part of technical debt cleanup. Refactored `ProjectStandardsDashboardViewModel.cs` and button command parameters in `ProjectStandardsDashboardWindow.xaml` to use string literals instead.
* **Test Suite Refactoring**: Refactored logic and integration test files across `SyntheticTests.Logic` and shared test directories to use boolean parameters rather than the obsolete enum constructor.
* **Merge Execution Tag Aggregation (Commit 4)**: Enhanced the duplicate merging logic in `ProjectStandardsDashboardViewModel.ExecuteMergeQueue` to combine execution flags (`WillEnforce` and `WillSave`) from non-survivors onto the primary survivor.
* **NUnit Verification Suite**: Added a new unit test `MergeQueueItems_ShouldCombineExecutionFlagsAndMergeAliasesOntoSurvivor` in `QueueMergeTests.cs` to verify execution flag aggregation and alias appending.
**Files Modified:**
* [ProjectStandardsDashboardViewModel.cs](../src/SyntheticShared/Modules/StandardsManagement/ViewModels/ProjectStandardsDashboardViewModel.cs)
* [QueueItemModel.cs](../src/SyntheticShared/Modules/StandardsManagement/ViewModels/QueueItemModel.cs)
* [ProjectStandardsDashboardWindow.xaml](../src/SyntheticShared/Modules/StandardsManagement/Views/ProjectStandardsDashboardWindow.xaml)
* [MockRevitAPI.cs](../tests/RevitAPIMock/MockRevitAPI.cs)
* [DashboardFindReplaceTests.cs](../tests/SyntheticTests.Logic/Modules/StandardsManagement/DashboardFindReplaceTests.cs)
* [DashboardParametersEditorTests.cs](../tests/SyntheticTests.Logic/Modules/StandardsManagement/DashboardParametersEditorTests.cs)
* [DependencyOriginTests.cs](../tests/SyntheticTests.Logic/Modules/StandardsManagement/DependencyOriginTests.cs)
* [PhasedExecutionTests.cs](../tests/SyntheticTests.Logic/Modules/StandardsManagement/PhasedExecutionTests.cs)
* [QueueItemBaselineCloneTests.cs](../tests/SyntheticTests.Logic/Modules/StandardsManagement/QueueItemBaselineCloneTests.cs)
* [QueueMergeTests.cs](../tests/SyntheticTests.Logic/Modules/StandardsManagement/QueueMergeTests.cs)
* [ProjectStandardsDashboardTests.cs](../tests/SyntheticTests.Logic/Modules/StandardsManagement/ProjectStandardsDashboardTests.cs)
* [Tier2_DashboardIntegrationTests.cs](../tests/SyntheticTests.Shared/Modules/StandardsManagement/Tier2_DashboardIntegrationTests.cs)

## 2026-07-09 - Shared UI Generalization: Item Selection Refactor (PRD 014 - DB 014-1, DB 014-2)
**Reason for Change:** Abstracted duplicate merge and selection views into a universally reusable, generic single-item selection component, decoupling cross-module UI dependencies and optimizing item binding logic.
**Key Enhancements:**
* **Generic ViewModel & Interface (Issue 014-1)**: Implemented `SingleItemSelectionViewModel<T>` implementing a new `ISingleItemSelectionViewModel` interface, allowing dynamic element rendering via constructor-injected `Func<T, string>` display delegates.
* **Non-Generic WPF Selection View**: Created `SingleItemSelectionWindow` styled with desaturated flat headers and custom chrome, displaying elements using single-select `RadioButton` items inside a transparent `ListBox`.
* **Dynamic Content Mapping**: Built `SingleItemSelectionDisplayConverter` (an `IMultiValueConverter`) resolving element text representations dynamically within WPF XAML.
* **JSON Editor Modernization (Issue 014-2)**: Refactored `JsonEditorMainViewModel` to instantiate `SingleItemSelectionViewModel<ElementTypeWrapperVM>` and the new selection window, removing legacy dependencies.
* **Dashboard Optimization**: Overhauled `ProjectStandardsDashboardViewModel` to directly pass `QueueItemModel` references to the generic ViewModel, bypassing wrapper translations and simplifying the primary survivor resolution.
* **Demolition of Tech Debt**: Permanently deleted obsolete `MergeSelectionViewModel.cs`, `MergeSelectionWindow.xaml`, and `MergeSelectionWindow.xaml.cs` from the `MergeDuplicates` namespace and updated all projitems references.
* **NUnit Verification Suite**: Wrote 5 headless Tier 1 tests in `SingleItemSelectionViewModelTests.cs` verifying defaults, selection updates, converter resolution, and dialog execution signals (all tests pass successfully).
* **Alias Merge Result Telemetry (Issue 014-2)**: Integrated alias merge success/failure telemetry mapping into `ImportLogItem` inside `ProjectStandardsDashboardViewModel` and `EnforceStandardsViewModel`, allowing successfully merged and failed aliases to display in the modal summary results grid.
**Files Added:**
* [ISingleItemSelectionViewModel.cs](../src/SyntheticShared/Shared/UI/ISingleItemSelectionViewModel.cs)
* [SingleItemSelectionViewModel.cs](../src/SyntheticShared/Shared/UI/SingleItemSelectionViewModel.cs)
* [SingleItemSelectionDisplayConverter.cs](../src/SyntheticShared/Shared/UI/SingleItemSelectionDisplayConverter.cs)
* [SingleItemSelectionWindow.xaml](../src/SyntheticShared/Shared/UI/SingleItemSelectionWindow.xaml)
* [SingleItemSelectionWindow.xaml.cs](../src/SyntheticShared/Shared/UI/SingleItemSelectionWindow.xaml.cs)
* [SingleItemSelectionViewModelTests.cs](../tests/SyntheticTests.Logic/Modules/StandardsManagement/SingleItemSelectionViewModelTests.cs)
**Files Modified:**
* [JsonEditorMainViewModel.cs](../src/SyntheticShared/Modules/StandardsManagement/ViewModels/JsonEditorMainViewModel.cs)
* [ProjectStandardsDashboardViewModel.cs](../src/SyntheticShared/Modules/StandardsManagement/ViewModels/ProjectStandardsDashboardViewModel.cs)
* [EnforceStandardsViewModel.cs](../src/SyntheticShared/Modules/StandardsManagement/ViewModels/EnforceStandardsViewModel.cs)
* [SyntheticShared.projitems](../src/SyntheticShared/SyntheticShared.projitems)
* [fix_qualified_references.py](../src/fix_qualified_references.py)
* [QueueMergeTests.cs](../tests/SyntheticTests.Logic/Modules/StandardsManagement/QueueMergeTests.cs)
* [Tier2_DashboardIntegrationTests.cs](../tests/SyntheticTests.Shared/Modules/StandardsManagement/Tier2_DashboardIntegrationTests.cs)
* [SyntheticShared_API_Documentation.md](SyntheticShared_API_Documentation.md)

## 2026-07-09 - RevitDOM Alias Assimilation Engine Refactoring (PRD 013 - DB 013-1, DB 013-2)
**Reason for Change:** Refactored duplicate element alias merging out of the UI layer into a Sequenced Post-Commit pipeline in the core serialization engine, eliminating nested transaction exceptions and establishing graceful degradation patterns.
**Key Enhancements:**
* **Sequenced Post-Commit Queue (Issue 013-1)**: Implemented a deferred queue structure inside `StandardSerializationEngine.ToRevit()` that queues elements containing aliases and performs deep-swapping and purging of redundant elements strictly after all primary transactions have committed. Missing aliases are skipped silently.
* **Alias ID Resolution & State Restoration**: Configured identity resolution to match the primary element name without fallback aliases to prevent resolving the primary element to its own alias. Added state preservation to prevent model bindings from wiping the POCO's alias list.
* **Discrete Telemetry**: Added `Action` and `Message` properties to `SerializationResultModel` to track and report individual alias merge outcomes ("Merged Alias" or "Alias Swap Failed").
* **Tier 2 Integration Testing (Issue 013-2)**: Created `Tier2_AliasAssimilationTests.cs` using the NUnit integration framework, verifying successful alias reference swapping, element purging, and graceful degradation during simulated resolution failure (using a custom mock `FailIdentityService` to early-delete alias elements).
**Files Added:**
* [Tier2_AliasAssimilationTests.cs](../tests/SyntheticTests.Shared/Modules/RevitDOM/Tier2_AliasAssimilationTests.cs)
**Files Modified:**
* [SerializationResultModel.cs](../src/SyntheticShared/Modules/RevitDOM/SerializationResultModel.cs)
* [StandardSerializationEngine.cs](../src/SyntheticShared/Modules/RevitDOM/StandardSerializationEngine.cs)
* [SyntheticTests.Shared.projitems](../tests/SyntheticTests.Shared/SyntheticTests.Shared.projitems)

## 2026-07-09 - Project Standards Edit Pane Refactoring (PRD 011 - DB 011-1, DB 011-2, DB 011-3, DB 011-4)
**Reason for Change:** Refactored the Project Standards Edit Pane, introducing single-element model binding (`SelectedElement`), single vs. multi-select identity header logic, reflection-based POCO property harvesting with two-way syncing, and unified Parameters collection prepending.
**Key Enhancements:**
* **SelectedElement & Renaming Cascade (Issue 011-1)**: Implemented `SelectedElement` selection wrapper tracking to support active element binding. Automatically cascadingly renames references across the queue when an element name is modified.
* **Identity Selection States (Issue 011-2)**: Overhauled WPF view and VM properties (`IsSingleElementSelected`, `SelectedNameOrCount`, `SelectedDisplayClass`, `SelectedAliasesString`) using Style Triggers to dynamically show names as read-only/borderless text and disable the Aliases input field when multiple elements are selected.
* **Reflection Property Harvesting (Issue 011-3)**: Developed a reflection sweep in `ElementTypeWrapperVM` to automatically harvest public properties (excluding base `ElementModel` metadata and JSON ignores). Maps primitives to dummy `ParameterModel` wrappers with event-driven reflection setters and complex properties to type-based nested data routers.
* **Unified Parameter Grid (Issue 011-4)**: Integrates and prepends harvested POCO wrappers to the top of the Parameters collection, registering them for element dirtiness monitoring and enabling natural propagation of bulk edits and Find & Replace modifications.
* **Multi-Version Testing**: Validated logic and integration tests across Revit 2023, 2024, 2025, and 2026 with a clean pass status (all 503 tests passed).
**Files Modified:**
* [ProjectStandardsDashboardViewModel.cs](../src/SyntheticShared/Modules/StandardsManagement/ViewModels/ProjectStandardsDashboardViewModel.cs)
* [ElementTypeWrapperVM.cs](../src/SyntheticShared/Modules/StandardsManagement/ViewModels/ElementTypeWrapperVM.cs)
* [ParameterWrapperVM.cs](../src/SyntheticShared/Modules/StandardsManagement/ViewModels/ParameterWrapperVM.cs)
* [ProjectStandardsDashboardWindow.xaml](../src/SyntheticShared/Modules/StandardsManagement/Views/ProjectStandardsDashboardWindow.xaml)
* [ProjectStandardsDashboardTests.cs](../tests/SyntheticTests.Logic/Modules/StandardsManagement/ProjectStandardsDashboardTests.cs)

## 2026-07-08 - Project Standards Revit Model Source Refactor (PRD 010 - DB 010-1, DB 010-2, DB 010-3)
**Reason for Change:** Refactored the Project Standards Revit model source extraction pipeline to run asynchronously, overhauled the select document selection dialog with a checkable categories tree and family scanning toggles, cleaned up the dashboard UI by removing obsolete checkboxes, and integrated deep recursive loadable family scanning.
**Key Enhancements:**
* **Centralized Taxonomy Engine**: Built a stateless `StandardsHierarchyUtility` to centralize grouping, sorting, and template hierarchy generation.
* **Selection Dialog Overhaul**: Redesigned the document selection window to support checkable category trees and primary/nested family scanning configuration toggles.
* **Asynchronous Extraction & Cancellation**: Wrapped extraction in `ProgressCoordinator` to pump dispatcher events and prevent UI freezes on the Revit main thread, supporting graceful user cancellation.
* **Recursive Family Scanning**: Integrated deep recursive scanning of loadable family documents using `doc.EditFamily` and `familyDoc.Close` to harvest nested element standards.
* **UI Cleanup**: Demolished all obsolete post-extraction checkboxes and options from the dashboard window and ViewModels to return the source tab to a clean, read-only view.
* **Test Verification**: Created `SelectRevitDocumentTests` for selection dialog state testing and added a Tier 2 integration test `AddRevitModel_ExtractsDeeplyNestedElements_BasedOnConfiguration` to verify recursive nested family extraction using the NUnit test suite (all 115 tests passed).
**Files Added:**
* [StandardsHierarchyUtility.cs](../src/SyntheticShared/Modules/StandardsManagement/Utilities/StandardsHierarchyUtility.cs)
* [StandardsHierarchyUtilityTests.cs](../tests/SyntheticTests.Logic/Modules/StandardsManagement/StandardsHierarchyUtilityTests.cs)
* [SelectRevitDocumentTests.cs](../tests/SyntheticTests.Logic/Modules/StandardsManagement/SelectRevitDocumentTests.cs)
**Files Modified:**
* [ProjectStandardsDashboardViewModel.cs](../src/SyntheticShared/Modules/StandardsManagement/ViewModels/ProjectStandardsDashboardViewModel.cs)
* [ProjectStandardsSourceViewModel.cs](../src/SyntheticShared/Modules/StandardsManagement/ViewModels/ProjectStandardsSourceViewModel.cs)
* [SelectRevitDocumentViewModel.cs](../src/SyntheticShared/Modules/StandardsManagement/ViewModels/SelectRevitDocumentViewModel.cs)
* [ProjectStandardsDashboardWindow.xaml](../src/SyntheticShared/Modules/StandardsManagement/Views/ProjectStandardsDashboardWindow.xaml)
* [SelectRevitDocumentWindow.xaml](../src/SyntheticShared/Modules/StandardsManagement/Views/SelectRevitDocumentWindow.xaml)
* [SyntheticShared.projitems](../src/SyntheticShared/SyntheticShared.projitems)
* [MockRevitAPI.cs](../tests/RevitAPIMock/MockRevitAPI.cs)
* [DashboardTabAndFamilyFilterTests.cs](../tests/SyntheticTests.Logic/Modules/StandardsManagement/DashboardTabAndFamilyFilterTests.cs)
* [ProjectStandardsDashboardTests.cs](../tests/SyntheticTests.Logic/Modules/StandardsManagement/ProjectStandardsDashboardTests.cs)
* [QueueMergeTests.cs](../tests/SyntheticTests.Logic/Modules/StandardsManagement/QueueMergeTests.cs)

## 2026-07-07 - Project Standards Selection, Overwrites, & Theme Calibration (DB 009-08, DB 009-09, DB 009-10)
**Reason for Change:** Implemented recursive dependency harvesting, search filtering and check/uncheck tree selection controls, custom WPF guardrail warning prompts for target file path overwrites, right-aligned button margin standardizations in themes, and summary window dimension calibrations.
**Key Enhancements:**
* **Source Tree Search & Selection Utilities**: Added check-state bubble propagation, "Select All" / "Select None" controls, and recursive filtering with parent expansion logic to the source tree pane.
* **Save Guardrails & Duplicate Merging**: Built custom WPF `GuardrailPromptWindow` handling overwrite, skip, save-as, and cancel decisions. Implemented composite key element merging (`Class` + `Name`) for duplicate detection.
* **Button Margin Theming**: Defined `Synthetic.Margins.RightAction` (10px gutter) and right-aligned sub-styles to prevent rightmost buttons from sitting flush against dialog borders.
* **ImportSummaryWindow Calibration**: Calibrated default startup dimensions to 550x500 (exactly 50% width) and configured details column to wrap text gracefully.
* **Multi-Version Revit Validation**: Fixed .NET Framework 4.8 compatibility issues (like `string.Contains` overload and `ElementId.Value`) to successfully build and run all test projects across Revit 2023, 2024, 2025, and 2026.
**Files Added:**
* [GuardrailPromptWindow.xaml](../src/SyntheticShared/Shared/UI/GuardrailPromptWindow.xaml)
* [GuardrailPromptWindow.xaml.cs](../src/SyntheticShared/Shared/UI/GuardrailPromptWindow.xaml.cs)
**Files Modified:**
* [ProjectStandardsDashboardViewModel.cs](../src/SyntheticShared/Modules/StandardsManagement/ViewModels/ProjectStandardsDashboardViewModel.cs)
* [ProjectStandardsSourceViewModel.cs](../src/SyntheticShared/Modules/StandardsManagement/ViewModels/ProjectStandardsSourceViewModel.cs)
* [SourceTreeItemViewModel.cs](../src/SyntheticShared/Modules/StandardsManagement/ViewModels/SourceTreeItemViewModel.cs)
* [ProjectStandardsDashboardWindow.xaml](../src/SyntheticShared/Modules/StandardsManagement/Views/ProjectStandardsDashboardWindow.xaml)
* [SyntheticTheme.xaml](../src/SyntheticShared/Shared/UI/SyntheticTheme.xaml)
* [ImportSummaryWindow.xaml](../src/SyntheticShared/Modules/StandardsManagement/Views/ImportSummaryWindow.xaml)
* [ThemeTests.cs](../tests/SyntheticTests.Logic/Infrastructure/UI/ThemeTests.cs)
* [DashboardTabAndFamilyFilterTests.cs](../tests/SyntheticTests.Logic/Modules/StandardsManagement/DashboardTabAndFamilyFilterTests.cs)

## 2026-07-07 - Project Standards Consolidation Dashboard, Phased Execution, & Post-Execution Reports (DB 008-6, DB 008-7)
**Reason for Change:** Built the modeless Project Standards Consolidation Dashboard to stage execution queues, verify diffs, perform phased Revit-first/JSON-second operations, enforce protected file guardrails, and generate interactive post-execution markdown reports.
**Key Enhancements:**
* **Modeless Dashboard Workspace**: Developed `ProjectStandardsDashboardWindow` driving multiple sources, hierarchical tree views, search filtering, staged action queues, and interactive find & replace name/parameter editing.
* **Phased Execution Engine**: Implemented a two-phase run queue pipeline separating live database overrides (under a safe `TransactionGroup` rollback-controlled pipeline) from serialization outputs.
* **Safety Guardrails**: Implemented active path protection for project settings and app configuration paths. Integrated `WindowsGuardrailPromptService` alerting users of protected-overwrite attempts with redirection/skip choices.
* **Post-Execution Summaries & Markdown Logs**: Built the `ImportSummaryWindow` displaying a modal checklist of executed changes. Automatically generates a formatted `.log.md` file next to target JSON styles and exposes manual "Export Log" action.
* **Headless Test Decoupling**: Abstracted UI prompts via mockable interfaces (`IGuardrailPromptService`, `ISummaryDisplayService`) to achieve 100% headless NUnit testing coverage without throwing STA thread exceptions.
**Files Added:**
* [ISummaryDisplayService.cs](../src/SyntheticShared/Shared/UI/ISummaryDisplayService.cs)
* [WindowsSummaryDisplayService.cs](../src/SyntheticShared/Shared/UI/WindowsSummaryDisplayService.cs)
* [FakeSummaryDisplayService.cs](../tests/SyntheticTests.Logic/Modules/StandardsManagement/FakeSummaryDisplayService.cs)
* [WindowsGuardrailPromptService.cs](../src/SyntheticShared/Shared/UI/WindowsGuardrailPromptService.cs)
* [FakeGuardrailPromptService.cs](../tests/SyntheticTests.Logic/Modules/StandardsManagement/FakeGuardrailPromptService.cs)
* [IGuardrailPromptService.cs](../src/SyntheticShared/Shared/UI/IGuardrailPromptService.cs)
* [ProjectStandardsDashboardWindow.xaml](../src/SyntheticShared/Modules/StandardsManagement/Views/ProjectStandardsDashboardWindow.xaml)
* [ProjectStandardsDashboardWindow.xaml.cs](../src/SyntheticShared/Modules/StandardsManagement/Views/ProjectStandardsDashboardWindow.xaml.cs)
* [CmdProjectStandards.cs](../src/SyntheticShared/Modules/StandardsManagement/Commands/CmdProjectStandards.cs)
* [QueueItemModel.cs](../src/SyntheticShared/Modules/StandardsManagement/ViewModels/QueueItemModel.cs)
* [StandardClassModel.cs](../src/SyntheticShared/Modules/StandardsManagement/ViewModels/StandardClassModel.cs)
* [StandardElementModel.cs](../src/SyntheticShared/Modules/StandardsManagement/ViewModels/StandardElementModel.cs)
* [StandardGroupModel.cs](../src/SyntheticShared/Modules/StandardsManagement/ViewModels/StandardGroupModel.cs)
* [ProjectStandardsSourceViewModel.cs](../src/SyntheticShared/Modules/StandardsManagement/ViewModels/ProjectStandardsSourceViewModel.cs)
* [SourceTreeItemViewModel.cs](../src/SyntheticShared/Modules/StandardsManagement/ViewModels/SourceTreeItemViewModel.cs)
* [IntentToColorConverter.cs](../src/SyntheticShared/Modules/StandardsManagement/Views/IntentToColorConverter.cs)
* [Tier2_DashboardIntegrationTests.cs](../tests/SyntheticTests.Shared/Modules/StandardsManagement/Tier2_DashboardIntegrationTests.cs)
**Files Modified:**
* [ProjectStandardsDashboardViewModel.cs](../src/SyntheticShared/Modules/StandardsManagement/ViewModels/ProjectStandardsDashboardViewModel.cs)
* [ImportSummaryViewModel.cs](../src/SyntheticShared/Modules/StandardsManagement/ViewModels/ImportSummaryViewModel.cs)
* [ImportSummaryWindow.xaml](../src/SyntheticShared/Modules/StandardsManagement/Views/ImportSummaryWindow.xaml)
* [ProjectStandardsDashboardTests.cs](../tests/SyntheticTests.Logic/Modules/StandardsManagement/ProjectStandardsDashboardTests.cs)
* [SyntheticShared.projitems](../src/SyntheticShared/SyntheticShared.projitems)
* [SyntheticRibbon.cs](../src/SyntheticShared/Core/SyntheticRibbon.cs)

## 2026-07-06 - Extended UI Standardization & Responsive Design (PRD 007)
**Reason for Change:** Standardized the UI design system across the application by implementing responsive layouts, custom modern dark scrollbars, flat tab headers with active underlines, clean tooltips, scaled window chrome, and removing hardcoded element heights and widths to prevent text clipping and DPI scaling issues.
**Key Enhancements:**
* **Window Chrome & Dialog Scaling:** Scaled caption/title heights from 40 to 45, adjusted title font sizes, and updated 7 utility dialogs to use SizeToContent with minimum bounds for DPI and localization safety.
* **Global Input Standards:** Implemented central theme styles for Button, TextBox, and ComboBox controls defining standard padding and minimum dimensions. Swept and removed hardcoded heights and widths from standard controls across all view files.
* **ScrollBar & DataGrid Columns:** Developed modern flat dark ScrollBar and ScrollBarThumb templates with interactive hover and drag highlights. Converted fixed pixel column widths in DataGrids to proportional star-sizing (*, 2*, etc.) or Auto.
* **Tab & ToolTip Theming:** Built implicit flat dark templates for TabControl, TabItem (featuring a 2px active orange underline effect), and ToolTip (disabling default drop shadows). Swept legacy style overrides in StandardsClassSelectionControl.xaml and remaining views.
* **Padding & Text Clipping Fix:** Resolved text truncation inside value cells by adjusting vertical padding to 2px for TextBox and ComboBox controls.
* **Theme Test Verification:** Integrated TabControl, TabItem, ScrollBar, and ToolTip into the headless NUnit theme test suite, ensuring 53/53 tests pass.
**Files Added:**
* [PRD 007 - Extended UI Standardization.md](prds/PRD%20007%20-%20Extended%20UI%20Standardization.md)
**Files Modified:**
* [SyntheticTheme.xaml](../src/SyntheticShared/Shared/UI/SyntheticTheme.xaml)
* [WindowChromeBehavior.cs](../src/SyntheticShared/Shared/UI/WindowChromeBehavior.cs)
* [ThemeTests.cs](../tests/SyntheticTests.Logic/Infrastructure/UI/ThemeTests.cs)
* [StandardsClassSelectionControl.xaml](../src/SyntheticShared/Modules/StandardsManagement/Views/StandardsClassSelectionControl.xaml)
* [JsonEditorWindow.xaml](../src/SyntheticShared/Modules/StandardsManagement/Views/JsonEditorWindow.xaml)
* [DetailItemFactoryView.xaml](../src/SyntheticShared/Modules/DetailItemFactory/Views/DetailItemFactoryView.xaml)
* [MergeDuplicatesWindow.xaml](../src/SyntheticShared/Modules/MergeDuplicates/Views/MergeDuplicatesWindow.xaml)
* [MergeDetailedReviewWindow.xaml](../src/SyntheticShared/Modules/MergeDuplicates/Views/MergeDetailedReviewWindow.xaml)
* [NetworkPathsWizardWindow.xaml](../src/SyntheticShared/Modules/SettingsDashboard/Views/NetworkPathsWizardWindow.xaml)
* View files for AutoTagger, DetailItemFactory, MergeDuplicates, SettingsDashboard, StandardsManagement, and Shared UI modules.

## 2026-07-01 - RevitDOM Full Roster Integration Testing & Dispatcher Hardening (PRD 005)
**Reason for Change:** Implemented a full roster sweep integration test to audit and validate dispatcher routing, hardened dispatcher telemetry, registered confirmed triage lists (Ignored and Pending), and supported text elements and spot dimensions.
**Key Enhancements:**
* **Full Roster Sweep Integration Test:** Created `Tier2_DispatcherSweepTests.cs` using a hybrid transaction loop (rolling back all test database mutations) and capturing thread-local warnings.
* **Triage List Registrations:** Configured `StandardSerializationEngine` to map 5 ignored types (e.g. `InternalOrigin`) and 13 pending types (e.g. `RailingType`), silencing system noise and cataloging technical debt.
* **Annotation & Spot Dimension Translators:** Created `TextElementTypeTranslator` and `SpotDimensionTypeTranslator` reusing `ElementTypeModel` to serialize and duplicate `TextNoteType`, `TextElementType`, `ModelTextType`, and `SpotDimensionType` correctly.
* **Toposolid Support (Revit 2024+):** Configured conditional registration in `StandardSerializationEngine` using preprocessor directives to route `ToposolidType` to `HostObjTypeTranslator` only on modern targets while remaining compilable on Revit 2022/2023.
* **UI VM & Dictionary Routing:** Updated `ModelsToSerialize` to host sorted `ModelTextTypes`, `SpotDimensionTypes`, and `ToposolidTypes` dictionaries, and integrated them into style filter views.
**Files Added:**
* [Tier2_DispatcherSweepTests.cs](../tests/SyntheticTests.Shared/Modules/RevitDOM/Tier2_DispatcherSweepTests.cs)
* [TextElementTypeTranslator.cs](../src/SyntheticShared/Modules/RevitDOM/TextElementTypeTranslator.cs)
* [SpotDimensionTypeTranslator.cs](../src/SyntheticShared/Modules/RevitDOM/SpotDimensionTypeTranslator.cs)
**Files Modified:**
* [StandardSerializationEngine.cs](../src/SyntheticShared/Modules/RevitDOM/StandardSerializationEngine.cs)
* [ModelDispatcher.cs](../src/SyntheticShared/Modules/RevitDOM/ModelDispatcher.cs)
* [ModelsToSerialize.cs](../src/SyntheticShared/Modules/RevitDOM/ModelsToSerialize.cs)
* [ExportStylesViewModel.cs](../src/SyntheticShared/Modules/StandardsManagement/ViewModels/ExportStylesViewModel.cs)
* [EnforceStandardsViewModel.cs](../src/SyntheticShared/Modules/StandardsManagement/ViewModels/EnforceStandardsViewModel.cs)
* [StandardsClassSelectionViewModel.cs](../src/SyntheticShared/Modules/StandardsManagement/ViewModels/StandardsClassSelectionViewModel.cs)
* [SyntheticShared.projitems](../src/SyntheticShared/SyntheticShared.projitems)
* [ViewModelSerializationTests.cs](../tests/SyntheticTests.Logic/Modules/RevitDOM/ViewModelSerializationTests.cs)
* [DatumAndAnnotationTranslatorTests.cs](../tests/SyntheticTests.Shared/Modules/RevitDOM/DatumAndAnnotationTranslatorTests.cs)

## 2026-06-28 - View Serialization Architecture & Polymorphic Translation (PRD 004)
**Reason for Change:** Implemented polymorphic view serialization, eliminated multiple-inheritance conflicts by absorbing ViewTemplateModel, established capability guardrails to bypass graphical queries on non-graphical views, separated plan specific properties into a ViewPlan subclass, and introduced clean sheet and schedule schemas devoid of graphical property noise.
**Key Enhancements:**
* **ViewTemplateModel Absorption:** Absorbed `ViewTemplateModel` properties into the base `ViewModel` and routed them dynamically based on the `IsTemplate` boolean, resolving multiple-inheritance conflicts and deleting `ViewTemplateModel.cs`.
* **Graphical Capability Guardrail:** Built `IsGraphicalView` check on `ViewType` to prevent querying graphical properties on sheets/schedules, removing legacy control-flow try-catch exceptions.
* **Polymorphic Plan Views:** Developed `ViewPlanModel` and `ViewPlanTranslator` to isolate plan-specific properties (`ViewRange` and underlay settings) from base ViewModel classes.
* **Polymorphic Sheet & Schedule Views:** Introduced `ViewSheetModel`/`ViewSheetTranslator` and `ViewScheduleModel`/`ViewScheduleTranslator`. Annotated base graphical properties to ignore nulls when serializing, delivering clean, noise-free JSON schemas.
* **Polymorphic Factory Integration:** Updated `ToModel` and `ToTemplateModel` view extension methods to return the correct polymorphic subclass based on the native view type.
* **Test Suite Verification:** Added logic and integration tests, successfully verifying the full test suite (402 tests) against Logic and all target Revit versions.
**Files Added:**
* [ViewPlanModel.cs](../src/SyntheticShared/Modules/RevitDOM/ViewPlanModel.cs)
* [ViewPlanTranslator.cs](../src/SyntheticShared/Modules/RevitDOM/ViewPlanTranslator.cs)
* [ViewSheetModel.cs](../src/SyntheticShared/Modules/RevitDOM/ViewSheetModel.cs)
* [ViewSheetTranslator.cs](../src/SyntheticShared/Modules/RevitDOM/ViewSheetTranslator.cs)
* [ViewScheduleModel.cs](../src/SyntheticShared/Modules/RevitDOM/ViewScheduleModel.cs)
* [ViewScheduleTranslator.cs](../src/SyntheticShared/Modules/RevitDOM/ViewScheduleTranslator.cs)
* [ViewModelSerializationTests.cs](../tests/SyntheticTests.Logic/Modules/RevitDOM/ViewModelSerializationTests.cs)
**Files Deleted:**
* `src/SyntheticShared/Modules/RevitDOM/ViewTemplateModel.cs`
**Files Modified:**
* [ViewModel.cs](../src/SyntheticShared/Modules/RevitDOM/ViewModel.cs)
* [ViewTranslator.cs](../src/SyntheticShared/Modules/RevitDOM/ViewTranslator.cs)
* [StandardSerializationEngine.cs](../src/SyntheticShared/Modules/RevitDOM/StandardSerializationEngine.cs)
* [SyntheticShared.projitems](../src/SyntheticShared/SyntheticShared.projitems)
* [ViewTranslatorTests.cs](../tests/SyntheticTests.Shared/Modules/RevitDOM/ViewTranslatorTests.cs)

## 2026-06-27 - Identity Service Seam & Legacy Utilities Demolition (PRD 003)
**Reason for Change:** Overhauled the RevitDOM module mapping infrastructure to transition from legacy static reference resolution to a decoupled, testable identity routing service (`IIdentityService`). Safely demolished monolithic legacy utility files, relocated remaining orphaned mapping functions to their strictly governed architectural homes, and verified the entire multi-version integration test suite.
**Key Enhancements:**
* **IIdentityService Seam & Implementation:** Designed `IIdentityService` interface and its production implementation `RevitIdentityService` supporting a 5-step fallback resolver sequence (`UniqueId` -> `Id` -> `Type Guard` -> `Name` -> `Alias`).
* **Headless Testing Mock:** Developed a headless `FakeIdentityService` mockup implementing `IIdentityService` to enable 100% database-independent unit tests for all translators.
* **Polymorphic Model Dispatches:** Integrated `IIdentityService` dependency injection and polymorphic `Extract` capabilities into `ModelDispatcher` and `StandardSerializationEngine`.
* **Legacy Demolition:** Permanently deleted legacy utility files `LegacyDomExtensions.cs`, `ReferenceResolver.cs`, and their associated tests.
* **Architectural Cleanup & Locality Remediation:** Moved remaining legacy mapping fragments to their correct locality: WallSweeps logic to `HostObjTypeTranslator.cs`, Category mappings to `CategoryTranslator.cs`, Color conversions to the new `ColorTranslator.cs`, and Enum conversions to `EnumUtil.cs`.
* **Git Hygiene:** Ignored and untracked Google Docs workspace folders `docs/issues` and `docs/development_briefs` via `.gitignore`.
* **Test Suite Verification:** Relocated unit and integration test fixtures, confirming that all 32 logic tests and 89 live Revit 2026 integration tests pass successfully.
**Files Added:**
* [RevitDomExtensions.cs](../src/SyntheticShared/Modules/RevitDOM/RevitDomExtensions.cs)
* [ColorTranslator.cs](../src/SyntheticShared/Modules/RevitDOM/ColorTranslator.cs)
* [RevitIdentityServiceIntegrationTests.cs](../tests/SyntheticTests.Shared/Modules/RevitDOM/RevitIdentityServiceIntegrationTests.cs)
* [ColorTranslatorTests.cs](../tests/SyntheticTests.Shared/Modules/RevitDOM/ColorTranslatorTests.cs)
**Files Deleted:**
* `src/SyntheticShared/Modules/RevitDOM/LegacyDomExtensions.cs`
* `src/SyntheticShared/Modules/RevitDOM/ReferenceResolver.cs`
* `tests/SyntheticTests.Shared/Modules/RevitDOM/LegacyDomExtensionsTests.cs`
**Files Modified:**
* [CategoryTranslator.cs](../src/SyntheticShared/Modules/RevitDOM/CategoryTranslator.cs)
* [CategoryIdModel.cs](../src/SyntheticShared/Modules/RevitDOM/CategoryIdModel.cs)
* [CategoryModel.cs](../src/SyntheticShared/Modules/RevitDOM/CategoryModel.cs)
* [ColorModel.cs](../src/SyntheticShared/Modules/RevitDOM/ColorModel.cs)
* [EnumModel.cs](../src/SyntheticShared/Modules/RevitDOM/EnumModel.cs)
* [HostObjTypeTranslator.cs](../src/SyntheticShared/Modules/RevitDOM/HostObjTypeTranslator.cs)
* [GraphicOverrideUtility.cs](../src/SyntheticShared/Modules/RevitDOM/GraphicOverrideUtility.cs)
* [ViewTranslator.cs](../src/SyntheticShared/Modules/RevitDOM/ViewTranslator.cs)
* [EnumUtil.cs](../src/SyntheticShared/Shared/EnumUtil.cs)
* [SyntheticShared.projitems](../src/SyntheticShared/SyntheticShared.projitems)
* [EnumModelTests.cs](../tests/SyntheticTests.Logic/Modules/RevitDOM/EnumModelTests.cs)
* [CategoryTranslatorTests.cs](../tests/SyntheticTests.Shared/Modules/RevitDOM/CategoryTranslatorTests.cs)
* [HostObjTypeTranslatorTests.cs](../tests/SyntheticTests.Shared/Modules/RevitDOM/HostObjTypeTranslatorTests.cs)
* [SyntheticTests.Shared.projitems](../tests/SyntheticTests.Shared/SyntheticTests.Shared.projitems)
* [.gitignore](../.gitignore)

## 2026-06-26 - RevitDOM Hybrid Dispatcher Refactor & Legacy Demolition (Issue 9 to 18)
**Reason for Change:** Completely overhauled the RevitDOM module to move away from the monolithic legacy transaction engine to a decentralized translator design managed by the `StandardSerializationEngine` dispatcher. Wired bulk execution paths to all standards management ViewModels and event handlers, refactored duplicate merging to avoid nested transaction exceptions, and permanently retired the legacy transaction engine.
**Key Enhancements:**
* **ModelDispatcher & Translators:** Built a modular bidirectional model translation system for `Material`, `FilledRegionType`, `ParameterFilterElement`, `HostObjType`, `DimensionType`, `GridType`, `LevelType`, `TextNoteType`, `ParameterElement`, and `BrowserOrganization`.
* **ViewModel & Handler Wiring:** Integrated the bulk `ByRevit()`, `Analyze()`, and `ToRevit()` execution paths in `ExportStylesViewModel`, `EnforceStandardsViewModel`, `JsonEditorExternalEventHandler`, and `StandardsDiffEngine` to run high-performance topological sorts and batch imports/exports.
* **Refactored Merging Transactions:** Refactored duplicate merging in `ProcessMergeEventHandler` to perform reference swapping outside of active Revit transaction blocks, eliminating nested transaction exceptions.
* **Graphic Override Isolation:** Moved graphic override mapping to a dedicated stateless `GraphicOverrideUtility` class.
* **Test Suite Verification:** Cleaned up obsolete tests (deleted `DomTransactionEngineTests.cs`) and verified the entire multi-version integration test suite (344 tests) with a clean pass state.
**Files Added:**
* [GraphicOverrideUtility.cs](../src/SyntheticShared/Modules/RevitDOM/GraphicOverrideUtility.cs)
* [ParameterElementTranslator.cs](../src/SyntheticShared/Modules/RevitDOM/ParameterElementTranslator.cs)
* [BrowserOrganizationTranslator.cs](../src/SyntheticShared/Modules/RevitDOM/BrowserOrganizationTranslator.cs)
* [StandardSerializationEngine.cs](../src/SyntheticShared/Modules/RevitDOM/StandardSerializationEngine.cs)
**Files Deleted:**
* [DomTransactionEngine.cs](../src/SyntheticShared/Modules/RevitDOM/DomTransactionEngine.cs)
* [DomTransactionEngineTests.cs](../tests/SyntheticTests.Shared/Modules/RevitDOM/DomTransactionEngineTests.cs)
**Files Modified:**
* [ExportStylesViewModel.cs](../src/SyntheticShared/Modules/StandardsManagement/ViewModels/ExportStylesViewModel.cs)
* [EnforceStandardsViewModel.cs](../src/SyntheticShared/Modules/StandardsManagement/ViewModels/EnforceStandardsViewModel.cs)
* [JsonEditorExternalEventHandler.cs](../src/SyntheticShared/Modules/StandardsManagement/Handlers/JsonEditorExternalEventHandler.cs)
* [ProcessMergeEventHandler.cs](../src/SyntheticShared/Modules/MergeDuplicates/Handlers/ProcessMergeEventHandler.cs)

## 2026-06-19 - Registry Hard Cutover, Legacy Archive, and Quarantine Triage (Module 5 - Issues 1-3)
**Reason for Change:** Completed the hard cutover of the PropertyRegistry, archived the legacy mappings, triaged the quarantine log to manually restore critical unmatched primitives, resolved cross-version Revit API compilation discrepancies, and verified the entire cutover via automated unit/integration tests and a manual JSON inspector command.
**Key Enhancements:**
* **Legacy Mapping Archive:** Extracted and archived the verbose legacy `PropertyRegistry` mappings (over 1,100 lines of explicit C# registrations) into [LegacyPropertyRegistry_Archive.cs.txt](../docs/legacy_code_archive/LegacyPropertyRegistry_Archive.cs.txt).
* **Lean Registry Mappings:** Replaced the active `PropertyRegistry` with generated lean mappings based on the primitive purging heuristics, reducing the overall footprint of the class by over 50%.
* **Quarantine Triage & Restoration:** Triaged unmatched primitives and restored 38 classes/settings groups with critical properties (e.g. SiteLocation, DuctSettings, PipeSettings, EnergyDataSettings, TextElement, ReferencePlane, GlobalParameter, RouteAnalysisSettings, etc.) that are not backed by standard parameter bindings.
* **Cross-Version API Guards:** Implemented conditional preprocessor directives to support backward compatibility (e.g. guarding `DuctSettings.AirDynamicViscosity` for Revit 2025+ and `DuctSettings.NetworkBasedCalculations` for Revit 2024+).
* **CmdTestSerializationInspector Command:** Implemented and refined a diagnostic UI command under the Synthetic ribbon to serialize selected element payloads to a temp JSON file. Added automatic resolution to the corresponding `ElementType` when a physical element instance is selected in the viewport.
* **Test Suite Verification:** Ran the entire unit and integration test suite across all target Revit versions (2023, 2024, 2025, 2026) and the headless Logic test project, confirming that all 142 tests passed.
**Files Added:**
* [LegacyPropertyRegistry_Archive.cs.txt](../docs/legacy_code_archive/LegacyPropertyRegistry_Archive.cs.txt)
**Files Modified:**
* [PropertyRegistry.cs](../src/SyntheticShared/Modules/RevitSerialization/PropertyRegistry.cs)
* [CmdTestSerializationInspector.cs](../src/SyntheticShared/Modules/RevitSerialization/Commands/CmdTestSerializationInspector.cs)

## 2026-06-18 - Relational Reference Resolution & Topological Sorting (Module 4 - Issues 1-3)
**Reason for Change:** Implemented relational reference harvesting, topological sorting dependency resolution, and multi-tiered reference resolution for import execution, alongside pure POCO models and translators for Color, UV, PlanViewRange, and CompoundStructures to isolate serialization logic from native Revit API dependencies.
**Key Enhancements:**
* **Relational Reference Extraction:** Implemented pure [ElementReferenceModel](../src/SyntheticShared/Modules/RevitSerialization/ElementReferenceModel.cs) and extended [PropertyRegistry](../src/SyntheticShared/Modules/RevitSerialization/PropertyRegistry.cs) and [PropertyRegistryBuilder](../src/SyntheticShared/Modules/RevitSerialization/PropertyRegistryBuilder.cs) to accept extraction delegates for harvesting deeply nested reference identities.
* **ReferenceResolver Fallback Protocol:** Built [ReferenceResolver](../src/SyntheticShared/Modules/RevitSerialization/ReferenceResolver.cs) utility with a 5-step fallback execution protocol (`UniqueId` -> `Id` -> `Type Guard` -> `Name` -> `Alias`) to resolve references safely during property injection.
* **Topological Sort Graph & Cycle Detection:** Developed [ImportExecutionRunner](../src/SyntheticShared/Modules/RevitSerialization/ImportExecutionRunner.cs) which scans import payloads, filters external references, builds a Directed Acyclic Graph (DAG), and performs a bottom-up topological sort. Implemented cycle detection with [CircularDependencyException](../src/SyntheticShared/Modules/RevitSerialization/CircularDependencyException.cs) yielding explicit path traces.
* **Pure POCO Translators:** Implemented pure POCO models and translators for Color ([ColorModel](../src/SyntheticShared/Modules/RevitSerialization/ColorModel.cs) / [ColorTranslator](../src/SyntheticShared/Modules/RevitSerialization/ColorTranslator.cs)), UV ([UVModel](../src/SyntheticShared/Modules/RevitSerialization/UVModel.cs) / [UVTranslator](../src/SyntheticShared/Modules/RevitSerialization/UVTranslator.cs)), PlanViewRange ([PlanViewRangeModel](../src/SyntheticShared/Modules/RevitSerialization/PlanViewRangeModel.cs) / [PlanViewRangeTranslator](../src/SyntheticShared/Modules/RevitSerialization/PlanViewRangeTranslator.cs)), and CompoundStructure ([CompoundStructureModel](../src/SyntheticShared/Modules/RevitSerialization/CompoundStructureModel.cs) / [SerialCompoundStructureLayer](../src/SyntheticShared/Modules/RevitSerialization/SerialCompoundStructureLayer.cs) / [CompoundStructureTranslator](../src/SyntheticShared/Modules/RevitSerialization/CompoundStructureTranslator.cs)) to isolate serialization from native Revit API dependencies.
* **Decoupled Instantiation:** Implemented [IElementFactory](../src/SyntheticShared/Modules/RevitSerialization/IElementFactory.cs), custom [TemplateNotFoundException](../src/SyntheticShared/Modules/RevitSerialization/TemplateNotFoundException.cs), and [TemplateDuplicationFactory](../src/SyntheticShared/Modules/RevitSerialization/TemplateDuplicationFactory.cs) for dynamic template element duplication.
* **Test Suites & Mocking Extensions:** Integrated logic tests ([CompoundStructureTranslatorTests](../tests/SyntheticTests.Logic/Modules/RevitSerialization/CompoundStructureTranslatorTests.cs), [ElementReferenceExtractionTests](../tests/SyntheticTests.Logic/Modules/RevitSerialization/ElementReferenceExtractionTests.cs), [ImportExecutionRunnerTests](../tests/SyntheticTests.Logic/Modules/RevitSerialization/ImportExecutionRunnerTests.cs), [PlanViewRangeUVTranslatorTests](../tests/SyntheticTests.Logic/Modules/RevitSerialization/PlanViewRangeUVTranslatorTests.cs), [PropertyRegistryEmbedTests](../tests/SyntheticTests.Logic/Modules/RevitSerialization/PropertyRegistryEmbedTests.cs), [PropertyRegistryFactoryTests](../tests/SyntheticTests.Logic/Modules/RevitSerialization/PropertyRegistryFactoryTests.cs)) and integration tests ([Tier2_ReferenceResolverIntegrationTests](../tests/SyntheticTests.Shared/Modules/RevitSerialization/Tier2_ReferenceResolverIntegrationTests.cs), [TestExecutionOrchestrator](../tests/SyntheticTests.Shared/TestExecutionOrchestrator.cs)).
**Files Added:**
* [CircularDependencyException.cs](../src/SyntheticShared/Modules/RevitSerialization/CircularDependencyException.cs)
* [ColorModel.cs](../src/SyntheticShared/Modules/RevitSerialization/ColorModel.cs)
* [ColorTranslator.cs](../src/SyntheticShared/Modules/RevitSerialization/ColorTranslator.cs)
* [CompoundStructureModel.cs](../src/SyntheticShared/Modules/RevitSerialization/CompoundStructureModel.cs)
* [CompoundStructureTranslator.cs](../src/SyntheticShared/Modules/RevitSerialization/CompoundStructureTranslator.cs)
* [ElementReferenceModel.cs](../src/SyntheticShared/Modules/RevitSerialization/ElementReferenceModel.cs)
* [IElementFactory.cs](../src/SyntheticShared/Modules/RevitSerialization/IElementFactory.cs)
* [ImportExecutionRunner.cs](../src/SyntheticShared/Modules/RevitSerialization/ImportExecutionRunner.cs)
* [PlanViewRangeModel.cs](../src/SyntheticShared/Modules/RevitSerialization/PlanViewRangeModel.cs)
* [PlanViewRangeTranslator.cs](../src/SyntheticShared/Modules/RevitSerialization/PlanViewRangeTranslator.cs)
* [ReferenceResolver.cs](../src/SyntheticShared/Modules/RevitSerialization/ReferenceResolver.cs)
* [SerialCompoundStructureLayer.cs](../src/SyntheticShared/Modules/RevitSerialization/SerialCompoundStructureLayer.cs)
* [TemplateDuplicationFactory.cs](../src/SyntheticShared/Modules/RevitSerialization/TemplateDuplicationFactory.cs)
* [TemplateNotFoundException.cs](../src/SyntheticShared/Modules/RevitSerialization/TemplateNotFoundException.cs)
* [UVModel.cs](../src/SyntheticShared/Modules/RevitSerialization/UVModel.cs)
* [UVTranslator.cs](../src/SyntheticShared/Modules/RevitSerialization/UVTranslator.cs)
* [CompoundStructureTranslatorTests.cs](../tests/SyntheticTests.Logic/Modules/RevitSerialization/CompoundStructureTranslatorTests.cs)
* [ElementReferenceExtractionTests.cs](../tests/SyntheticTests.Logic/Modules/RevitSerialization/ElementReferenceExtractionTests.cs)
* [ImportExecutionRunnerTests.cs](../tests/SyntheticTests.Logic/Modules/RevitSerialization/ImportExecutionRunnerTests.cs)
* [PlanViewRangeUVTranslatorTests.cs](../tests/SyntheticTests.Logic/Modules/RevitSerialization/PlanViewRangeUVTranslatorTests.cs)
* [PropertyRegistryEmbedTests.cs](../tests/SyntheticTests.Logic/Modules/RevitSerialization/PropertyRegistryEmbedTests.cs)
* [PropertyRegistryFactoryTests.cs](../tests/SyntheticTests.Logic/Modules/RevitSerialization/PropertyRegistryFactoryTests.cs)
* [Tier2_ReferenceResolverIntegrationTests.cs](../tests/SyntheticTests.Shared/Modules/RevitSerialization/Tier2_ReferenceResolverIntegrationTests.cs)
* [TestExecutionOrchestrator.cs](../tests/SyntheticTests.Shared/TestExecutionOrchestrator.cs)
**Files Modified:**
* [PropertyAdapter.cs](../src/SyntheticShared/Modules/RevitSerialization/PropertyAdapter.cs)
* [PropertyRegistry.cs](../src/SyntheticShared/Modules/RevitSerialization/PropertyRegistry.cs)
* [PropertyRegistryBuilder.cs](../src/SyntheticShared/Modules/RevitSerialization/PropertyRegistryBuilder.cs)
* [SyntheticShared.projitems](../src/SyntheticShared/SyntheticShared.projitems)
* [MockRevitAPI.cs](../tests/RevitAPIMock/MockRevitAPI.cs)
* [SyntheticTests.Logic.csproj](../tests/SyntheticTests.Logic/SyntheticTests.Logic.csproj)
* [SyntheticTests.Shared.projitems](../tests/SyntheticTests.Shared/SyntheticTests.Shared.projitems)
* [Tier2_SerializationIntegrationTests.cs](../tests/SyntheticTests.Shared/Tier2_SerializationIntegrationTests.cs)

## 2026-06-14 - Decoupled Instantiation Factories: Property Registry Extensions & Execution Orchestrator Integration (Issues 2-3)
**Reason for Change:** Extended the compiled property registry and builder with fluent instantiation factory registrations and established a coordinated two-step orchestrator pipeline, validating the decoupled architecture end-to-end against a live Revit database.
**Key Enhancements:**
* **Factory Registration Extensions:** Expanded `PropertyRegistry` to manage factory delegates without reflection. Implemented inheritance traversal (`GetFactory`) for resolving base-class factory registrations polymorphicly.
* **Fluent Builder Overloads:** Added two `.ConstructVia()` overloads to `PropertyRegistryBuilder<T>`, enabling developers to bind creation delegates via custom lambdas or generic `IElementFactory` classes.
* **Execution Orchestration:** Created `TestExecutionOrchestrator` to execute the sequential "Resolve -> Instantiate -> Inject" workflow.
* **Tier 2 Integration Tests:** Added NUnit integration tests inside `Tier2_SerializationIntegrationTests` to run the orchestrator under Revit. Tested both WallType duplication (via `TemplateDuplicationFactory` and parameter overrides) and LinePatternElement construction (via static lambdas), enforcing the Zero-Leak Rule via TransactionGroup rollbacks.
* **Logic Unit Tests:** Added `PropertyRegistryFactoryTests` verifying direct delegate registration, generic factory wrappers, inheritance traversal, and unconfigured type fallbacks.
**Files Added:**
* [TestExecutionOrchestrator.cs](../tests/SyntheticTests.Shared/TestExecutionOrchestrator.cs)
* [PropertyRegistryFactoryTests.cs](../tests/SyntheticTests.Logic/Modules/RevitSerialization/PropertyRegistryFactoryTests.cs)
**Files Modified:**
* [PropertyRegistry.cs](../src/SyntheticShared/Modules/RevitSerialization/PropertyRegistry.cs)
* [PropertyRegistryBuilder.cs](../src/SyntheticShared/Modules/RevitSerialization/PropertyRegistryBuilder.cs)
* [SyntheticTests.Shared.projitems](../tests/SyntheticTests.Shared/SyntheticTests.Shared.projitems)
* [Tier2_SerializationIntegrationTests.cs](../tests/SyntheticTests.Shared/Tier2_SerializationIntegrationTests.cs)


## 2026-06-14 - Metadata-Driven Serialization Pipeline: Generic Element Model, Property Registry, Property Adapter & Tier 2 Integration Tests (Issues 1-5)
**Reason for Change:** Established the core pure C# foundational serialization state container, the high-performance compiled property registry, and the stateless adapter service to map properties/parameters. Also created a live Revit integration test suite to validate end-to-end round-trip fidelity.
**Key Enhancements:**
* **GenericElementModel Container:** Created a pure POCO state container dividing data into `Properties` and `Parameters` (using a strongly-typed `Dictionary<string, ParameterModel>`) with zero Revit API dependencies.
* **Compiled Property Registry:** Implemented reflection-free property mapping using strongly-typed delegates for high-performance reading and writing. Added base-class traversal support for polymorphic property resolution on runtime subclasses.
* **PropertyAdapter Service:** Created a stateless bridge to extract and inject primitive properties and parameters between Revit elements and the generic state model, prioritizing shared parameter matching by GUID and falling back to name/type resolution.
* **Tier 2 Integration Tests:** Built a live NUnit integration test suite (`Tier2_SerializationIntegrationTests`) utilizing the `ricaun.RevitTest` runner. The suite creates a `Level` element, extracts/serializes/deserializes it, mutates properties/parameters, injects changes back, asserts live mutation, and rolls back all elements via a transaction group (Zero-Leak Rule).
**Files Added:**
* [ParameterModel.cs](../src/SyntheticShared/Modules/RevitSerialization/ParameterModel.cs)
* [GenericElementModel.cs](../src/SyntheticShared/Modules/RevitSerialization/GenericElementModel.cs)
* [PropertyRegistry.cs](../src/SyntheticShared/Modules/RevitSerialization/PropertyRegistry.cs)
* [PropertyRegistryBuilder.cs](../src/SyntheticShared/Modules/RevitSerialization/PropertyRegistryBuilder.cs)
* [PropertyAdapter.cs](../src/SyntheticShared/Modules/RevitSerialization/PropertyAdapter.cs)
* [Tier2_SerializationIntegrationTests.cs](../tests/SyntheticTests.Shared/Tier2_SerializationIntegrationTests.cs)
**Files Modified:**
* [SyntheticShared.projitems](../src/SyntheticShared/SyntheticShared.projitems)
* [SyntheticTests.Shared.projitems](../tests/SyntheticTests.Shared/SyntheticTests.Shared.projitems)
* [GenericElementModelTests.cs](../tests/SyntheticTests.Logic/Modules/RevitSerialization/GenericElementModelTests.cs)
* [MockRevitAPI.cs](../tests/RevitAPIMock/MockRevitAPI.cs)

## 2026-06-13 - Architectural Refactoring: RevitDOM SRP Refactor & Legacy Cleanup (Issues 7 & Remediation 1-3)
**Reason for Change:** Completed the refactoring of all RevitDOM external handlers, event handlers, and view models to route database execution through the new centralized `DomTransactionEngine` under `TransactionGroup` with the `.Assimilate()` pattern, and destructively cleaned up all legacy execution logic, duplicate methods, and routing blocks from the POCO models and `ModelsToSerialize` to achieve a pure POCO architecture.
**Key Enhancements:**
* **Modeless Import Loop Refactoring:** Updated the modeless element processing loop in `JsonEditorExternalEventHandler` to run asynchronously on the Revit API thread under a `TransactionGroup` with the `.Assimilate()` pattern, handling partial commits gracefully and logging warnings to the Revit journal.
* **Non-Blocking Merge Handler:** Updated `ProcessMergeEventHandler` cluster transaction error handling to assign exceptions to `MergeExecutionReport.ErrorMessage` and write to the Revit journal, removing the blocking `TaskDialog` warnings.
* **POCO Model Execution Purge:** Destructively removed all legacy database execution methods (`ModifyElement`, `_ModifyProperties`, `ResolveAndRenameAliases`, `ModifyMaterial`, `CreateMaterial`, `ModifyCategory`, `ModifyView`, `CreateWallType`, `ModifyWallType`) from base, system, view, category, and hierarchy models (`ElementModel`, `MaterialModel`, `MissingModels`, `CategoryModel`, `ViewModel`, `HostObjTypeModel`), turning them into 100% pure state containers.
* **Legacy Router Demolition:** Deleted the obsolete static `ModifyElement` method and its large conditional type check routing blocks from `ModelsToSerialize.cs`.
**Files Modified:**
* [DomTransactionEngine.cs](../src/SyntheticShared/Modules/RevitDOM/DomTransactionEngine.cs)
* [ModelsToSerialize.cs](../src/SyntheticShared/Modules/RevitDOM/ModelsToSerialize.cs)
* [CategoryModel.cs](../src/SyntheticShared/Modules/RevitDOM/CategoryModel.cs)
* [ElementModel.cs](../src/SyntheticShared/Modules/RevitDOM/ElementModel.cs)
* [HostObjTypeModel.cs](../src/SyntheticShared/Modules/RevitDOM/HostObjTypeModel.cs)
* [MaterialModel.cs](../src/SyntheticShared/Modules/RevitDOM/MaterialModel.cs)
* [MissingModels.cs](../src/SyntheticShared/Modules/RevitDOM/MissingModels.cs)
* [ViewModel.cs](../src/SyntheticShared/Modules/RevitDOM/ViewModel.cs)
* [JsonEditorExternalEventHandler.cs](../src/SyntheticShared/Modules/StandardsManagement/Handlers/JsonEditorExternalEventHandler.cs)
* [EnforceStandardsViewModel.cs](../src/SyntheticShared/Modules/StandardsManagement/ViewModels/EnforceStandardsViewModel.cs)
* [ProcessMergeEventHandler.cs](../src/SyntheticShared/Modules/MergeDuplicates/Handlers/ProcessMergeEventHandler.cs)

## 2026-06-13 - Testing Infrastructure: NUnit Integration & ricaun Adapter Conflict Resolution
**Reason for Change:** Standardized the test suite execution by establishing NUnit unit testing project templates and resolving compiler/runtime conflicts with the `ricaun.RevitTest` adapter across multi-version assemblies (Revit 2023–2026).
**Key Enhancements:**
* **NUnit Test Projects Integration:** Created unified logic testing project `SyntheticTests.Logic` and shared Revit testing resources `SyntheticTests.Shared`.
* **Multi-Version Test Harnesses:** Created dedicated testing projects (`SyntheticTests2023`, `SyntheticTests2024`, `SyntheticTests2025`, `SyntheticTests2026`) aligned to target Revit versions, running NUnit tests within the Revit context.
* **Test Runner Orchestrator Upgrade:** Upgraded `test_executor.py` to automate building the solution, dynamically patching registry GUIDs for Revit test runner authentication, executing `dotnet test` suites, and compiling results into TRX summaries.
* **Smoke Testing Implementation:** Added `Tier1_LogicSmokeTests` and `Tier2_RevitSmokeTests` to verify initialization, serializations, and database transaction engines.
**Files Added/Relocated:**
* [SyntheticTests.Logic.csproj](../tests/SyntheticTests.Logic/SyntheticTests.Logic.csproj)
* [Tier1_LogicSmokeTests.cs](../tests/SyntheticTests.Logic/Tier1_LogicSmokeTests.cs)
* [SyntheticTests.Shared.projitems](../tests/SyntheticTests.Shared/SyntheticTests.Shared.projitems)
* [Tier2_RevitSmokeTests.cs](../tests/SyntheticTests.Shared/Tier2_RevitSmokeTests.cs)
* [SyntheticTests2023.csproj](../tests/SyntheticTests2023/SyntheticTests2023.csproj)
* [SyntheticTests2024.csproj](../tests/SyntheticTests2024/SyntheticTests2024.csproj)
* [SyntheticTests2025.csproj](../tests/SyntheticTests2025/SyntheticTests2025.csproj)
* [SyntheticTests2026.csproj](../tests/SyntheticTests2026/SyntheticTests2026.csproj)
**Files Modified:**
* [Synthetic.sln](../src/Synthetic.sln)
* [Synthetic2022.csproj](../src/Synthetic2022/Synthetic2022.csproj)
* [Synthetic2023.csproj](../src/Synthetic2023/Synthetic2023.csproj)
* [Synthetic2024.csproj](../src/Synthetic2024/Synthetic2024.csproj)
* [Synthetic2025.csproj](../src/Synthetic2025/Synthetic2025.csproj)
* [Synthetic2026.csproj](../src/Synthetic2026/Synthetic2026.csproj)
* [test_executor.py](../.agents/plugins/RevitQualityAssurance/scripts/test_executor.py)

## 2026-06-12 - Standards Editor: View Filter Deep Serialization, View Template Import Fixes, View Extents/Bounds Serialization & Shopping Cart Integration
**Reason for Change:** Resolved import failures for view templates and fill patterns, implemented full deep serialization for view filters (LogicalAnd, LogicalOr, and ElementParameterFilters) supporting all Revit target configurations, added missing view geometric extents and bounds (ViewRange, CropBox, depth clipping, underlay, and parts visibility) to ensure 100% template transfer fidelity, and integrated the selective export button and grouping in the WPF export shopping cart.
**Key Enhancements:**
* **View Filter Deep Serialization:** Created `FilterRuleModel` and recursively parsed native `ElementFilter` trees into it on export. Reconstructed filters recursively on import using version-safe conditional compilation blocks (`ParameterFilterRuleFactory` for Revit 2022/2023, and `ParameterValueProvider`/Evaluator classes for Revit 2024+).
* **View Extents & Bounds Serialization:** Created `PlanViewRangeModel` to serialize plan view offsets and level references recursively. Added full serialization/deserialization for view ranges, crop settings, far clip settings/offsets, underlays (base level and orientation via modern version-safe APIs), and parts visibility inside `ViewModel.cs` within isolated try-catch blocks to prevent template assignment crashes.
* **View Template & Fill Pattern Import Fixes:** Replaced invalid `baseView.Duplicate()` calls with `ElementTransformUtils.CopyElements` for view templates, used `CreateViewTemplate()` for faster duplication, implemented name-based matching for View Templates to update existing ones in place, shielded category override exceptions (`CanCategoryBeHidden`), and shielded prohibited system name assignments.
* **Export Shopping Cart Integration:** Integrated `AddParameterFiltersCommand` in the export cart and WPF view to selectively query and check/add filters. Aligned the cart group mapping string to `"Parameter Filters"` to match the JSON editor.
**Files Added:**
* [FilterRuleModel.cs](../src/SyntheticShared/Modules/RevitDOM/FilterRuleModel.cs)
* [ParameterFilterElementModel.cs](../src/SyntheticShared/Modules/RevitDOM/ParameterFilterElementModel.cs)
* [PlanViewRangeModel.cs](../src/SyntheticShared/Modules/RevitDOM/PlanViewRangeModel.cs)
* [ViewFilterOverrideModel.cs](../src/SyntheticShared/Modules/RevitDOM/ViewFilterOverrideModel.cs)
* [ViewTemplateModel.cs](../src/SyntheticShared/Modules/RevitDOM/ViewTemplateModel.cs)
**Files Modified:**
* [SyntheticShared.projitems](../src/SyntheticShared/SyntheticShared.projitems)
* [CategoryGraphicOverrideModel.cs](../src/SyntheticShared/Modules/RevitDOM/CategoryGraphicOverrideModel.cs)
* [MissingModels.cs](../src/SyntheticShared/Modules/RevitDOM/MissingModels.cs)
* [ModelsToSerialize.cs](../src/SyntheticShared/Modules/RevitDOM/ModelsToSerialize.cs)
* [ViewModel.cs](../src/SyntheticShared/Modules/RevitDOM/ViewModel.cs)
* [EnforceStandardsViewModel.cs](../src/SyntheticShared/Modules/StandardsManagement/ViewModels/EnforceStandardsViewModel.cs)
* [ExportStylesViewModel.cs](../src/SyntheticShared/Modules/StandardsManagement/ViewModels/ExportStylesViewModel.cs)
* [JsonEditorMainViewModel.cs](../src/SyntheticShared/Modules/StandardsManagement/ViewModels/JsonEditorMainViewModel.cs)
* [StandardsClassSelectionViewModel.cs](../src/SyntheticShared/Modules/StandardsManagement/ViewModels/StandardsClassSelectionViewModel.cs)
* [ExportStylesView.xaml](../src/SyntheticShared/Modules/StandardsManagement/Views/ExportStylesView.xaml)
* [StandardsClassSelectionControl.xaml](../src/SyntheticShared/Modules/StandardsManagement/Views/StandardsClassSelectionControl.xaml)

## 2026-06-12 - Standards Editor: Preselect Object Groups in Import Selected
**Reason for Change:** Implemented automated preselection of checkbox groups in the Enforce Standards dialog when using the "Import Selected" feature of the Standards Editor, including recursive scanning of nested sub-objects.
**Key Enhancements:**
* **Object Group Preselection:** Automatically checks the corresponding class, category, view, or system family checkbox groups inside the `StandardsClassSelectionViewModel` when importing selected styles.
* **Recursive Dependency Scanning:** Implemented a reflection-based object graph scanner `PreselectGroupsBasedOnStyles` inside `EnforceStandardsViewModel` that traverses parent models, compound structures, category models, and parameter sets to identify and check dependency checkboxes (e.g. automatically checking the Materials group when a Wall Type referencing materials is selected).
* **High Performance Optimization:** Restricted property reflection to namespaces starting with `"Synthetic"`, preventing overhead or exceptions from reflecting on Revit API objects or UI elements.
**Files Modified:**
* [EnforceStandardsViewModel.cs](../src/SyntheticShared/Modules/StandardsManagement/ViewModels/EnforceStandardsViewModel.cs)

## 2026-06-06 - Architectural Refactoring: Vertical Slicing, Namespace Realignment & Documentation Sync (Phase 4)
**Reason for Change:** Executed the final phases of the vertical slicing architectural refactoring to extract all subsystems into dedicated domain modules, aligned namespaces and XAML class attributes solution-wide, and synchronized the updated documentation system with Google Drive.
**Key Enhancements:**
* **Vertical Domain Modules:** Reorganized the codebase by migrating files from legacy horizontal folders (`Commands/`, `Handlers/`, `ViewModels/`, `Views/`, `Models/`, `Utilities/`) into dedicated vertical domain slices under `src/SyntheticShared/Modules/` (`MergeDuplicates`, `StandardsManagement`, `SettingsDashboard`, `Worksets`, `ViewManagement`) and core shared components under `src/SyntheticShared/Shared/`.
* **Namespace & XAML Realignment:** Systematically aligned C# namespaces and WPF XAML markup attributes (`x:Class`, XML namespaces for viewmodels, views, and controls) across the entire solution to resolve compilation dependencies.
* **Manifest Manifestations:** Updated the `.addin` manifest files for all target versions to point the application `FullClassName` to the new relocated entrypoint `Synthetic.Core.App`.
* **Obsolete Folder Cleanups:** Purged and deleted all empty legacy horizontal folders to complete the vertical slicing refactoring process.
* **Documentation Segmentation & Google Drive Sync:** Segmented the codebase documentation into specialized markdown targets (`Synthetic_src_Foundation.md`, `Synthetic_src_ModelManagement.md`, `Synthetic_src_ViewManagement.md`, `Synthetic_src_Operations.md`), updated the generation script `generate_docs.py` and `project_atlas.md`, and synchronized the new files with Google Drive, updating the permanent `drive_id`s in `sync_config.json`.
**Files Added/Relocated:**
* Files relocated to `src/SyntheticShared/Modules/` and `src/SyntheticShared/Shared/`
* [Synthetic_src_Foundation.md](Synthetic_src_Foundation.md)
* [Synthetic_src_ModelManagement.md](Synthetic_src_ModelManagement.md)
* [Synthetic_src_ViewManagement.md](Synthetic_src_ViewManagement.md)
* [Synthetic_src_Operations.md](Synthetic_src_Operations.md)
**Files Modified:**
* [SyntheticShared.projitems](../src/SyntheticShared/SyntheticShared.projitems)
* [project_atlas.md](project_atlas.md)
* [sync_config.json](../../Revit%20API%20Synthetic%20v2%20Support/auth/sync_config.json)
* [generate_docs.py](../.agents/plugins/RevitWorkspaceDevOps/scripts/generate_docs.py)
* Project `.csproj` and `.addin` files

## 2026-06-05 - Detail Item Factory: Settings Integration & Headless Command
**Reason for Change:** Implemented persistent user configuration and modal settings panel support for the Detail Item Factory, enabling headless execution test harnesses.
**Key Enhancements:**
* **Settings Persistence:** Added `DetailItemFactorySettings` integrated with Revit's Extensible Storage schema via `SettingsManager` to save default output folders per-document.
* **Dashboard Settings Panel:** Designed `DetailItemFactorySettingsView` and ViewModel, nesting them inside the Settings Dashboard.
* **Headless Command Integration:** Added `CmdDetailItemFactoryHeadless` to run conversions bypassing the settings UI while retaining modeless progress bar telemetry.
**Files Added:**
* [CmdDetailItemFactoryHeadless.cs](../src/SyntheticShared/Commands/CmdDetailItemFactoryHeadless.cs)
* [DetailItemFactorySettings.cs](../src/SyntheticShared/Settings/DetailItemFactorySettings.cs)
* [DetailItemFactorySettingsViewModel.cs](../src/SyntheticShared/ViewModels/DetailItemFactorySettingsViewModel.cs)
* [DetailItemFactorySettingsView.xaml](../src/SyntheticShared/Views/DetailItemFactorySettingsView.xaml)
* [DetailItemFactorySettingsView.xaml.cs](../src/SyntheticShared/Views/DetailItemFactorySettingsView.xaml.cs)
**Files Modified:**
* [Config.cs](../src/SyntheticShared/Settings/Config.cs)
* [SettingsManager.cs](../src/SyntheticShared/Settings/SettingsManager.cs)
* [SyntheticSettings.template.json](../src/SyntheticShared/Settings/SyntheticSettings.template.json)
* [SyntheticShared.projitems](../src/SyntheticShared/SyntheticShared.projitems)
* [DetailItemFactoryViewModel.cs](../src/SyntheticShared/ViewModels/DetailItemFactoryViewModel.cs)
* [SettingsDashboardViewModel.cs](../src/SyntheticShared/ViewModels/SettingsDashboardViewModel.cs)
* [SettingsDashboardWindow.xaml](../src/SyntheticShared/Views/SettingsDashboardWindow.xaml)

## 2026-06-05 - Build System: Multi-Version Platform Configurations & Deploy Targets
**Reason for Change:** Restructured MSBuild target configurations and deploy hooks to align AssemblyName naming conventions and automate binary deployment.
**Key Enhancements:**
* **Configuration-Aware Deploy Targets:** Implemented `DeployAddin` target tasks across all project files to automatically clean and copy manifest `.addin` and build outputs to Autodesk Addins folders.
* **SDK-Style Project Alignments:** Standardized AssemblyName naming, preprocessor constants (`REVIT2025`, `REVIT2026`), and solution configuration mappings.
**Files Modified:**
* [Synthetic2022.csproj](../src/Synthetic2022/Synthetic2022.csproj)
* [Synthetic2023.csproj](../src/Synthetic2023/Synthetic2023.csproj)
* [Synthetic2024.csproj](../src/Synthetic2024/Synthetic2024.csproj)
* [Synthetic2025.csproj](../src/Synthetic2025/Synthetic2025.csproj)
* [Synthetic2026.csproj](../src/Synthetic2026/Synthetic2026.csproj)
* [Synthetic.sln](../src/Synthetic.sln)

## 2026-06-05 - Feature Finalization: XML Documentation Sync & Project Atlas Mappings
**Reason for Change:** Executed the feature branch checkout gate, synchronizing compiled C# XML documentation, auditing compilation targets, and mapping missing commands inside the Project Atlas.
**Key Enhancements:**
* **XML API Documentation Sync:** Compiled and parsed triple-slash C# developer comments (`/// <summary>`) from the Shared project files and merged them into `SyntheticShared_API_Documentation.md`.
* **Project Atlas Synchronization:** Audited codebase registry and mapped the headless test command (`CmdDetailItemFactoryHeadless`) into `project_atlas.md`.
* **Version Matrix Verification:** Checked `.csproj` version configurations against the environment constant `CURRENT_LATEST_VERSION` (2026) to ensure target consistency before branch checkout.
**Files Modified:**
* [project_atlas.md](project_atlas.md)
* [SyntheticShared_API_Documentation.md](SyntheticShared_API_Documentation.md)

## 2026-06-04 - Codebase Refactoring: Null-Safety & Obsolete API Resolution
**Reason for Change:** Executed a comprehensive warning resolution sweep across all projects, enforcing null-safety rules, resolving obsolete API warnings, and enforcing x64 compilation.
**Key Enhancements:**
* **Nullability Sweep:** Resolved over 200+ compiler warnings (CS8600 null conversion, CS8601, CS8625 null reference assignment) across all shared commands, utilities, viewmodels, and models.
* **Obsolete API Modernization:** Replaced deprecated `ElementId(int)` constructor and `IntegerValue` property calls with `new ElementId((long)...)` conversions to align with Revit 2024+ API.
* **Global x64 Platform Enforcement:** Enforced `x64` processor architecture globally across all versioned project files to prevent architecture mismatch warnings.
* **Code Hygiene Refactoring:** Removed unused variables (CS0219/CS0168), resolved inherited member hiding warnings, and cleaned up redundant null checks.
**Files Modified:**
* [SyntheticShared.projitems](../src/SyntheticShared/SyntheticShared.projitems)
* Most files under `Commands/`, `Models/`, `Settings/`, `Utilities/`, `ViewModels/`, and `Views/`

## 2026-06-03 - Detail Item Factory: Geometry Resilience, Modeless UI & Smart Defaults
**Reason for Change:** Developed and iterated the production Detail Item Factory utility, transitioning it from a prototype into a resilient, modeless background converter tool.
**Key Enhancements:**
* **Modeless External Event Handlers:** Replaced blocking execution with an asynchronous `ExternalEvent` handler (`DetailItemFactoryEventHandler`) and progress bar UI (`DetailItemFactoryProgressView`) to process transactions without locking the main thread.
* **WPF MVVM Frontend & Per-Element Views:** Built the main configurations UI (`DetailItemFactoryView` and ViewModel) supporting per-element orientation selections (Front, Back, Plan) and smart defaults for Legend Components.
* **Results Dashboard & Purging:** Created a batch results view (`DetailItemFactoryResultsView` and ViewModel) displaying success and fail stats, with clean garbage collection to remove temporary `.dwg` and `.pcp` files.
* **Calibrated Origin Coordinate Alignment:** Adjusted the detail placement origin to align with the chosen element's bounding geometry boundaries.
* **Geometry Resilience & Spline Healer:** Added inner Try/Catch loop iterations, isolated nested subcomponents during DWG export, handled unbound curves (arcs/ellipses) using bifurcation, and implemented spline curve tessellation healing.
* **Telemetry & Diagnostic Logging:** Added diagnostic logging under `[AG2_ERROR_UNBOUND]` and `[AG2_ERROR_TRACE]` tags.
**Files Added:**
* [CmdDetailItemFactory.cs](../src/SyntheticShared/Commands/CmdDetailItemFactory.cs)
* [DetailItemFactoryEventHandler.cs](../src/SyntheticShared/Handlers/DetailItemFactoryEventHandler.cs)
* [DetailItemResultItem.cs](../src/SyntheticShared/Models/DetailItemResultItem.cs)
* [DetailItemFactoryResultsViewModel.cs](../src/SyntheticShared/ViewModels/DetailItemFactoryResultsViewModel.cs)
* [SelectedElementItemViewModel.cs](../src/SyntheticShared/ViewModels/SelectedElementItemViewModel.cs)
* [DetailItemFactoryProgressView.xaml](../src/SyntheticShared/Views/DetailItemFactoryProgressView.xaml)
* [DetailItemFactoryProgressView.xaml.cs](../src/SyntheticShared/Views/DetailItemFactoryProgressView.xaml.cs)
* [DetailItemFactoryResultsView.xaml](../src/SyntheticShared/Views/DetailItemFactoryResultsView.xaml)
* [DetailItemFactoryResultsView.xaml.cs](../src/SyntheticShared/Views/DetailItemFactoryResultsView.xaml.cs)
* [Detail Item.rft](../src/SyntheticShared/Assets/Templates/Detail Item.rft)
**Files Modified:**
* [SyntheticShared.projitems](../src/SyntheticShared/SyntheticShared.projitems)
* [SyntheticRibbon.cs](../src/SyntheticShared/UI/SyntheticRibbon.cs)
* [DetailItemFactoryViewModel.cs](../src/SyntheticShared/ViewModels/DetailItemFactoryViewModel.cs)
* [DetailItemFactoryView.xaml](../src/SyntheticShared/Views/DetailItemFactoryView.xaml)
* [DetailItemFactoryView.xaml.cs](../src/SyntheticShared/Views/DetailItemFactoryView.xaml.cs)

## 2026-06-01 - Merge Duplicates Feature Workflow: Dynamic Type Mapping & Migration Renaming
**Reason for Change:** Implemented dynamic type mapping and migration renaming for the "Merge Duplicates" workflow across Revit element types (Families, Groups, Assemblies), providing safe transaction execution, real-time input validation, and extended redundancy purging.
**Key Enhancements:**
* **Dynamic Type Mapping Matrix:** Created the Type Mapping UI inside `MergeDetailedReviewWindow`, dynamically swapping inputs (ComboBox, TextBlock, TextBox) based on user-selected recommended actions (Merge, Migrate, Exclude), showing all parameters, and resolving ElementId parameter references to human-readable names.
* **Migration Renaming with Character Validation:** Enabled customizable renaming for "Migrate" actions, enforcing real-time WPF validation on illegal Revit naming characters (`\ : { } [ ] | ; < > ? ' ~`) and showing tooltips to block invalid entries.
* **Robust Duplication Event Handlers:** Refactored `ProcessMergeEventHandler` to perform name sanitization and execute duplication within a safe try/catch retry loop, appending `_Migrated` or sequential counters `_Migrated_N` (up to 10 attempts) to prevent naming collisions.
* **Expanded Redundancy Deletion:** Upgraded individual type purging for non-family elements (GroupType, AssemblyType) in the transaction event pipeline when entire duplicate families cannot be deleted.
**Files Modified:**
* [TypeMappingModel.cs](../src/SyntheticShared/Models/TypeMappingModel.cs)
* [MergeAnalysisEngine.cs](../src/SyntheticShared/Utilities/MergeAnalysisEngine.cs)
* [MergeDetailedReviewViewModel.cs](../src/SyntheticShared/ViewModels/MergeDetailedReviewViewModel.cs)
* [MergeDuplicatesViewModel.cs](../src/SyntheticShared/ViewModels/MergeDuplicatesViewModel.cs)
* [MergeDetailedReviewWindow.xaml](../src/SyntheticShared/Views/MergeDetailedReviewWindow.xaml)
* [MergeDetailedReviewWindow.xaml.cs](../src/SyntheticShared/Views/MergeDetailedReviewWindow.xaml.cs)
* [ProcessMergeEventHandler.cs](../src/SyntheticShared/Handlers/ProcessMergeEventHandler.cs)

## 2026-06-01 - Enforce Standards Feature Workflow: Deep-Scan, Visual Review & Governed Integration
**Reason for Change:** Implemented the complete "Enforce Standards" workflow (Phases 5-7) enabling deep-scan parameter comparisons, selective standards execution, interactive overrides review, and centralised settings persistence inside Revit documents.
**Key Enhancements:**
* **Centralized Settings Persistence:** Implemented `StandardsSettings` and integrated it with Revit's Extensible Storage schema via `SettingsManager` to persist standards paths per-document.
* **Granular Class Filter Selection:** Added `StandardsClassSelectionControl` and `StandardsClassSelectionViewModel` to allow selective standard classes filtering (annotations, materials, system types, views, templates) with automatic document context checks (e.g. disabling system family and view tabs inside family documents).
* **High-Fidelity Standards Diff Engine:** Built `StandardsDiffEngine` to run deep scans comparing in-memory/JSON standard element definitions against live Revit elements, generating structured conflict clusters (`DuplicateClusterModel`) while safely skipping system families in family documents.
* **Interactive Reviews Window:** Created `StandardsReviewWindow` and `StandardsReviewViewModel` to visually display conflicts, select specific overrides to apply/skip, execute final transactions immediately, and log QA journal directives.
* **Master Integration Dialog:** Renamed the legacy import view/viewmodel to `EnforceStandardsView` and `EnforceStandardsViewModel`, integrating class-selection tabs, silent execution pathways, deep-scan analysis hooks, and background progress window workflows.
* **Refactoring & Code Hygiene:** Removed all obsolete generic import/export commands (`ElementTypeImportJson`/`ElementTypeExportJson`), cleaned up dead code, sorted using directives, and audited logging output to ensure only authorized journal hooks are printed to the console.
**Files Added/Renamed:**
* [StandardsSettings.cs](../src/SyntheticShared/Settings/StandardsSettings.cs)
* [StandardsDiffEngine.cs](../src/SyntheticShared/Utilities/StandardsDiffEngine.cs)
* [StandardsClassSelectionViewModel.cs](../src/SyntheticShared/ViewModels/StandardsClassSelectionViewModel.cs)
* [StandardsClassSelectionControl.xaml](../src/SyntheticShared/Views/StandardsClassSelectionControl.xaml)
* [StandardsClassSelectionControl.xaml.cs](../src/SyntheticShared/Views/StandardsClassSelectionControl.xaml.cs)
* [StandardsReviewViewModel.cs](../src/SyntheticShared/ViewModels/StandardsReviewViewModel.cs)
* [StandardsReviewWindow.xaml](../src/SyntheticShared/Views/StandardsReviewWindow.xaml)
* [StandardsReviewWindow.xaml.cs](../src/SyntheticShared/Views/StandardsReviewWindow.xaml.cs)
* [EnforceStandardsViewModel.cs](../src/SyntheticShared/ViewModels/EnforceStandardsViewModel.cs)
* [EnforceStandardsView.xaml](../src/SyntheticShared/Views/EnforceStandardsView.xaml)
* [EnforceStandardsView.xaml.cs](../src/SyntheticShared/Views/EnforceStandardsView.xaml.cs)
* [CmdEnforceStandards.cs](../src/SyntheticShared/Commands/CmdEnforceStandards.cs)
* [CmdExportStandards.cs](../src/SyntheticShared/Commands/CmdExportStandards.cs)
**Files Modified:**
* [SyntheticShared.projitems](../src/SyntheticShared/SyntheticShared.projitems)
* [SyntheticRibbon.cs](../src/SyntheticShared/UI/SyntheticRibbon.cs)
* [JsonEditorExternalEventHandler.cs](../src/SyntheticShared/Handlers/JsonEditorExternalEventHandler.cs)

## 2026-05-31 - Feature Finalization: Nuanced Category Control & Import Safety Guards
**Reason for Change:** Finalized the Nuanced Category Control feature and implemented critical architectural import safety guards to ensure robust category synchronization and prevent native exceptions.
**Key Enhancements:**
* **Category Hierarchy Isolation:** Refactored `CategoryModel.ModifyCategory` to perform a strict hierarchical subcategory search/create flow (resolving the parent category context first) to prevent global name clashes and incorrect overwriting.
* **Cross-Document ID Pollution Safeguards:** Integrated a Type-Matching Guard inside `ElementIdModel.GetElem` ensuring that integer IDs resolved from import files match the expected C# class type, preventing ID pollution.
* **Line Weight Boundary Enforcement:** Enforced strict Revit line weight bounds (between 1 and 16) inside the import pipeline to prevent API exceptions.
* **Uncuttable Category Support:** Wrapped cut properties (line weights and pattern assignments) in `IsCuttable` checks and Try/Catch blocks within `CategoryModel.ModifyCategory` and the constructors to prevent native exceptions on uncuttable/2D categories.
* **Refined Solid Line Pattern Handling:** Migrated all Solid line pattern references across serialization bridges (`OverrideGraphicSettingsModel` and `CategoryModel`) from magic numbers (`-1`, `0`) to Revit's native `LinePatternElement.GetSolidPatternId()`.
* **Built-in Element ID Preservation:** Configured `ElementIdModel.ShouldSerializeId` to preserve negative values (system/built-in elements) in exported templates while excluding temporary positive user IDs.
* **Import Loop Isolation:** Refactored `ImportStylesViewModel.ExecuteImport` to isolate category imports from standard assembly reflection checks, solving execution crashes on system classes like Toposolid.
**Files Modified:**
* [CategoryModel.cs](../src/SyntheticShared/Models/CategoryModel.cs)
* [ElementIdModel.cs](../src/SyntheticShared/Models/ElementIdModel.cs)
* [OverrideGraphicSettingsModel.cs](../src/SyntheticShared/Models/OverrideGraphicSettingsModel.cs)
* [ImportStylesViewModel.cs](../src/SyntheticShared/ViewModels/ImportStylesViewModel.cs)

## 2026-05-30 - Revit Journal Automator Diagnostics & Path Mapping Wizard
**Reason for Change:** Added diagnostic logging capabilities and refined user settings wizards for configuring folder mapping, absolute-to-relative document paths, and standardizing Excel interop.
**Key Enhancements:**
* **Journal Automator Diagnostics:** Created a reusable Revit journal tailing/searching utility to trace real-time API journal outputs.
* **Network Path Wizard:** Implemented a new setup wizard (`NetworkPathsWizardWindow` & `NetworkPathsWizardViewModel`) for visual folder mapping configuration with background path verification.
* **Project Excel Integration:** Replaced COM-based Excel interop with standard NuGet package references for increased stability across multi-version assemblies.
* **Documentation Sync:** Compiled C# XML documentation and transformed absolute links into relative format in `SyntheticShared_API_Documentation.md`.
**Files Added:**
* [NetworkPathsWizardWindow.xaml](../src/SyntheticShared/Views/NetworkPathsWizardWindow.xaml)
* [NetworkPathsWizardWindow.xaml.cs](../src/SyntheticShared/Views/NetworkPathsWizardWindow.xaml.cs)
* [NetworkPathsWizardViewModel.cs](../src/SyntheticShared/ViewModels/NetworkPathsWizardViewModel.cs)
* [PathMappingViewModel.cs](../src/SyntheticShared/ViewModels/PathMappingViewModel.cs)
**Files Modified:**
* [SyntheticRibbon.cs](../src/SyntheticShared/UI/SyntheticRibbon.cs)
* [SyntheticShared.projitems](../src/SyntheticShared/SyntheticShared.projitems)

## 2026-05-29 - Extensible Storage Settings Refactor & Settings Dashboard
**Reason for Change:** Refactored settings architecture to store granular parameters within Revit's Extensible Storage schema, replacing legacy text/config files and introducing a unified settings dashboard for tracking configuration drift.
**Key Enhancements:**
* **Granular Extensible Storage:** Created specialized C# classes for Settings modules (`SyncSettings`, `FileUtilitySettings`, `MaterialLibrarySettings`, `ProjectMaterialSettings`) backed by JSON schemas in `SettingsManager`.
* **WPF Settings Dashboard:** Developed `SettingsDashboardWindow` with two-way bindings to observe active workspace paths, project overrides, and external config synchronization.
* **Sync & Drift Resolution Wizards:** Built `SyncWizardWindow` and `SyncResolutionWindow` to identify configuration drift between Revit project storage and external files, allowing manual overrides.
**Files Added:**
* [SettingsManager.cs](../src/SyntheticShared/Settings/SettingsManager.cs)
* [SyntheticSettingsJsonSchema.cs](../src/SyntheticShared/Settings/SyntheticSettingsJsonSchema.cs)
* [LegacySettingsMigration.cs](../src/SyntheticShared/Settings/LegacySettingsMigration.cs)
* [MaterialLibrarySettings.cs](../src/SyntheticShared/Settings/MaterialLibrarySettings.cs)
* [ProjectMaterialSettings.cs](../src/SyntheticShared/Settings/ProjectMaterialSettings.cs)
* [SyncSettings.cs](../src/SyntheticShared/Settings/SyncSettings.cs)
* [FileUtilitySettings.cs](../src/SyntheticShared/Settings/FileUtilitySettings.cs)
* [SettingsDashboardWindow.xaml](../src/SyntheticShared/Views/SettingsDashboardWindow.xaml)
* [SettingsDashboardWindow.xaml.cs](../src/SyntheticShared/Views/SettingsDashboardWindow.xaml.cs)
* [SettingsDashboardViewModel.cs](../src/SyntheticShared/ViewModels/SettingsDashboardViewModel.cs)
* [SyncWizardWindow.xaml](../src/SyntheticShared/Views/SyncWizardWindow.xaml)
* [SyncWizardWindow.xaml.cs](../src/SyntheticShared/Views/SyncWizardWindow.xaml.cs)
* [SyncSettingsViewModel.cs](../src/SyntheticShared/ViewModels/SyncSettingsViewModel.cs)
* [SyncResolutionWindow.xaml](../src/SyntheticShared/Views/SyncResolutionWindow.xaml)
* [SyncResolutionWindow.xaml.cs](../src/SyntheticShared/Views/SyncResolutionWindow.xaml.cs)
* [SyncToastNotification.xaml](../src/SyntheticShared/Views/SyncToastNotification.xaml)
**Files Modified:**
* [SyntheticShared.projitems](../src/SyntheticShared/SyntheticShared.projitems)
* [SettingsDashboardCommand.cs](../src/SyntheticShared/Commands/SettingsDashboardCommand.cs)

## 2026-05-26 - Viewport Swap & Reference Plane Group-Swapping
**Reason for Change:** Enhanced sheet view utilities with transaction handlers for viewport sheet swapping and group-level reference plane swaps to streamline sheet assembly maintenance.
**Key Enhancements:**
* **Viewport Replacement Transaction:** Added a clean sub-transaction pipeline inside `LegendsUtil` to replace viewport references on active sheets during detail conversions.
* **Reference Plane Swapping:** Integrated group-swapping logic for Reference Planes that suppresses Revit constraint alerts.
* **XML Documentation Compilation:** Generated complete C# XML comments across all classes in `SyntheticShared`, including detailed specs for View Selection and Convert to Drafting commands.
**Files Modified:**
* [LegendsUtil.cs](../src/SyntheticShared/Utilities/LegendsUtil.cs)
* [ConvertDraftingToLegend.cs](../src/SyntheticShared/Commands/ConvertDraftingToLegend.cs)
* [ConvertLegendToDrafting.cs](../src/SyntheticShared/Commands/ConvertLegendToDrafting.cs)

## 2026-05-25 - Merge Duplicates: Instance Swapping, Triage Queue & Grouping
**Reason for Change:** Designed and implemented the modeless workspace for merging duplicate families, groups, and assemblies, integrating a safe transactional execution engine to redirect element instances, inject parameters, and delete redundant duplicates.
**Key Enhancements:**
* **Modeless Triage Workspace:** Built `MergeDuplicatesWindow` enabling Revit users to scan the active document for duplicate elements, exclude specific elements, and queue groups/types for merging.
* **Detailed Review & Conflict UI:** Created `MergeDetailedReviewWindow` featuring split-view parameter diff rows, primary/secondary picker selectors, and radio button resolution options.
* **Transactional Swapping Engine:** Developed `ProcessMergeEventHandler` running on the Revit main execution thread. The workflow loads parameter mappings, swaps instance types, updates dependent groups/assemblies, and purges redundant duplicates.
* **Gatekeeper Schema Guard:** Added validation checks to detect parameter schema conflicts before executing swaps, preventing silent data losses.
* **Parameter Filtering:** Excluded read-only identity parameters from similarity calculations to prevent false duplicates.
**Files Added:**
* [MergeDuplicatesWindow.xaml](../src/SyntheticShared/Views/MergeDuplicatesWindow.xaml)
* [MergeDuplicatesWindow.xaml.cs](../src/SyntheticShared/Views/MergeDuplicatesWindow.xaml.cs)
* [MergeDuplicatesViewModel.cs](../src/SyntheticShared/ViewModels/MergeDuplicatesViewModel.cs)
* [MergeDetailedReviewWindow.xaml](../src/SyntheticShared/Views/MergeDetailedReviewWindow.xaml)
* [MergeDetailedReviewWindow.xaml.cs](../src/SyntheticShared/Views/MergeDetailedReviewWindow.xaml.cs)
* [MergeDetailedReviewViewModel.cs](../src/SyntheticShared/ViewModels/MergeDetailedReviewViewModel.cs)
* [ProcessMergeEventHandler.cs](../src/SyntheticShared/Handlers/ProcessMergeEventHandler.cs)
* [DuplicateClusterModel.cs](../src/SyntheticShared/Models/DuplicateClusterModel.cs)
* [ParameterDiffRowModel.cs](../src/SyntheticShared/Models/ParameterDiffRowModel.cs)
**Files Modified:**
* [SyntheticRibbon.cs](../src/SyntheticShared/UI/SyntheticRibbon.cs)
* [SyntheticShared.projitems](../src/SyntheticShared/SyntheticShared.projitems)

## 2026-05-23 - Modeless JSON Standards Editor: Dependency Sorting, Nested Editor, & Shopping Cart
**Reason for Change:** Upgraded the JSON Standards Editor to process complex relational elements (Materials, Styles, Categories) via transactional event handlers, adding nested value editors and detailed schema export carts.
**Key Enhancements:**
* **Relational Dependency Harvesting:** Implemented dependency sorting logic to automatically resolve and sort dependencies (e.g. nested subcategories, materials, fill patterns) in strict hierarchical order during import/export.
* **Nested Data Editor Dialog:** Created a sub-editor window (`NestedDataEditorWindow`) to manage compound structure layers and child subcategories using two-way bindings.
* **Export Shopping Cart UI:** Integrated class-level and category-level selection queues (shopping carts) into `ExportStylesViewModel` allowing granular selection of elements for standards packaging.
* **Consolidation & Aliases:** Added a "Merge Elements" dialogue and an editable comma-separated aliases field in the WPF grid to group legacy styles.
* **Modeless External Event Handlers:** Added `JsonEditorExternalEventHandler` to execute style updates safely on the Revit execution context.
**Files Added:**
* [NestedDataEditorWindow.xaml](../src/SyntheticShared/Views/NestedDataEditorWindow.xaml)
* [NestedDataEditorWindow.xaml.cs](../src/SyntheticShared/Views/NestedDataEditorWindow.xaml.cs)
* [NestedDataEditorViewModel.cs](../src/SyntheticShared/ViewModels/NestedDataEditorViewModel.cs)
* [JsonEditorExternalEventHandler.cs](../src/SyntheticShared/Handlers/JsonEditorExternalEventHandler.cs)
**Files Modified:**
* [ExportStylesViewModel.cs](../src/SyntheticShared/ViewModels/ExportStylesViewModel.cs)
* [JsonEditorWindow.xaml](../src/SyntheticShared/Views/JsonEditorWindow.xaml)

## 2026-05-22 - Modeless JSON Editor Shell & UI Modernization
**Reason for Change:** Standardized the addon's user interface by migrating legacy WinForms dialogs to WPF, implementing a modern modeless JSON Standards Editor to manage styling schemas.
**Key Enhancements:**
* **WinForms to WPF Migration:** Completely replaced all WinForms UI components with unified WPF windows and user controls, establishing standard styles and parenting handles under `RevitWindowHelper`.
* **Modeless JSON Standards Editor:** Designed `JsonEditorWindow` featuring grouped tree views, listbox selections with multi-select support, and `<Varies>` property values indicators.
* **Find & Replace Module:** Implemented name search and text replacement across JSON nodes with case/scope toggles.
* **POCO Serialization Models:** Created POCO models mapping Revit category properties, fill patterns, dimension settings, and text types for clean JSON schema output.
* **Auto-Tagger Conflict Resolution:** Integrated a pre-flight window to resolve conflicts on setting auto-tag coordinates.
**Files Added:**
* [JsonEditorWindow.xaml](../src/SyntheticShared/Views/JsonEditorWindow.xaml)
* [JsonEditorWindow.xaml.cs](../src/SyntheticShared/Views/JsonEditorWindow.xaml.cs)
* [JsonEditorMainViewModel.cs](../src/SyntheticShared/ViewModels/JsonEditorMainViewModel.cs)
* [ModelsToSerialize.cs](../src/SyntheticShared/Models/ModelsToSerialize.cs)
* [MissingModels.cs](../src/SyntheticShared/Models/MissingModels.cs)
**Files Modified:**
* [SyntheticRibbon.cs](../src/SyntheticShared/UI/SyntheticRibbon.cs)
* [SyntheticShared.projitems](../src/SyntheticShared/SyntheticShared.projitems)

## 2026-05-20 - Universal Auto-Tagger: Phase 5 - Batch Tag Execution
**Reason for Change:** Implemented the final execution engine for the Universal Auto-Tagger. The tool now automates tagging for pre-selected elements or entire active views using hierarchical template matching (Type > Family > Category) inside a fail-safe iteration loop.
**Files Added:**
* [CmdBatchTag.cs](../src/SyntheticShared/Commands/CmdBatchTag.cs)
**Files Modified:**
* [SyntheticShared.projitems](../src/SyntheticShared/SyntheticShared.projitems)
* [SyntheticRibbon.cs](../src/SyntheticShared/UI/SyntheticRibbon.cs)

## 2026-05-20 - Universal Auto-Tagger: Phase 4 - Manage Templates
**Reason for Change:** Implemented the central control panel for the Universal Auto-Tagger, including CRUD operations, JSON import/export syncing, and a context-switching loop for updating coordinate locations on the Revit canvas.
**Files Added:**
* [ManageTemplatesViewModel.cs](../src/SyntheticShared/ViewModels/ManageTemplatesViewModel.cs)
* [ManageTemplatesView.xaml](../src/SyntheticShared/Views/ManageTemplatesView.xaml)
* [ManageTemplatesView.xaml.cs](../src/SyntheticShared/Views/ManageTemplatesView.xaml.cs)
* [CmdManageTemplates.cs](../src/SyntheticShared/Commands/CmdManageTemplates.cs)
**Files Modified:**
* [TemplateStorageRepository.cs](../src/SyntheticShared/Repositories/TemplateStorageRepository.cs)
* [SyntheticShared.projitems](../src/SyntheticShared/SyntheticShared.projitems)
* [SyntheticRibbon.cs](../src/SyntheticShared/UI/SyntheticRibbon.cs)

## 2026-05-20 - Universal Auto-Tagger: Phase 3 - UI Dialog & Tag Template Capture
**Reason for Change:** Implemented the user interface dialog and selection filters to capture and store tag placement offset templates.
**Files Added:**
* [SetTemplateViewModel.cs](../src/SyntheticShared/ViewModels/SetTemplateViewModel.cs)
* [SetTemplateView.xaml](../src/SyntheticShared/Views/SetTemplateView.xaml)
* [SetTemplateView.xaml.cs](../src/SyntheticShared/Views/SetTemplateView.xaml.cs)
* [CmdSetTemplate.cs](../src/SyntheticShared/Commands/CmdSetTemplate.cs)
**Files Modified:**
* [SyntheticShared.projitems](../src/SyntheticShared/SyntheticShared.projitems)
* [SyntheticRibbon.cs](../src/SyntheticShared/UI/SyntheticRibbon.cs)

## 2026-05-20 - Universal Auto-Tagger: Phase 1 & 2 - MVVM Infrastructure, Data Persistence & Geometry Core
**Reason for Change:** Established the foundational shared MVVM infrastructure, window focus helpers, local/world transformation mappings, and Extensible Storage repositories for the Universal Auto-Tagger.
**Key Enhancements:**
* **MVVM Infrastructure:** Created core classes to handle bindings and injected legacy support dependencies across compilation versions.
* **Coordinate Transformation:** Designed transformations to align tag coordinates consistently relative to their target elements.
* **Extensible Storage:** Developed template storage database logic inside active `.rvt` project files.
**Files Added:**
* [ViewModelBase.cs](../src/SyntheticShared/ViewModels/ViewModelBase.cs)
* [RelayCommand.cs](../src/SyntheticShared/ViewModels/RelayCommand.cs)
* [RevitWindowHelper.cs](../src/SyntheticShared/UI/RevitWindowHelper.cs)
* [TagTemplate.cs](../src/SyntheticShared/Models/TagTemplate.cs)
* [CoordinateUtility.cs](../src/SyntheticShared/Utilities/CoordinateUtility.cs)
* [TemplateStorageRepository.cs](../src/SyntheticShared/Repositories/TemplateStorageRepository.cs)
**Files Modified:**
* [SyntheticShared.projitems](../src/SyntheticShared/SyntheticShared.projitems)
* [Synthetic2022.csproj](../src/Synthetic2022/Synthetic2022.csproj)
* [Synthetic2023.csproj](../src/Synthetic2023/Synthetic2023.csproj)
* [Synthetic2024.csproj](../src/Synthetic2024/Synthetic2024.csproj)

## 2026-05-20 - Scope Box Relocation Utility
**Reason for Change:** Implemented a new setup utility command to automate relocations of scope box elements within complex projects.
**Key Enhancements:**
* **Scope Box Workset Migration:** Created `ScopeBoxesMoveToWorkset` Revit External Command to mass transfer scope boxes to a chosen workset.
**Files Added:**
* [ScopeBoxesMoveToWorkset.cs](../src/SyntheticShared/Commands/ScopeBoxesMoveToWorkset.cs)

## 2025-05-09 - Elements on Workset Database Recording
**Reason for Change:** Added recovery utilities to export and restore element-to-workset associations, preventing loss of workset grouping metadata.
**Key Enhancements:**
* **Workset Data Recovery:** Added commands to record element-to-workset mappings to an external file and reload them onto active models.
**Files Added:**
* [ElementsOnWorksetRecord.cs](../src/SyntheticShared/Commands/ElementsOnWorksetRecord.cs)
* [ElementsOnWorksetReload.cs](../src/SyntheticShared/Commands/ElementsOnWorksetReload.cs)
**Files Modified:**
* [ElementsOnWorkset.cs](../src/SyntheticShared/Models/ElementsOnWorkset.cs)

## 2025-04-26 - Material Asset Transmit & Render Path Management
**Reason for Change:** Provided tools to package rendering images and bulk-repath material rendering asset links to resolve missing textures in centralized material libraries.
**Key Enhancements:**
* **Material Asset Repathing:** Mass-repaths render assets of materials from specified search directories.
* **Asset Packaging:** Gathers and packages render images mapped inside materials to an export folder.
**Files Added:**
* [MaterialsRepathAll.cs](../src/SyntheticShared/Commands/MaterialsRepathAll.cs)
* [MaterialImagesPackage.cs](../src/SyntheticShared/Commands/MaterialImagesPackage.cs)

## 2025-04-22 - Audit and Purge Schema Revisions
**Reason for Change:** Implemented background family auditing, warning logging, and extensible storage schema purging to keep loaded Revit family sizes down.
**Key Enhancements:**
* **Family Audit Loop:** Iteratively opens, logs exceptions, purges schemas, and closes all family components.
**Files Added:**
* [AuditPurgeAllFamilies.cs](../src/SyntheticShared/Commands/AuditPurgeAllFamilies.cs)

## 2025-02-11 - Paint Elements Utility
**Reason for Change:** Automates face painting of select geometries with a set material.
**Key Enhancements:**
* **Paint Elements Command:** Mass-paints selected face geometry.
**Files Added:**
* [PaintElements.cs](../src/SyntheticShared/Commands/PaintElements.cs)

## 2025-02-01 - Filled Region Type & Fill Pattern Serialization
**Reason for Change:** Extended standards serialization models to support Filled Region types and Fill Pattern models.
**Key Enhancements:**
* **Filled Region & Pattern Models:** Added C# wrappers for `FilledRegionTypeModel` and `FillPatternModel` for standards parsing.
**Files Added:**
* [FilledRegionTypeModel.cs](../src/SyntheticShared/Models/FilledRegionTypeModel.cs)
* [FillPatternModel.cs](../src/SyntheticShared/Models/FillPatternModel.cs)

## 2025-01-17 - Workset Import, Settings & Config System
**Reason for Change:** Created Excel-to-Revit Workset importer and synchronized project start view settings.
**Key Enhancements:**
* **Excel Workset Importer:** Automates new workset generation using definitions stored in Excel.
* **Config Models:** Created generic settings classes (`Config.cs`, `ConfigCollection.cs`).
**Files Added:**
* [WorksetsImport.cs](../src/SyntheticShared/Commands/WorksetsImport.cs)
* [WorksetSetFile.cs](../src/SyntheticShared/Commands/WorksetSetFile.cs)
* [WorksetSettingsShow.cs](../src/SyntheticShared/Commands/WorksetSettingsShow.cs)
* [WorksetStartView.cs](../src/SyntheticShared/Commands/WorksetStartView.cs)
* [Config.cs](../src/SyntheticShared/Settings/Config.cs)
* [ConfigCollection.cs](../src/SyntheticShared/Settings/ConfigCollection.cs)

## 2024-11-24 - Printing and View/Legend Converters
**Reason for Change:** Batch printer automation across multiple documents, and details transferring between Legends and Drafting Views.
**Key Enhancements:**
* **Multi-Doc Batch Printing:** Automated sheet prints across multiple models.
* **Legend/Drafting View Converters:** Batch transfers details and symbols between Drafting Views and Legends.
**Files Added:**
* [PrintBatchMultiDoc.cs](../src/SyntheticShared/Commands/PrintBatchMultiDoc.cs)
* [ConvertLegendToDrafting.cs](../src/SyntheticShared/Commands/ConvertLegendToDrafting.cs)
* [ConvertDraftingToLegend.cs](../src/SyntheticShared/Commands/ConvertDraftingToLegend.cs)

## 2024-11-09 - View Auto-Numbering & Renaming Utilities
**Reason for Change:** Automated renaming and numbering views placed on sheets.
**Key Enhancements:**
* **Views Auto-Numbering:** Automatically renames and numbers views placed on sheets.
**Files Added:**
* [ViewsAutoNumber.cs](../src/SyntheticShared/Commands/ViewsAutoNumber.cs)
* [ViewAutoNumberConfig.cs](../src/SyntheticShared/Commands/ViewAutoNumberConfig.cs)

## 2024-10-25 - Initial Add-in Setup
**Reason for Change:** Setup basic Revit external application, ribbon menu, and initial project structure.
**Files Added:**
* [App.cs](../src/SyntheticShared/App.cs)
* [SyntheticRibbon.cs](../src/SyntheticShared/UI/SyntheticRibbon.cs)
