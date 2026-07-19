using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Autodesk.Revit.DB;
using NUnit.Framework;
using Synthetic.Modules.RevitDOM;
using Synthetic.Modules.StandardsManagement.Engine;
using Synthetic.Modules.StandardsManagement.Models;
using Synthetic.Modules.StandardsManagement.Utilities;
using Synthetic.Modules.StandardsManagement.ViewModels;
using Synthetic.Shared.UI;
using Synthetic.Modules.MergeDuplicates.Models;

namespace SyntheticTests.Modules.StandardsManagement
{
    [TestFixture]
    public class StandardsExecutionPipelineTests
    {
        private Document _doc = null!;
        private ConfigurableSerializationEngine _fakeEngine = null!;
        private ConfigurableExportService _fakeExportService = null!;
        private FakeFamilyEnforcer _fakeFamilyEnforcer = null!;

        [SetUp]
        public void Setup()
        {
            _doc = (Document)Activator.CreateInstance(typeof(Document), true)!;
            _fakeEngine = new ConfigurableSerializationEngine();
            _fakeExportService = new ConfigurableExportService();
            _fakeFamilyEnforcer = new FakeFamilyEnforcer();
            ProgressCoordinator.SuppressUI = true;
        }

        [Test]
        public void Execute_WithWriteDatabaseTrue_CallsSerializationEngineAndSucceeds()
        {
            // Arrange
            var pipeline = new StandardsExecutionPipeline(_fakeEngine, _fakeExportService, _fakeFamilyEnforcer);
            var options = new StandardsExecutionOptions
            {
                WriteRevitDatabase = true,
                SaveLocalFiles = false,
                UseTransactionGroup = false
            };

            var model = new ElementModel { Name = "TestStandard", Class = "Autodesk.Revit.DB.TextNoteType" };
            var item = new StandardsExecutionItem(model)
            {
                WillEnforce = true,
                WillSave = false
            };

            bool ToRevitCalled = false;
            _fakeEngine.ToRevitHandler = (models, doc) =>
            {
                ToRevitCalled = true;
                Assert.AreEqual(_doc, doc);
                Assert.AreEqual(1, models.Count());
                Assert.AreEqual(model, models.First());
                return new List<SerializationResultModel> { new SerializationResultModel(model) };
            };

            // Act
            var result = pipeline.Execute(_doc, new[] { item }, options);

            // Assert
            Assert.IsTrue(ToRevitCalled, "ToRevit should have been called.");
            Assert.IsTrue(result.Success, "Execution should be successful.");
            Assert.AreEqual(1, result.Items.Count);
            Assert.AreEqual("Created", result.Items[0].Action);
        }

        [Test]
        public void Execute_WithSaveLocalFilesTrue_CallsExportService()
        {
            // Arrange
            var pipeline = new StandardsExecutionPipeline(_fakeEngine, _fakeExportService, _fakeFamilyEnforcer);
            var options = new StandardsExecutionOptions
            {
                WriteRevitDatabase = false,
                SaveLocalFiles = true,
                StandardsFilePath = @"C:\Temp\TestStandards.json"
            };

            var model = new ElementModel { Name = "TestStandard", Class = "Autodesk.Revit.DB.TextNoteType" };
            var item = new StandardsExecutionItem(model)
            {
                WillEnforce = false,
                WillSave = true
            };

            // Act
            var result = pipeline.Execute(_doc, new[] { item }, options);

            // Assert
            Assert.IsTrue(_fakeExportService.ExportCalled, "Export should have been called.");
            Assert.AreEqual(@"C:\Temp\TestStandards.json", _fakeExportService.TargetPathReceived);
            Assert.IsTrue(result.Success, "Execution should be successful.");
            Assert.AreEqual("Saved", result.Items[0].Action);
        }

