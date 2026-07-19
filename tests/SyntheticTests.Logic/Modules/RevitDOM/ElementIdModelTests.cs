using System;
using NUnit.Framework;
using Newtonsoft.Json.Linq;
using Synthetic.Infrastructure.Serialization;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;

namespace SyntheticTests.Modules.RevitDOM
{
    [TestFixture]
    public class ElementIdModelTests
    {
        [Test]
        public void StandardSymmetry_PositiveIdNonTemplate_SerializesAndDeserializesId()
        {
            // Arrange
            var model = new ElementIdModel
            {
                Id = 12345,
                IsTemplate = false,
                Name = "TestElement",
                Class = "Autodesk.Revit.DB.Wall",
                Category = "Walls",
                UniqueId = "abc-123-xyz"
            };

            // Act
            var json = Json.Encode(model);
            var decoded = Json.Decode(json);
            var roundTrip = ElementIdModel.ByJSON(json);

            // Assert
            Assert.IsNotNull(decoded, "Decoded object should not be null.");
            Assert.IsInstanceOf<JObject>(decoded, "Decoded object should be a JObject.");
            
            var jObj = (JObject)decoded!;
            Assert.IsTrue(jObj.ContainsKey("Id"), "JSON should contain the 'Id' property.");
            Assert.AreEqual(12345, (long)jObj["Id"]!, "Serialized ID should match original positive ID.");
            Assert.IsTrue(jObj.ContainsKey("UniqueId"), "JSON should contain the 'UniqueId' property.");
            Assert.AreEqual("abc-123-xyz", (string)jObj["UniqueId"]!, "Serialized UniqueId should match original.");

            Assert.IsNotNull(roundTrip, "Round-trip deserialized object should not be null.");
            Assert.AreEqual(12345, roundTrip!.Id, "Deserialized ID should match original positive ID.");
            Assert.AreEqual("abc-123-xyz", roundTrip.UniqueId, "Deserialized UniqueId should match original.");
        }

        [Test]
        public void TemplateIDOmission_PositiveIdTemplate_OmitsId()
        {
            // Arrange
            var model = new ElementIdModel
            {
                Id = 12345,
                IsTemplate = true,
                Name = "TestElement",
                Class = "Autodesk.Revit.DB.Wall",
                Category = "Walls",
                UniqueId = "abc-123-xyz"
            };

            // Act
            var shouldSerializeId = model.ShouldSerializeId();
            var shouldSerializeUniqueId = model.ShouldSerializeUniqueId();
            var json = Json.Encode(model);
            var decoded = Json.Decode(json);
            var roundTrip = ElementIdModel.ByJSON(json);

            // Assert
            Assert.IsFalse(shouldSerializeId, "ShouldSerializeId should return false for a positive ID in a template.");
            Assert.IsFalse(shouldSerializeUniqueId, "ShouldSerializeUniqueId should return false for a template.");
            
            Assert.IsNotNull(decoded, "Decoded object should not be null.");
            Assert.IsInstanceOf<JObject>(decoded, "Decoded object should be a JObject.");
            
            var jObj = (JObject)decoded!;
            Assert.IsFalse(jObj.ContainsKey("Id"), "JSON should not contain the 'Id' property when IsTemplate is true and ID is positive.");
            Assert.IsFalse(jObj.ContainsKey("UniqueId"), "JSON should not contain the 'UniqueId' property when IsTemplate is true.");

            Assert.IsNotNull(roundTrip, "Round-trip deserialized object should not be null.");
            Assert.AreEqual(0, roundTrip!.Id, "Deserialized ID should evaluate to default (0) because it was omitted during serialization.");
            Assert.IsNull(roundTrip.UniqueId, "Deserialized UniqueId should evaluate to null because it was omitted during serialization.");
        }

        [Test]
        public void BuiltInPreservation_NegativeIdTemplate_PreservesId()
        {
            // Arrange
            var model = new ElementIdModel
            {
                Id = -2000100, // Built-in category element ID
                IsTemplate = true,
                Name = "Walls",
                Class = "Autodesk.Revit.DB.Category",
                Category = ""
            };

            // Act
            var shouldSerializeId = model.ShouldSerializeId();
            var json = Json.Encode(model);
            var decoded = Json.Decode(json);
            var roundTrip = ElementIdModel.ByJSON(json);

            // Assert
            Assert.IsTrue(shouldSerializeId, "ShouldSerializeId should return true for a negative ID in a template.");
            
            Assert.IsNotNull(decoded, "Decoded object should not be null.");
            Assert.IsInstanceOf<JObject>(decoded, "Decoded object should be a JObject.");
            
            var jObj = (JObject)decoded!;
            Assert.IsTrue(jObj.ContainsKey("Id"), "JSON should contain the 'Id' property even if IsTemplate is true for negative IDs.");
            Assert.AreEqual(-2000100, (long)jObj["Id"]!, "Serialized ID should match original negative ID.");

            Assert.IsNotNull(roundTrip, "Round-trip deserialized object should not be null.");
            Assert.AreEqual(-2000100, roundTrip!.Id, "Deserialized ID should match original negative ID.");
        }
    }
}
