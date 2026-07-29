using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using Newtonsoft.Json;

using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Operations.Merge;
using Synthetic.RevitDOM.Utilities;
using SyntheticTests.Helpers;


namespace SyntheticTests
{
    [TestFixture]
    public class Tier2_MergeDuplicatesHeadlessTests
    {
        private static string GetJsonAssetPath(string fileName)
        {
            string testDir = TestContext.CurrentContext.TestDirectory;

            // Strategy 1: Copied to output Assets directory
            string localAsset = Path.Combine(testDir, "Assets", fileName);
            if (File.Exists(localAsset)) return localAsset;

            // Strategy 2: Copied to output directory root
            string directAsset = Path.Combine(testDir, fileName);
            if (File.Exists(directAsset)) return directAsset;

            // Strategy 3: Walk up directory tree to repo root
            string? dir = testDir;
            while (dir != null)
            {
                string candidate = Path.Combine(dir, "tests", "SyntheticTests.Shared", "Assets", fileName);
                if (File.Exists(candidate)) return candidate;
                dir = Path.GetDirectoryName(dir);
            }

            // Strategy 4: TestPathHelper fallback
            try
            {
                string projectRoot = TestPathHelper.GetProjectRoot();
                string candidate = Path.Combine(projectRoot, "tests", "SyntheticTests.Shared", "Assets", fileName);
                if (File.Exists(candidate)) return candidate;
            }
            catch { }

            return Path.Combine(testDir, "Assets", fileName);
        }

        #region Test Factory Helpers

        private static ElementModel CreateTestElementModel(
            long id,
            string name,
            string category = "Detail Items",
            string className = "Autodesk.Revit.DB.FamilySymbol",
            List<ParameterModel>? parameters = null,
            List<ElementModel>? nestedTypes = null)
        {
            return new ElementModel
            {
                ElementId = new ElementIdModel { Id = id, UniqueId = $"uid-{id}" },
                Name = name,
                Category = category,
                Class = className,
                Parameters = parameters ?? new List<ParameterModel>(),
                NestedTypes = nestedTypes ?? new List<ElementModel>()
            };
        }

        private static ParameterModel CreateTestParameterModel(
            string name,
            string value,
            string storageType = "String",
            long id = 100)
        {
            return new ParameterModel
            {
                Name = name,
                Value = value,
                StorageType = storageType,
                Id = id
            };
        }

        private static void SetLoadableFlag(DuplicateClusterModel cluster, bool isLoadable)
        {
            foreach (var item in cluster.Items)
            {
                item.IsLoadableFamily = isLoadable;
            }
        }

        #endregion

        [Test]
        public void Test_GetBaseName_StripsTrailingDigitsAndSeparators()
        {
            Assert.AreEqual("Running Section", MergeAnalysisEngine.GetBaseName("Running Section 1"));
            Assert.AreEqual("Soldier & Plan", MergeAnalysisEngine.GetBaseName("Soldier & Plan-2"));
            Assert.AreEqual("Rowlock", MergeAnalysisEngine.GetBaseName("Rowlock_3"));
            Assert.AreEqual("SimpleName", MergeAnalysisEngine.GetBaseName("SimpleName"));
        }

        [Test]
        public void Test_BuildClustersFromModels_GroupsDuplicateFamilies()
        {
            string jsonPath = GetJsonAssetPath("test_duplicate_families.json");
            Assert.IsTrue(File.Exists(jsonPath), $"Snapshot file not found at: {jsonPath}");

            string json = File.ReadAllText(jsonPath);
            var elements = JsonConvert.DeserializeObject<List<ElementModel>>(json);
            Assert.IsNotNull(elements);
            Assert.AreEqual(6, elements.Count);

            var token = CancellationToken.None;
            var clusters = MergeAnalysisEngine.BuildClustersFromModels(elements, token);

            // Assertions for clustering
            Assert.IsNotNull(clusters, "Clusters collection should not be null.");
            Assert.AreEqual(3, clusters.Count, "Should detect 3 duplicate family symbol clusters.");

            // Assert category grouping across all clusters
            foreach (var cluster in clusters)
            {
                Assert.IsTrue(cluster.Items.All(i => i.CategoryName == "Detail Items"),
                    $"All items in cluster '{cluster.ClusterName}' should belong to Detail Items category.");
                Assert.AreEqual(1, cluster.Items.Count(i => i.IsPrimary),
                    $"Exactly one item in cluster '{cluster.ClusterName}' should be flagged as primary.");

                var primary = cluster.Items.First(i => i.IsPrimary);
                var shortestNameItem = cluster.Items.OrderBy(i => i.ItemName.Length).First();
                Assert.AreEqual(shortestNameItem.ItemName, primary.ItemName,
                    $"Primary item for '{cluster.ClusterName}' should have the shortest name length.");
            }

            var runningSectionCluster = clusters.FirstOrDefault(c => c.ClusterName.Contains("Running Section"));
            Assert.IsNotNull(runningSectionCluster, "Should contain Running Section cluster.");
            Assert.AreEqual(2, runningSectionCluster.Items.Count);
            Assert.IsTrue(runningSectionCluster.Items.Any(i => i.IsPrimary), "One item should be flagged as primary.");

            var soldierCluster = clusters.FirstOrDefault(c => c.ClusterName.Contains("Soldier & Plan"));
            Assert.IsNotNull(soldierCluster, "Should contain Soldier & Plan cluster.");
            Assert.AreEqual(2, soldierCluster.Items.Count);

            var rowlockCluster = clusters.FirstOrDefault(c => c.ClusterName.Contains("Rowlock"));
            Assert.IsNotNull(rowlockCluster, "Should contain Rowlock cluster.");
            Assert.AreEqual(2, rowlockCluster.Items.Count);
        }

