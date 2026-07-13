# Project Atlas: Synthetic Revit Addon

> Last updated: 2026-07-13

This document serves as the high-level map of the **Synthetic Revit Addon** codebase, detailing the folder structure, multi-version configuration, active Revit API namespaces, core class architecture, and the registry of existing commands.

---

## 1. High-Level Folder Structure

The repository is structured to support multi-version compilation for Revit 2022 through 2026. The core business logic, view models, views, and utilities are housed within the shared project, while version-specific project folders compile targeting specific Revit API DLL versions and runtime configurations.

```
Revit API Synthetic v2/
├── .agents/                        # Antigravity agent configuration
│   ├── agent-settings.json          # Agent profile and core directives
│   ├── workflows.json               # Custom workflow triggers (/finalize, /sync)
│   ├── rules/                       # Behavioural guardrail rule files
│   │   ├── atlas_policy.md          # Governs when/how the atlas may be modified
│   │   └── revit_api_rules.md       # Multi-version API deprecation matrix
│   └── plugins/                     # Agent plugins
│       ├── RevitArchitectureGuard/  # Enforces transaction batching & storage patterns
│       ├── RevitQualityAssurance/   # NUnit & ricaun.RevitTest test automation & telemetry
│       │   └── scripts/             # test_executor.py (executes tests and parses TRX results)
│       ├── RevitUIRibbonManager/   # Ribbon JSON schema guardrail
│       └── RevitWorkspaceDevOps/   # Version matrix, doc generation & Drive sync
│           ├── requirements.txt     # Python deps for Drive sync (pip install)
│           └── scripts/             # aggregate.py, drive_sync.py, sync_all.py, generate_docs.py
├── docs/                           # Documentation folder
│   ├── project_atlas.md             # This file (high-level codebase map)
│   ├── change_log.md               # Development phase change logs
│   ├── SyntheticShared_API_Documentation.md  # Compiled C# API documentation
│   ├── Synthetic_UI_Theme_Specification.md   # UI Theme Specification & Design System tokens
│   └── source_code/                 # Aggregated bundle source code directory
│       ├── Synthetic_agents.md      # Aggregated bundle: .agents config
│       ├── Synthetic_src_Foundation.md  # Aggregated bundle: Core, Infrastructure, Settings, Shared
│       ├── Synthetic_src_ModelManagement.md  # Aggregated bundle: model-domain modules
│       ├── Synthetic_src_ViewManagement.md   # Aggregated bundle: view-domain modules
│       ├── Synthetic_src_Operations.md  # Aggregated bundle: operations modules
│       └── Synthetic_tests.md       # Aggregated bundle: tests
├── src/                            # Source code folder
│   ├── Synthetic.sln               # Visual Studio Solution compiling all versions
│   ├── SyntheticShared/            # Shared compilation project (core codebase)
│   │   ├── Assets/                 # Ribbon icon PNG and template assets
│   │   ├── Core/                   # Core lifecycle, entry points, and Ribbon creation (App.cs, SyntheticRibbon.cs)
│   │   ├── Infrastructure/         # Cross-cutting system helpers (IO, Persistence, Serialization)
│   │   ├── Modules/                # Cohesive domain-specific modules (AutoTagger, DetailItemFactory, MergeDuplicates, StandardsManagement, SettingsDashboard, Worksets, ViewManagement, MaterialManagement, BatchPrint, FamilyManagement, RevitDOM)
│   │   ├── Settings/               # Common settings structures (Config.cs, ConfigCollection.cs, etc.)
│   │   └── Shared/                 # Reusable domain-neutral logic (UI converters, base viewmodels, Revit API helpers)
│   ├── Synthetic2022/              # Revit 2022 specific project (.NET Framework 4.8)
│   ├── Synthetic2023/              # Revit 2023 specific project (.NET Framework 4.8)
│   ├── Synthetic2024/              # Revit 2024 specific project (.NET Framework 4.8)
│   ├── Synthetic2025/              # Revit 2025 specific project (.NET 8.0-windows)
│   └── Synthetic2026/              # Revit 2026 specific project (.NET 8.0-windows)
├── build/                          # Build configurations and deployment scripts
└── tests/                          # NUnit unit and ricaun.RevitTest integration test projects
```

---

## 2. Multi-Version Compatibility & Compilation

* **Target Frameworks:**
  * **Revit 2022 – 2024:** Targets `.NET Framework 4.8` (Legacy MSBuild/CSProj structure).
  * **Revit 2025 – 2026:** Targets `.NET 8.0-windows8.0` (Modern SDK-style CSProj structure).
