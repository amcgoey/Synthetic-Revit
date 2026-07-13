using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using Synthetic.Modules.RevitDOM;
using Synthetic.Modules.StandardsManagement.Engine;
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

        [Test]
        public void RunDeepScan_ShouldFlagConflict_WhenParameterMutated()
        {
            Assert.IsNotNull(_uiapp, "Revit UIApplication context should not be null.");
            var app = _uiapp!.Application;
            Document doc = app.NewProjectDocument(UnitSystem.Metric);

            try
            {
                using (TransactionGroup txGroup = new TransactionGroup(doc, "Tier2_StandardsDiffEngineTests"))
                {
                    txGroup.Start();
                    try
                    {
                        // 1. Find a baseline element type (TextNoteType is guaranteed to exist in any project template)
                        TextNoteType? textNoteType = new FilteredElementCollector(doc)
                            .OfClass(typeof(TextNoteType))
                            .Cast<TextNoteType>()
                            .FirstOrDefault();

                        if (textNoteType == null)
                        {
                            Assert.Ignore("No TextNoteType found in the active document to test deep scan.");
                            return;
                        }

                        // 2. Extract its POCO standard representation
                        var model = (ElementTypeModel)textNoteType.ToModel(true);
                        Assert.IsNotNull(model, "Extracted ElementTypeModel should not be null.");
                        Assert.IsNotNull(model.Parameters, "Extracted model parameters should not be null.");

                        // 3. Find a writable parameter to mutate
                        var targetParam = model.Parameters.FirstOrDefault(p => !p.IsReadOnly);
                        if (targetParam == null)
                        {
                            Assert.Ignore("No writable parameters found on TextNoteType to test mutation.");
                            return;
                        }

                        // 4. Artificially mutate the value in memory
                        string originalValue = targetParam.Value ?? "";
                        string mutatedValue = originalValue + "_MutatedForTest";
                        if (targetParam.StorageType == "Double" || targetParam.StorageType == "Integer")
                        {
                            mutatedValue = "999";
                        }
                        targetParam.Value = mutatedValue;

                        // 5. Run the deep scan comparison
                        var clusters = StandardsDiffEngine.RunDeepScan(doc, new List<ElementModel> { model });

                        // 6. Assert that conflicts were successfully identified
                        Assert.IsNotNull(clusters, "RunDeepScan should return a non-null collection.");
                        Assert.IsTrue(clusters.Count > 0, "A conflict cluster should be returned.");

                        // Find the cluster matching our element type
                        var cluster = clusters.FirstOrDefault(c => c.TypeMappings.Any(m => m.SourceType != null && m.SourceType.RevitTypeId == textNoteType.Id));
                        Assert.IsNotNull(cluster, "Should find a cluster matching the tested TextNoteType.");

                        var mapping = cluster.TypeMappings.FirstOrDefault(m => m.SourceType != null && m.SourceType.RevitTypeId == textNoteType.Id);
                        Assert.IsNotNull(mapping, "Should find a type mapping for the tested TextNoteType.");

                        var conflictRow = mapping.ParameterResolutions.FirstOrDefault(r => r.ParameterName == targetParam.Name);
                        Assert.IsNotNull(conflictRow, $"A parameter resolution row should exist for the mutated parameter '{targetParam.Name}'.");
                        Assert.IsTrue(conflictRow.HasConflict, "The parameter resolution row should indicate a conflict.");
                    }
                    finally
                    {
                        txGroup.RollBack();
                    }
                }
            }
            finally
            {
                doc.Close(false);
            }
        }
    }
}