        [Test]
        public void Test_BuildClustersFromModels_GroupsDuplicateGroups()
        {
            string jsonPath = GetJsonAssetPath("test_duplicate_groups.json");
            Assert.IsTrue(File.Exists(jsonPath), $"Snapshot file not found at: {jsonPath}");

            string json = File.ReadAllText(jsonPath);
            var elements = JsonConvert.DeserializeObject<List<ElementModel>>(json);
            Assert.IsNotNull(elements);
            Assert.AreEqual(2, elements.Count);

            var token = CancellationToken.None;
            var clusters = MergeAnalysisEngine.BuildClustersFromModels(elements, token);

            // Assertions for clustering
            Assert.IsNotNull(clusters, "Clusters collection should not be null.");
            Assert.AreEqual(1, clusters.Count, "Should detect 1 duplicate group cluster.");

            var groupCluster = clusters.First();
            Assert.IsTrue(groupCluster.ClusterName.Contains("TestGroup"), "Cluster name should contain TestGroup.");
            Assert.AreEqual(2, groupCluster.Items.Count, "Should contain 2 group items.");
            Assert.IsTrue(groupCluster.Items.All(i => i.CategoryName == "Model Groups"), "All items in group cluster should belong to Model Groups category.");
            Assert.AreEqual(1, groupCluster.Items.Count(i => i.IsPrimary), "Exactly one item should be flagged as primary.");

            var primaryItem = groupCluster.Items.First(i => i.IsPrimary);
            Assert.AreEqual("TestGroup", primaryItem.ItemName, "Item with shorter name should be primary.");
            
            var nonPrimaryItem = groupCluster.Items.First(i => !i.IsPrimary);
            Assert.AreEqual("TestGroup1", nonPrimaryItem.ItemName);
        }

        [Test]
        public void Test_CompareParameters_IdentifiesMatches()
        {
            var primaryElem = CreateTestElementModel(
                1001,
                "Running Section",
                parameters: new List<ParameterModel>
                {
                    CreateTestParameterModel("Cost", "100.0", "Double", 101),
                    CreateTestParameterModel("Type Comments", "Standard", "String", 102)
                });

            var duplicateElem = CreateTestElementModel(
                1002,
                "Running Section",
                parameters: new List<ParameterModel>
                {
                    CreateTestParameterModel("Cost", "100.0", "Double", 101),
                    CreateTestParameterModel("Type Comments", "Standard", "String", 102)
                });

            var elements = new List<ElementModel> { primaryElem, duplicateElem };
            var token = CancellationToken.None;
            var clusters = MergeAnalysisEngine.BuildClustersFromModels(elements, token);
            Assert.AreEqual(1, clusters.Count);

            var cluster = clusters[0];
            var primaryItem = cluster.Items.First(i => i.RevitElementId.Id == 1001);
            cluster.UpdatePrimaryItem(primaryItem);

            MergeAnalysisEngine.GenerateRecommendations(cluster);

            var mapping = cluster.TypeMappings.First();
            Assert.IsNotNull(mapping);

            var costRow = mapping.ParameterResolutions.FirstOrDefault(r => r.ParameterName == "Cost");
            Assert.IsNotNull(costRow);
            Assert.IsFalse(costRow.HasConflict, "Identical Cost parameter should not have conflict.");
            Assert.IsFalse(costRow.IsSchemaMismatch, "Identical Cost parameter should not have schema mismatch.");

            var commentRow = mapping.ParameterResolutions.FirstOrDefault(r => r.ParameterName == "Type Comments");
            Assert.IsNotNull(commentRow);
            Assert.IsFalse(commentRow.HasConflict, "Identical Type Comments parameter should not have conflict.");
            Assert.IsFalse(commentRow.IsSchemaMismatch, "Identical Type Comments parameter should not have schema mismatch.");
        }