* **WPF & Modern UI Integration:**
  * To support modern WPF and XAML UI across all versions, the legacy `.csproj` files (`Synthetic2022` through `Synthetic2024`) incorporate `ProjectTypeGuids` for legacy WPF support, while the modern ones (`Synthetic2025` and `Synthetic2026`) target `.NET 8.0-windows8.0` directly enabling WPF. All legacy WinForms dialogs have been modernized to WPF.
* **Conditional Compilation:**
  * Each version defines a custom compile constant (e.g., `REVIT2022`, `REVIT2024`, `REVIT2026`) used in code blocks requiring version-specific Revit API overrides.
* **Shared Reference System:**
  * Clean sharing of code is facilitated via [SyntheticShared.projitems](../src/SyntheticShared/SyntheticShared.projitems), which is imported by each version-specific `.csproj` file.

---

## 3. Active Revit API Namespaces

The project relies extensively on the following Revit namespaces:

* `Autodesk.Revit.ApplicationServices` — Application lifecycle and global settings.
* `Autodesk.Revit.Attributes` — Transaction attributes (`[Transaction(TransactionMode.Manual)]`).
* `Autodesk.Revit.DB` — Core database elements, geometry, parameters, and materials.
* `Autodesk.Revit.DB.Events` — Event triggers like document opening and closing.
* `Autodesk.Revit.DB.ExtensibleStorage` — Custom schemas to embed JSON and settings inside `.rvt` files.
* `Autodesk.Revit.UI` — UI controlled applications, ribbons, tasks dialogs, and external event handling.
* `Autodesk.Revit.UI.Selection` — User picking, selection filters, and active canvas operations.

---

## 4. Key Classes & Architecture Map

### Core Application Entry Point
* **[App.cs](../src/SyntheticShared/Core/App.cs)**: Implements `IExternalApplication`. Handles startup configurations, ribbon creation call, and registers event triggers (`DocumentOpened`, `DocumentClosing`).

### User Interface & Layout
* **[SyntheticRibbon.cs](../src/SyntheticShared/Core/SyntheticRibbon.cs)**: Builds the ribbon layout under the "Synthetic" tab. Organizes the pushing buttons, icons, tooltips, and drop-down selectors across six panels:
  1. *Views & Tags*
  2. *Legends*
  3. *Model Management*
  4. *Worksets & Setup*
  5. *Publish*
  6. *Admin & Settings*

### Extensible Storage & Data Persistence
* **[TemplateStorageRepository.cs](../src/SyntheticShared/Modules/AutoTagger/Repositories/TemplateStorageRepository.cs)**: Implements storage and retrieval of the Universal Auto-Tagger templates inside the document using Revit Extensible Storage schemas.
* **[SettingsManager.cs](../src/SyntheticShared/Infrastructure/Persistence/SettingsManager.cs)**: Unified manager handling lazy-loading, caching, persistence, and default fallback mechanisms for in-document settings modules using Extensible Storage.
* **[SyntheticSettingsJsonSchema.cs](../src/SyntheticShared/Infrastructure/Persistence/SyntheticSettingsJsonSchema.cs)**: Schema definition to store settings modules as serialized JSON string payloads inside Revit extensible storage.
* **[LegacySettingsMigration.cs](../src/SyntheticShared/Infrastructure/Persistence/LegacySettingsMigration.cs)**: Utility to identify and safely remove obsolete schemas and legacy setting storage from the Revit document database.

### Core Business & Geometry Logic (Utilities)
* **[MergeAnalysisEngine.cs](../src/SyntheticShared/Modules/MergeDuplicates/Engine/MergeAnalysisEngine.cs)**: Engine behind element merge operations; performs similarity checks and runs duplication matching algorithms.
* **[CoordinateUtility.cs](../src/SyntheticShared/Shared/RevitAPI/CoordinateUtility.cs)**: Geometry coordinate systems converter (Transforms between Local and World coordinates) for exact tag placement.
* **[FamilyUtil.cs](../src/SyntheticShared/Shared/RevitAPI/FamilyUtil.cs)**: Helper classes to audit, re-import, and clean up Revit family instances.
* **[LegendsUtil.cs](../src/SyntheticShared/Modules/ViewManagement/Utilities/LegendsUtil.cs)**: Handles conversions between Drafting Views and Legends, including advanced reference plane group-swapping (suppressing constraint warnings) and sheet viewport swapping.

