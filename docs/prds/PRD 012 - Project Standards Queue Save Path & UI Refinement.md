# PRD 012 - Project Standards Queue Save Path & UI Refinement - Product Requirement Document

## Problem Statement

In the Project Standards Dashboard, the newly introduced "Save Target File Path" UI and the "Family Processing Options" UI were inadvertently placed in overlapping layout grids at the bottom of the Queue pane. This obscures the save functionality, leading users to believe it hasn't been implemented. Furthermore, when the save field is accessible, it initializes as blank. This forces users to manually navigate the file system every time they want to save staged standards, creating unnecessary friction.
Additionally, the queue state management currently uses a singular QueueIntent enum, which forces execution intents (Save, Enforce) to collide with historical UI states (Edited, Diffed). This prevents an item from accurately tracking multiple actions. Furthermore, the execution engine currently attempts to save a file simply because a file path is populated, rather than strictly verifying the presence of "Save" tagged items. Finally, the "Overwrite Guard" logic (which protects firmwide standards from accidental overwrites) is currently intertwined with the Dashboard's ViewModel execution loop, creating a tight coupling between UI state and file I/O operations.
## Solution

Refactor the bottom section of the Queue pane to organize the "Save Target JSON File" and "Family Processing Options" into stacked, collapsible Expander controls, matching the design pattern established by the "Find & Replace" utility.
To reduce friction, the Dashboard will implement a "Smart Defaulting" algorithm upon initialization. This will evaluate the active document's worksharing and cloud status to automatically populate a safe, logical default file path (e.g., [Model Name] Standards.json).
The QueueItemModel will transition from a singular QueueIntent enum to a multi-state boolean model (WillEnforce, WillSave, IsEdited, IsDiffed). This allows items to persist their historical UI state while explicitly declaring multiple future execution actions. The execution pipeline will evaluate these tags per item. Saving to a JSON file will *only* occur if items flagged with WillSave exist in the queue, regardless of whether a Save Path is populated. The post-execution results dialog will log discrete results for each executed action per object.
Finally, the File I/O and overwrite guardrail logic will be evaluated and extracted from the ProjectStandardsDashboardViewModel into a dedicated File/Serialization handler, ensuring the ViewModel remains a pure state orchestrator.
## User Stories

- As a UI user, I want the Save Path and Family Processing options to be housed in collapsible Expanders at the bottom of the Queue pane, so they do not overlap and I can hide them when not in use.
- As a user staging standards for export, I want the Save Path to automatically default to my current model's directory with the filename [Model Name] Standards.json, so I don't have to manually browse for standard saves. If the model is workshared, then the 'Model Name' should be the Central model’s name, not the local model’s name.
- As a user working on a BIM360/ACC Cloud Model, I want the default save path to automatically resolve to my local sync folder, since I cannot save JSON files directly to a cloud URI.
- As a user working on a local workshared model, I want the default save path to resolve to the Central Model's directory rather than my local detached copy, so that standards are saved where the team can access them.
- As a user staging multiple actions, I want an item to display multiple tags simultaneously in the UI (e.g., 'Save', 'Enforce', and 'Edited'), so I don't lose track of its execution intent after modifying it.
- As an architectural guard, I want the system to only attempt a file save if there are items explicitly tagged with 'Save' in the queue, rather than saving just because a file path is configured.
- As a QA reviewer, I want the summary dialog to list separate results for each action taken on a single object (e.g., 'Created' in Revit, 'Saved' to file), so I have a complete audit of the multi-tag execution.
- As a system architect, I want the overwrite guard logic and JSON file saving execution moved out of the Dashboard ViewModel, so that the file persistence layer is decoupled from the UI execution loop.
## Implementation Decisions

- **UI Layout Stack:** In ProjectStandardsDashboardWindow.xaml, the Grid row housing the bottom controls will be converted to a StackPanel (or auto-sized Grid rows) containing two Expander controls. They will utilize the existing SyntheticTheme expander styling.
- **Multi-Tag Queue State:** Deprecate the singular QueueIntent enum. Introduce explicit boolean properties to QueueItemModel: WillEnforce, WillSave, IsEdited, and IsDiffed. Update the WPF ItemTemplate in the Queue ListBox to dynamically render multiple tag badges based on these booleans.
- **Explicit Save Trigger:** Update ProjectStandardsDashboardViewModel.RunQueueInternal() to strictly verify ActionQueue.Any(i => i.WillSave) before attempting Phase 2 (File I/O). The presence of a populated SaveFilePath alone will not trigger file serialization.
- **Discrete Result Logging:** Ensure the execution pipeline and ImportSummaryViewModel can register multiple ImportLogItem entries per QueueItemModel (e.g., generating one log row for the Database transaction success, and a separate log row for the File serialization success).
- **Smart Defaulting Algorithm:** Added to ProjectStandardsDashboardViewModel.Initialize():
  - Extract the base Document.Title.
  - If doc.IsModelInCloud: Set directory to Environment.SpecialFolder.MyDocuments.
  - Else if doc.IsWorkshared: Extract the Central Model path via doc.GetWorksharingCentralModelPath(), convert to User Visible Path, and extract the directory.
  - Else: Extract directory from doc.PathName.
  - Combine the resolved directory with "{Document.Title} Standards.json" and assign it to SaveFilePath.
- **Overwrite Guard Extraction:** The Phase 2 File I/O execution loop (currently inside ProjectStandardsDashboardViewModel.RunQueueInternal()) will be extracted. We will evaluate moving this to a dedicated StandardsExportService or the StandardSerializationEngine to handle the IGuardrailPromptService interception and JSON writing cleanly.
## Testing Decisions

- **Testing Seam (Smart Defaults):** Write Tier 1 (Logic) NUnit tests injecting mock Document instances with varying states (IsModelInCloud = true, IsWorkshared = true) to verify the SaveFilePath resolves to the correct theoretical strings without requiring a live Revit instance.
- **Testing Seam (Multi-Tag Logic):** Verify that modifying an element sets IsEdited = true without stripping its original WillEnforce or WillSave states. Verify the RunQueueInternal logic skips file writing entirely if no items have WillSave = true.
- **Testing Seam (Execution Isolation):** Verify that the newly extracted Export Service cleanly interrupts the pipeline and returns the correct SerializationResultModel states when the user clicks "Cancel" on a guardrail prompt.
## Out of Scope

- Adding real-time background syncing of JSON standards.
- Implementing automatic merging of individual JSON properties during an overwrite (currently restricted to file-level Replace, Save-As, or Skip).
## Further Notes

This refinement solidifies the ergonomics of the staging workspace, ensuring users have immediate, non-obstructive access to critical execution configurations while protecting the underlying architecture from UI coupling and rigid state limitations.
