using System;
using Newtonsoft.Json;
using NUnit.Framework;
using Synthetic.Core;

namespace SyntheticTests.Infrastructure.UI
{
    [TestFixture]
    public class RibbonManagerTests
    {
        [Test]
        public void IsVersionMatch_NoVersionBounds_ReturnsTrue()
        {
            // Arrange
            var item = new RibbonItemConfig();

            // Act & Assert
            Assert.IsTrue(RibbonManager.IsVersionMatch(item, 2022));
            Assert.IsTrue(RibbonManager.IsVersionMatch(item, 2024));
            Assert.IsTrue(RibbonManager.IsVersionMatch(item, 2026));
        }

        [Test]
        public void IsVersionMatch_MinVersionOnly_FiltersCorrectly()
        {
            // Arrange
            var item = new RibbonItemConfig { MinVersion = 2024 };

            // Act & Assert
            Assert.IsFalse(RibbonManager.IsVersionMatch(item, 2022));
            Assert.IsFalse(RibbonManager.IsVersionMatch(item, 2023));
            Assert.IsTrue(RibbonManager.IsVersionMatch(item, 2024));
            Assert.IsTrue(RibbonManager.IsVersionMatch(item, 2026));
        }

        [Test]
        public void IsVersionMatch_MaxVersionOnly_FiltersCorrectly()
        {
            // Arrange
            var item = new RibbonItemConfig { MaxVersion = 2025 };

            // Act & Assert
            Assert.IsTrue(RibbonManager.IsVersionMatch(item, 2022));
            Assert.IsTrue(RibbonManager.IsVersionMatch(item, 2025));
            Assert.IsFalse(RibbonManager.IsVersionMatch(item, 2026));
        }

        [Test]
        public void IsVersionMatch_BothBounds_FiltersCorrectly()
        {
            // Arrange
            var item = new RibbonItemConfig { MinVersion = 2023, MaxVersion = 2025 };

            // Act & Assert
            Assert.IsFalse(RibbonManager.IsVersionMatch(item, 2022));
            Assert.IsTrue(RibbonManager.IsVersionMatch(item, 2023));
            Assert.IsTrue(RibbonManager.IsVersionMatch(item, 2024));
            Assert.IsTrue(RibbonManager.IsVersionMatch(item, 2025));
            Assert.IsFalse(RibbonManager.IsVersionMatch(item, 2026));
        }

        [Test]
        public void Deserialize_WithVersionBounds_ParsesCorrectly()
        {
            // Arrange
            string json = @"
            {
                ""type"": ""PushButton"",
                ""name"": ""TestButton"",
                ""minVersion"": 2024,
                ""maxVersion"": 2026
            }";

            // Act
            var item = JsonConvert.DeserializeObject<RibbonItemConfig>(json);

            // Assert
            Assert.IsNotNull(item);
            Assert.AreEqual("PushButton", item!.Type);
            Assert.AreEqual("TestButton", item.Name);
            Assert.AreEqual(2024, item.MinVersion);
            Assert.AreEqual(2026, item.MaxVersion);
        }

        [Test]
        public void Deserialize_WithoutVersionBounds_ParsesAsNull()
        {
            // Arrange
            string json = @"
            {
                ""type"": ""PushButton"",
                ""name"": ""TestButton""
            }";

            // Act
            var item = JsonConvert.DeserializeObject<RibbonItemConfig>(json);

            // Assert
            Assert.IsNotNull(item);
            Assert.IsNull(item!.MinVersion);
            Assert.IsNull(item.MaxVersion);
        }
    }
}
