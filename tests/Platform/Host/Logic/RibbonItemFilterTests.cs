using Newtonsoft.Json;
using NUnit.Framework;
using Synthetic.Platform.Host;

namespace SyntheticTests.Platform.Host
{
    [TestFixture]
    [UnitTest]
    public class RibbonItemFilterTests
    {
        [Test]
        public void IsVersionMatch_NoVersionBounds_ReturnsTrue()
        {
            var item = new RibbonItemConfig();

            Assert.IsTrue(RibbonItemFilter.IsVersionMatch(item, 2023));
            Assert.IsTrue(RibbonItemFilter.IsVersionMatch(item, 2024));
            Assert.IsTrue(RibbonItemFilter.IsVersionMatch(item, 2026));
        }

        [Test]
        public void IsVersionMatch_MinVersionOnly_FiltersCorrectly()
        {
            var item = new RibbonItemConfig { MinVersion = 2024 };

            Assert.IsFalse(RibbonItemFilter.IsVersionMatch(item, 2023));
            Assert.IsTrue(RibbonItemFilter.IsVersionMatch(item, 2024));
            Assert.IsTrue(RibbonItemFilter.IsVersionMatch(item, 2026));
        }

        [Test]
        public void IsVersionMatch_MaxVersionOnly_FiltersCorrectly()
        {
            var item = new RibbonItemConfig { MaxVersion = 2025 };

            Assert.IsTrue(RibbonItemFilter.IsVersionMatch(item, 2023));
            Assert.IsTrue(RibbonItemFilter.IsVersionMatch(item, 2025));
            Assert.IsFalse(RibbonItemFilter.IsVersionMatch(item, 2026));
        }

        [Test]
        public void IsVersionMatch_BothBounds_FiltersCorrectly()
        {
            var item = new RibbonItemConfig { MinVersion = 2024, MaxVersion = 2025 };

            Assert.IsFalse(RibbonItemFilter.IsVersionMatch(item, 2023));
            Assert.IsTrue(RibbonItemFilter.IsVersionMatch(item, 2024));
            Assert.IsTrue(RibbonItemFilter.IsVersionMatch(item, 2025));
            Assert.IsFalse(RibbonItemFilter.IsVersionMatch(item, 2026));
        }

        [Test]
        public void ShouldIncludeItem_DebugOnly_ExcludesReleaseAndRespectsVersionBounds()
        {
            var debugItem = new RibbonItemConfig { DebugOnly = true, MinVersion = 2025 };

            Assert.IsFalse(RibbonItemFilter.ShouldIncludeItem(debugItem, isDebug: false, activeVersion: 2026));
            Assert.IsFalse(RibbonItemFilter.ShouldIncludeItem(debugItem, isDebug: true, activeVersion: 2024));
            Assert.IsTrue(RibbonItemFilter.ShouldIncludeItem(debugItem, isDebug: true, activeVersion: 2026));
        }

        [Test]
        public void Deserialize_WithVersionBounds_ParsesCorrectly()
        {
            string json = @"
            {
                ""type"": ""PushButton"",
                ""name"": ""TestButton"",
                ""minVersion"": 2024,
                ""maxVersion"": 2026
            }";

            var item = JsonConvert.DeserializeObject<RibbonItemConfig>(json);

            Assert.IsNotNull(item);
            Assert.AreEqual("PushButton", item!.Type);
            Assert.AreEqual("TestButton", item.Name);
            Assert.AreEqual(2024, item.MinVersion);
            Assert.AreEqual(2026, item.MaxVersion);
        }

        [Test]
        public void Deserialize_WithoutVersionBounds_ParsesAsNull()
        {
            string json = @"
            {
                ""type"": ""PushButton"",
                ""name"": ""TestButton""
            }";

            var item = JsonConvert.DeserializeObject<RibbonItemConfig>(json);

            Assert.IsNotNull(item);
            Assert.IsNull(item!.MinVersion);
            Assert.IsNull(item.MaxVersion);
        }
    }
}
