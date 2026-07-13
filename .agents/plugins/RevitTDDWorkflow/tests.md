# **Tests**

## **Good Tests**

* Tests behavior users/callers care about.  
* Uses public API only.  
* Survives internal refactors.  
* Describes WHAT, not HOW.  
* One logical assertion per test.

### **Integration-style**

Test through real interfaces, not mocks of internal parts.

```

// GOOD: Tests observable behavior through the public manager
[Test]
public void RenameViews_WithValidPrefix_UpdatesViewNames()
{
    // Arrange
    var viewManager = new ViewManager(Document);
    var viewsToRename = new List<View> { view1, view2 };

    // Act
    viewManager.ApplyPrefix(viewsToRename, "EXT_");

    // Assert
    // We only care that the final state is correct, not how the strings were combined internally.
    Assert.That(view1.Name, Does.StartWith("EXT_"));
    Assert.That(view2.Name, Does.StartWith("EXT_"));
}

```

## **Bad Tests**

Implementation-detail tests: Coupled to internal structure.

```

// BAD: Tests implementation details and mocks internal collaborators
[Test]
public void ApplyPrefix_CallsInternalStringFormatter()
{
    // Arrange
    var mockFormatter = new Mock<IStringFormatter>();
    var viewManager = new ViewManager(Document, mockFormatter.Object);
    
    // Act
    viewManager.ApplyPrefix(viewsToRename, "EXT_");

    // Assert
    // RED FLAG: Fails if we later refactor to remove IStringFormatter and just do formatting inline!
    mockFormatter.Verify(f => f.Format("EXT_", It.IsAny<string>()), Times.Exactly(2)); 
}

```

### **Red flags**

* Mocking internal collaborators.  
* Testing private methods.  
* Asserting on call counts/order.  
* Test breaks when refactoring without behavior change.  
* Test name describes HOW not WHAT.  
* Verifying through external means instead of interface.

```

// BAD: Bypasses interface to verify (e.g., querying raw Extensible Storage instead of the manager)
[Test]
public void SaveConfig_WritesToExtensibleStorage()
{
    var config = new ProjectConfig { Prefix = "INT_" };
    ConfigManager.Save(config, Document);
    
    // Bypassing the public interface to verify internal database state
    var entity = Document.ProjectInformation.GetEntity(Schema);
    Assert.That(entity.IsValid(), Is.True);
}

// GOOD: Verifies through the public interface
[Test]
public void SaveConfig_MakesConfigRetrievable()
{
    var config = new ProjectConfig { Prefix = "INT_" };
    ConfigManager.Save(config, Document);
    
    // Retrieving through the same public interface
    var retrieved = ConfigManager.Load(Document);
    Assert.That(retrieved.Prefix, Is.EqualTo("INT_"));
}

```