### WPF UI Modernization & Reusable Helpers
All legacy WinForms dialogs have been modernized to WPF/XAML. The official desaturated dark theme specifications, corner geometry, and WCAG AA contrast rules are documented in [Synthetic_UI_Theme_Specification.md](Synthetic_UI_Theme_Specification.md). Key UI foundation elements include:
* **[RevitWindowHelper.cs](../src/SyntheticShared/Shared/UI/RevitWindowHelper.cs)**: Sets the Win32 owner of modeless/modal WPF windows to Revit's main window handle, ensuring correct focus and stacking order.
* **[FileDialogHelper.cs](../src/SyntheticShared/Shared/UI/FileDialogHelper.cs)**: Standardized wrapper around Windows API OpenFileDialog and SaveFileDialog for WPF context compatibility.
* **[GuardrailPromptWindow.xaml](../src/SyntheticShared/Shared/UI/GuardrailPromptWindow.xaml)** / **[GuardrailPromptWindow.xaml.cs](../src/SyntheticShared/Shared/UI/GuardrailPromptWindow.xaml.cs)**: Custom warning prompt dialog used to prevent user-configuration overwrite actions by exposing Advanced Save decisions.
* **[IGuardrailPromptService.cs](../src/SyntheticShared/Shared/UI/IGuardrailPromptService.cs)** / **[WindowsGuardrailPromptService.cs](../src/SyntheticShared/Shared/UI/WindowsGuardrailPromptService.cs)**: Dialog presentation service governing file-overwrite warning decisions.
* **Reusable UI Views/ViewModels**: Reusable elements located under `Shared/UI/`, such as [DropdownSelectionView](../src/SyntheticShared/Shared/UI/DropdownSelectionView.xaml) (selection list picker), [ListByCheckboxView](../src/SyntheticShared/Shared/UI/ListByCheckboxView.xaml) (checkbox-driven lists), [SelectSearchPathsView](../src/SyntheticShared/Shared/UI/SelectSearchPathsView.xaml) (search directory configuration), and [SingleItemSelectionWindow](../src/SyntheticShared/Shared/UI/SingleItemSelectionWindow.xaml) / [SingleItemSelectionViewModel](../src/SyntheticShared/Shared/UI/SingleItemSelectionViewModel.cs) (a generic single-item list selection window displaying radio-button choices styled with standard window chrome and theme elements).

