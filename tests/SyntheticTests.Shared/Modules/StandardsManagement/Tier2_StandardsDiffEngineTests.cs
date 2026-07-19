using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.RevitDOM.Operations.Standards;
using Synthetic.Modules.MergeDuplicates.Models;

namespace SyntheticTests
{
    [TestFixture]
    public class Tier2_StandardsDiffEngineTests
    {
        private UIApplication? _uiapp;

        [OneTimeSetUp]
        public void Setup(UIApplication uiapp)
        {
            _uiapp = uiapp;
        }

        private struct MutatedTestContext
        {
            public Document Doc { get; set; }
            public TextNoteType TextNoteType { get; set; }
            public ElementTypeModel Model { get; set; }
            public ParameterModel TargetParam { get; set; }
            public TransactionGroup TxGroup { get; set; }
        }

        private MutatedTestContext CreateMutatedTextNoteTypeContext(string transactionGroupName)
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            TransactionGroup txGroup = new TransactionGroup(doc, transactionGroupName);
            txGroup.Start();

            try
            {
                TextNoteType? textNoteType = new FilteredElementCollector(doc)
                    .OfClass(typeof(TextNoteType))
                    .Cast<TextNoteType>()
                    .FirstOrDefault();

                if (textNoteType == null)
                {
                    txGroup.RollBack();
                    doc.Close(false);
                    Assert.Ignore("No TextNoteType found in the active document to test deep scan.");
                }

                var model = (ElementTypeModel)textNoteType!.ToModel(true);
                Assert.IsNotNull(model, "Extracted ElementTypeModel should not be null.");
                Assert.IsNotNull(model.Parameters, "Extracted model parameters should not be null.");

                var targetParam = model.Parameters.FirstOrDefault(p => !p.IsReadOnly);
                if (targetParam == null)
                {
                    txGroup.RollBack();
                    doc.Close(false);
                    Assert.Ignore("No writable parameters found on TextNoteType to test mutation.");
                }

                string originalValue = targetParam!.Value ?? "";
                string mutatedValue = originalValue + "_MutatedForTest";
                if (targetParam.StorageType == "Double" || targetParam.StorageType == "Integer")
                {
                    mutatedValue = "999";
                }
                targetParam.Value = mutatedValue;

                return new MutatedTestContext
                {
                    Doc = doc,
                    TextNoteType = textNoteType,
                    Model = model,
                    TargetParam = targetParam,
                    TxGroup = txGroup
                };
            }
            catch (Exception)
            {
                txGroup.RollBack();
                doc.Close(false);
                throw;
            }
        }

        [Test]
        public void RunDeepScan_ShouldFlagConflict_WhenParameterMutated()
        {
            var context = CreateMutatedTextNoteTypeContext("RunDeepScan_ShouldFlagConflict_WhenParameterMutated");
            try
            {
                // 5. Run the comparison directly on the engine
                var engine = new Synthetic.RevitDOM.Operations.Diffing.PocoToRevitDiffEngine();
                var clusters = engine.Compare(new List<ObjectModel> { context.Model }, context.Doc).ToList();

                // 6. Assert that conflicts were successfully identified
                Assert.IsNotNull(clusters, "RunDeepScan should return a non-null collection.");
                Assert.IsTrue(clusters.Count > 0, "A conflict cluster should be returned.");

                // Find the cluster matching our element type
                var cluster = clusters.FirstOrDefault(c => c.TypeMappings.Any(m => m.SourceType != null && m.SourceType.RevitTypeId == context.TextNoteType.Id));
                Assert.IsNotNull(cluster, "Should find a cluster matching the tested TextNoteType.");

                var mapping = cluster!.TypeMappings.FirstOrDefault(m => m.SourceType != null && m.SourceType.RevitTypeId == context.TextNoteType.Id);
                Assert.IsNotNull(mapping, "Should find a type mapping for the tested TextNoteType.");

                var conflictRow = mapping!.ParameterResolutions.FirstOrDefault(r => r.ParameterName == context.TargetParam.Name);
                Assert.IsNotNull(conflictRow, $"A parameter resolution row should exist for the mutated parameter '{context.TargetParam.Name}'.");
                Assert.IsTrue(conflictRow!.HasConflict, "The parameter resolution row should indicate a conflict.");
            }
            finally
            {
                context.TxGroup.RollBack();
                context.Doc.Close(false);
            }
        }

        [Test]
        public void RunDeepScan_ShouldDelegateToAnalyze_Correctly()
        {
            var context = CreateMutatedTextNoteTypeContext("RunDeepScan_ShouldDelegateToAnalyze_Correctly");
            try
            {
                var serializationEngine = new StandardSerializationEngine();
                var listModels = new List<ElementModel> { context.Model };

                var analyzeClusters = serializationEngine.Analyze(listModels, context.Doc).ToList();
                var deepScanClusters = StandardsDiffEngine.RunDeepScan(context.Doc, listModels, serializationEngine).ToList();

                Assert.IsNotNull(analyzeClusters);
                Assert.IsNotNull(deepScanClusters);
                Assert.AreEqual(analyzeClusters.Count, deepScanClusters.Count, "Analyze and RunDeepScan should return the same number of clusters.");

                if (analyzeClusters.Count > 0)
                {
                    var clusterAnalyze = analyzeClusters[0];
                    var clusterDeepScan = deepScanClusters[0];
                    Assert.AreEqual(clusterAnalyze.TypeMappings.Count, clusterDeepScan.TypeMappings.Count, "Mappings count should match.");
                }
            }
            finally
            {
                context.TxGroup.RollBack();
                context.Doc.Close(false);
            }
        }
    }
}

