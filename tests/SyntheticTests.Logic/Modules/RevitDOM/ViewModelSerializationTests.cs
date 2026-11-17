using System;
using NUnit.Framework;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Synthetic.Infrastructure.Serialization;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;

namespace SyntheticTests.Modules.RevitDOM
{
    [TestFixture]
    public class ViewModelSerializationTests
    {
        [Test]
        public void ViewSheetModel_Serialization_OmitsGraphicalPropertiesWhenNull()
        {
            // Arrange
            var sheetModel = new ViewSheetModel
            {
                Name = "Sheet_Test",
                Class = "Autodesk.Revit.DB.ViewSheet",
                Id = 12345
            };

            // Act
            var json = Json.Encode(sheetModel);

            // Assert
            Assert.IsNotNull(json);
            Assert.IsFalse(json.Contains("\"Scale\""), "JSON should not contain Scale.");
            Assert.IsFalse(json.Contains("\"DetailLevel\""), "JSON should not contain DetailLevel.");
            Assert.IsFalse(json.Contains("\"DisplayStyle\""), "JSON should not contain DisplayStyle.");
            Assert.IsFalse(json.Contains("\"SunlightIntensity\""), "JSON should not contain SunlightIntensity.");
            Assert.IsFalse(json.Contains("\"ShadowIntensity\""), "JSON should not contain ShadowIntensity.");
            Assert.IsFalse(json.Contains("\"CropBoxActive\""), "JSON should not contain CropBoxActive.");
        }

        [Test]
        public void ViewScheduleModel_Serialization_OmitsGraphicalPropertiesWhenNull()
        {
            // Arrange
            var scheduleModel = new ViewScheduleModel
            {
                Name = "Schedule_Test",
                Class = "Autodesk.Revit.DB.ViewSchedule",
                Id = 67890
            };

            // Act
            var json = Json.Encode(scheduleModel);

            // Assert
            Assert.IsNotNull(json);
            Assert.IsFalse(json.Contains("\"Scale\""), "JSON should not contain Scale.");
            Assert.IsFalse(json.Contains("\"DetailLevel\""), "JSON should not contain DetailLevel.");
            Assert.IsFalse(json.Contains("\"DisplayStyle\""), "JSON should not contain DisplayStyle.");
            Assert.IsFalse(json.Contains("\"SunlightIntensity\""), "JSON should not contain SunlightIntensity.");
            Assert.IsFalse(json.Contains("\"ShadowIntensity\""), "JSON should not contain ShadowIntensity.");
            Assert.IsFalse(json.Contains("\"CropBoxActive\""), "JSON should not contain CropBoxActive.");
        }

        [Test]
        public void ModelsToSerialize_DeserializeByJson_RoutesModelTextAndSpotDimensionCorrectly()
        {
            // Arrange
            var json = @"{
                ""ModelTextTypes"": {
                    ""TestModelText"": {
                        ""Class"": ""Autodesk.Revit.DB.ModelTextType"",
                        ""Name"": ""TestModelText"",
                        ""Id"": 1010
                    }
                },
                ""SpotDimensionTypes"": {
                    ""TestSpotDimension"": {
                        ""Class"": ""Autodesk.Revit.DB.SpotDimensionType"",
                        ""Name"": ""TestSpotDimension"",
                        ""Id"": 2020
                    }
                }
            }";

            // Act
            var flatList = System.Linq.Enumerable.ToList(ModelsToSerialize.DeserializeByJson(json));

            // Assert
            Assert.AreEqual(2, flatList.Count, "Flat list should contain exactly 2 element models.");
            
            var modelText = flatList.Find(x => x.Class == "Autodesk.Revit.DB.ModelTextType");
            var spotDim = flatList.Find(x => x.Class == "Autodesk.Revit.DB.SpotDimensionType");

            Assert.IsNotNull(modelText, "ModelTextType element model should be found in flat list.");
            Assert.IsNotNull(spotDim, "SpotDimensionType element model should be found in flat list.");
            Assert.AreEqual("TestModelText", modelText.Name);
            Assert.AreEqual("TestSpotDimension", spotDim.Name);
        }

