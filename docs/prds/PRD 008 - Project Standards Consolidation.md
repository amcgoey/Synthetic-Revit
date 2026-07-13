# PRD 008 - Project Standards Consolidation and Staging Workspace - Product Requirement Document

## Problem Statement

The current Standards Management architecture fragments related domain workflows—extracting, editing, diffing, and injecting Revit standards—across three distinct commands and disjointed WPF dialogs (Standards Editor, Enforce Standards, and Generate/Export). This shallow interface design forces the user to manually coordinate data flow between models and files. The UI relies on a tabbed interface for class selection, which hides the active state and obscures what will actually be processed. Furthermore, family processing options are cumbersome, and the lack of a unified staging area prevents users from safely queueing multiple actions (like batch editing before enforcing) without risking state pollution or accidental overwrites of central firm standards.
## Solution

Consolidate the fragmented commands into a single, highly leveraged "Project Standards" command that launches a unified, modeless Staging Workspace. The interface will feature a three-pane layout: a read-only "Source Pane" (allowing users to seamlessly load from the Default Firm Standard, custom files, or open Revit documents), an "Action Queue Pane" for staging, and a dynamic "Sub-Workspace Pane" for inline editing and conflict resolution.
Items selected from a source are deep-copied into the Queue and tagged with execution intents (Enforce, Save, Save & Enforce). "Edit" and "Diff" are treated as immediate, inline sub-workspaces that operate strictly on the detached, queued POCOs. When the user executes the queue, the engine utilizes a strict "Revit-First, File-Second" execution pipeline to guarantee database integrity. A dual-layer guardrail intercepts any attempt to save over the firm's default standards file, prompting the user for confirmation. Finally, a detailed summary dialog with a Markdown export cleanly closes the QA loop.
## User Stories

- As a BIM Manager, I want a single "Project Standards" ribbon command, so that I don't have to navigate between different tools to manage my project styles.
- As a user, I want the workspace to open with my default firm standards already loaded in the Source pane, so that I can quickly apply common styles without browsing my network drive.
- As a user, I want the Source pane to use a hierarchical tree view (Groups -> Classes -> Elements) and a search bar, so that I can quickly find and see exactly what is available to process.
- As a user transferring standards, I want to click an "Add Revit Model" button to create a new Source tab for an open document, so that I can easily pull View Templates from a linked model into my active project.
- As a user extracting styles, I want the "Add Revit Model" tab to include explicit toggles for scanning nested families, so that I can control the depth of my extraction.
- As a UI developer, I want items added to the Queue to be deep-copied, so that if the user cancels their queue, the original Source tab data remains unpolluted in memory.
- As a user, I want to select multiple elements in the Source tree and click "Enforce", so that they are added to the Queue with an "Enforce" tag indicating they will be written to the Revit database.
- As a user, I want to select queued items and click "Edit", so that the right pane dynamically updates to an editor, allowing me to find-and-replace parameter values or merge elements across the batch before committing.
- As a user reviewing changes, I want to select queued items and click "Diff", so that the system immediately runs a deep-scan against the active document and displays a side-by-side conflict resolution window in the right pane.
- As a user managing the queue, I want to easily remove individual items, classes, or entire groups from the Queue list, so that I can correct mistakes before execution.
- As an architectural guard, I want the "Run Queue" command to execute Revit database enforcement (Phase 1) completely before executing any JSON File saves (Phase 2), so that failed Revit transactions do not pollute the clean standards files.
- As a BIM Manager protecting firm assets, I want the system to intercept any Phase 2 File Save targeting the active Project or App-level StandardsFilePath, so that I am warned and given a "Save As" option before accidentally overwriting the master file.
- As a user, I want a detailed Summary Dialog to appear after execution finishes, so that I can review which elements succeeded, failed, or were skipped.
- As a QA reviewer, I want a button on the Summary Dialog to export the results to a Markdown file, so that I can keep an audit log of the standards synchronization.
## Implementation Decisions

- **Command Consolidation:** The existing CmdEnforceStandards, CmdExportStandards, and StandardsEditorShow commands will be deprecated and replaced by a single CmdProjectStandards entry point in the StandardsManagement module.
- **Deep Copy Utility:** To support the detached Queue state without shallow-copy reference leaks, a new extension method (e.g., DeepClone<T>()) will be added to RevitDOM extensions. This will utilize the existing Infrastructure.Serialization JSON methods to serialize and immediately deserialize the POCO models, guaranteeing 100% object detachment.
- **Three-Pane UI Architecture:** The ProjectStandardsDashboardWindow will feature a three-column layout. The left column houses the Source pane (with a search filter and tabbed sources), the center column houses the Action Queue (grouped by class using CollectionViewSource), and the right column houses a dynamic ContentControl.
- **Inline Sub-Workspaces:** The "Edit" and "Diff" operations will not spawn separate modeless dialog windows. Instead, they will dynamically swap the ContentTemplate of the right pane using a WorkspaceMode enum state (Idle, Edit, Diff) to render the refactored editor and review interfaces natively within the dashboard.
- **Phased Execution Pipeline:** ProjectStandardsDashboardViewModel.CmdRunQueue will be structured to process all items tagged "Enforce" or "Save & Enforce" via StandardSerializationEngine.ToRevit() within a TransactionGroup with .Assimilate(). Only upon completion of this phase will it serialize and write "Save" and "Save & Enforce" items to disk.
- **Guardrail Interception:** During the File Save phase, the target path will be compared against SettingsManager.Get<StandardsSettings>(doc).StandardsFilePath (Project level) and ConfigCollection App-level defaults. If a match occurs, an IUserPromptService dialog will interrupt the loop to offer Overwrite, Save As, or Skip.
## Testing Decisions

- **Philosophy:** Testing will target the highest possible seams without launching the slow Revit UI. We will focus heavily on state isolation and execution order.
- **Tier 1 (Logic Tests):** - Test the DeepClone<T>() utility to assert that modifying the cloned POCO does not affect the original object.
  - Test the Guardrail Interception logic by injecting a FakeSettingsManager and FakeFileDialogService into the ViewModel, asserting that the prompt triggers correctly when the paths match.
  - Test Queue State Management to ensure tagging (Enforce vs. Save) groups the elements into the correct execution buckets.
- **Tier 2 (Integration Tests):**
  - Execute a headless Run Queue pipeline via ricaun.RevitTest. Verify that Revit modifications successfully commit while File I/O happens to a temporary directory in the correct Phase 1 -> Phase 2 sequence. Roll back the database changes using the Zero-Leak Rule (TransactionGroup teardown).
## Out of Scope

- Adding support for entirely new Revit API classes to the StandardSerializationEngine (this PRD is strictly focused on UI consolidation and staging workflows).
- Refactoring the underlying styling or XAML control templates (this was resolved in PRD 006 and PRD 007).
- Real-time background syncing of standards files (execution remains explicit via the "Run Queue" button).
## Further Notes

- This architecture replaces the legacy workflows and heavily leverages the modularity achieved in the previous RevitDOM Dispatcher refactoring (PRD 001).
- It establishes the "Revit-First, File-Second" execution standard for all future batch processes.
