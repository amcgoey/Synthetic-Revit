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
    public class BooleanModelTests
    {
        [Test]
        public void BooleanSymmetry_TrueValue_SerializesAndDeserializesSymmetrically()
        {
            // Arrange
            var model = new BooleanModel(true);

            // Act
            var json = Json.Encode(model);
            var decoded = Json.Decode(json);
            var roundTrip = JsonConvert.DeserializeObject<BooleanModel>(json);

            // Assert
            Assert.IsNotNull(decoded, "Decoded object should not be null.");
            Assert.IsInstanceOf<JObject>(decoded, "Decoded object should be a JObject.");
            
            var jObj = (JObject)decoded!;
            Assert.IsTrue(jObj.ContainsKey("boolean"), "JSON should contain the 'boolean' property.");
            Assert.IsTrue((bool)jObj["boolean"]!, "Decoded boolean value should be true.");

            Assert.IsNotNull(roundTrip, "Round-trip deserialized object should not be null.");
            Assert.IsTrue(roundTrip!.boolean, "Deserialized boolean value should be true.");
        }

        [Test]
        public void BooleanSymmetry_FalseValue_SerializesAndDeserializesSymmetrically()
        {
            // Arrange
            var model = new BooleanModel(false);

            // Act
            var json = Json.Encode(model);
            var decoded = Json.Decode(json);
            var roundTrip = JsonConvert.DeserializeObject<BooleanModel>(json);

            // Assert
            Assert.IsNotNull(decoded, "Decoded object should not be null.");
            Assert.IsInstanceOf<JObject>(decoded, "Decoded object should be a JObject.");
            
            var jObj = (JObject)decoded!;
            Assert.IsTrue(jObj.ContainsKey("boolean"), "JSON should contain the 'boolean' property.");
            Assert.IsFalse((bool)jObj["boolean"]!, "Decoded boolean value should be false.");

            Assert.IsNotNull(roundTrip, "Round-trip deserialized object should not be null.");
            Assert.IsFalse(roundTrip!.boolean, "Deserialized boolean value should be false.");
        }
    }
}
