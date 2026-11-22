using System;
using NUnit.Framework;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Synthetic.Infrastructure.Serialization;
using Synthetic.RevitDOM.Models;
using Synthetic.RevitDOM.Translation;
using Synthetic.RevitDOM.Operations;
using Synthetic.RevitDOM;
using Synthetic.Shared;

namespace SyntheticTests.Modules.RevitDOM
{
    [TestFixture]
    public class EnumModelTests
    {
        [Test]
        public void EnumStringMapping_ValidStringValues_SerializesAndDeserializesSymmetrically()
        {
            // Arrange
            var model = new EnumModel("System.DayOfWeek", "Friday");

            // Act
            var json = Json.Encode(model);
            var decoded = Json.Decode(json);
            var roundTrip = JsonConvert.DeserializeObject<EnumModel>(json);

            // Assert
            Assert.IsNotNull(decoded, "Decoded object should not be null.");
            Assert.IsInstanceOf<JObject>(decoded, "Decoded object should be a JObject.");
            
            var jObj = (JObject)decoded!;
            Assert.AreEqual("System.DayOfWeek", (string)jObj["Type"]!, "Enum type name matches.");
            Assert.AreEqual("Friday", (string)jObj["Value"]!, "Enum string value matches.");

            Assert.IsNotNull(roundTrip, "Round-trip deserialized object should not be null.");
            Assert.AreEqual("System.DayOfWeek", roundTrip!.Type, "Deserialized type name matches.");
            Assert.AreEqual("Friday", roundTrip.Value, "Deserialized value name matches.");
        }

        [Test]
        public void EnumTypeReconstruction_NativeCLRType_ReconstructsEnumTypeAndValue()
        {
            // Arrange
            var model = new EnumModel(typeof(DayOfWeek), DayOfWeek.Monday);

            // Act
            var json = Json.Encode(model);
            var decoded = Json.Decode(json);
            var roundTrip = JsonConvert.DeserializeObject<EnumModel>(json);

            // Assert
            Assert.IsNotNull(decoded, "Decoded object should not be null.");
            Assert.IsInstanceOf<JObject>(decoded, "Decoded object should be a JObject.");
            
            var jObj = (JObject)decoded!;
            Assert.AreEqual("System.DayOfWeek", (string)jObj["Type"]!, "Enum type name matches.");
            Assert.AreEqual("Monday", (string)jObj["Value"]!, "Enum string value matches.");

            Assert.IsNotNull(roundTrip, "Round-trip deserialized object should not be null.");
            Assert.AreEqual("System.DayOfWeek", roundTrip!.Type, "Deserialized type name matches.");
            Assert.AreEqual("Monday", roundTrip.Value, "Deserialized value name matches.");

            var reconstructedEnum = roundTrip.ToEnum();
            Assert.IsNotNull(reconstructedEnum, "Reconstructed enum should not be null.");
            Assert.IsInstanceOf<DayOfWeek>(reconstructedEnum, "Reconstructed enum should be of type DayOfWeek.");
            Assert.AreEqual(DayOfWeek.Monday, (DayOfWeek)reconstructedEnum, "Reconstructed enum value should match.");
        }
    }
}
