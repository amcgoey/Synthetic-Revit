using System;
using System.Collections.Generic;
using NUnit.Framework;
using Autodesk.Revit.DB;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;

namespace SyntheticTests.Modules.RevitDOM
{
    // Define custom mock models and translators for testing the Dispatcher routing logic
    public class MockObjectModel : ObjectModel
    {
        public string? TestProperty { get; set; }
    }

    public class MockTranslator : IModelTranslator<Material, MockObjectModel>
    {
        public bool Extracted { get; private set; }
        public bool Injected { get; private set; }

        public void ExtractSpecifics(Material revitElement, MockObjectModel model, Document doc)
        {
            Extracted = true;
        }

        public Material? InjectSpecifics(MockObjectModel model, Material? revitElement, Document doc)
        {
            Injected = true;
            return revitElement;
        }

        void IModelTranslator.ExtractSpecifics(object revitElement, ObjectModel model, Document doc)
        {
            ExtractSpecifics((Material)revitElement, (MockObjectModel)model, doc);
        }

        object? IModelTranslator.InjectSpecifics(ObjectModel model, object? revitElement, Document doc)
        {
            return InjectSpecifics((MockObjectModel)model, (Material?)revitElement, doc);
        }
    }

    // Mock classes for One-to-Many tests
    public class MockHostObjTypeModel : ObjectModel
    {
    }

    public class MockHostObjTypeTranslator : IModelTranslator<Element, MockHostObjTypeModel>
    {
        public void ExtractSpecifics(Element revitElement, MockHostObjTypeModel model, Document doc) { }
        public Element? InjectSpecifics(MockHostObjTypeModel model, Element? revitElement, Document doc)
        {
            return revitElement;
        }

        void IModelTranslator.ExtractSpecifics(object revitElement, ObjectModel model, Document doc)
        {
            ExtractSpecifics((Element)revitElement, (MockHostObjTypeModel)model, doc);
        }

        object? IModelTranslator.InjectSpecifics(ObjectModel model, object? revitElement, Document doc)
        {
            return InjectSpecifics((MockHostObjTypeModel)model, (Element?)revitElement, doc);
        }
    }

    [TestFixture]
    public class DispatcherTests
    {
        [Test]
        public void RegisterAndResolve_SingleTypeMapping_CorrectlyRoutesByModelAndRevitTypes()
        {
            // Arrange
            var dispatcher = new ModelDispatcher(new FakeIdentityService());
            var translator = new MockTranslator();

            // Act
            dispatcher.Register<MockObjectModel, MockTranslator>(translator, typeof(Material));

            // Assert
            var resolvedByModel = dispatcher.GetTranslatorByModelType(typeof(MockObjectModel));
            var resolvedByRevit = dispatcher.GetTranslatorByRevitType(typeof(Material));

            Assert.IsNotNull(resolvedByModel, "Translator should be resolved by Model type.");
            Assert.IsNotNull(resolvedByRevit, "Translator should be resolved by Revit type.");
            Assert.AreSame(translator, resolvedByModel, "Resolved model translator instance should be the same.");
            Assert.AreSame(translator, resolvedByRevit, "Resolved Revit translator instance should be the same.");
        }

        [Test]
        public void RegisterAndResolve_OneToManyMapping_CorrectlyRoutesMultipleRevitTypesToSingleTranslator()
        {
            // Arrange
            var dispatcher = new ModelDispatcher(new FakeIdentityService());
            var translator = new MockHostObjTypeTranslator();

            // Act
            // Register a single translator mapped to multiple Revit element types (WallType and ViewPlan)
            dispatcher.Register<MockHostObjTypeModel, MockHostObjTypeTranslator>(
                translator, 
                typeof(WallType), 
                typeof(ViewPlan)
            );

            // Assert
            var resolvedByWallType = dispatcher.GetTranslatorByRevitType(typeof(WallType));
            var resolvedByViewPlan = dispatcher.GetTranslatorByRevitType(typeof(ViewPlan));
            var resolvedByModel = dispatcher.GetTranslatorByModelType(typeof(MockHostObjTypeModel));

            Assert.IsNotNull(resolvedByWallType, "Translator should resolve for WallType.");
            Assert.IsNotNull(resolvedByViewPlan, "Translator should resolve for ViewPlan.");
            Assert.IsNotNull(resolvedByModel, "Translator should resolve for MockHostObjTypeModel.");

            Assert.AreSame(translator, resolvedByWallType, "Resolved WallType translator should be the registered instance.");
            Assert.AreSame(translator, resolvedByViewPlan, "Resolved ViewPlan translator should be the registered instance.");
            Assert.AreSame(translator, resolvedByModel, "Resolved Model translator should be the registered instance.");
        }

