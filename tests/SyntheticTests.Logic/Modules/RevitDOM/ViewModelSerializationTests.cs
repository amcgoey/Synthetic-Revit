using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Autodesk.Revit.DB;
using Synthetic.Settings;
using Synthetic.Platform.UI;
using SyntheticTests.Modules.StandardsManagement;

namespace SyntheticTests.Modules.RevitDOM
{
    [TestFixture]
    public class ViewModelSerializationTests
    {
        [SetUp]
        public void SetUp()
        {
            ProgressCoordinator.SuppressUI = true;
        }

        [Test]
        public void StandardsSourceTreeViewModel_LoadFileSource_LoadsLegacy26BucketJsonFixtureIntoHierarchy()
        {
            // Arrange
            string tempFile = Path.Combine(Path.GetTempPath(), "legacy_26_bucket_" + Guid.NewGuid() + ".json");
            try
            {
                string json = @"{
                    ""FillRegionTypes"": {
                        ""Region_Cross"": { ""Name"": ""Region_Cross"" }
                    },
                    ""GridTypes"": {
                        ""Grid_1"": { ""Name"": ""Grid_1"" }
                    },
                    ""LevelTypes"": {
                        ""Level_1"": { ""Name"": ""Level_1"" }
                    },
                    ""CurtainSystemTypes"": {
                        ""Curtain_5x10"": { ""Name"": ""Curtain_5x10"" }
                    },
                    ""MullionTypes"": {
                        ""Mullion_Rect"": { ""Name"": ""Mullion_Rect"" }
                    },
                    ""FasciaTypes"": {
                        ""Fascia_Flat"": { ""Name"": ""Fascia_Flat"" }
                    },
                    ""GutterTypes"": {
                        ""Gutter_Bevel"": { ""Name"": ""Gutter_Bevel"" }
                    },
                    ""ViewFamilyTypes"": {
                        ""VFT_FloorPlan"": { ""Name"": ""VFT_FloorPlan"" }
                    },
                    ""Materials"": {
                        ""Concrete"": { ""Name"": ""Concrete"" }
                    },
                    ""ElementTypes"": {
                        ""LegacyLabel"": { ""Class"": ""Autodesk.Revit.DB.TextElementType"", ""Name"": ""LegacyLabel"" }
                    }
                }";
                File.WriteAllText(tempFile, json);

                Document doc = (Document)Activator.CreateInstance(typeof(Document), true)!;
                var parent = DashboardTestFactory.Create(doc, settings: new StandardsSettings());
                var treeVM = parent.SourceTreeViewModel;

                // Act
                treeVM.LoadFileSource(tempFile, "Legacy Source Fixture");

                // Assert
                Assert.AreEqual(1, treeVM.AvailableSources.Count, "Exactly 1 source should be loaded.");
                var source = treeVM.AvailableSources[0];
                Assert.AreEqual("Legacy Source Fixture", source.DisplayName);
                Assert.IsFalse(source.IsRevitSource);

                var groups = source.SourceHierarchy;
                Assert.IsTrue(groups.Count > 0, "SourceHierarchy should contain groups.");

                var groupNames = groups.Select(g => g.Name).ToList();
                Assert.IsTrue(groupNames.Contains("Annotations"), "Should contain Annotations group.");
                Assert.IsTrue(groupNames.Contains("System Types"), "Should contain System Types group.");
                Assert.IsTrue(groupNames.Contains("Views"), "Should contain Views group.");
                Assert.IsTrue(groupNames.Contains("Materials & Assets"), "Should contain Materials & Assets group.");

                // Check Annotations classes: Filled Region Types, Grid Types, Level Types, Label Types
                var annotationsGroup = groups.First(g => g.Name == "Annotations");
                var annClassNames = annotationsGroup.Children.Select(c => c.Name).ToList();
                Assert.IsTrue(annClassNames.Contains("Filled Region Types"), "Annotations must contain Filled Region Types.");
                Assert.IsTrue(annClassNames.Contains("Grid Types"), "Annotations must contain Grid Types.");
                Assert.IsTrue(annClassNames.Contains("Level Types"), "Annotations must contain Level Types.");
                Assert.IsTrue(annClassNames.Contains("Label Types"), "Annotations must contain Label Types.");

                // Check System Types classes: Curtain System Types, Mullion Types, Fascia Types, Gutter Types
                var systemGroup = groups.First(g => g.Name == "System Types");
                var sysClassNames = systemGroup.Children.Select(c => c.Name).ToList();
                Assert.IsTrue(sysClassNames.Contains("Curtain System Types"), "System Types must contain Curtain System Types.");
                Assert.IsTrue(sysClassNames.Contains("Mullion Types"), "System Types must contain Mullion Types.");
                Assert.IsTrue(sysClassNames.Contains("Fascia Types"), "System Types must contain Fascia Types.");
                Assert.IsTrue(sysClassNames.Contains("Gutter Types"), "System Types must contain Gutter Types.");

                // Check Views classes: View Family Types
                var viewsGroup = groups.First(g => g.Name == "Views");
                var viewClassNames = viewsGroup.Children.Select(c => c.Name).ToList();
                Assert.IsTrue(viewClassNames.Contains("View Family Types"), "Views must contain View Family Types.");
            }
            finally
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
            }
        }
    }
}
