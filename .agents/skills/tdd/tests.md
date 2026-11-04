# Good and Bad Tests

## Good Tests

**Integration-style**: Test through real public interfaces, not mocks of internal parts.

### Headless RevitDOM POCO Seam (`SyntheticLogic.Tests`)

Tests domain logic over pure C# models headlessly without Revit dependencies:

```csharp
// GOOD: Tests observable behavior through the public interface of StandardsMerge
[Test]
public void Merge_WhenDuplicateElementEncountered_AppendsAliasToSurvivorAndPurgesDuplicate()
{
    var survivor = new ElementModel { Class = "Autodesk.Revit.DB.Material", Name = "Material - Oak" };
    var duplicate = new ElementModel { Class = "Autodesk.Revit.DB.Material", Name = "Material - Pine" };
    var engine = new StandardsMergeEngine();

    var result = engine.Merge(new[] { survivor, duplicate }, primarySelector: m => m.Name == "Material - Oak");

    Assert.That(result.RemainingElements, Has.Count.EqualTo(1));
    Assert.That(result.RemainingElements[0].Name, Is.EqualTo("Material - Oak"));
    Assert.That(result.RemainingElements[0].Aliases, Does.Contain("Material - Pine"));
}
```

### Revit Database Seam (`Synthetic.Tests` / `SyntheticTests.Shared`)

Tests live database modifications against a real Revit `Document`:

```csharp
// GOOD: Tests observable Revit database state through public translation interface
[Test]
public void StandardSerializationEngine_ApplyTemplate_CreatesMaterialInDocument()
{
    var template = new MaterialModel { Name = "Synthetic Finish 01", Color = new ColorModel(255, 0, 0) };
    var engine = new StandardSerializationEngine(_doc);

    using (var trans = new Transaction(_doc, "Apply Material"))
    {
        trans.Start();
        engine.Apply(template);
        trans.Commit();
    }

    var created = new FilteredElementCollector(_doc)
        .OfClass(typeof(Material))
        .Cast<Material>()
        .FirstOrDefault(m => m.Name == "Synthetic Finish 01");

    Assert.That(created, Is.Not.Null);
    Assert.That(created.Color.Red, Is.EqualTo(255));
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
// BAD: Bypasses the module interface to inspect private dictionaries or backing stores
[Test]
public void RegisterElement_AddsToInternalDictionary()
{
    var identityService = new PocoIdentityService();
    identityService.Register("KEY-123", new ElementModel { Name = "Door" });

    var internalMap = (Dictionary<string, ElementModel>)typeof(PocoIdentityService)
        .GetField("_cache", BindingFlags.NonPublic | BindingFlags.Instance)
        .GetValue(identityService);

    Assert.That(internalMap.ContainsKey("KEY-123"), Is.True);
}

// GOOD: Verifies through the public interface
[Test]
public void RegisterElement_MakesElementResolvableByKey()
{
    var identityService = new PocoIdentityService();
    var element = new ElementModel { Name = "Door" };

    identityService.Register("KEY-123", element);
    var resolved = identityService.Resolve("KEY-123");

    Assert.That(resolved, Is.SameAs(element));
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