        [Test]
        public void ModelsToSerialize_SerializeToJson_SortsModelTextAndSpotDimensionCorrectly()
        {
            // Arrange
            var modelText = new ElementTypeModel
            {
                Class = "Autodesk.Revit.DB.ModelTextType",
                Name = "TestModelText",
                Id = 1010
            };
            var spotDim = new ElementTypeModel
            {
                Class = "Autodesk.Revit.DB.SpotDimensionType",
                Name = "TestSpotDimension",
                Id = 2020
            };

            var list = new System.Collections.Generic.List<ObjectModel> { modelText, spotDim };

            // Act
            var json = ModelsToSerialize.SerializeToJson(list);

            // Assert
            Assert.IsNotNull(json);
            Assert.IsTrue(json.Contains("\"ModelTextTypes\""), "JSON should contain ModelTextTypes property.");
            Assert.IsTrue(json.Contains("\"SpotDimensionTypes\""), "JSON should contain SpotDimensionTypes property.");
            Assert.IsTrue(json.Contains("\"TestModelText\""), "JSON should contain TestModelText.");
            Assert.IsTrue(json.Contains("\"TestSpotDimension\""), "JSON should contain TestSpotDimension.");
        }

        [Test]
        public void ModelsToSerialize_SerializationAndDeserialization_RoutesToposolidTypeCorrectly()
        {
            // Arrange
            var topoType = new HostObjTypeModel
            {
                Class = "Autodesk.Revit.DB.ToposolidType",
                Name = "TestToposolidType",
                Id = 3030
            };
            var list = new System.Collections.Generic.List<ObjectModel> { topoType };

            // Act - Serialize
            var json = ModelsToSerialize.SerializeToJson(list);

            // Assert - Serialization
            Assert.IsNotNull(json);
            Assert.IsTrue(json.Contains("\"ToposolidTypes\""), "JSON should contain ToposolidTypes property.");
            Assert.IsTrue(json.Contains("\"TestToposolidType\""), "JSON should contain TestToposolidType.");

            // Act - Deserialize
            var flatList = System.Linq.Enumerable.ToList(ModelsToSerialize.DeserializeByJson(json));

            // Assert - Deserialization
            Assert.AreEqual(1, flatList.Count);
            var deserialized = flatList[0];
            Assert.IsInstanceOf<HostObjTypeModel>(deserialized);
            Assert.AreEqual("Autodesk.Revit.DB.ToposolidType", deserialized.Class);
            Assert.AreEqual("TestToposolidType", deserialized.Name);
        }

        [Test]
        public void StandardSerializationEngine_ToJson_EmitsCanonicalOrderAndFilledRegionTypesAndOmitsEmptyBuckets()
        {
            // Arrange
            var fillPattern = new FillPatternElementModel { Name = "PatternA", Class = "Autodesk.Revit.DB.FillPatternElement" };
            var material = new MaterialModel { Name = "MaterialA", Class = "Autodesk.Revit.DB.Material" };
            var region = new FilledRegionTypeModel { Name = "RegionA", Class = "Autodesk.Revit.DB.FilledRegionType" };
            var genericElem = new ElementModel { Name = "DirectShapeA", Class = "Autodesk.Revit.DB.DirectShape" };

            var list = new System.Collections.Generic.List<ObjectModel> { genericElem, region, material, fillPattern };

            // Act
            string json = StandardSerializationEngine.ToJson(list);

            // Assert
            Assert.IsNotNull(json);
            Assert.IsTrue(json.Contains("\"FilledRegionTypes\""), "Must use 'FilledRegionTypes' bucket name.");
            Assert.IsFalse(json.Contains("\"FillRegionTypes\""), "Must never emit 'FillRegionTypes'.");
            Assert.IsFalse(json.Contains("\"LinePatternElements\""), "Must omit empty buckets.");
            Assert.IsFalse(json.Contains("\"HostObjTypes\""), "Must omit empty buckets.");
            Assert.IsTrue(json.Contains("\"Elements\""), "Must preserve non-empty Elements list.");

            // Check canonical ordering in JSON: FillPatternElements (order 1) before Materials (order 4) before FilledRegionTypes (order 8) before Elements
            int idxPattern = json.IndexOf("\"FillPatternElements\"");
            int idxMat = json.IndexOf("\"Materials\"");
            int idxRegion = json.IndexOf("\"FilledRegionTypes\"");
            int idxElements = json.IndexOf("\"Elements\"");

            Assert.IsTrue(idxPattern < idxMat, "FillPatternElements must precede Materials.");
            Assert.IsTrue(idxMat < idxRegion, "Materials must precede FilledRegionTypes.");
            Assert.IsTrue(idxRegion < idxElements, "FilledRegionTypes must precede Elements.");
        }

