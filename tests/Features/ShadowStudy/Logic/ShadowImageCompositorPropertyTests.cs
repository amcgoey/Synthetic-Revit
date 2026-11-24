using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CsCheck;
using NUnit.Framework;
using Synthetic.Features.ShadowStudy.Services;

namespace SyntheticTests.Features.ShadowStudy
{
    [TestFixture]
    [UnitTest]
    public class ShadowImageCompositorPropertyTests
    {
        private sealed class CompositeBufferCase
        {
            public int Width { get; set; }
            public int Height { get; set; }
            public byte[] BaseReferencePixels { get; set; } = Array.Empty<byte>();
            public List<byte[]> FramePixels { get; set; } = new List<byte[]>();
            public byte[] ExtraFramePixels { get; set; } = Array.Empty<byte>();
        }

        private sealed class PerFrameBufferCase
        {
            public int Width { get; set; }
            public int Height { get; set; }
            public List<byte[]> ReferencePixels { get; set; } = new List<byte[]>();
            public List<byte[]> FramePixels { get; set; } = new List<byte[]>();
            public byte[] ExtraReferencePixels { get; set; } = Array.Empty<byte>();
            public byte[] ExtraFramePixels { get; set; } = Array.Empty<byte>();
            public int BaseReferenceIndex { get; set; }
        }

        private static Gen<byte[]> GenOpaqueBgraBuffer(int pixelCount)
        {
            return Gen.Byte.Array[pixelCount * 3].Select(rgb =>
            {
                byte[] bgra = new byte[pixelCount * 4];
                for (int p = 0; p < pixelCount; p++)
                {
                    int rgbIdx = p * 3;
                    int bgraIdx = p * 4;
                    bgra[bgraIdx + 0] = rgb[rgbIdx + 0];
                    bgra[bgraIdx + 1] = rgb[rgbIdx + 1];
                    bgra[bgraIdx + 2] = rgb[rgbIdx + 2];
                    bgra[bgraIdx + 3] = 255;
                }
                return bgra;
            });
        }

        private static readonly Gen<CompositeBufferCase> GenCompositeBufferCase =
            Gen.SelectMany(
                Gen.Int[1, 12],
                Gen.Int[1, 12],
                Gen.Int[1, 4],
                (width, height, frameCount) =>
                {
                    int pixelCount = width * height;
                    return Gen.Select(
                        GenOpaqueBgraBuffer(pixelCount),
                        GenOpaqueBgraBuffer(pixelCount).List[frameCount],
                        GenOpaqueBgraBuffer(pixelCount),
                        (refBuf, frames, extraFrame) => new CompositeBufferCase
                        {
                            Width = width,
                            Height = height,
                            BaseReferencePixels = refBuf,
                            FramePixels = frames,
                            ExtraFramePixels = extraFrame
                        });
                });

        private static readonly Gen<PerFrameBufferCase> GenPerFrameBufferCase =
            Gen.SelectMany(
                Gen.Int[1, 10],
                Gen.Int[1, 10],
                Gen.Int[1, 3],
                (width, height, pairCount) =>
                {
                    int pixelCount = width * height;
                    return Gen.Select(
                        GenOpaqueBgraBuffer(pixelCount).List[pairCount],
                        GenOpaqueBgraBuffer(pixelCount).List[pairCount],
                        GenOpaqueBgraBuffer(pixelCount),
                        GenOpaqueBgraBuffer(pixelCount),
                        Gen.Int[0, pairCount - 1],
                        (refs, frames, extraRef, extraFrame, baseIdx) => new PerFrameBufferCase
                        {
                            Width = width,
                            Height = height,
                            ReferencePixels = refs,
                            FramePixels = frames,
                            ExtraReferencePixels = extraRef,
                            ExtraFramePixels = extraFrame,
                            BaseReferenceIndex = baseIdx
                        });
                });