        [Test]
        public void Test_CompareParameters_IdentifiesConflicts()
        {
            var primaryElem = CreateTestElementModel(
                1001,
                "Running Section",
                parameters: new List<ParameterModel>
                {
                    CreateTestParameterModel("Type Comments", "Standard", "String", 102)
                });

            var duplicateElem = CreateTestElementModel(
                1002,
                "Running Section",
                parameters: new List<ParameterModel>
                {
                    CreateTestParameterModel("Type Comments", "Override", "String", 102)
                });

            var elements = new List<ElementModel> { primaryElem, duplicateElem };
            var token = CancellationToken.None;
            var clusters = MergeAnalysisEngine.BuildClustersFromModels(elements, token);
            var cluster = clusters[0];
            var primaryItem = cluster.Items.First(i => i.RevitElementId.Id == 1001);
            cluster.UpdatePrimaryItem(primaryItem);

            MergeAnalysisEngine.GenerateRecommendations(cluster);

            var mapping = cluster.TypeMappings.First();
            var row = mapping.ParameterResolutions.FirstOrDefault(r => r.ParameterName == "Type Comments");
            Assert.IsNotNull(row);
            Assert.IsTrue(row.HasConflict, "Differing parameter values should flag conflict.");
            Assert.IsFalse(row.IsSchemaMismatch, "Differing parameter values with same storage type should not have schema mismatch.");
            Assert.AreEqual(2, row.Options.Count);
            Assert.AreEqual("Override", row.Options[0].DisplayText);
            Assert.AreEqual("Standard", row.Options[1].DisplayText);
        }

        [Test]
        public void Test_CompareParameters_IdentifiesSchemaMismatches()
        {
            var primaryElem = CreateTestElementModel(
                1001,
                "Running Section",
                parameters: new List<ParameterModel>
                {
                    CreateTestParameterModel("Cost", "100.0", "Double", 101)
                });

            var duplicateElem = CreateTestElementModel(
                1002,
                "Running Section",
                parameters: new List<ParameterModel>
                {
                    CreateTestParameterModel("Cost", "100.0", "String", 101)
                });

            var elements = new List<ElementModel> { primaryElem, duplicateElem };
            var token = CancellationToken.None;
            var clusters = MergeAnalysisEngine.BuildClustersFromModels(elements, token);
            var cluster = clusters[0];
            var primaryItem = cluster.Items.First(i => i.RevitElementId.Id == 1001);
            cluster.UpdatePrimaryItem(primaryItem);

            MergeAnalysisEngine.GenerateRecommendations(cluster);

            var mapping = cluster.TypeMappings.First();
            var row = mapping.ParameterResolutions.FirstOrDefault(r => r.ParameterName == "Cost");
            Assert.IsNotNull(row);
            Assert.IsTrue(row.IsSchemaMismatch, "Differing storage types should flag schema mismatch.");
        }

        [Test]
        public void Test_CompareParameters_IdentifiesMissingParameters()
        {
            var primaryElem = CreateTestElementModel(
                1001,
                "Running Section",
                parameters: new List<ParameterModel>
                {
                    CreateTestParameterModel("OnlyInPrimary", "Hello", "String", 103)
                });

            var duplicateElem = CreateTestElementModel(
                1002,
                "Running Section",
                parameters: new List<ParameterModel>
                {
                    CreateTestParameterModel("OnlyInSource", "World", "String", 104)
                });

            var elements = new List<ElementModel> { primaryElem, duplicateElem };
            var token = CancellationToken.None;
            var clusters = MergeAnalysisEngine.BuildClustersFromModels(elements, token);
            var cluster = clusters[0];
            var primaryItem = cluster.Items.First(i => i.RevitElementId.Id == 1001);
            cluster.UpdatePrimaryItem(primaryItem);

            MergeAnalysisEngine.GenerateRecommendations(cluster);

            var mapping = cluster.TypeMappings.First();
            
            // OnlyInPrimary row (in target, missing in source)
            var primRow = mapping.ParameterResolutions.FirstOrDefault(r => r.ParameterName == "OnlyInPrimary");
            Assert.IsNotNull(primRow);
            Assert.IsFalse(primRow.IsSchemaMismatch, "Missing parameter on source is not a schema mismatch.");
            Assert.IsFalse(primRow.IsInjectEnabled, "Missing parameter on source should not be inject-enabled on target.");

            // OnlyInSource row (in source, missing in target)
            var srcRow = mapping.ParameterResolutions.FirstOrDefault(r => r.ParameterName == "OnlyInSource");
            Assert.IsNotNull(srcRow);
            Assert.IsFalse(srcRow.IsSchemaMismatch, "Missing parameter on target is not a schema mismatch.");
            Assert.IsTrue(srcRow.IsInjectEnabled, "Missing parameter on target should be inject-enabled.");
        }