### RevitDOM Translation & Execution Subsystem
Centralized domain model repository mapping Revit elements to pure POCO state containers and processing their database updates:
* **[StandardSerializationEngine.cs](../src/SyntheticShared/Modules/RevitDOM/StandardSerializationEngine.cs)**: Central model dispatcher and serialization engine. Orchestrates type-specific translators and performs batch imports/exports and analysis.
* **[ModelDispatcher.cs](../src/SyntheticShared/Modules/RevitDOM/ModelDispatcher.cs)**: Maps and dispatches type-specific translator registrations.
* **[GraphicOverrideUtility.cs](../src/SyntheticShared/Modules/RevitDOM/GraphicOverrideUtility.cs)**: Stateless utility class for resolving pattern element IDs and modifying Revit View graphics and filters overrides.
* **[IIdentityService.cs](../src/SyntheticShared/Modules/RevitDOM/IIdentityService.cs)**: Interface governing reference identity lookup and bulk resolution mapping.
* **[RevitIdentityService.cs](../src/SyntheticShared/Modules/RevitDOM/RevitIdentityService.cs)**: Production implementation of `IIdentityService` that executes a multi-tiered identity search during property injection, including bulk element resolution with telemetry-driven warning logs.
* **[IPocoIdentityService.cs](../src/SyntheticShared/Modules/RevitDOM/IPocoIdentityService.cs)**: Interface governing POCO-layer reference identity lookup and mapping.
* **[PocoIdentityService.cs](../src/SyntheticShared/Modules/RevitDOM/PocoIdentityService.cs)**: Pure C# implementation of `IPocoIdentityService` executing a 5-step fallback identity resolution strategy on in-memory object graph pools.
* **[RevitDomExtensions.cs](../src/SyntheticShared/Modules/RevitDOM/RevitDomExtensions.cs)**: Clean typing-friendly API extensions for client-side translator dispatching.
* **[ColorTranslator.cs](../src/SyntheticShared/Modules/RevitDOM/ColorTranslator.cs)**: Dedicated static utility hosting pure-math color conversions.
* **[ImportExecutionRunner.cs](../src/SyntheticShared/Modules/RevitDOM/ImportExecutionRunner.cs)**: Graph orchestrator that constructs a Directed Acyclic Graph (DAG) of elements using their internal references and sorts them bottom-up to prevent import ordering exceptions.
* **[AliasSwapEngine.cs](../src/SyntheticShared/Modules/RevitDOM/AliasSwapEngine.cs)**: Swap reference helper used during type merging.
* **[RevitDomDependencyScanner.cs](../src/SyntheticShared/Modules/RevitDOM/RevitDomDependencyScanner.cs)**: Pure C# reflection scanner that recursively sweeps POCO graphs to locate nested `ElementIdModel` references without Revit API context.
* **Pure POCO Models**: Serialization models including [ModelsToSerialize.cs](../src/SyntheticShared/Modules/RevitDOM/ModelsToSerialize.cs), [MissingModels.cs](../src/SyntheticShared/Modules/RevitDOM/MissingModels.cs), [CategoryModel.cs](../src/SyntheticShared/Modules/RevitDOM/CategoryModel.cs), [ElementModel.cs](../src/SyntheticShared/Modules/RevitDOM/ElementModel.cs), [HostObjTypeModel.cs](../src/SyntheticShared/Modules/RevitDOM/HostObjTypeModel.cs), [MaterialModel.cs](../src/SyntheticShared/Modules/RevitDOM/MaterialModel.cs), [ViewModel.cs](../src/SyntheticShared/Modules/RevitDOM/ViewModel.cs), [ViewPlanModel.cs](../src/SyntheticShared/Modules/RevitDOM/ViewPlanModel.cs), [ViewSheetModel.cs](../src/SyntheticShared/Modules/RevitDOM/ViewSheetModel.cs), [ViewScheduleModel.cs](../src/SyntheticShared/Modules/RevitDOM/ViewScheduleModel.cs), [ParameterFilterElementModel.cs](../src/SyntheticShared/Modules/RevitDOM/ParameterFilterElementModel.cs), [FilterRuleModel.cs](../src/SyntheticShared/Modules/RevitDOM/FilterRuleModel.cs), [ViewFilterOverrideModel.cs](../src/SyntheticShared/Modules/RevitDOM/ViewFilterOverrideModel.cs), [PlanViewRangeModel.cs](../src/SyntheticShared/Modules/RevitDOM/PlanViewRangeModel.cs), [XYZModel.cs](../src/SyntheticShared/Modules/RevitDOM/XYZModel.cs), and [UVModel.cs](../src/SyntheticShared/Modules/RevitDOM/UVModel.cs) acting as 100% pure state containers.
* **Bidirectional Translators**: Dedicated classes that map native Revit objects to/from clean POCO representation, including:
  * **[MaterialTranslator.cs](../src/SyntheticShared/Modules/RevitDOM/MaterialTranslator.cs)**
  * **[ViewTranslator.cs](../src/SyntheticShared/Modules/RevitDOM/ViewTranslator.cs)**
  * **[ViewPlanTranslator.cs](../src/SyntheticShared/Modules/RevitDOM/ViewPlanTranslator.cs)**
  * **[ViewSheetTranslator.cs](../src/SyntheticShared/Modules/RevitDOM/ViewSheetTranslator.cs)**
  * **[ViewScheduleTranslator.cs](../src/SyntheticShared/Modules/RevitDOM/ViewScheduleTranslator.cs)**
  * **[HostObjTypeTranslator.cs](../src/SyntheticShared/Modules/RevitDOM/HostObjTypeTranslator.cs)**
  * **[DimensionTypeTranslator.cs](../src/SyntheticShared/Modules/RevitDOM/DimensionTypeTranslator.cs)**
  * **[TextElementTypeTranslator.cs](../src/SyntheticShared/Modules/RevitDOM/TextElementTypeTranslator.cs)**
  * **[SpotDimensionTypeTranslator.cs](../src/SyntheticShared/Modules/RevitDOM/SpotDimensionTypeTranslator.cs)**
  * **[FilledRegionTypeTranslator.cs](../src/SyntheticShared/Modules/RevitDOM/FilledRegionTypeTranslator.cs)**
  * **[ParameterFilterElementTranslator.cs](../src/SyntheticShared/Modules/RevitDOM/ParameterFilterElementTranslator.cs)**
  * **[ParameterElementTranslator.cs](../src/SyntheticShared/Modules/RevitDOM/ParameterElementTranslator.cs)**
  * **[BrowserOrganizationTranslator.cs](../src/SyntheticShared/Modules/RevitDOM/BrowserOrganizationTranslator.cs)**