        [Test]
        public void Composite_ArbitrarySmallBuffers_PreservesDimensionsChannelBoundsAndZeroSelfDarkening()
        {
            GenCompositeBufferCase.Sample(testCase =>
            {
                string iterDir = CreateIterationTempDirectory();
                try
                {
                    string refPath = Path.Combine(iterDir, "ref.png");
                    WriteBgra32Png(refPath, testCase.Width, testCase.Height, testCase.BaseReferencePixels);

                    var framePaths = new List<string>(testCase.FramePixels.Count);
                    for (int i = 0; i < testCase.FramePixels.Count; i++)
                    {
                        string framePath = Path.Combine(iterDir, $"frame_{i}.png");
                        WriteBgra32Png(framePath, testCase.Width, testCase.Height, testCase.FramePixels[i]);
                        framePaths.Add(framePath);
                    }

                    string outPath = Path.Combine(iterDir, "composite.png");
                    var compositor = new ShadowImageCompositor();
                    compositor.Composite(refPath, framePaths, outPath);

                    byte[] outPixels = ReadBgra32Png(outPath, out int outW, out int outH);
                    Assert.AreEqual(testCase.Width, outW);
                    Assert.AreEqual(testCase.Height, outH);
                    Assert.AreEqual(testCase.Width * testCase.Height * 4, outPixels.Length);

                    int pixelCount = testCase.Width * testCase.Height;
                    for (int p = 0; p < pixelCount; p++)
                    {
                        int bIdx = p * 4;
                        Assert.AreEqual(255, outPixels[bIdx + 3], "Output alpha must always be opaque (255).");

                        for (int c = 0; c < 3; c++)
                        {
                            byte baseVal = testCase.BaseReferencePixels[bIdx + c];
                            byte outVal = outPixels[bIdx + c];

                            Assert.LessOrEqual(outVal, baseVal, "Multiply composite must never brighten above reference.");

                            if (baseVal == 0)
                            {
                                Assert.AreEqual(0, outVal, "Zero-valued reference pixels (black linework) must remain 0.");
                            }

                            bool neverShadowedInAnyFrame = true;
                            for (int f = 0; f < testCase.FramePixels.Count; f++)
                            {
                                if (testCase.FramePixels[f][bIdx + c] < baseVal)
                                {
                                    neverShadowedInAnyFrame = false;
                                    break;
                                }
                            }

                            if (neverShadowedInAnyFrame)
                            {
                                Assert.AreEqual(baseVal, outVal, "Unshadowed channels must not self-darken.");
                            }
                        }
                    }
                }
                finally
                {
                    CleanupIterationTempDirectory(iterDir);
                }
            }, iter: 40);
        }

        [Test]
        public void Composite_AdditionalShadowFrame_MonotonicallyDarkensOrPreservesAllChannels()
        {
            GenCompositeBufferCase.Sample(testCase =>
            {
                string iterDir = CreateIterationTempDirectory();
                try
                {
                    string refPath = Path.Combine(iterDir, "ref.png");
                    WriteBgra32Png(refPath, testCase.Width, testCase.Height, testCase.BaseReferencePixels);

                    var baseFrames = new List<string>(testCase.FramePixels.Count);
                    for (int i = 0; i < testCase.FramePixels.Count; i++)
                    {
                        string framePath = Path.Combine(iterDir, $"frame_{i}.png");
                        WriteBgra32Png(framePath, testCase.Width, testCase.Height, testCase.FramePixels[i]);
                        baseFrames.Add(framePath);
                    }

                    string extraFramePath = Path.Combine(iterDir, "frame_extra.png");
                    WriteBgra32Png(extraFramePath, testCase.Width, testCase.Height, testCase.ExtraFramePixels);

                    var extendedFrames = new List<string>(baseFrames) { extraFramePath };

                    string outBeforePath = Path.Combine(iterDir, "out_before.png");
                    string outAfterPath = Path.Combine(iterDir, "out_after.png");

                    var compositor = new ShadowImageCompositor();
                    compositor.Composite(refPath, baseFrames, outBeforePath);
                    compositor.Composite(refPath, extendedFrames, outAfterPath);

                    byte[] beforePixels = ReadBgra32Png(outBeforePath, out int w1, out int h1);
                    byte[] afterPixels = ReadBgra32Png(outAfterPath, out int w2, out int h2);

                    Assert.AreEqual(testCase.Width, w1);
                    Assert.AreEqual(testCase.Height, h1);
                    Assert.AreEqual(testCase.Width, w2);
                    Assert.AreEqual(testCase.Height, h2);

                    int pixelCount = testCase.Width * testCase.Height;
                    for (int p = 0; p < pixelCount; p++)
                    {
                        int bIdx = p * 4;
                        Assert.AreEqual(255, beforePixels[bIdx + 3]);
                        Assert.AreEqual(255, afterPixels[bIdx + 3]);

                        for (int c = 0; c < 3; c++)
                        {
                            Assert.LessOrEqual(
                                afterPixels[bIdx + c],
                                beforePixels[bIdx + c],
                                "Adding another Shadow Frame must monotonically darken or preserve every pixel channel.");
                        }
                    }
                }
                finally
                {
                    CleanupIterationTempDirectory(iterDir);
                }
            }, iter: 40);
        }

