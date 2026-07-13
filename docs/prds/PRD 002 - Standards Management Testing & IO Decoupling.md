# Standards Management Testing & I/O Decoupling - Product Requirement Document

## Problem Statement

The ViewModels orchestrating the Standards Management workflows (ExportStylesViewModel, EnforceStandardsViewModel) are currently tightly coupled to the OS file system. They directly instantiate Microsoft.Win32.OpenFileDialog and Microsoft.Win32.SaveFileDialog to prompt users for file paths. This hard coupling prevents the execution of fast, headless unit tests (Tier 1), as the UI components will block the automated test runner waiting for human input. Furthermore, the complex business logic within the standards editor, diff engine, and export harvester currently lacks dedicated test coverage, introducing risk when refactoring these critical workflows.
## Solution

Decouple the file I/O operations from the ViewModels by introducing an architectural seam: the IFileDialogService. By injecting this interface into the ViewModels, we can supply a native Windows implementation in production and a FakeFileDialogService during automated testing. Once this boundary is established, we will implement a robust suite of Tier 1 (Logic) and Tier 2 (Revit Integration) tests to secure the behavior of the Standards Management subsystem.
## User Stories

- As a developer, I want to abstract file dialogs into an IFileDialogService, so that I can inject a fake service during automated testing and prevent the test runner from hanging.
- As a developer, I want to test the dirty state tracking of ElementTypeWrapperVM in isolation, so that I can ensure the UI correctly reflects unsaved changes without needing a live Revit session.
- As a developer, I want to verify the FindReplaceViewModel logic via fast NUnit tests, so that I am confident bulk string replacements are accurate and respect read-only parameter constraints.
- As a developer, I want to unit test the Class Selection filtering in ExportStylesViewModel, so that I can guarantee user exclusions (e.g., ignoring WallTypes) are respected before the export payload is generated.
- As a developer, I want to test the conflict resolution state updates in DuplicateClusterModel, so that I know the diff UI accurately promotes user-selected parameter overrides.
- As an automated integration tester, I want to execute StandardsDiffEngine.RunDeepScan against a live Revit document containing artificial discrepancies, so that I can verify the engine correctly flags mismatches.
- As an automated integration tester, I want to execute ExportStylesViewModel.HarvestDependencies against a known WallType, so that I can verify it correctly traverses and extracts the associated Material and FillPattern dependencies without infinite loops.
- As an automated integration tester, I want to invoke JsonEditorExternalEventHandler with a mock payload, so that I can verify the modeless execution securely commits changes to the database on the Revit API thread without throwing context exceptions.
## Implementation Decisions

- **The IFileDialogService Interface:** We will introduce a new interface in Synthetic.Shared.UI (or a dedicated Synthetic.Infrastructure.IO namespace) to define the file dialog contracts.// Architectural Shape Decisionpublic interface IFileDialogService{    string? ShowOpenDialog(string filter, string title);    string? ShowSaveDialog(string filter, string title, string defaultFileName);}
- **Production Adapter:** A WindowsFileDialogService will be created to implement IFileDialogService using standard Microsoft.Win32 dialogs.
- **Test Adapter (Fake):** A FakeFileDialogService will be created in the test project that allows us to pre-program the returned file path without showing a UI.
- **Dependency Injection:** The constructors for ExportStylesViewModel and EnforceStandardsViewModel will be updated to accept IFileDialogService. The top-level Revit Commands (CmdExportStandards, CmdEnforceStandards) will instantiate the WindowsFileDialogService and pass it down.
- **UI Threading Abstraction:** Any remaining MessageBox.Show() calls inside the ViewModels will also be abstracted into an IUserPromptService to ensure 100% headless testability.
## Testing Decisions

- **Philosophy:** We will strictly test external, observable behavior. We will *not* mock internal classes or use complex mock frameworks like Moq. We will only use Handcrafted Fakes at the edge boundaries (File System).
- **Tier 1 Modules (Logic Tests):** Target MVVM state transitions and pure data manipulation logic. Run via standard NUnit.
  - JsonEditorMainViewModel
  - ElementTypeWrapperVM
  - FindReplaceViewModel
  - ExportStylesViewModel
  - DuplicateClusterModel & TypeMappingModel
- **Tier 2 Modules (Integration Tests):** Target the live database seams using the ricaun.RevitTest runner.
  - StandardsDiffEngine
  - ExportStylesViewModel.HarvestDependencies
  - JsonEditorExternalEventHandler
- **The Zero-Leak Rule:** All Tier 2 tests that modify the Revit database must be wrapped in a TransactionGroup that is explicitly rolled back in the test teardown to ensure state pollution does not occur between test runs.
## Out of Scope

- Modifying the structure or schema of the JSON standards files.
- Adding new UI features or functional capabilities to the Standards Editor.
- Refactoring the internal XAML code or visual styling of the views.
- Implementing Dependency Injection containers (e.g., Microsoft.Extensions.DependencyInjection); we will use simple constructor injection.
## Further Notes

- This testing strategy aligns with the RevitTDDWorkflow blueprint and the "Mocking at the Edges" philosophy. By decoupling the OS file system, we elevate the ViewModels to pure logic controllers, vastly improving their maintainability.
