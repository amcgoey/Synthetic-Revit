using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;

namespace ExternalNamespace
{
    public class ExternalModel : ObjectModel
    {
        public ElementIdModel? HiddenDependency { get; set; }
    }
}

namespace SyntheticTests.Modules.RevitDOM
{
    public class SimpleTestModel : ObjectModel
    {
        public ElementIdModel? DependencyId { get; set; }
        public string Name { get; set; } = string.Empty;
        public int IntVal { get; set; }
    }

    public class CircularTestModel : ObjectModel
    {
        public CircularTestModel? SelfReference { get; set; }
        public ElementIdModel? DependencyId { get; set; }
    }

    public class ComplexTestModel : ObjectModel
    {
        public List<SimpleTestModel> SimpleModels { get; set; } = new List<SimpleTestModel>();
        public SimpleTestModel? DirectNested { get; set; }
    }

    [TestFixture]
    public class RevitDomDependencyScannerTests
    {
        [Test]
        public void Scan_NullModel_ReturnsEmpty()
        {
            // Act & Assert
            // Passing null is not allowed by the signature since it expects ObjectModel,
            // but we can test scanning a model that has null properties.
            var model = new SimpleTestModel { DependencyId = null };
            var result = RevitDomDependencyScanner.Scan(model);
            Assert.IsEmpty(result);
        }

        [Test]
        public void Scan_SimpleModel_ReturnsDirectDependency()
        {
            // Arrange
            var dep = new ElementIdModel { Id = 100, Class = "WallType", Category = "Walls" };
            var model = new SimpleTestModel { DependencyId = dep, Name = "Test", IntVal = 42 };

            // Act
            var result = RevitDomDependencyScanner.Scan(model).ToList();

            // Assert
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(100, result[0].Id);
            Assert.AreEqual("WallType", result[0].Class);
        }

        [Test]
        public void Scan_CircularReference_AvoidsStackOverflow()
        {
            // Arrange
            var dep = new ElementIdModel { Id = 200, Class = "Material" };
            var model = new CircularTestModel { DependencyId = dep };
            model.SelfReference = model; // Circular loop!

            // Act
            var result = RevitDomDependencyScanner.Scan(model).ToList();

            // Assert
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(200, result[0].Id);
        }

        [Test]
        public void Scan_ComplexNestedAndCollections_ReturnsAllDependencies()
        {
            // Arrange
            var dep1 = new ElementIdModel { Id = 301, Class = "Material" };
            var dep2 = new ElementIdModel { Id = 302, Class = "FillPattern" };
            var dep3 = new ElementIdModel { Id = 303, Class = "LinePattern" };

            var nested1 = new SimpleTestModel { DependencyId = dep1 };
            var nested2 = new SimpleTestModel { DependencyId = dep2 };

            var model = new ComplexTestModel
            {
                DirectNested = new SimpleTestModel { DependencyId = dep3 },
                SimpleModels = new List<SimpleTestModel> { nested1, nested2 }
            };

            // Act
            var result = RevitDomDependencyScanner.Scan(model).ToList();

            // Assert
            Assert.AreEqual(3, result.Count);
            var ids = result.Select(r => r.Id).ToList();
            CollectionAssert.AreEquivalent(new long[] { 301, 302, 303 }, ids);
        }

        [Test]
        public void Scan_ExternalNamespaceProperty_IsIgnored()
        {
            // Arrange
            var dep = new ElementIdModel { Id = 400, Class = "Material" };
            var external = new ExternalNamespace.ExternalModel { HiddenDependency = dep };
            
            // We put the external model inside a SimpleTestModel
            // Since SimpleTestModel is in SyntheticTests namespace, it will be scanned.
            // But when it reflects into external, it should see that external's namespace is "ExternalNamespace"
            // and NOT reflect into it, thus missing the HiddenDependency.
            var model = new SimpleTestModel
            {
                DependencyId = null
            };

            // We create a wrapper class in SyntheticTests namespace to hold the external property
            var wrapper = new WrapperModel { ExternalObj = external };

            // Act
            var result = RevitDomDependencyScanner.Scan(wrapper).ToList();

            // Assert
            Assert.IsEmpty(result, "Should not inspect namespaces that do not start with 'Synthetic'.");
        }

        public class WrapperModel : ObjectModel
        {
            public ExternalNamespace.ExternalModel? ExternalObj { get; set; }
        }
    }
}
