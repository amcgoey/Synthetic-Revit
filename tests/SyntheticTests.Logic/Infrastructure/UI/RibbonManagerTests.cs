using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
            Assert.IsTrue(RibbonManager.IsVersionMatch(item, 2023));
            Assert.IsTrue(RibbonManager.IsVersionMatch(item, 2024));
            Assert.IsTrue(RibbonManager.IsVersionMatch(item, 2026));
        }

        [Test]
        public void IsVersionMatch_MinVersionOnly_FiltersCorrectly()
        {
            // Arrange
            var item = new RibbonItemConfig { MinVersion = 2024 };

            // Act & Assert
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
            Assert.IsTrue(RibbonManager.IsVersionMatch(item, 2023));
            Assert.IsTrue(RibbonManager.IsVersionMatch(item, 2025));
            Assert.IsFalse(RibbonManager.IsVersionMatch(item, 2026));
        }

        [Test]
        public void IsVersionMatch_BothBounds_FiltersCorrectly()
        {
            // Arrange
            var item = new RibbonItemConfig { MinVersion = 2024, MaxVersion = 2025 };

            // Act & Assert
            Assert.IsFalse(RibbonManager.IsVersionMatch(item, 2023));
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

        [Test]
        public void LoadBitmapImage_ReleasesFileLockImmediatelyForExclusiveOverwrite()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "RibbonIconLockTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            string pngPath = Path.Combine(tempDir, "test_icon_32.png");

            try
            {
                WriteTestPng(pngPath);

                BitmapImage loaded = RibbonManager.LoadBitmapImage(pngPath);

                Assert.IsNotNull(loaded);
                Assert.IsTrue(loaded.IsFrozen);
                Assert.AreEqual(2, loaded.PixelWidth);
                Assert.AreEqual(2, loaded.PixelHeight);

                // Immediately verify no open file handle remains by opening with FileShare.None and overwriting
                using (var exclusiveStream = new FileStream(pngPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    byte[] replacement = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
                    exclusiveStream.SetLength(0);
                    exclusiveStream.Write(replacement, 0, replacement.Length);
                    exclusiveStream.Flush();
                }

                Assert.AreEqual(4, new FileInfo(pngPath).Length);
            }
            finally
            {
                try
                {
                    if (Directory.Exists(tempDir))
                    {
                        Directory.Delete(tempDir, recursive: true);
                    }
                }
                catch
                {
                    // Best-effort cleanup
                }
            }
        }

        private static void WriteTestPng(string path)
        {
            byte[] pixels = new byte[2 * 2 * 4];
            for (int i = 0; i < pixels.Length; i += 4)
            {
                pixels[i] = 255;
                pixels[i + 3] = 255;
            }

            BitmapSource source = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Pbgra32, null, pixels, 2 * 4);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                encoder.Save(fs);
            }
        }
    }
}