        [Test]
        public void Execute_WhenDbPhaseThrows_RollsBackAndFails()
        {
            // Arrange
            var pipeline = new StandardsExecutionPipeline(_fakeEngine, _fakeExportService, _fakeFamilyEnforcer);
            var options = new StandardsExecutionOptions
            {
                WriteRevitDatabase = true,
                SaveLocalFiles = true,
                UseTransactionGroup = false
            };

            var model = new ElementModel { Name = "TestStandard", Class = "Autodesk.Revit.DB.TextNoteType" };
            var item = new StandardsExecutionItem(model)
            {
                WillEnforce = true,
                WillSave = true
            };

            _fakeEngine.ToRevitHandler = (models, doc) =>
            {
                throw new InvalidOperationException("Simulated database write crash.");
            };

            // Act
            var result = pipeline.Execute(_doc, new[] { item }, options);

            // Assert
            Assert.IsFalse(result.Success, "Execution should fail when DB writes throw.");
            Assert.IsFalse(_fakeExportService.ExportCalled, "Export should not be called if DB writes fail.");
            Assert.AreEqual("Failed", result.Items[0].Action);
            Assert.IsTrue(result.Items[0].Message.Contains("Simulated database write crash."));
        }

        [Test]
        public void Execute_WithProcessFamiliesTrue_ProcessesFamiliesAndHandlesFamilyException()
        {
            // Arrange
            var fakeFamilyEnforcer = new FakeFamilyEnforcer();
            var pipeline = new StandardsExecutionPipeline(_fakeEngine, _fakeExportService, fakeFamilyEnforcer);
            var options = new StandardsExecutionOptions
            {
                WriteRevitDatabase = true,
                SaveLocalFiles = false,
                ProcessFamilies = true,
                UseTransactionGroup = false
            };

            var model = new ElementModel { Name = "TestMaterial", Class = "Autodesk.Revit.DB.Material" };
            var item = new StandardsExecutionItem(model)
            {
                WillEnforce = true,
                WillSave = false
            };

            _fakeEngine.ToRevitHandler = (models, doc) =>
            {
                return new List<SerializationResultModel> { new SerializationResultModel(model) };
            };

            fakeFamilyEnforcer.EnforceHandler = (doc, standards, opts, progress, dbResults, token) =>
            {
                var failedModel = new ElementModel
                {
                    Name = "FaultyFamily",
                    Class = "Autodesk.Revit.DB.Family"
                };
                var result = new SerializationResultModel(failedModel, "Failed to update family document contents")
                {
                    OperationTarget = StandardsPipelineConstants.TargetDatabase,
                    Action = StandardsPipelineConstants.ActionFailed,
                    Message = "Failed to update family document contents"
                };
                dbResults.Add(result);
            };

            // Act
            var result = pipeline.Execute(_doc, new[] { item }, options);

            // Assert
            Assert.IsTrue(result.Success, $"Overall execution can still succeed with individual family error isolation. Message: {result.Items.FirstOrDefault()?.Message}. Report: {result.ReportMarkdown}");
            var familyResult = result.Items.FirstOrDefault(i => i.Model.GetType().Name == "Family");
            Assert.IsNull(familyResult, "Family itself was not in the input items.");
            
            // Check that the markdown report contains the failed log item
            Assert.IsTrue(result.ReportMarkdown.Contains("FaultyFamily"), $"Report should contain the family name. Report: {result.ReportMarkdown}");
            Assert.IsTrue(result.ReportMarkdown.Contains("Failed to update family document contents"), "Report should contain the error detail.");
        }

        private T CreateMockElement<T>(Document doc, string name, int idVal) where T : Element
        {
            var elem = (T)Activator.CreateInstance(typeof(T), true)!;
            
            // Set Name
            var nameProp = typeof(T).GetProperty("Name");
            nameProp?.SetValue(elem, name);

            // Set Id
            var idProp = typeof(T).GetProperty("Id");
            if (idProp != null && idProp.CanWrite)
            {
                idProp.SetValue(elem, new ElementId(idVal));
            }

            // Add to doc
            try
            {
                var addElemMethod = doc.GetType().GetMethod("AddElement");
                if (addElemMethod != null)
                {
                    addElemMethod.Invoke(doc, new object[] { elem, elem.Id });
                }
            }
            catch (Exception)
            {
            }

            return elem;
        }