        [Test]
        public void StandardSerializationEngine_ToJson_SortsBucketKeysDeterministicallyAndHandlesDuplicatesLastInWins()
        {
            // Arrange
            SerializationResultModel.ClearWarnings();
            var matB = new MaterialModel { Name = "b_Material", Class = "Autodesk.Revit.DB.Material" };
            var matA = new MaterialModel { Name = "a_Material", Class = "Autodesk.Revit.DB.Material" };
            var matADup = new MaterialModel { Name = "a_Material", Class = "Autodesk.Revit.DB.Material", UniqueId = "last-in-wins-uid" };

            var list = new System.Collections.Generic.List<ObjectModel> { matB, matA, matADup };

            // Act
            string json = StandardSerializationEngine.ToJson(list);

            // Assert
            Assert.IsNotNull(json);
            Assert.IsTrue(SerializationResultModel.CurrentThreadWarnings.Count > 0, "Warning must be logged for duplicate key.");
            Assert.IsTrue(SerializationResultModel.CurrentThreadWarnings[0].Contains("Duplicate key 'a_Material'"), "Warning must mention duplicate key.");
            Assert.IsTrue(json.Contains("\"last-in-wins-uid\""), "Last-in-wins entry must be preserved.");

            int idxA = json.IndexOf("\"a_Material\"");
            int idxB = json.IndexOf("\"b_Material\"");
            Assert.IsTrue(idxA < idxB, "Keys must be sorted deterministically OrdinalIgnoreCase.");
        }

        [Test]
        public void StandardSerializationEngine_FromJson_HandlesLegacyBucketsAndHydratesPolymorphically()
        {
            // Arrange - JSON with legacy 'FillRegionTypes', legacy 'GridTypes', legacy TextElementType inside 'ElementTypes', and ViewTemplates
            string json = @"{
                ""FillRegionTypes"": {
                    ""LegacyRegion"": {
                        ""Name"": ""LegacyRegion"",
                        ""Id"": 101
                    }
                },
                ""GridTypes"": {
                    ""LegacyGrid"": {
                        ""Name"": ""LegacyGrid"",
                        ""Id"": 102
                    }
                },
                ""ElementTypes"": {
                    ""LegacyTextElement"": {
                        ""Class"": ""Autodesk.Revit.DB.TextElementType"",
                        ""Name"": ""LegacyTextElement"",
                        ""Id"": 103
                    }
                },
                ""ViewTemplates"": {
                    ""PlanTemplate"": {
                        ""Class"": ""Autodesk.Revit.DB.ViewPlan"",
                        ""Name"": ""PlanTemplate"",
                        ""Id"": 104
                    }
                }
            }";

            // Act
            var models = System.Linq.Enumerable.ToList(StandardSerializationEngine.FromJson(json));

            // Assert
            Assert.AreEqual(4, models.Count, "All 4 entries across legacy and current buckets must be deserialized.");

            var region = models.Find(m => m.Name == "LegacyRegion");
            Assert.IsNotNull(region);
            Assert.IsInstanceOf<FilledRegionTypeModel>(region, "FillRegionTypes must hydrate to FilledRegionTypeModel.");
            Assert.AreEqual("Autodesk.Revit.DB.FilledRegionType", region!.Class, "Default Class must be inferred when omitted.");

            var grid = models.Find(m => m.Name == "LegacyGrid");
            Assert.IsNotNull(grid);
            Assert.IsInstanceOf<ElementTypeModel>(grid, "GridTypes must hydrate to ElementTypeModel.");
            Assert.AreEqual("Autodesk.Revit.DB.GridType", grid!.Class, "Default GridType class must be inferred when omitted.");

            var textElem = models.Find(m => m.Name == "LegacyTextElement");
            Assert.IsNotNull(textElem);
            Assert.AreEqual("Autodesk.Revit.DB.TextElementType", textElem!.Class, "TextElementType must be preserved from ElementTypes.");

            var template = models.Find(m => m.Name == "PlanTemplate");
            Assert.IsNotNull(template);
            Assert.IsInstanceOf<ViewPlanModel>(template, "ViewPlan class in ViewTemplates must hydrate polymorphically to ViewPlanModel.");
            Assert.IsTrue(template!.IsTemplate, "IsTemplate must be forced to true for ViewTemplates entries.");
        }
    }
}
