# When to Mock in a Revit Add-in

Mock at **system boundaries** only:

- External file system and disk I/O (JSON template exports, **Dot Sheet Index** `.xlsx` workbooks)
- External process or network boundaries
- User interaction and UI prompts (`FakeFileDialogService`, `FakeFolderDialogService`, `FakeUserPromptService`, `FakeGuardrailPromptService` in [`tests/Fixtures/Shared/`](../../../tests/Fixtures/Shared/))
- Slice Revit seams (`FakeSheetIndexAdapter`, `FakeSharedCoordinatesAdapter`, `FakeShadowStudyAdapter` implementing `I<Slice>Adapter`)
- System clock and randomness

Don't mock:

- Your own classes and modules (`RevitDOM` models, comparison engines, serialization pipelines)
- Internal collaborators
- Anything you control

## Autodesk.Revit.DB is NOT a Mockable Boundary

`Autodesk.Revit.DB` (`Document`, `Element`, `Parameter`, `Transaction`, `FilteredElementCollector`) is a native C++-backed interop layer. The Revit API classes lack public constructors, rely on internal native pointers (`m_pElement`), and cannot be safely mocked headlessly via Moq or NSubstitute. Mocking Revit API objects with dynamic proxies causes `InternalException`s, access violations, or brittle tests that pass while failing inside real Revit.

### The Solution: Partitioned `Core/` + `I<Slice>Adapter` Seams

Synthetic isolates Revit database calls in `Addin/` behind `I<Slice>Adapter` interfaces and **RevitDOM** translators:

1. **Headless Logic Seam (`Synthetic.LogicTests`):** Pure `Core/` modules, ViewModels, `Fake<Slice>Adapter` test doubles, and **RevitDOM** POCOs (`ElementModel`, `ParameterModel`, `CategoryModel`) execute headlessly across all [`Synthetic.LogicTests.csproj`](../../../tests/Projects/Synthetic.LogicTests/Synthetic.LogicTests.csproj) target frameworks via `python .agents/skills/revit-tests/scripts/test_executor.py --version Logic` with zero Revit API dependencies or mocks.
2. **Revit Database Seam (`Synthetic.RevitTests.<Year>`):** `Revit<Slice>Adapter` implementations and **RevitDOM** translators (`RevitDOM` ↔ `Autodesk.Revit.DB`) run inside live Revit `Document` fixtures (`RevitTemplateRollbackFixture` / `RevitIsolatedDocumentFixture`) via `python .agents/skills/revit-tests/scripts/test_executor.py --version <CURRENT_LATEST_VERSION>` (see [`revit-config.md`](../../../docs/agents/revit-config.md)).

## Designing for Mockability and Substitutability

At external system boundaries, design interfaces that are simple to substitute:

### 1. Use Dependency Injection

Pass external boundaries in rather than instantiating them directly:

```csharp
// Testable: UI dialog prompt injected via Platform/UI interface
public class ExportSettingsViewModel
{
    private readonly IFileDialogService _dialogService;

    public ExportSettingsViewModel(IFileDialogService dialogService)
    {
        _dialogService = dialogService;
    }

    public void RequestExportPath()
    {
        if (_dialogService.SaveFileDialog("JSON Files (*.json)|*.json", "Export Settings", "settings.json") is string path)
        {
            ExportPath = path;
        }
    }
}

// Hard to test: Creates modal SaveFileDialog directly inside ViewModel
public class ExportSettingsViewModel
{
    public void RequestExportPath()
    {
        var dialog = new SaveFileDialog(); // Violates FitnessRule.ExternalEventAndDialogPurity
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