        [Test]
        public void Execute_WithCancellationTokenCancelled_AbortsAndReturnsFailure()
        {
            // Arrange
            var pipeline = new StandardsExecutionPipeline(_fakeEngine, _fakeExportService, _fakeFamilyEnforcer);
            var options = new StandardsExecutionOptions
            {
                WriteRevitDatabase = true,
                SaveLocalFiles = false,
                UseTransactionGroup = false
            };

            var model = new ElementModel { Name = "TestStandard", Class = "Autodesk.Revit.DB.TextNoteType" };
            var item = new StandardsExecutionItem(model)
            {
                WillEnforce = true,
                WillSave = false
            };

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            // Act
            var result = pipeline.Execute(_doc, new[] { item }, options, cancellationToken: cts.Token);

            // Assert
            Assert.IsFalse(result.Success, "Execution should be marked unsuccessful on cancellation.");
            Assert.AreEqual("Canceled", result.Items[0].Action);
            Assert.AreEqual("Execution cancelled by user.", result.Items[0].Message);
        }
    }

    public class ConfigurableSerializationEngine : IStandardSerializationEngine
    {
        public Func<IEnumerable<ObjectModel>, Document, IEnumerable<SerializationResultModel>>? ToRevitHandler { get; set; }

        public IEnumerable<ObjectModel> ByRevit(IEnumerable<Element> elements, Document doc, bool isTemplate, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
        {
            return new List<ObjectModel>();
        }

        public IEnumerable<DuplicateClusterModel> Analyze(IEnumerable<ObjectModel> models, Document doc, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
        {
            return new List<DuplicateClusterModel>();
        }

        public IEnumerable<SerializationResultModel> ToRevit(IEnumerable<ObjectModel> models, Document doc, IProgress<string>? progress = null, CancellationToken cancellationToken = default, IFailuresPreprocessor? failuresPreprocessor = null)
        {
            if (ToRevitHandler != null)
            {
                return ToRevitHandler(models, doc);
            }
            return models.Select(m => new SerializationResultModel(m));
        }

        public ObjectModel? ExtractCategory(Category category, Document doc, bool isTemplate)
        {
            return null;
        }
    }

    public class ConfigurableExportService : IStandardsExportService
    {
        public bool ExportCalled { get; set; }
        public string? TargetPathReceived { get; set; }
        public Func<List<QueueItemModel>, string?, List<SerializationResultModel>, HashSet<string>, List<SerializationResultModel>>? ExportHandler { get; set; }

        public List<SerializationResultModel> Export(
            List<QueueItemModel> fileItems,
            string? targetPath,
            List<SerializationResultModel> dbResults,
            HashSet<string> protectedPaths,
            out string? finalPathUsed)
        {
            ExportCalled = true;
            TargetPathReceived = targetPath;
            finalPathUsed = targetPath ?? @"C:\Temp\Exported.json";

            if (ExportHandler != null)
            {
                return ExportHandler(fileItems, targetPath, dbResults, protectedPaths);
            }

            return fileItems.Select(item => new SerializationResultModel(item.Model)).ToList();
        }
    }

    public class FakeFamilyEnforcer : IFamilyEnforcer
    {
        public Action<Document, IEnumerable<ElementModel>, StandardsExecutionOptions, Action<string, string, int>, List<SerializationResultModel>, CancellationToken>? EnforceHandler { get; set; }

        public void Enforce(
            Document doc,
            IEnumerable<ElementModel> standards,
            StandardsExecutionOptions options,
            Action<string, string, int> reportProgress,
            List<SerializationResultModel> dbResults,
            CancellationToken cancellationToken)
        {
            if (EnforceHandler != null)
            {
                EnforceHandler(doc, standards, options, reportProgress, dbResults, cancellationToken);
            }
        }
    }
}
