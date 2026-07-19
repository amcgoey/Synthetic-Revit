using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using System.Threading;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;

namespace SyntheticTests
{
    [TestFixture]
    public class StandardSerializationEngineTests
    {
        private UIApplication? _uiapp;

        [OneTimeSetUp]
        public void Setup(UIApplication uiapp)
        {
            _uiapp = uiapp;
        }

        private class StubLevelTranslator : IModelTranslator
        {
            public bool WasInjected { get; private set; }

            public void ExtractSpecifics(object revitElement, ObjectModel model, Document doc) { }

            public object? InjectSpecifics(ObjectModel model, object? revitElement, Document doc)
            {
                WasInjected = true;
                if (revitElement is Element elem)
                {
                    elem.Name = "ModifiedByStubTranslator";
                    return elem;
                }
                return revitElement;
            }
        }

        private class UnregisteredDummyModel : ElementModel
        {
        }

        [Test]
        public void ToRevit_GracefulDegradation_CapturesFailedAndSuccessfulTransactions()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                // Find a default level to modify
                Level? defaultLevel = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .Cast<Level>()
                    .FirstOrDefault();

                Assert.IsNotNull(defaultLevel, "A default Level should exist.");

                // Create a successful model using ElementModel targeting the level
                var successModel = new ElementModel
                {
                    ElementId = defaultLevel!.Id.ToModel(doc),
                    Name = "SomeDifferentName"
                };

                // Create a failed model (e.g. UnregisteredDummyModel) targeting a level or fake element,
                // but we won't register any translator for UnregisteredDummyModel so it will fail with NotSupportedException.
                var failModel = new UnregisteredDummyModel
                {
                    Name = "FakeHost"
                };

                var engine = new StandardSerializationEngine();
                
                // Register our stub translator for ElementModel
                var stubTranslator = new StubLevelTranslator();
                engine.Dispatcher.Register<ElementModel, StubLevelTranslator>(stubTranslator, typeof(Level));

                var modelsBatch = new List<ObjectModel> { successModel, failModel };

                // Act
                IEnumerable<SerializationResultModel> results = engine.ToRevit(modelsBatch, doc);
                var resultsList = results.ToList();

                // Assert
                Assert.AreEqual(2, resultsList.Count, "Should return results for both models.");

                var successResult = resultsList.FirstOrDefault(r => r.Model == successModel);
                Assert.IsNotNull(successResult, "Success result should exist.");
                Assert.IsTrue(successResult!.Success, "The valid model should report success.");
                Assert.IsNull(successResult.ErrorMessage, "Success result should have no error message.");

                var failResult = resultsList.FirstOrDefault(r => r.Model == failModel);
                Assert.IsNotNull(failResult, "Failure result should exist.");
                Assert.IsFalse(failResult!.Success, "The invalid model should report failure.");
                Assert.IsNotNull(failResult.ErrorMessage, "Failure result should contain an error message.");
                Assert.IsTrue(failResult.ErrorMessage!.Contains("No translator registered"), "Error message should mention registration failure.");

                // Assert that the successful element was actually modified and committed
                Assert.AreEqual("ModifiedByStubTranslator", defaultLevel.Name, "The successful model modifications should be committed.");
                Assert.IsTrue(stubTranslator.WasInjected, "Translator InjectSpecifics should have run.");
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void ToRevit_Cancellation_RollsBackAllChangesAndThrowsException()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                // Find default level
                Level? defaultLevel = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .Cast<Level>()
                    .FirstOrDefault();

                Assert.IsNotNull(defaultLevel, "A default Level should exist.");
                string originalName = defaultLevel.Name;

                // Create a valid model targeting the level
                var successModel = new ElementModel
                {
                    ElementId = defaultLevel!.Id.ToModel(doc),
                    Name = defaultLevel.Name
                };

                var engine = new StandardSerializationEngine();
                var stubTranslator = new StubLevelTranslator();
                engine.Dispatcher.Register<ElementModel, StubLevelTranslator>(stubTranslator, typeof(Level));

                // Create a cancellation token that is canceled
                var cts = new CancellationTokenSource();
                cts.Cancel();

                // Act & Assert
                Assert.Throws<OperationCanceledException>(() =>
                {
                    engine.ToRevit(new List<ObjectModel> { successModel }, doc, cancellationToken: cts.Token);
                }, "Should throw OperationCanceledException when token is cancelled.");

                // Assert that the level was NOT modified because the outer transaction group was rolled back
                Assert.AreEqual(originalName, defaultLevel.Name, "Changes must be rolled back on cancellation.");
            }
            finally
            {
                doc.Close(false);
            }
        }

        [Test]
        public void ToRevit_UnchangedElement_SkipsModificationsAndReturnsUnchanged()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                // Find a default level to target
                Level? defaultLevel = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .Cast<Level>()
                    .FirstOrDefault();

                Assert.IsNotNull(defaultLevel, "A default Level should exist.");
                string originalName = defaultLevel.Name;

                // Create a model matching the level exactly
                var unchangedModel = new ElementModel
                {
                    ElementId = defaultLevel!.Id.ToModel(doc),
                    Name = defaultLevel.Name
                };

                var engine = new StandardSerializationEngine();
                
                // Register our stub translator for ElementModel
                var stubTranslator = new StubLevelTranslator();
                engine.Dispatcher.Register<ElementModel, StubLevelTranslator>(stubTranslator, typeof(Level));

                // Act
                var resultsList = engine.ToRevit(new List<ObjectModel> { unchangedModel }, doc).ToList();

                // Assert
                Assert.AreEqual(1, resultsList.Count);
                var result = resultsList[0];
                Assert.IsTrue(result.Success);
                Assert.AreEqual("Unchanged", result.Action, "Should be flagged as Unchanged.");
                Assert.IsFalse(stubTranslator.WasInjected, "InjectSpecifics should be skipped when element is unchanged.");
                Assert.AreEqual(originalName, defaultLevel.Name, "Level name should not have changed.");
            }
            finally
            {
                doc.Close(false);
            }
        }
    }
}
