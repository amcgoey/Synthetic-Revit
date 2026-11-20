using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NUnit.Framework;
using Synthetic.Modules.ShadowStudy.Services;

namespace SyntheticTests.Modules.ShadowStudy
{
    [TestFixture]
    public class ShadowImageCompositorTests
    {
        private string _tempDir = string.Empty;

        [SetUp]
        public void SetUp()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "ShadowCompositorTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }

        [Test]
        [Apartment(ApartmentState.MTA)]
        public void Composite_UnderMtaThread_PreservesWhiteBlackHalftoneAndMonotonicDarkening()
        {
            RunCoreCompositingVerification();
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void Composite_UnderStaThread_PreservesWhiteBlackHalftoneAndMonotonicDarkening()
        {
            RunCoreCompositingVerification();
        }

        [Test]
        public void Composite_DimensionMismatch_ThrowsInvalidOperationException()
        {
            string refPath = Path.Combine(_tempDir, "ref.png");
            string framePath = Path.Combine(_tempDir, "frame.png");
            string outPath = Path.Combine(_tempDir, "out.png");

            FakeShadowStudyAdapter.WriteSolidTestPng(refPath, 8, 8, 255, 255, 255, 255);
            FakeShadowStudyAdapter.WriteSolidTestPng(framePath, 10, 8, 200, 200, 200, 255);

            var compositor = new ShadowImageCompositor();
            Assert.Throws<InvalidOperationException>(() =>
                compositor.Composite(refPath, new[] { framePath }, outPath));
        }

        [Test]
        public void Composite_ReleasesFileHandlesImmediatelyAfterReturning()
        {
            string refPath = Path.Combine(_tempDir, "ref.png");
            string framePath = Path.Combine(_tempDir, "frame.png");
            string outPath = Path.Combine(_tempDir, "out.png");

            FakeShadowStudyAdapter.WriteSolidTestPng(refPath, 4, 4, 255, 255, 255, 255);
            FakeShadowStudyAdapter.WriteSolidTestPng(framePath, 4, 4, 217, 217, 217, 255);

            var compositor = new ShadowImageCompositor();
            compositor.Composite(refPath, new[] { framePath }, outPath);

            Assert.DoesNotThrow(() => File.Delete(refPath), "Reference PNG must not be locked.");
            Assert.DoesNotThrow(() => File.Delete(framePath), "Frame PNG must not be locked.");
            Assert.IsTrue(File.Exists(outPath));
        }

        [Test]
        public void CompositePerFrameReference_PreservesBaseSurfaceColorWhileMultiplyingShadows()
        {
            string ref0 = Path.Combine(_tempDir, "ref0.png");
            string frame0 = Path.Combine(_tempDir, "frame0.png");
            string ref1 = Path.Combine(_tempDir, "ref1.png");
            string frame1 = Path.Combine(_tempDir, "frame1.png");
            string outPath = Path.Combine(_tempDir, "out_shaded.png");

            // Frame 0: sunlit surface RGB(200, 180, 160), shadowed by 0.80 -> RGB(160, 144, 128)
            FakeShadowStudyAdapter.WriteSolidTestPng(ref0, 2, 2, 160, 180, 200, 255);
            FakeShadowStudyAdapter.WriteSolidTestPng(frame0, 2, 2, 128, 144, 160, 255);

            // Frame 1: directional lighting shifted surface to RGB(150, 135, 120), unshadowed in this frame
            FakeShadowStudyAdapter.WriteSolidTestPng(ref1, 2, 2, 120, 135, 150, 255);
            FakeShadowStudyAdapter.WriteSolidTestPng(frame1, 2, 2, 120, 135, 150, 255);

            var compositor = new ShadowImageCompositor();
            compositor.CompositePerFrameReference(
                new[]
                {
                    new KeyValuePair<string, string>(frame0, ref0),
                    new KeyValuePair<string, string>(frame1, ref1)
                },
                outPath);

            byte[] result = ReadBgra32Pixels(outPath, out _, out _);
            Assert.AreEqual(128, result[0]);
            Assert.AreEqual(144, result[1]);
            Assert.AreEqual(160, result[2]);
            Assert.AreEqual(255, result[3]);
        }

        private void RunCoreCompositingVerification()
        {
            // 5x1 strip:
            // x=0: Pure white (255,255,255) never shadowed
            // x=1: Black linework (0,0,0) inside shadowed area (0/0 guard)
            // x=2: Halftone gray (190,190,190) never shadowed
            // x=3: White (255,255,255) shadowed in 1 of 4 frames (intensity ~33% => byte 170)
            // x=4: White (255,255,255) shadowed in all 4 of 4 frames (0.669^4 ~ 0.20 => RGB ~51)
            const int width = 5;
            const int height = 1;
            string refPath = Path.Combine(_tempDir, "ref.png");

            byte[] refPixels = new byte[width * height * 4];
            SetPixel(refPixels, 0, 255, 255, 255, 255);
            SetPixel(refPixels, 1, 0, 0, 0, 255);
            SetPixel(refPixels, 2, 190, 190, 190, 255);
            SetPixel(refPixels, 3, 255, 255, 255, 255);
            SetPixel(refPixels, 4, 255, 255, 255, 255);
            WritePixelsPng(refPath, width, height, refPixels);

            // For N=4 at 80% darkness, ShadowIntensity = 33 => shaded white = round(255 * 0.67) = 171
            var framePaths = new List<string>();
            for (int f = 0; f < 4; f++)
            {
                string framePath = Path.Combine(_tempDir, $"frame_{f}.png");
                byte[] framePixels = new byte[width * height * 4];
                SetPixel(framePixels, 0, 255, 255, 255, 255);
                SetPixel(framePixels, 1, 0, 0, 0, 255);
                SetPixel(framePixels, 2, 190, 190, 190, 255);
                SetPixel(framePixels, 3, f == 0 ? (byte)171 : (byte)255, f == 0 ? (byte)171 : (byte)255, f == 0 ? (byte)171 : (byte)255, 255);
                SetPixel(framePixels, 4, 171, 171, 171, 255);
                WritePixelsPng(framePath, width, height, framePixels);
                framePaths.Add(framePath);
            }

            string outPath = Path.Combine(_tempDir, "composite.png");
            var compositor = new ShadowImageCompositor();
            compositor.Composite(refPath, framePaths, outPath);

            byte[] outPixels = ReadBgra32Pixels(outPath, out int outW, out int outH);
            Assert.AreEqual(width, outW);
            Assert.AreEqual(height, outH);

            // Pixel 0: Pure white preserved
            Assert.AreEqual(255, outPixels[0 * 4 + 0]);
            Assert.AreEqual(255, outPixels[0 * 4 + 3]);

            // Pixel 1: Black linework (0/0 guard) stays 0,0,0 with Alpha=255
            Assert.AreEqual(0, outPixels[1 * 4 + 0]);
            Assert.AreEqual(0, outPixels[1 * 4 + 1]);
            Assert.AreEqual(0, outPixels[1 * 4 + 2]);
            Assert.AreEqual(255, outPixels[1 * 4 + 3]);

            // Pixel 2: Halftone gray (190) never self-darkens
            Assert.AreEqual(190, outPixels[2 * 4 + 0]);
            Assert.AreEqual(190, outPixels[2 * 4 + 1]);
            Assert.AreEqual(190, outPixels[2 * 4 + 2]);

            // Pixel 3 (1 overlap) lighter than Pixel 4 (4 overlaps)
            byte singleOverlap = outPixels[3 * 4 + 0];
            byte fullOverlap = outPixels[4 * 4 + 0];
            Assert.AreEqual(171, singleOverlap);
            Assert.That(fullOverlap, Is.InRange(48, 55));
            Assert.Less(fullOverlap, singleOverlap);
        }

        private static void SetPixel(byte[] buffer, int x, byte b, byte g, byte r, byte a)
        {
            int idx = x * 4;
            buffer[idx + 0] = b;
            buffer[idx + 1] = g;
            buffer[idx + 2] = r;
            buffer[idx + 3] = a;
        }

        private static void WritePixelsPng(string path, int width, int height, byte[] pixels)
        {
            var bmp = BitmapSource.Create(width, height, 96.0, 96.0, PixelFormats.Bgra32, null, pixels, width * 4);
            bmp.Freeze();
            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bmp));
            encoder.Save(fs);
        }

        private static byte[] ReadBgra32Pixels(string path, out int width, out int height)
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var decoder = new PngBitmapDecoder(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var converted = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Bgra32, null, 0);
            converted.Freeze();
            width = converted.PixelWidth;
            height = converted.PixelHeight;
            byte[] pixels = new byte[width * height * 4];
            converted.CopyPixels(pixels, width * 4, 0);
            return pixels;
        }
    }
}
