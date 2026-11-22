# When to Mock in a Revit Add-in

Mock at **system boundaries** only:

- External file system and disk I/O (JSON template exports, Excel files via `Dot Sheet Index`)
- External cloud or network APIs (Google Drive synchronization)
- Modeless user interaction and UI prompts (`FakeFileDialogService`, `FakeUserPromptService`, `FakeGuardrailPromptService`)
- System clock and randomness

Don't mock:

- Your own classes and modules (`RevitDOM` models, comparison engines, serialization pipelines)
- Internal collaborators
- Anything you control

## Autodesk.Revit.DB is NOT a Mockable Boundary

`Autodesk.Revit.DB` (`Document`, `Element`, `Parameter`, `Transaction`, `FilteredElementCollector`) is a native C++-backed interop layer. The Revit API classes lack public constructors, rely on internal native pointers (`m_pElement`), and cannot be safely mocked headlessly via Moq or NSubstitute. Mocking Revit API objects with dynamic proxies causes `InternalException`s, access violations, or brittle tests that pass while failing inside real Revit.

### The Solution: The RevitDOM POCO Adapter Pattern

Synthetic completely decouples domain business logic from the native Revit runtime through the **RevitDOM POCO adapter pattern**:

1. **Headless Logic Seam (`SyntheticLogic.Tests`):** Pure C# modules consume and produce `RevitDOM` POCOs (`ElementModel`, `ParameterModel`, `CategoryModel`). These tests execute headlessly via `python .agents/skills/revit-tests/scripts/test_executor.py --version Logic` in milliseconds without any Revit API dependencies, stubs, or native mocks.
2. **Revit Database Seam (`Synthetic.Tests` / `SyntheticTests.Shared`):** Translators and database operations crossing the native boundary (`RevitDOM` ↔ `Autodesk.Revit.DB`) are verified using real `Document` fixtures inside an actual Revit process via `python .agents/skills/revit-tests/scripts/test_executor.py --version 2026`.

## Designing for Mockability and Substitutability

At external system boundaries, design interfaces that are simple to substitute:

### 1. Use Dependency Injection

Pass external boundaries in rather than instantiating them directly:

```csharp
// Testable: UI dialog prompt injected via interface
public class ExportSheetIndexViewModel
{
    private readonly IFileDialogService _dialogService;

    public ExportSheetIndexViewModel(IFileDialogService dialogService)
    {
        _dialogService = dialogService;
    }

    public void RequestExportPath()
    {
        if (_dialogService.ShowSaveDialog(".xlsx") is { } path)
        {
            ExportPath = path;
        }
    }
}

// Hard to test: Creates WPF / Windows Forms dialogs inline
public class ExportSheetIndexViewModel
{
    public void RequestExportPath()
    {
        var dialog = new SaveFileDialog(); // Hard boundary coupling
        if (dialog.ShowDialog() == true) { ... }
    }
}
```

### 2. Prefer Specific Ports over Generic Proxies

Create focused interfaces for distinct operations instead of broad generic proxies:

```csharp
// GOOD: Specific, independently substitutable operations
public interface IStandardStorageService
{
    StandardTemplate LoadTemplate(string filePath);
    void SaveTemplate(string filePath, StandardTemplate template);
}

// BAD: Generic dispatcher requiring complex setup in tests
public interface IGenericDispatcher
{
    object Execute(string commandName, params object[] parameters);
}
```

The specific port approach ensures:
- Each test stand-in returns one predictable shape
- No conditional branching in test setup
- Clear observability of which operations a test touches
- Full compile-time type safety across all supported Revit versions