### Standards JSON Editor Subsystem
Provides standards management with JSON tree parsing, inline editing, and selective import/export workflows:
* **[NestedDataEditorWindow.xaml](../src/SyntheticShared/Modules/StandardsManagement/Views/NestedDataEditorWindow.xaml)** / **[NestedDataEditorViewModel.cs](../src/SyntheticShared/Modules/StandardsManagement/ViewModels/NestedDataEditorViewModel.cs)**: Modal sub-editor to manage nested properties (e.g., Compound Structure Layers) with relational bindings and subcategory harvesting.

#### Standards Importer Quality & Safety Guards
To ensure robust synchronization of styles across projects without throwing exceptions or corrupting the Revit database, the importer incorporates the following architectural rules:
1. **Category Hierarchy Isolation:** Subcategories are resolved and created strictly within the context of their parent category (rather than via global name queries) to prevent collision with other subcategories of the same name.
2. **Cross-Document ID Clash Safeguards:** When retrieving an element by ID, `ElementIdModel.GetElem` verifies that the element's actual Revit class is assignable to the expected class type, preventing invalid casting or referencing of unrelated elements if IDs differ between documents.
3. **Line Weight Boundary Enforcement:** All line weights are bounded between 1 and 16 before setting them on categories.
4. **Uncuttable Category Exception Guards:** Graphic overrides for cut representation are wrapped in `IsCuttable` checks and Try/Catch blocks within category constructor queries and modification workflows to prevent native Revit exceptions.
5. **Magic-Number Free Line Patterns:** Translates solid patterns dynamically to `LinePatternElement.GetSolidPatternId()`.

### Project Standards Consolidation Dashboard Subsystem
Enables centralized modeless project standards consolidation with staged execution queues, interactive find & replace editing, diff inspection panels, and post-execution reports:
* **[ProjectStandardsDashboardWindow.xaml](../src/SyntheticShared/Modules/StandardsManagement/Views/ProjectStandardsDashboardWindow.xaml)** / **[ProjectStandardsDashboardViewModel.cs](../src/SyntheticShared/Modules/StandardsManagement/ViewModels/ProjectStandardsDashboardViewModel.cs)**: Main modeless dashboard UI and logic. Houses multiple sources, hierarchical tree views, search filtering, staged action queues, and workspace navigation.
* **[QueueItemModel.cs](../src/SyntheticShared/Modules/StandardsManagement/ViewModels/QueueItemModel.cs)**: Model representing staged items in the run queue, capturing the source POCO model, active target properties, and execution intents (e.g. Enforce, Save, SaveAndEnforce).
* **[ISummaryDisplayService.cs](../src/SyntheticShared/Shared/UI/ISummaryDisplayService.cs)** / **[WindowsSummaryDisplayService.cs](../src/SyntheticShared/Shared/UI/WindowsSummaryDisplayService.cs)**: Decoupled dialog presentation service displaying the modal execution summaries.
* **[ImportSummaryViewModel.cs](../src/SyntheticShared/Modules/StandardsManagement/ViewModels/ImportSummaryViewModel.cs)**: View model driving the execution summaries modal. Formats log details into interactive tables and exports structured Markdown reports.
* **[StandardsHierarchyUtility.cs](../src/SyntheticShared/Modules/StandardsManagement/Utilities/StandardsHierarchyUtility.cs)**: Stateless utility to centralize element taxonomy grouping, alphabetical sorting, and template tree generation.
* **[IStandardsExtractionOrchestrator.cs](../src/SyntheticShared/Modules/StandardsManagement/Engine/IStandardsExtractionOrchestrator.cs)** / **[StandardsExtractionOrchestrator.cs](../src/SyntheticShared/Modules/StandardsManagement/Engine/StandardsExtractionOrchestrator.cs)**: Dedicated orchestrator service managing the recursive extraction and dependency harvesting of Revit standards via a POCO-driven Directed Acyclic Graph (DAG) traversal.

