using System;
using System.Collections.Generic;
using NUnit.Framework;
using Autodesk.Revit.DB;
using Synthetic.Modules.MergeDuplicates.Models;
using Synthetic.Modules.MergeDuplicates.ViewModels;

namespace SyntheticTests.Modules.StandardsManagement
{
    [TestFixture]
    public class DuplicateClusterModelTests
    {
        [Test]
        public void CommitToQueue_ShouldUpdateClusterParameterResolutions_WithWinningValues()
        {
            // Arrange
            var cluster = new DuplicateClusterModel();
            var detailVM = new MergeDetailedReviewViewModel(cluster);

            var mapping = new TypeMappingModel();
            var row = new ParameterDiffRowModel
            {
                ParameterName = "LineWidth",
                IsApproved = true
            };

            var elementId1 = new ElementId(101);
            var elementId2 = new ElementId(102);

            // Add options for source and target values
            row.Options.Add(new ParameterValueOption { ElementId = elementId1, DisplayText = "1" });
            row.Options.Add(new ParameterValueOption { ElementId = elementId2, DisplayText = "2" });

            // Simulate target value winning
            row.WinningValueElementId = elementId2;

            mapping.ParameterResolutions.Add(row);
            cluster.TypeMappings.Add(mapping);

            // Act
            detailVM.CmdCommitToQueue.Execute(null);

            // Assert
            Assert.IsFalse(cluster.IsBlocked, "Cluster should be unblocked after committing.");
            Assert.IsTrue(cluster.ParameterResolutions.ContainsKey("LineWidth"), "Cluster resolutions dictionary should contain the parameter.");
            Assert.AreEqual(elementId2, cluster.ParameterResolutions["LineWidth"], "Winning value ElementId should match selection.");
        }
    }
}
