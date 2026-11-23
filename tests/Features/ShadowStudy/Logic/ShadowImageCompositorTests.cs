using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NUnit.Framework;
using Synthetic.Features.ShadowStudy.Services;
using SyntheticTests.Fixtures;

namespace SyntheticTests.Features.ShadowStudy
{
    [TestFixture]
    [UnitTest]
    [FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
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
            string outPathMidpoint = Path.Combine(_tempDir, "out_shaded_midpoint.png");
            string outPathExplicitZero = Path.Combine(_tempDir, "out_shaded_explicit0.png");

            // Frame 0: sunlit surface RGB(200, 180, 160), shadowed by 0.80 -> RGB(160, 144, 128)
            FakeShadowStudyAdapter.WriteSolidTestPng(ref0, 2, 2, 160, 180, 200, 255);
            FakeShadowStudyAdapter.WriteSolidTestPng(frame0, 2, 2, 128, 144, 160, 255);

            // Frame 1 (midpoint): directional lighting shifted surface to RGB(150, 135, 120), unshadowed in this frame
            FakeShadowStudyAdapter.WriteSolidTestPng(ref1, 2, 2, 120, 135, 150, 255);
            FakeShadowStudyAdapter.WriteSolidTestPng(frame1, 2, 2, 120, 135, 150, 255);

            var pairs = new[]
            {
                new KeyValuePair<string, string>(frame0, ref0),
                new KeyValuePair<string, string>(frame1, ref1)
            };

            var compositor = new ShadowImageCompositor();

            // Default outputBaseReferenceIndex = null selects midpoint (index 1: ref1 * 0.80 = RGB(120, 108, 96))
            compositor.CompositePerFrameReference(pairs, outPathMidpoint);
            byte[] midpointResult = ReadBgra32Pixels(outPathMidpoint, out _, out _);
            Assert.AreEqual(96, midpointResult[0]);
            Assert.AreEqual(108, midpointResult[1]);
            Assert.AreEqual(120, midpointResult[2]);
            Assert.AreEqual(255, midpointResult[3]);

            // Explicit outputBaseReferenceIndex = 0 selects ref0 * 0.80 = RGB(160, 144, 128)
            compositor.CompositePerFrameReference(pairs, outPathExplicitZero, outputBaseReferenceIndex: 0);
            byte[] explicitResult = ReadBgra32Pixels(outPathExplicitZero, out _, out _);
            Assert.AreEqual(128, explicitResult[0]);
            Assert.AreEqual(144, explicitResult[1]);
            Assert.AreEqual(160, explicitResult[2]);
            Assert.AreEqual(255, explicitResult[3]);
        }

