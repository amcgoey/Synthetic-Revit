using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Synthetic.Platform.Host;
using SyntheticTests.Helpers;

namespace SyntheticTests.Platform.Host
{
    [TestFixture]
    [UnitTest]
    public class RibbonConfigTests
    {
        [Test]
        public void RibbonConfig_ParsesWithoutErrors_AndDeserializesPanels()
        {
            string projectRoot = TestPathHelper.GetProjectRoot();
            string configPath = Path.Combine(projectRoot, "src", "Assets", "ribbon_config.json");

            Assert.IsTrue(File.Exists(configPath), $"Ribbon configuration file 'ribbon_config.json' not found at: {configPath}");

            string jsonContent = File.ReadAllText(configPath);

            Assert.DoesNotThrow(() =>
            {
                JObject rawConfig = JObject.Parse(jsonContent);
                Assert.IsNotNull(rawConfig);
            }, "Expected ribbon_config.json to parse as valid JSON.");

            RibbonConfig? config = JsonConvert.DeserializeObject<RibbonConfig>(jsonContent);
            Assert.IsNotNull(config);
            Assert.IsFalse(string.IsNullOrWhiteSpace(config!.TabName));
            Assert.IsNotNull(config.Panels);
            Assert.IsNotEmpty(config.Panels!);
        }
    }
}
