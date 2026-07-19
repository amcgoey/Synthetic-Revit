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
    }
}