        [Test]
        public void Test_GenerateRecommendedAction_MatchesDecisions()
        {
            // Case 1: Loadable Family (IsLoadableFamily = true)
            var typeA1 = CreateTestElementModel(10011, "Type A");
            var primaryFam = CreateTestElementModel(
                1001,
                "Running Section",
                nestedTypes: new List<ElementModel> { typeA1 });

            var typeA2 = CreateTestElementModel(10021, "Type A");
            var typeB2 = CreateTestElementModel(10022, "Type B");
            var duplicateFam = CreateTestElementModel(
                1002,
                "Running Section 1",
                nestedTypes: new List<ElementModel> { typeA2, typeB2 });

            var elements = new List<ElementModel> { primaryFam, duplicateFam };
            var token = CancellationToken.None;
            var clusters = MergeAnalysisEngine.BuildClustersFromModels(elements, token);
            Assert.AreEqual(1, clusters.Count);

            var cluster = clusters[0];
            var primaryItem = cluster.Items.First(i => i.RevitElementId.Id == 1001);
            cluster.UpdatePrimaryItem(primaryItem);

            // Mock IsLoadableFamily directly on items rather than relying on Class name checks
            SetLoadableFlag(cluster, true);

            MergeAnalysisEngine.GenerateRecommendations(cluster);

            Assert.AreEqual(2, cluster.TypeMappings.Count);
            
            var typeAMapping = cluster.TypeMappings.First(m => m.SourceType.Name == "Type A");
            Assert.AreEqual(RecommendedAction.Merge, typeAMapping.RecommendedAction, "Type A exists in primary and should be Merged.");

            var typeBMapping = cluster.TypeMappings.First(m => m.SourceType.Name == "Type B");
            Assert.AreEqual(RecommendedAction.Migrate, typeBMapping.RecommendedAction, "Type B is unique to duplicate family and should be Migrated.");

            // Case 2: Group (IsLoadableFamily = false)
            var primaryGroup = CreateTestElementModel(
                2001,
                "TestGroup",
                category: "Model Groups",
                className: "Autodesk.Revit.DB.GroupType");

            var duplicateGroup = CreateTestElementModel(
                2002,
                "TestGroup 1",
                category: "Model Groups",
                className: "Autodesk.Revit.DB.GroupType");

            var groupElements = new List<ElementModel> { primaryGroup, duplicateGroup };
            var groupClusters = MergeAnalysisEngine.BuildClustersFromModels(groupElements, token);
            Assert.AreEqual(1, groupClusters.Count);

            var groupCluster = groupClusters[0];
            var primaryGroupItem = groupCluster.Items.First(i => i.RevitElementId.Id == 2001);
            groupCluster.UpdatePrimaryItem(primaryGroupItem);

            // Mock IsLoadableFamily directly to false for group items
            SetLoadableFlag(groupCluster, false);

            MergeAnalysisEngine.GenerateRecommendations(groupCluster);

            Assert.AreEqual(1, groupCluster.TypeMappings.Count);
            var groupMapping = groupCluster.TypeMappings.First();
            Assert.AreEqual(RecommendedAction.Merge, groupMapping.RecommendedAction, "Groups should default to Merge even if names differ.");
        }

