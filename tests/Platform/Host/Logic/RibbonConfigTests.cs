using System;
using System.IO;
using System.Linq;
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

        [Test]
        public void RibbonConfig_CmdDetailItemFactory_IsEnabledAcrossSupportedRevitVersions()
        {
            string configPath = Path.Combine(TestPathHelper.GetProjectRoot(), "src", "Assets", "ribbon_config.json");
            RibbonConfig? config = JsonConvert.DeserializeObject<RibbonConfig>(File.ReadAllText(configPath));
            Assert.IsNotNull(config?.Panels);

            RibbonItemConfig? detailItemButton = config!.Panels!
                .SelectMany(p => p.Items ?? Enumerable.Empty<RibbonItemConfig>())
                .FirstOrDefault(i => string.Equals(
                    i.Class,
                    "Synthetic.Features.DetailItemFactory.Commands.CmdDetailItemFactory",
                    StringComparison.Ordinal));

            Assert.IsNotNull(detailItemButton, "Expected CmdDetailItemFactory in ribbon_config.json.");
            Assert.IsNull(detailItemButton!.MinVersion, "CmdDetailItemFactory should not have a minVersion restriction.");
            Assert.IsNull(detailItemButton.MaxVersion, "CmdDetailItemFactory should not have a maxVersion restriction.");

            foreach (int version in new[] { 2023, 2024, 2025, 2026, 2027 })
            {
                Assert.IsTrue(
                    RibbonItemFilter.ShouldIncludeItem(detailItemButton, isDebug: false, activeVersion: version),
                    $"Expected CmdDetailItemFactory to be enabled in Release for Revit {version}.");
                Assert.IsTrue(
                    RibbonItemFilter.ShouldIncludeItem(detailItemButton, isDebug: true, activeVersion: version),
                    $"Expected CmdDetailItemFactory to be enabled in Debug for Revit {version}.");
            }
        }
    }
}
