# Good and Bad Tests

## Good Tests

**Integration-style**: Test through real public interfaces, not mocks of internal parts.

### Headless Logic Seam (`Synthetic.LogicTests`, `[UnitTest]`)

Tests pure `Core/` logic, ViewModels, and **RevitDOM** models headlessly across all [`Synthetic.LogicTests.csproj`](../../../tests/Projects/Synthetic.LogicTests/Synthetic.LogicTests.csproj) target frameworks without Revit dependencies:

```csharp
// GOOD: Tests observable behavior and Typed Outcome Object at the public seam
[TestFixture]
[UnitTest]
public class SheetIndexExporterTests
{
    [Test]
    public void Export_WithSelectedSheetsAndRevisions_ReturnsSucceededOutcomeAndWritesWorkbook()
    {
        var revisions = new[] { new SheetIndexRevisionModel("rev-1", "Permit Set", "2026-01-15", 1) };
        var sheets = new[] { new SheetIndexSheetModel("s-1", "A101", "Floor Plan", new[] { "rev-1" }) };
        string outputPath = Path.Combine(Path.GetTempPath(), $"DotSheetIndex_{Guid.NewGuid():N}.xlsx");

        SheetIndexExportOutcome outcome = new SheetIndexExporter().Export(sheets, revisions, outputPath);

        Assert.IsTrue(outcome.Succeeded);
        Assert.AreEqual(1, outcome.ExportedSheetCount);
        Assert.AreEqual(1, outcome.ExportedRevisionColumnCount);
        Assert.IsTrue(File.Exists(outputPath));
    }
}
```

### Revit Database Seam (`Synthetic.RevitTests.<Year>`, `[RevitAdapterTest]`)

Tests live database modifications against a cached baseline `Document` via `RevitTemplateRollbackFixture` and `DocumentTransactionExtensions`:

```csharp
// GOOD: Tests observable Revit database state inside a rolled-back adapter fixture
[TestFixture]
[RevitAdapterTest]
public class StandardSerializationEngineRevitTests : RevitTemplateRollbackFixture
{
    [Test]
    public void ToRevit_MaterialModel_UpdatesMaterialInDocument()
    {
        WithRolledBackDocument(doc =>
        {
            var template = new MaterialModel { Name = "Synthetic Finish 01", Color = new ColorModel(255, 0, 0) };
            var engine = new StandardSerializationEngine();

            IEnumerable<SerializationResultModel> results = engine.ToRevit(new[] { template }, doc);

            Assert.IsTrue(results.All(r => r.Success));
            Material? created = new FilteredElementCollector(doc)
                .OfClass(typeof(Material))
                .Cast<Material>()
                .FirstOrDefault(m => m.Name == "Synthetic Finish 01");
            Assert.IsNotNull(created);
            Assert.AreEqual(255, created!.Color.Red);
        });
    }
}
```

Characteristics of good tests:

- Tests behavior users and callers care about
- Uses public interface only
- Survives internal refactors
- Describes WHAT, not HOW
- One logical assertion per test

## Bad Tests

**Implementation-detail tests**: Coupled to internal structure.

```csharp
// BAD: Tests internal collaborator calls instead of observable outcome
[Test]
public void MergeQueueItems_CallsInternalPurgeHelper()
{
    var mockPurgeService = new Mock<IInternalPurgeService>();
    var vm = new ProjectStandardsDashboardViewModel(_doc, mockPurgeService.Object);

    vm.ExecuteMerge();

    mockPurgeService.Verify(p => p.PurgeUnreferencedInternal(It.IsAny<string>()), Times.Once());
}
```

Red flags:

- Mocking internal collaborators
- Testing private methods or internal state
- Asserting on internal call counts or execution order
- Test breaks when refactoring even though public behavior hasn't changed
- Test name describes HOW, not WHAT

```csharp
// BAD: Bypasses the module interface to inspect private fields via reflection
[Test]
public void ViewModel_TracksAllProjectSheetsInternally()
{
    var snapshot = new SheetIndexSnapshot(new[] { new SheetIndexSheetModel("s-1", "A101", "Plan") }, ...);
    var vm = new ExportSheetIndexViewModel(snapshot);

    var internalSheets = (List<SheetIndexSheetModel>)typeof(ExportSheetIndexViewModel)
        .GetField("_allProjectSheets", BindingFlags.NonPublic | BindingFlags.Instance)!
        .GetValue(vm)!;

    Assert.That(internalSheets, Has.Count.EqualTo(1));
}

// GOOD: Verifies observable selection behavior through the public ViewModel interface
[Test]
public void ViewModel_LoadsSnapshotSheetsIntoPublicCollection()
{
    var snapshot = new SheetIndexSnapshot(new[] { new SheetIndexSheetModel("s-1", "A101", "Plan") }, ...);
    var vm = new ExportSheetIndexViewModel(snapshot);

    Assert.That(vm.Sheets, Has.Count.EqualTo(1));
    Assert.That(vm.Sheets[0].SheetNumber, Is.EqualTo("A101"));
}
```

**Tautological tests**: Expected value restates the implementation, so the test passes by construction.

```csharp
// BAD: Expected value is recomputed the way the code computes it
[Test]
public void ComputeStandardElementKey_FormatsClassAndName()
{
    var model = new ElementModel { Class = "WallType", Name = "Exterior 8in" };
    var expected = $"{model.Class}::{model.Name}"; // Re-computing implementation logic!
    Assert.That(IdentityUtility.ComputeKey(model), Is.EqualTo(expected));
}

// GOOD: Expected value is an independent, known-good literal
[Test]
public void ComputeStandardElementKey_FormatsClassAndName()
{
    var model = new ElementModel { Class = "WallType", Name = "Exterior 8in" };
    Assert.That(IdentityUtility.ComputeKey(model), Is.EqualTo("WallType::Exterior 8in"));
}
```
