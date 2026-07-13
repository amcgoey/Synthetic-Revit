using System;
using NUnit.Framework;
using Newtonsoft.Json.Linq;
using Synthetic.Infrastructure.Serialization;
using Synthetic.Modules.RevitDOM;

namespace SyntheticTests.Modules.RevitDOM
{
    [TestFixture]
    public class ColorModelTests
    {
        [Test]
        public void StandardSymmetry_MidRangeValues_SerializesAndDeserializesSymmetrically()
        {
            // Arrange
            var model = new ColorModel(100, 150, 200);

            // Act
            var json = Json.Encode(model);
            var decoded = Json.Decode(json);
            var roundTrip = ColorModel.ByJSON(json);

            // Assert
            Assert.IsNotNull(decoded, "Decoded object should not be null.");
            Assert.IsInstanceOf<JObject>(decoded, "Decoded object should be a JObject.");
            
            var jObj = (JObject)decoded!;
            Assert.AreEqual(100, (byte)jObj["Red"]!, "Red component matches.");
            Assert.AreEqual(150, (byte)jObj["Green"]!, "Green component matches.");
            Assert.AreEqual(200, (byte)jObj["Blue"]!, "Blue component matches.");
            Assert.IsTrue((bool)jObj["IsValid"]!, "IsValid matches.");

            Assert.IsNotNull(roundTrip, "Round-trip deserialized object should not be null.");
            Assert.AreEqual(100, roundTrip!.Red, "Deserialized Red component matches.");
            Assert.AreEqual(150, roundTrip.Green, "Deserialized Green component matches.");
            Assert.AreEqual(200, roundTrip.Blue, "Deserialized Blue component matches.");
            Assert.IsTrue(roundTrip.IsValid, "Deserialized IsValid matches.");
        }

        [Test]
        public void BoundaryTest_MaximumValues_SerializesAndDeserializesWithoutOverflow()
        {
            // Arrange
            var model = new ColorModel(255, 255, 255);

            // Act
            var json = Json.Encode(model);
            var decoded = Json.Decode(json);
            var roundTrip = ColorModel.ByJSON(json);

            // Assert
            Assert.IsNotNull(decoded, "Decoded object should not be null.");
            Assert.IsInstanceOf<JObject>(decoded, "Decoded object should be a JObject.");
            
            var jObj = (JObject)decoded!;
            Assert.AreEqual(255, (byte)jObj["Red"]!, "Red component matches at max.");
            Assert.AreEqual(255, (byte)jObj["Green"]!, "Green component matches at max.");
            Assert.AreEqual(255, (byte)jObj["Blue"]!, "Blue component matches at max.");

            Assert.IsNotNull(roundTrip, "Round-trip deserialized object should not be null.");
            Assert.AreEqual(255, roundTrip!.Red, "Deserialized Red component matches at max.");
            Assert.AreEqual(255, roundTrip.Green, "Deserialized Green component matches at max.");
            Assert.AreEqual(255, roundTrip.Blue, "Deserialized Blue component matches at max.");
        }

        [Test]
        public void BoundaryTest_MinimumValues_SerializesAndDeserializesSymmetrically()
        {
            // Arrange
            var model = new ColorModel(0, 0, 0);

            // Act
            var json = Json.Encode(model);
            var decoded = Json.Decode(json);
            var roundTrip = ColorModel.ByJSON(json);

            // Assert
            Assert.IsNotNull(decoded, "Decoded object should not be null.");
            Assert.IsInstanceOf<JObject>(decoded, "Decoded object should be a JObject.");
            
            var jObj = (JObject)decoded!;
            Assert.AreEqual(0, (byte)jObj["Red"]!, "Red component matches at min.");
            Assert.AreEqual(0, (byte)jObj["Green"]!, "Green component matches at min.");
            Assert.AreEqual(0, (byte)jObj["Blue"]!, "Blue component matches at min.");

            Assert.IsNotNull(roundTrip, "Round-trip deserialized object should not be null.");
            Assert.AreEqual(0, roundTrip!.Red, "Deserialized Red component matches at min.");
            Assert.AreEqual(0, roundTrip.Green, "Deserialized Green component matches at min.");
            Assert.AreEqual(0, roundTrip.Blue, "Deserialized Blue component matches at min.");
        }
    }
}