        [Test]
        public void CompositePerFrameReference_AdditionalShadowFrame_PreservesDimensionsBoundsAndMonotonicDarkening()
        {
            GenPerFrameBufferCase.Sample(testCase =>
            {
                string iterDir = CreateIterationTempDirectory();
                try
                {
                    var pairsBefore = new List<KeyValuePair<string, string>>(testCase.FramePixels.Count);
                    for (int i = 0; i < testCase.FramePixels.Count; i++)
                    {
                        string rPath = Path.Combine(iterDir, $"ref_{i}.png");
                        string fPath = Path.Combine(iterDir, $"frame_{i}.png");
                        WriteBgra32Png(rPath, testCase.Width, testCase.Height, testCase.ReferencePixels[i]);
                        WriteBgra32Png(fPath, testCase.Width, testCase.Height, testCase.FramePixels[i]);
                        pairsBefore.Add(new KeyValuePair<string, string>(fPath, rPath));
                    }

                    string extraRefPath = Path.Combine(iterDir, "ref_extra.png");
                    string extraFramePath = Path.Combine(iterDir, "frame_extra.png");
                    WriteBgra32Png(extraRefPath, testCase.Width, testCase.Height, testCase.ExtraReferencePixels);
                    WriteBgra32Png(extraFramePath, testCase.Width, testCase.Height, testCase.ExtraFramePixels);

                    var pairsAfter = new List<KeyValuePair<string, string>>(pairsBefore)
                    {
                        new KeyValuePair<string, string>(extraFramePath, extraRefPath)
                    };

                    string outBeforePath = Path.Combine(iterDir, "perframe_before.png");
                    string outAfterPath = Path.Combine(iterDir, "perframe_after.png");

                    var compositor = new ShadowImageCompositor();
                    compositor.CompositePerFrameReference(pairsBefore, outBeforePath, testCase.BaseReferenceIndex);
                    compositor.CompositePerFrameReference(pairsAfter, outAfterPath, testCase.BaseReferenceIndex);

                    byte[] baseRefPixels = testCase.ReferencePixels[testCase.BaseReferenceIndex];
                    byte[] beforePixels = ReadBgra32Png(outBeforePath, out int w1, out int h1);
                    byte[] afterPixels = ReadBgra32Png(outAfterPath, out int w2, out int h2);

                    Assert.AreEqual(testCase.Width, w1);
                    Assert.AreEqual(testCase.Height, h1);
                    Assert.AreEqual(testCase.Width, w2);
                    Assert.AreEqual(testCase.Height, h2);

                    int pixelCount = testCase.Width * testCase.Height;
                    for (int p = 0; p < pixelCount; p++)
                    {
                        int bIdx = p * 4;
                        Assert.AreEqual(255, beforePixels[bIdx + 3]);
                        Assert.AreEqual(255, afterPixels[bIdx + 3]);

                        for (int c = 0; c < 3; c++)
                        {
                            Assert.LessOrEqual(beforePixels[bIdx + c], baseRefPixels[bIdx + c]);
                            Assert.LessOrEqual(afterPixels[bIdx + c], beforePixels[bIdx + c]);
                        }
                    }
                }
                finally
                {
                    CleanupIterationTempDirectory(iterDir);
                }
            }, iter: 40);
        }

        private static string CreateIterationTempDirectory()
        {
            string dir = Path.Combine(
                Path.GetTempPath(),
                "SyntheticShadowCompositorCsCheck_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static void CleanupIterationTempDirectory(string dir)
        {
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        private static void WriteBgra32Png(string path, int width, int height, byte[] pixels)
        {
            var bmp = BitmapSource.Create(width, height, 96.0, 96.0, PixelFormats.Bgra32, null, pixels, width * 4);
            bmp.Freeze();

            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bmp));
                encoder.Save(fs);
            }
        }

        private static byte[] ReadBgra32Png(string path, out int width, out int height)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
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
}
