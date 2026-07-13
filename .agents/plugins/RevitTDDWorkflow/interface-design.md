# **Interface Design for Testability**

Good interfaces make testing natural, especially when decoupling from the slow Revit API.

## **Accept dependencies, don't create them (Dependency Injection)**

Pass external dependencies in rather than creating them internally. This makes substituting handcrafted Fakes simple.

```

// GOOD: Testable class. We can pass a FakeIElementProvider in our fast NUnit tests.
public class WallAnalyzer
{
    private readonly IElementProvider _provider;
    
    public WallAnalyzer(IElementProvider provider)
    {
        _provider = provider;
    }
    
    public int CountStructuralWalls() { /* ... */ }
}

// BAD: Hard to test. It directly instantiates a Revit FilteredElementCollector, forcing a slow Revit API boot to test basic logic.
public class WallAnalyzer
{
    public int CountStructuralWalls(Document doc)
    {
        var collector = new FilteredElementCollector(doc);
        // ...
    }
}

```

## **Return results, don't produce side effects**

When possible, calculate state changes in memory and return the result as pure data, rather than modifying the Revit Document immediately.

```

// GOOD: Testable pure function. We can test the math instantly without a Revit Transaction.
public double CalculateTotalVolume(IEnumerable<ElementData> elements)
{
    return elements.Sum(e => e.Volume);
}

// BAD: Hard to test. Mutates state and requires a live Revit Transaction just to verify the math.
public void ApplyVolumeToSharedParameter(Document doc, List<Element> elements)
{
    using var t = new Transaction(doc, "Set Vol");
    // ... math and DB mutation intertwined
}

```

## **Small surface area**

Fewer public methods \= fewer tests needed. Fewer parameters \= simpler test setup.
