using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using Newtonsoft.Json;

using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Operations.Merge;


namespace SyntheticTests
{
    [TestFixture]
    public class Tier2_MergeDuplicatesHeadlessTests
    {
        private static string GetProjectRoot()
        {
            string envPath = Environment.GetEnvironmentVariable("SYNTHETIC_PROJECT_ROOT");
            if (!string.IsNullOrEmpty(envPath) && Directory.Exists(envPath))
            {
                return envPath;
            }

            string dir = TestContext.CurrentContext.TestDirectory;
            while (dir != null && !Directory.Exists(Path.Combine(dir, "tests")))
            {
                dir = Path.GetDirectoryName(dir);
            }
            return dir ?? TestContext.CurrentContext.TestDirectory;
        }

        [Test]
        public void Test_GetBaseName_StripsTrailingDigitsAndSeparators()
        {
            Assert.AreEqual("MyFamily", MergeAnalysisEngine.GetBaseName("MyFamily_1"));
            Assert.AreEqual("MyFamily", MergeAnalysisEngine.GetBaseName("MyFamily-2"));
            Assert.AreEqual("MyFamily", MergeAnalysisEngine.GetBaseName("MyFamily 3"));
            Assert.AreEqual("MyFamily", MergeAnalysisEngine.GetBaseName("MyFamily#4"));
            Assert.AreEqual("MyFamily", MergeAnalysisEngine.GetBaseName("MyFamily.5"));
            Assert.AreEqual("MyFamily", MergeAnalysisEngine.GetBaseName("MyFamily"));
            Assert.AreEqual("", MergeAnalysisEngine.GetBaseName(""));
            Assert.AreEqual("", MergeAnalysisEngine.GetBaseName(null));
        }

        [Test]
        public void Test_BuildClustersFromModels_GroupsDuplicateFamilies()
        {
            string projectRoot = GetProjectRoot();
            string jsonPath = Path.Combine(projectRoot, "tests", "SyntheticTests.Shared", "Assets", "test_duplicate_families.json");
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
            string projectRoot = GetProjectRoot();
            string jsonPath = Path.Combine(projectRoot, "tests", "SyntheticTests.Shared", "Assets", "test_duplicate_groups.json");
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
            Assert.IsTrue(groupCluster.Items.Any(i => i.IsPrimary), "One item should be flagged as primary.");

            var primaryItem = groupCluster.Items.First(i => i.IsPrimary);
            Assert.AreEqual("TestGroup", primaryItem.ItemName, "Item with shorter name should be primary.");
            
            var nonPrimaryItem = groupCluster.Items.First(i => !i.IsPrimary);
            Assert.AreEqual("TestGroup1", nonPrimaryItem.ItemName);
        }
    }
}