        [Test]
        public void Register_ParameterlessConstructor_InstantiatesAndRegistersSuccessfully()
        {
            // Arrange
            var dispatcher = new ModelDispatcher(new FakeIdentityService());

            // Act
            dispatcher.Register<MockObjectModel, MockTranslator>(typeof(Material));

            // Assert
            var resolved = dispatcher.GetTranslatorByModelType(typeof(MockObjectModel));
            Assert.IsNotNull(resolved, "ModelDispatcher should instantiate and register the translator type successfully.");
            Assert.IsInstanceOf<MockTranslator>(resolved, "Instantiated translator should be of MockTranslator type.");
        }

        private T CreateMockElement<T>(long id, string name, string uniqueId) where T : Element
        {
            var elem = (T)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(T));
            typeof(Element).GetProperty("Id")?.SetValue(elem, new ElementId(id));
            typeof(Element).GetProperty("UniqueId")?.SetValue(elem, uniqueId);
            typeof(Element).GetProperty("Parameters")?.SetValue(elem, new ParameterSet());
            typeof(Element).GetProperty("Document")?.SetValue(elem, (Document)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(Document)));
            elem.Name = name;
            return elem;
        }

        [Test]
        public void Extract_WhenTypeIsIgnored_ReturnsNullSilently()
        {
            // Arrange
            var dispatcher = new ModelDispatcher(new FakeIdentityService());
            dispatcher.RegisterIgnored(typeof(Material));
            var elem = CreateMockElement<Material>(123L, "IgnoredMaterial", "uid-ignored-123");
            SerializationResultModel.ClearWarnings();

            // Act
            var result = dispatcher.Extract(elem, false);

            // Assert
            Assert.IsNull(result, "Extract should return null for ignored element.");
            Assert.IsEmpty(SerializationResultModel.CurrentThreadWarnings, "Warnings list should be empty.");
        }

        [Test]
        public void Extract_WhenTypeIsPending_ReturnsNullAndLogsWarning()
        {
            // Arrange
            var dispatcher = new ModelDispatcher(new FakeIdentityService());
            dispatcher.RegisterPending(typeof(Material));
            var elem = CreateMockElement<Material>(456L, "PendingMaterial", "uid-pending-456");
            SerializationResultModel.ClearWarnings();

            // Act
            var result = dispatcher.Extract(elem, false);

            // Assert
            Assert.IsNull(result, "Extract should return null for pending element.");
            Assert.AreEqual(1, SerializationResultModel.CurrentThreadWarnings.Count, "Exactly one warning should be logged.");
            Assert.IsTrue(SerializationResultModel.CurrentThreadWarnings[0].Contains("pending future support"), "Warning message should contain 'pending future support'.");
        }

        [Test]
        public void Extract_WhenTypeIsUnregisteredElementType_FallsBackToElementTypeModelAndLogsWarning()
        {
            // Arrange
            var dispatcher = new ModelDispatcher(new FakeIdentityService());
            var elem = CreateMockElement<FamilySymbol>(789L, "UnregisteredType", "uid-unregistered-789");
            SerializationResultModel.ClearWarnings();

            // Act
            var result = dispatcher.Extract(elem, false);

            // Assert
            Assert.IsNotNull(result, $"Extract should not return null for unregistered ElementType. Warnings: {string.Join(" | ", SerializationResultModel.CurrentThreadWarnings)}");
            Assert.IsInstanceOf<ElementTypeModel>(result, "Result should be an instance of ElementTypeModel.");
            Assert.AreEqual(1, SerializationResultModel.CurrentThreadWarnings.Count, "Exactly one warning should be logged.");
            Assert.IsTrue(SerializationResultModel.CurrentThreadWarnings[0].Contains("unsupported, falling back to generic ElementTypeModel"), "Warning message should contain 'unsupported, falling back to generic ElementTypeModel'.");
        }

        [Test]
        public void Extract_WhenTypeIsUnregisteredElement_FallsBackToElementModelAndLogsWarning()
        {
            // Arrange
            var dispatcher = new ModelDispatcher(new FakeIdentityService());
            var elem = CreateMockElement<AppearanceAssetElement>(101112L, "UnregisteredAsset", "uid-unregistered-101112");
            SerializationResultModel.ClearWarnings();

            // Act
            var result = dispatcher.Extract(elem, false);

            // Assert
            Assert.IsNotNull(result, $"Extract should not return null for unregistered Element. Warnings: {string.Join(" | ", SerializationResultModel.CurrentThreadWarnings)}");
            Assert.IsInstanceOf<ElementModel>(result, "Result should be an instance of ElementModel.");
            Assert.IsNotInstanceOf<ElementTypeModel>(result, "Result should not be an instance of ElementTypeModel.");
            Assert.AreEqual(1, SerializationResultModel.CurrentThreadWarnings.Count, "Exactly one warning should be logged.");
            Assert.IsTrue(SerializationResultModel.CurrentThreadWarnings[0].Contains("unsupported, falling back to generic ElementModel"), "Warning message should contain 'unsupported, falling back to generic ElementModel'.");
        }
    }
}