### Modeless Merge Duplicates Consolidation Subsystem
Enables automated type merging, instance swapping, redundant type deletion, and family parameter schema synchronization:
* **[MergeDuplicatesWindow.xaml](../src/SyntheticShared/Modules/MergeDuplicates/Views/MergeDuplicatesWindow.xaml)** / **[MergeDuplicatesViewModel.cs](../src/SyntheticShared/Modules/MergeDuplicates/ViewModels/MergeDuplicatesViewModel.cs)**: Modeless staging workspace to scan, exclude items, group duplicate family/type clusters, and queue them for transaction.
* **[MergeDetailedReviewWindow.xaml](../src/SyntheticShared/Modules/MergeDuplicates/Views/MergeDetailedReviewWindow.xaml)** / **[MergeDetailedReviewViewModel.cs](../src/SyntheticShared/Modules/MergeDuplicates/ViewModels/MergeDetailedReviewViewModel.cs)**: Granular conflict mapping interface with side-by-side parameter value comparisons, primary naming overrides, and schema parameter mapping controls.
* **[ProcessMergeEventHandler.cs](../src/SyntheticShared/Modules/MergeDuplicates/Handlers/ProcessMergeEventHandler.cs)**: Modeless transaction pipeline which runs the complex multi-step database process on the Revit API thread (Adds missing parameters to families -> duplicates and re-maps types -> redirects family instances, groups, and assemblies -> removes redundant duplicate elements -> saves processing summary to JSON).
* **[DuplicateClusterModel.cs](../src/SyntheticShared/Modules/MergeDuplicates/Models/DuplicateClusterModel.cs)** / **[ParameterDiffRowModel.cs](../src/SyntheticShared/Modules/MergeDuplicates/Models/ParameterDiffRowModel.cs)**: Data models defining similarity clusters, diff tracking rows, and recommended resolution outcomes.

### Unified Settings, File Utility & Modeless Sync Subsystem
Manages granular extensible settings modules, detects workspace configuration drift, runs UI setup wizards, and marshals modeless synchronization (pull/push) options:
* **[MaterialLibrarySettings.cs](../src/SyntheticShared/Settings/MaterialLibrarySettings.cs)**: Settings module storing the local or network material library directory path.
* **[ProjectMaterialSettings.cs](../src/SyntheticShared/Settings/ProjectMaterialSettings.cs)**: Settings module holding default relative and override paths to resolve project-specific materials.
* **[SyncSettings.cs](../src/SyntheticShared/Settings/SyncSettings.cs)**: Settings module specifying the file path to link the current document to external settings files.
* **[FileUtilitySettings.cs](../src/SyntheticShared/Settings/FileUtilitySettings.cs)**: Settings module storing decoupled network archive paths and alternate path mappings for local directories.
* **[SettingsDashboardWindow.xaml](../src/SyntheticShared/Modules/SettingsDashboard/Views/SettingsDashboardWindow.xaml)** / **[SettingsDashboardViewModel.cs](../src/SyntheticShared/Modules/SettingsDashboard/ViewModels/SettingsDashboardViewModel.cs)**: Unified WPF MVVM dashboard displaying active module states, checking for drift with external configurations, and orchestrating setting modules.
* **[SyncWizardWindow.xaml](../src/SyntheticShared/Modules/SettingsDashboard/Views/SyncWizardWindow.xaml)** / **[SyncWizardViewModel.cs](../src/SyntheticShared/Modules/SettingsDashboard/ViewModels/SyncWizardViewModel.cs)**: Wizard context guiding users through linking external files, establishing paths, or creating template schemas.
* **[SyncResolutionWindow.xaml](../src/SyntheticShared/Modules/SettingsDashboard/Views/SyncResolutionWindow.xaml)** / **[SyncResolutionWindow.xaml.cs](../src/SyntheticShared/Modules/SettingsDashboard/Views/SyncResolutionWindow.xaml.cs)**: Modal interface resolving settings drift between in-document storage and external settings files.
* **[SyncExternalEventHandler.cs](../src/SyntheticShared/Modules/SettingsDashboard/Handlers/SyncExternalEventHandler.cs)**: Marshals modeless settings pull/push sync requests from the UI onto the Revit API thread.
* **[MaterialLibraryViewModel.cs](../src/SyntheticShared/Modules/SettingsDashboard/ViewModels/MaterialLibraryViewModel.cs)** / **[ProjectMaterialsViewModel.cs](../src/SyntheticShared/Modules/SettingsDashboard/ViewModels/ProjectMaterialsViewModel.cs)** / **[SyncSettingsViewModel.cs](../src/SyntheticShared/Modules/SettingsDashboard/ViewModels/SyncSettingsViewModel.cs)** / **[FileUtilitySettingsViewModel.cs](../src/SyntheticShared/Modules/SettingsDashboard/ViewModels/FileUtilitySettingsViewModel.cs)**: Specific sub-viewmodels mapping individual extensible settings modules to their dashboard UI views.
* **[NetworkPathsWizardWindow.xaml](../src/SyntheticShared/Modules/SettingsDashboard/Views/NetworkPathsWizardWindow.xaml)** / **[NetworkPathsWizardWindow.xaml.cs](../src/SyntheticShared/Modules/SettingsDashboard/Views/NetworkPathsWizardWindow.xaml.cs)**: Wizard interface window allowing visual configuration of archive paths and alternate path mappings.
* **[NetworkPathsWizardViewModel.cs](../src/SyntheticShared/Modules/SettingsDashboard/ViewModels/NetworkPathsWizardViewModel.cs)**: ViewModel driving the paths wizard, facilitating list management and background validation.
* **[PathMappingViewModel.cs](../src/SyntheticShared/Modules/SettingsDashboard/ViewModels/PathMappingViewModel.cs)**: Observable wrapper for dictionary key-value path mapping to allow two-way WPF DataGrid binding.
* **[SyncToastNotification.xaml](../src/SyntheticShared/Modules/SettingsDashboard/Views/SyncToastNotification.xaml)** / **[SyncToastNotification.xaml.cs](../src/SyntheticShared/Modules/SettingsDashboard/Views/SyncToastNotification.xaml.cs)**: Lightweight modeless popup notifying users on successful settings sync actions.

