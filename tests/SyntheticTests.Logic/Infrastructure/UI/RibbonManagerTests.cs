using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NUnit.Framework;
using Synthetic.Platform.Host;

namespace SyntheticTests.Infrastructure.UI
{
    [TestFixture]
    public class RibbonManagerTests
    {
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
