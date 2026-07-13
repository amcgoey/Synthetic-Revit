# **Mocking**

## **The Core Rule: Don't mock what you own.**

Mocking should be reserved strictly for architectural boundaries and external dependencies that are slow, non-deterministic, or hard to set up.

## **The Revit API as a Boundary**

Because booting Revit via `ricaun.RevitTest` is slow, the Revit API itself should be treated as an external boundary whenever possible.

* Extract data from Revit `Element` objects into pure C# Data Transfer Objects (DTOs) or primitive types.  
* Pass those pure types into your business logic managers.  
* Test your business logic using standard, lightning-fast NUnit tests.  
* Only use `ricaun.RevitTest` for the "Integration" phase to verify that your data successfully reads/writes to the actual Revit Document.

## **Good Mocking**

* Mocking at the edges: File System, External HTTP APIs, or the Revit API wrapping layer.  
* Using **Fakes** (simple, handcrafted in-memory classes that implement your interfaces) instead of complex third-party Mocking frameworks.

## **Bad Mocking**

* Mocking internal classes or collaborators just to mathematically isolate a single unit test.  
* Mocking pure functions, domain logic, or calculation engines.  
* "Mockist" TDD: Asserting that Method A called Method B. (We care about the final observable state, not the internal wiring).  
* Over-specifying mock setups, which leads to tests breaking when refactoring internal logic.

## **Examples**

### **Bad: Mocking Internal Collaborators**

```
// BAD: We own ViewAnalyzer and it contains pure business logic. 
// We shouldn't mock it to test the ViewManager. We should test the real ViewManager WITH the real ViewAnalyzer.

// BAD: Asserting on internal implementation details (verifying a method was called).
// We only care about the final observable state.
```

### **Good: Using a Handcrafted Fake for an Architectural Boundary**

```
// GOOD: IFileExportService represents a boundary to the OS file system.
// We handcraft a simple "Fake" so our automated tests don't write actual files to disk and remain fast.

// 1. Handcraft a Fake
public class FakeFileExporter : IFileExportService 
{
    public string ExportedPath { get; private set; }
    public string ExportedPayload { get; private set; }

    public void ExportToDisk(string path, string payload) 
    {
        ExportedPath = path;
        ExportedPayload = payload;
    }
}

// 2. Test using the Fake
[Test]
public void ReportCommand_GeneratesCorrectPayload()
{
    // Arrange
    var fakeExporter = new FakeFileExporter();
    var reportCommand = new ReportCommand(fakeExporter);

    // Act using real internal logic
    reportCommand.Execute(new List<string> { "Data1", "Data2" });

    // Assert that the system correctly crossed the boundary with the right payload
    Assert.That(fakeExporter.ExportedPayload, Is.EqualTo("Expected_Payload_String"));
}
```
