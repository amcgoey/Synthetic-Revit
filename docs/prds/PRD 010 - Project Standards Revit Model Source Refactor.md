# PRD 010 - Project Standards Revit Model Source Refactor - Product Requirement Document

## Problem Statement

When users add an open Revit model as a source to the Project Standards Dashboard, the workflow and UI are presented out of order. Currently, the "Select Revit Models Source" dialog uses a hardcoded, flat list of filters that fails to match the hierarchical taxonomy of the resulting Source Pane. Furthermore, critical family scanning options (e.g., "Scan nested families") are orphaned on the resulting Source Tab UI *after* the extraction has already occurred, rendering them useless and confusing the user. Finally, processing multiple Revit projects and their nested families on the main UI thread risks freezing the application, giving the impression of a crash during heavy batch extractions.
## Solution

Restructure the Revit Model Source extraction workflow so that all configuration happens before execution, utilizing a centralized taxonomy engine and a safe, asynchronous progress pipeline.
The "Select Revit Models" dialog will be overhauled to include three distinct sections: Project Selection, a Hierarchical Filter List, and Family Processing Options ("Scan Families" and "Include Nested Families"). To ensure the filter list perfectly mirrors the Source Pane, all grouping and sorting logic will be abstracted out of the ViewModels into a stateless StandardsHierarchyUtility. When the user confirms the dialog, the heavy extraction process—including recursive family scanning—will be handed off to the ProgressCoordinator to maintain UI responsiveness and provide cancellation capabilities. Finally, the orphaned family processing UI will be purged from the resulting Source Tab, returning it to a clean, read-only representation of the extracted data.
## User Stories

- As a user, I want the "Select Revit Models Source" dialog to present all extraction options upfront, so that I can fully configure what data is pulled before the heavy processing begins.
- As a user, I want the filter list in the selection dialog to use the exact same hierarchical grouping (e.g., "Materials & Assets", "System Types") as the final Source Pane, so that my mental model remains consistent across the application.
- As a maintainer, I want all category routing and sorting logic centralized in a single StandardsHierarchyUtility, so that I don't have to manually update hardcoded UI lists whenever a new Revit class is supported.
- As a BIM Manager extracting standards, I want explicit checkbox controls for "Scan Families" and "Include Nested Families", so that I can strategically control the depth and performance impact of my extraction.
- As a UI developer, I want the orphaned "Scan nested families" checkboxes removed from the Source Tab, so that the tab accurately reflects its post-extraction, read-only state.
- As a user triggering a deep recursive scan of multiple project files, I want the operation to display a modeless progress bar with a cancel button, so that the Revit UI doesn't freeze and I can abort long-running tasks.
## Implementation Decisions

- **The Taxonomy Utility:** Create a new stateless class, StandardsHierarchyUtility, in the Synthetic.Modules.StandardsManagement module. This utility will take over the BuildHierarchy and GetGroupName logic currently inside ProjectStandardsDashboardViewModel. It will be responsible for converting raw ObjectModel lists into the ObservableCollection<StandardGroupModel> used by the UI, as well as generating an empty "template" hierarchy to populate the selection dialog checkboxes.
- **Dialog UI Overhaul:** Update SelectRevitDocumentWindow.xaml and SelectRevitDocumentViewModel.cs to utilize the StandardsHierarchyUtility for its filter list. Add new boolean properties for ScanFamilies and IncludeNestedFamilies, ensuring state-aware visibility (e.g., nested scanning is only enabled if primary scanning is checked).
- **Execution Pipeline:** The "OK" command execution within the Dashboard ViewModel will be wrapped in ProgressCoordinator.Initialize(). The recursive family scanning logic will check ProgressCoordinator.IsCancelled() frequently to allow graceful aborts.
- **UI Demolition:** Remove ScanNestedFamilies and associated UI elements from ProjectStandardsSourceViewModel.cs and the Source Pane DataTemplates in ProjectStandardsDashboardWindow.xaml.
## Testing Decisions

- **Tier 1 (Logic Tests):**
  - Test StandardsHierarchyUtility by passing it a heterogeneous list of mock POCOs to verify it correctly groups and alphabetizes the output StandardGroupModel collections.
  - Test the empty template generation of StandardsHierarchyUtility to ensure all expected primary groups exist.
  - Test SelectRevitDocumentViewModel state transitions (e.g., ensuring IncludeNestedFamilies resets or disables when ScanFamilies is toggled off).
- **Tier 2 (Integration Tests):**
  - Verify the execution pipeline by triggering a headless extraction with IncludeNestedFamilies = true against a known test model, asserting that elements deeply nested inside loadable families are successfully extracted and mapped to the resulting source hierarchy.
## Out of Scope

- Modifying the core Revit-to-POCO translation logic inside StandardSerializationEngine.
- JSON file extraction workflows (this PRD is strictly scoped to live Revit document sources).
## Further Notes

This refactor significantly improves the user experience by adhering to a standard "Configure -> Execute -> Review" workflow, while the introduction of the StandardsHierarchyUtility pays down technical debt by establishing a single source of truth for all standards taxonomy across the application.