### Detail Item Factory Subsystem
Provides automated 2D projection and detail component compilation for 3D Revit elements:
* **[CmdDetailItemFactory.cs](../src/SyntheticShared/Modules/DetailItemFactory/Commands/CmdDetailItemFactory.cs)**: Main external command. Verifies active view compatibility (requires 2D projection view), processes active selections, and raises modeless background generation events.
* **[DetailItemFactoryEventHandler.cs](../src/SyntheticShared/Modules/DetailItemFactory/Handlers/DetailItemFactoryEventHandler.cs)**: Modeless database event handler. Operates on the Revit API thread to temporarily export geometry to DWG, imports DWG, extracts 2D curves, splits and heals curves, creates detail components, and cleans up temp files.
* **[SelectedElementItemViewModel.cs](../src/SyntheticShared/Modules/DetailItemFactory/ViewModels/SelectedElementItemViewModel.cs)**: View model representing individual elements selected for conversion, managing orientation overrides (e.g. Front, Back, Plan) and automatically resolving legend component smart defaults.
* **[DetailItemFactoryViewModel.cs](../src/SyntheticShared/Modules/DetailItemFactory/ViewModels/DetailItemFactoryViewModel.cs)** / **[DetailItemFactoryResultsViewModel.cs](../src/SyntheticShared/Modules/DetailItemFactory/ViewModels/DetailItemFactoryResultsViewModel.cs)**: View models driving the main configurations window and conversion completion dialogs.
* **[DetailItemFactoryView.xaml](../src/SyntheticShared/Modules/DetailItemFactory/Views/DetailItemFactoryView.xaml)** / **[DetailItemFactoryResultsView.xaml](../src/SyntheticShared/Modules/DetailItemFactory/Views/DetailItemFactoryResultsView.xaml)**: WPF views implementing the configuration modal, modeless progress dashboard, and batch completion summary UI.

---

## 5. Command Registry

Below is a complete registry of all `IExternalCommand` classes defined in `SyntheticShared/Modules/`:

