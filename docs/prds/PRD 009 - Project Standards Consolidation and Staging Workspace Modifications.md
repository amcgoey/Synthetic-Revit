# Project Standards Consolidation and Staging Workspace Modifications - Product Requirement Document

## Problem Statement

The initial implementation of the Project Standards Consolidation (PRD 008) failed to adequately integrate the core, robust business logic of the legacy Standards Editor. While a modeless staging workspace was successfully introduced, critical functionality such as deep data-scope editing, safe dependency harvesting, nuanced duplicate identification, phased execution guardrails, and distinct memory management (tabs) were omitted or incorrectly implemented. This leaves the user with a hollow interface that cannot safely execute complex BIM management tasks or ensure data integrity during file-saves.
## Solution

Retrofit and expand the existing ProjectStandardsDashboardWindow architecture to incorporate the full suite of legacy Editor logic alongside strict new execution guardrails. The architecture will transition to a "Baseline Clone Pattern" for staged queue items to guarantee true statelessness and memory safety. The execution pipeline will be hardened to ensure Revit database transactions (Phase 1) fully resolve before JSON file saves (Phase 2), with partial failures explicitly stripped from the save payload. A robust inline editor will be restored, multi-model source extraction will be supported via distinct tabs, and a strict Class + Name composite key will govern duplicate detection during protected-file merges.
## User Stories

- As a UI developer, I want items added to the Queue to utilize a "Baseline Clone Pattern" (creating both a read-only Baseline POCO and a mutable Target POCO), so that the UI can remain perfectly stateless, memory-safe, and instantly calculate diffs without cache invalidation issues.
- As a user, I want the system to automatically harvest nested dependencies (e.g., Materials attached to a WallType) and display them as flat, explicit rows in the Queue with a "DependencyOrigin" badge, so that I have complete transparency over what is being added to my project.
- As an architectural guard, I want the "Run Queue" command to execute Revit database enforcement (Phase 1) completely before executing any JSON File saves (Phase 2), so that failed Revit transactions do not pollute the clean standards files.
- As an architectural guard, I want the engine to dynamically strip any Phase 1 execution failures from the Phase 2 JSON save payload, so that the external standards files perfectly mirror the reality of the Revit database.
- As an architectural guard, I want the entire recursive family injection process to be wrapped in a single TransactionGroup with a "Total Rollback" rule, so that if I click "Cancel" mid-process, the project does not suffer from partial enforcement.
- As a BIM Manager protecting firm assets, I want the system to intercept any Phase 2 File Save targeting a protected StandardsFilePath, offering advanced choices (Overwrite All, Merge and Overwrite, Merge and Preserve, Save As), so that I don't accidentally wipe out historical firm standards.
- As a system integrating JSON merges, I want the engine to use a composite key of Class + Name to identify duplicates, so that document-specific IDs do not break cross-project file transfers.
- As a user, I want successfully executed items to be directly purged from the Queue ObservableCollection, leaving only the failed items behind with their error messages, so that my Queue becomes a self-cleaning "to-do" list.
- As a user managing legacy styles, I want to multi-select items in the Queue and click "Merge", so that I can pick a primary survivor, append the deleted redundancies to its Aliases, and have the consumed items immediately purged from the Queue UI to prevent clutter.
- As a user, when I click the “Add Revit Model” button to add a source, I want to be able to select from the Open Projects in a new dialog.
- As a user transferring standards, I want the "Add Revit Model" dialog to generate distinct, separate tabs for each selected Revit document in the Source Pane, so that I maintain data provenance and can close individual tabs to free up memory.
- As a user configuring standard extraction and injection, I want the Family Processing UI to use Contextual Labeling ("Extract from..." vs "Push to...") and State-Aware visibility, so that I am not confused by identical UI panels performing completely different tasks.
- As a user, I want the Find & Replace utility to operate on a "Deep Data Scope" across all selected Queue items' underlying POCOs, rather than just visually intersecting parameters, so that I can bulk-rename items with massive leverage.
- As a user, I want the dashboard to launch with the Default Firm Standard's hierarchical tree checkboxes preselected, but with the Queue remaining completely empty, so that I am prompted to act but not overwhelmed with staged data upon opening.
## Implementation Decisions

- **Baseline Clone Pattern:** To support the detached Queue state, items pushed to the queue will utilize a DeepClone method to instantiate two POCOs: a BaselineModel (immutable history) and a TargetModel (mutable active state).
- **Stateless Editor:** The ElementTypeWrapperVM will not be permanently cached. It will be generated on the fly, comparing the TargetModel to the BaselineModel to calculate IsDirty states dynamically.
- **Deep Data Scope Find & Replace:** Text replacement utilities will execute against the underlying TargetModel parameters directly, rather than relying on the visible visual intersection in the UI.
- **Phased Execution Pipeline:** ProjectStandardsDashboardViewModel.CmdRunQueue will process all items tagged "Enforce" or "Save & Enforce" via StandardSerializationEngine.ToRevit() within a TransactionGroup. Only successful results will proceed to Phase 2 (File Save).
- **Total Rollback:** The RunQueue execution will throw an OperationCanceledException if the user clicks cancel during nested family processing, rolling back the entire TransactionGroup.
- **Direct Collection Purging:** Post-execution, the ViewModel will iterate the SerializationResultModel list and physically .Remove() successful items from the Queue's ObservableCollection.
- **Composite Key Merging:** The custom JSON merge utility triggered by the Guardrail will use element.Class + element.Name to identify and overwrite/preserve objects.
- **Distinct Source Tabs:** Selecting multiple documents in the "Add Revit Model" dialog will spawn a separate ProjectStandardsSourceViewModel tab for each document.
- **Contextual Family UI:** The Family Processing configuration UI will be shared but utilize state-aware visibility and contextual labeling to prevent UX confusion.
## Testing Decisions

- **Testing Seam (Logic):** Use NUnit logic tests to verify the Baseline Clone Pattern, ensuring that mutations to the TargetModel do not affect the BaselineModel, and that IsDirty evaluates correctly on-the-fly.
- **Testing Seam (Execution):** Verify the Phase 1 failure stripping logic by mocking a failed StandardSerializationEngine response and asserting that the resulting Phase 2 payload excludes the failed item.
- **Testing Seam (Merge):** Unit test the Class + Name composite key duplicate identification logic for the JSON merge engine.
## Out of Scope

- Adding support for entirely new Revit API classes to the StandardSerializationEngine.
- Real-time background syncing of standards files (execution remains explicit via the "Run Queue" button).
## Further Notes

This architecture establishes the "Revit-First, File-Second" execution standard for all future batch processes and provides a robust, self-cleaning modeless workspace for complex BIM management workflows.
