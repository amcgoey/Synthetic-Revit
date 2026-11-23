using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Synthetic.Platform.IO;

namespace SyntheticTests.Platform.IO
{
    [TestFixture]
    [UnitTest]
    public class JsonTests
    {
        [Test]
        public void EncodeAndEncodeMinimal_FormatAsExpected()
        {
            var payload = new Dictionary<string, object>
            {
                { "Name", "Wall-01" },
                { "Count", 3 }
            };

            string indented = Json.Encode(payload);
            string minimal = Json.EncodeMinimal(payload);

            StringAssert.Contains("\n", indented);
            Assert.IsFalse(minimal.Contains("\n"));
            Assert.AreEqual("{\"Name\":\"Wall-01\",\"Count\":3}", minimal);
        }

        [Test]
        public void MemoryStreamRoundTrip_EncodeAndDecode_PreservesStructure()
        {
            var original = new Dictionary<string, object>
            {
                { "Id", 42 },
                { "Category", "Walls" }
            };

            string encoded = Json.Encode(original);
            string readBack;

            using (var stream = new MemoryStream())
            {
                using (var writer = new StreamWriter(stream, Encoding.UTF8, bufferSize: 1024, leaveOpen: true))
                {
                    writer.Write(encoded);
                    writer.Flush();
                }

                stream.Position = 0;

                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    readBack = reader.ReadToEnd();
                }
            }

            var decoded = Json.Decode(readBack) as JObject;
            Assert.IsNotNull(decoded);
            Assert.AreEqual(42, (int)decoded!["Id"]!);
            Assert.AreEqual("Walls", (string)decoded["Category"]!);
        }

        [Test]
        public void ListToJSONAndJsonToList_RoundTripsObjectList()
        {
            var items = new List<object> { "alpha", 7, true };

            string json = Json.ListToJSON(items);
            var decoded = Json.JsonToList(json) as JArray;

            Assert.IsNotNull(decoded);
            Assert.AreEqual(3, decoded!.Count);
            Assert.AreEqual("alpha", (string)decoded[0]!);
            Assert.AreEqual(7, (int)decoded[1]!);
            Assert.IsTrue((bool)decoded[2]!);
        }
    }
}
