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
    public class UVModelTests
    {
        [Test]
        public void StandardSymmetry_StandardCoordinates_SerializesAndDeserializesSymmetrically()
        {
            // Arrange
            var model = new UVModel(10.5, -5.25);

            // Act
            var json = Json.Encode(model);
            var decoded = Json.Decode(json);
            var roundTrip = UVModel.ByJSON(json);

            // Assert
            Assert.IsNotNull(decoded, "Decoded object should not be null.");
            Assert.IsInstanceOf<JObject>(decoded, "Decoded object should be a JObject.");
            
            var jObj = (JObject)decoded!;
            Assert.AreEqual(10.5, (double)jObj["U"]!, "U coordinate matches.");
            Assert.AreEqual(-5.25, (double)jObj["V"]!, "V coordinate matches.");

            Assert.IsNotNull(roundTrip, "Round-trip deserialized object should not be null.");
            Assert.AreEqual(10.5, roundTrip.U, "Deserialized U coordinate matches.");
            Assert.AreEqual(-5.25, roundTrip.V, "Deserialized V coordinate matches.");
        }

        [Test]
        public void HighPrecision_FloatingPointNumbers_RetainsPrecisionWithoutRoundingLoss()
        {
            // Arrange
            var model = new UVModel(0.123456789012345, -0.987654321098765);

            // Act
            var json = Json.Encode(model);
            var decoded = Json.Decode(json);
            var roundTrip = UVModel.ByJSON(json);

            // Assert
            Assert.IsNotNull(decoded, "Decoded object should not be null.");
            Assert.IsInstanceOf<JObject>(decoded, "Decoded object should be a JObject.");
            
            var jObj = (JObject)decoded!;
            Assert.AreEqual(0.123456789012345, (double)jObj["U"]!, "High-precision U matches.");
            Assert.AreEqual(-0.987654321098765, (double)jObj["V"]!, "High-precision V matches.");

            Assert.IsNotNull(roundTrip, "Round-trip deserialized object should not be null.");
            Assert.AreEqual(0.123456789012345, roundTrip.U, "Deserialized high-precision U matches.");
            Assert.AreEqual(-0.987654321098765, roundTrip.V, "Deserialized high-precision V matches.");
        }

        [Test]
        public void ZeroVector_ZeroCoordinates_SerializesAndDeserializesSymmetrically()
        {
            // Arrange
            var model = new UVModel(0.0, 0.0);

            // Act
            var json = Json.Encode(model);
            var decoded = Json.Decode(json);
            var roundTrip = UVModel.ByJSON(json);

            // Assert
            Assert.IsNotNull(decoded, "Decoded object should not be null.");
            Assert.IsInstanceOf<JObject>(decoded, "Decoded object should be a JObject.");
            
            var jObj = (JObject)decoded!;
            Assert.AreEqual(0.0, (double)jObj["U"]!, "Zero U coordinate matches.");
            Assert.AreEqual(0.0, (double)jObj["V"]!, "Zero V coordinate matches.");

            Assert.IsNotNull(roundTrip, "Round-trip deserialized object should not be null.");
            Assert.AreEqual(0.0, roundTrip.U, "Deserialized zero U coordinate matches.");
            Assert.AreEqual(0.0, roundTrip.V, "Deserialized zero V coordinate matches.");
        }
    }
}