        [Test]
        public void CompositePerFrameReference_WithVaryingHourlyShading_NormalizesLightingWithoutSelfDarkeningAndDarkensShadowsMonotonically()
        {
            const int width = 4;
            const int height = 1;

            // 3 timestamps where directional N*L surface lighting varies across morning, peak noon, and afternoon:
            // x=0: sunlit colored surface in all 3 frames (must match peak ref1 without self-darkening!)
            // x=1: black linework (0,0,0) (0/0 guard stays 0,0,0)
            // x=2: shadowed in 1 of 3 frames (morning 0.80x)
            // x=3: shadowed in all 3 frames (0.80^3 = 0.512x)
            string ref0 = Path.Combine(_tempDir, "morn_ref.png");
            string frame0 = Path.Combine(_tempDir, "morn_frame.png");
            string ref1 = Path.Combine(_tempDir, "noon_ref.png");
            string frame1 = Path.Combine(_tempDir, "noon_frame.png");
            string ref2 = Path.Combine(_tempDir, "aft_ref.png");
            string frame2 = Path.Combine(_tempDir, "aft_frame.png");
            string outPath = Path.Combine(_tempDir, "peak_shaded.png");

            byte[] r0 = new byte[width * 4];
            SetPixel(r0, 0, 150, 170, 190, 255);
            SetPixel(r0, 1, 0, 0, 0, 255);
            SetPixel(r0, 2, 150, 170, 190, 255);
            SetPixel(r0, 3, 150, 170, 190, 255);
            WritePixelsPng(ref0, width, height, r0);

            byte[] f0 = new byte[width * 4];
            SetPixel(f0, 0, 150, 170, 190, 255); // sunlit -> ratio 1.0
            SetPixel(f0, 1, 0, 0, 0, 255);
            SetPixel(f0, 2, 120, 136, 152, 255); // 0.80x shadow
            SetPixel(f0, 3, 120, 136, 152, 255); // 0.80x shadow
            WritePixelsPng(frame0, width, height, f0);

            byte[] r1 = new byte[width * 4];
            SetPixel(r1, 0, 180, 200, 220, 255); // peak-solar-altitude noon surface
            SetPixel(r1, 1, 0, 0, 0, 255);
            SetPixel(r1, 2, 180, 200, 220, 255);
            SetPixel(r1, 3, 180, 200, 220, 255);
            WritePixelsPng(ref1, width, height, r1);

            byte[] f1 = new byte[width * 4];
            SetPixel(f1, 0, 180, 200, 220, 255); // sunlit -> ratio 1.0
            SetPixel(f1, 1, 0, 0, 0, 255);
            SetPixel(f1, 2, 180, 200, 220, 255); // sunlit -> ratio 1.0
            SetPixel(f1, 3, 144, 160, 176, 255); // 0.80x shadow
            WritePixelsPng(frame1, width, height, f1);

            byte[] r2 = new byte[width * 4];
            SetPixel(r2, 0, 125, 145, 165, 255);
            SetPixel(r2, 1, 0, 0, 0, 255);
            SetPixel(r2, 2, 125, 145, 165, 255);
            SetPixel(r2, 3, 125, 145, 165, 255);
            WritePixelsPng(ref2, width, height, r2);

            byte[] f2 = new byte[width * 4];
            SetPixel(f2, 0, 125, 145, 165, 255); // sunlit -> ratio 1.0
            SetPixel(f2, 1, 0, 0, 0, 255);
            SetPixel(f2, 2, 125, 145, 165, 255); // sunlit -> ratio 1.0
            SetPixel(f2, 3, 100, 116, 132, 255); // 0.80x shadow
            WritePixelsPng(frame2, width, height, f2);

            var compositor = new ShadowImageCompositor();
            compositor.CompositePerFrameReference(
                new[]
                {
                    new KeyValuePair<string, string>(frame0, ref0),
                    new KeyValuePair<string, string>(frame1, ref1),
                    new KeyValuePair<string, string>(frame2, ref2)
                },
                outPath,
                outputBaseReferenceIndex: 1);

            byte[] outPixels = ReadBgra32Pixels(outPath, out _, out _);

            // x=0: Sunlit surface matches noon peak reference B_1 = (180, 200, 220) exactly (no self-darkening)
            Assert.AreEqual(180, outPixels[0 * 4 + 0]);
            Assert.AreEqual(200, outPixels[0 * 4 + 1]);
            Assert.AreEqual(220, outPixels[0 * 4 + 2]);
            Assert.AreEqual(255, outPixels[0 * 4 + 3]);

            // x=1: Black linework preserved
            Assert.AreEqual(0, outPixels[1 * 4 + 0]);
            Assert.AreEqual(0, outPixels[1 * 4 + 1]);
            Assert.AreEqual(0, outPixels[1 * 4 + 2]);
            Assert.AreEqual(255, outPixels[1 * 4 + 3]);

            // x=2: 1-frame shadow (0.80 * peak ref1) = (144, 160, 176)
            Assert.AreEqual(144, outPixels[2 * 4 + 0]);
            Assert.AreEqual(160, outPixels[2 * 4 + 1]);
            Assert.AreEqual(176, outPixels[2 * 4 + 2]);

            // x=3: 3-frame shadow (0.512 * peak ref1) = (92, 102, 113) -> strictly darker than 1-frame shadow
            Assert.AreEqual(92, outPixels[3 * 4 + 0]);
            Assert.AreEqual(102, outPixels[3 * 4 + 1]);
            Assert.AreEqual(113, outPixels[3 * 4 + 2]);
            Assert.Less(outPixels[3 * 4 + 0], outPixels[2 * 4 + 0]);
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