| Command Class | Ribbon Panel | Core Functionality |
| :--- | :--- | :--- |
| **[ViewsAutoNumber](../src/SyntheticShared/Modules/ViewManagement/Commands/ViewsAutoNumber.cs)** | Views & Tags | Automatically renumbers views placed on sheets. |
| **[CmdBatchTag](../src/SyntheticShared/Modules/AutoTagger/Commands/CmdBatchTag.cs)** | Views & Tags | Iteratively tags selected family elements or active view using template mapping rules. |
| **[CmdManageTemplates](../src/SyntheticShared/Modules/AutoTagger/Commands/CmdManageTemplates.cs)** | Views & Tags | Control panel to manage, delete, import, or export tag template files. |
| **[ViewAutoNumberConfig](../src/SyntheticShared/Modules/ViewManagement/Commands/ViewAutoNumberConfig.cs)** | Views & Tags | Displays configuration details for view auto-numbering. |
| **[CmdSetTemplate](../src/SyntheticShared/Modules/AutoTagger/Commands/CmdSetTemplate.cs)** | *WPF Dialog context* | Launches coordinate picker to calculate coordinate offsets and save them as templates. |
| **[CmdDetailItemFactory](../src/SyntheticShared/Modules/DetailItemFactory/Commands/CmdDetailItemFactory.cs)** | Views & Tags | Batch-processes selected 3D model elements into 2D Detail Item families via temporary DWG projection and tracing. |
| **[ConvertDraftingToLegend](../src/SyntheticShared/Modules/ViewManagement/Commands/ConvertDraftingToLegend.cs)** | Legends | Batch transfers details and symbols from Drafting Views into Legends. |
| **[ConvertLegendToDrafting](../src/SyntheticShared/Modules/ViewManagement/Commands/ConvertLegendToDrafting.cs)** | Legends | Batch transfers details and symbols from Legends into Drafting Views. |
| **[CmdMergeDuplicates](../src/SyntheticShared/Modules/MergeDuplicates/Commands/CmdMergeDuplicates.cs)** | Model Management | Launches dialog to merge duplicate family types, groups, and assemblies. |
| **[MaterialsRepathAll](../src/SyntheticShared/Modules/MaterialManagement/Commands/MaterialsRepathAll.cs)** | Model Management | Mass-repaths render assets of materials from specified search directories. |
| **[MaterialImagesPackage](../src/SyntheticShared/Modules/MaterialManagement/Commands/MaterialImagesPackage.cs)** | Model Management | Gathers and packages render images mapped inside materials to an export folder. |
| **[PaintElements](../src/SyntheticShared/Modules/MaterialManagement/Commands/PaintElements.cs)** | Model Management | Standardizes face painting of select geometries with a set material. |
| **[AuditPurgeAllFamilies](../src/SyntheticShared/Modules/FamilyManagement/Commands/AuditPurgeAllFamilies.cs)** | Model Management | Iteratively opens, logs errors, clears legacy schemas, and closes all family components. |
| **[FamiliesForceReinsert](../src/SyntheticShared/Modules/FamilyManagement/Commands/FamiliesForceReinsert.cs)** | Model Management | Re-imports/overwrites families to fix post-upgrade text sizing discrepancies. |
| **[CmdProjectStandards](../src/SyntheticShared/Modules/StandardsManagement/Commands/CmdProjectStandards.cs)** | Model Management | Displays the modeless Project Standards Dashboard enabling enqueued style execution, guardrails, and markdown logging. |
| **[WorksetsImport](../src/SyntheticShared/Modules/Worksets/Commands/WorksetsImport.cs)** | Worksets & Setup | Automates new workset generation using definitions stored in Excel. |
| **[ScopeBoxesMoveToWorkset](../src/SyntheticShared/Modules/Worksets/Commands/ScopeBoxesMoveToWorkset.cs)** | Worksets & Setup | Mass relocates project scope boxes to a designated workset. |
| **[WorksetSetFile](../src/SyntheticShared/Modules/Worksets/Commands/WorksetSetFile.cs)** | Worksets & Setup | Assigns the template spreadsheet path utilized by the workset importer. |
| **[WorksetSettingsShow](../src/SyntheticShared/Modules/Worksets/Commands/WorksetSettingsShow.cs)** | Worksets & Setup | Displays the registered project workset Excel file mappings. |
| **[WorksetStartView](../src/SyntheticShared/Modules/Worksets/Commands/WorksetStartView.cs)** | Worksets & Setup | Synchronizes initial workspace settings (e.g. startup view details) from Excel. |
| **[ElementsOnWorksetRecord](../src/SyntheticShared/Modules/Worksets/Commands/ElementsOnWorksetRecord.cs)** | Worksets & Setup | Exports unique element-to-workset associations to a text recovery file. |
| **[ElementsOnWorksetReload](../src/SyntheticShared/Modules/Worksets/Commands/ElementsOnWorksetReload.cs)** | Worksets & Setup | Resolves and reassigns elements back to their recorded worksets from file. |
| **[PrintBatchMultiDoc](../src/SyntheticShared/Modules/BatchPrint/Commands/PrintBatchMultiDoc.cs)** | Publish | Multi-document batch printer processing sheets across loaded models. |
| **[SettingsDashboardCommand](../src/SyntheticShared/Modules/SettingsDashboard/Commands/SettingsDashboardCommand.cs)** | Admin & Settings | Displays the unified settings dashboard to manage configuration overrides and drift resolution. |
| **[StorageQuery](../src/SyntheticShared/Infrastructure/Diagnostics/StorageQuery.cs)** | Admin & Settings | Queries document metadata for registered extensible storage schemas. |
| **[StorageDelete](../src/SyntheticShared/Infrastructure/Diagnostics/StorageDelete.cs)** | Admin & Settings | Deletes custom extensible storage schemas from the project database. |