        [Test]
        public void Test_BoundingBoxXYZModel_GetSize_CalculatesDimensionsCorrectly()
        {
            var validBBox = new BoundingBoxXYZModel
            {
                Min = new XYZModel(-10.0, -5.0, 0.0),
                Max = new XYZModel(10.0, 15.0, 30.0)
            };

            var size = validBBox.GetSize();
            Assert.IsNotNull(size);
            Assert.AreEqual(20.0, size!.X, 1e-6);
            Assert.AreEqual(20.0, size.Y, 1e-6);
            Assert.AreEqual(30.0, size.Z, 1e-6);

            var nullBBox = new BoundingBoxXYZModel
            {
                Min = null,
                Max = new XYZModel(10.0, 10.0, 10.0)
            };
            Assert.IsNull(nullBBox.GetSize(), "GetSize should return null when Min is null.");

            var invalidBBox = new BoundingBoxXYZModel
            {
                Min = new XYZModel(double.NaN, 0, 0),
                Max = new XYZModel(10.0, 10.0, 10.0)
            };
            Assert.IsNull(invalidBBox.GetSize(), "GetSize should return null when Min contains NaN.");
        }

        [Test]
        public void Test_BoundingBoxXYZModel_GetCenter_CalculatesCenterCorrectly()
        {
            var validBBox = new BoundingBoxXYZModel
            {
                Min = new XYZModel(-10.0, -20.0, 0.0),
                Max = new XYZModel(10.0, 20.0, 100.0)
            };

            var center = validBBox.GetCenter();
            Assert.IsNotNull(center);
            Assert.AreEqual(0.0, center!.X, 1e-6);
            Assert.AreEqual(0.0, center.Y, 1e-6);
            Assert.AreEqual(50.0, center.Z, 1e-6);

            var nullBBox = new BoundingBoxXYZModel();
            Assert.IsNull(nullBBox.GetCenter(), "GetCenter should return null when Min and Max are unassigned.");
        }

        [Test]
        public void Test_BoundingBoxXYZModel_IsValid_ReturnsCorrectStatus()
        {
            var valid = new BoundingBoxXYZModel
            {
                Min = new XYZModel(0, 0, 0),
                Max = new XYZModel(1, 1, 1)
            };
            Assert.IsTrue(valid.IsValid);

            var nullMin = new BoundingBoxXYZModel
            {
                Min = null,
                Max = new XYZModel(1, 1, 1)
            };
            Assert.IsFalse(nullMin.IsValid);

            var nanVal = new BoundingBoxXYZModel
            {
                Min = new XYZModel(0, 0, double.NaN),
                Max = new XYZModel(1, 1, 1)
            };
            Assert.IsFalse(nanVal.IsValid);

            var infVal = new BoundingBoxXYZModel
            {
                Min = new XYZModel(0, 0, 0),
                Max = new XYZModel(1, double.PositiveInfinity, 1)
            };
            Assert.IsFalse(infVal.IsValid);
        }

        [Test]
        public void Test_XYZModel_IsOffsetEqual_EvaluatesTolerancesAndNullsCorrectly()
        {
            var vecA = new XYZModel(3.0, 4.0, 0.0); // length = 5.0
            var vecB = new XYZModel(0.0, 5.0, 0.0); // length = 5.0, but different spatial displacement
            var vecC = new XYZModel(3.0, 4.0, 0.0005); // spatial displacement within tolerance 1e-3

            Assert.IsFalse(XYZModel.IsOffsetEqual(vecA, vecB, 1e-3), "Vectors pointing in different directions should not be offset equal.");
            Assert.IsTrue(XYZModel.IsOffsetEqual(vecA, vecC, 1e-3), "Slightly differing vectors within tolerance should be equal.");

            var vecD = new XYZModel(10.0, 0.0, 0.0); // length = 10.0
            Assert.IsFalse(XYZModel.IsOffsetEqual(vecA, vecD, 1e-3), "Vectors with different lengths should not be offset equal.");

            // Null cases
            Assert.IsTrue(XYZModel.IsOffsetEqual(null, null), "Two null vectors should be equal.");
            Assert.IsFalse(XYZModel.IsOffsetEqual(vecA, null), "Vector compared to null should be false.");
            Assert.IsFalse(XYZModel.IsOffsetEqual(null, vecB), "Null compared to vector should be false.");

            // NaN / Infinity cases
            var nanVec = new XYZModel(double.NaN, 0, 0);
            Assert.IsFalse(XYZModel.IsOffsetEqual(vecA, nanVec), "Comparison with NaN vector should return false.");
        }

        [Test]
        public void Test_XYZModel_IsValid_DetectsNaNAndInfinity()
        {
            Assert.IsTrue(new XYZModel(1.0, 2.0, 3.0).IsValid);
            Assert.IsFalse(new XYZModel(double.NaN, 2.0, 3.0).IsValid);
            Assert.IsFalse(new XYZModel(1.0, double.NegativeInfinity, 3.0).IsValid);
            Assert.IsFalse(new XYZModel(1.0, 2.0, double.PositiveInfinity).IsValid);
        }
    }
}
