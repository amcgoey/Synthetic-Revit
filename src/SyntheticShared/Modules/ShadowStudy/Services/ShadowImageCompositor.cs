using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Synthetic.Modules.ShadowStudy.Services
{
    /// <summary>
    /// Streaming, MTA/STA-safe WPF <see cref="System.Windows.Media.Imaging"/> compositor performing
    /// base-ratio Multiply blending against an unshadowed <c>ShadowIntensity = 0</c> reference pass.
    /// </summary>
    public class ShadowImageCompositor
    {
        /// <summary>
        /// Composites <paramref name="framePngPaths"/> against a single shared <paramref name="referencePngPath"/>
        /// and writes an opaque 32-bit BGRA PNG to <paramref name="outputPngPath"/>.
        /// </summary>
        public void Composite(
            string referencePngPath,
            IReadOnlyList<string> framePngPaths,
            string outputPngPath)
        {
            if (string.IsNullOrWhiteSpace(referencePngPath))
                throw new ArgumentException("Reference PNG path is required.", nameof(referencePngPath));
            if (framePngPaths == null || framePngPaths.Count == 0)
                throw new ArgumentException("At least one frame PNG path is required.", nameof(framePngPaths));

            var pairs = new List<KeyValuePair<string, string>>(framePngPaths.Count);
            foreach (string framePath in framePngPaths)
            {
                pairs.Add(new KeyValuePair<string, string>(framePath, referencePngPath));
            }

            CompositeInternal(referencePngPath, pairs, outputPngPath);
        }

        /// <summary>
        /// Composites frame PNGs where each frame carries its own <c>ShadowIntensity = 0</c> reference pass,
        /// normalizing each frame against its paired reference before Multiply-blending shadow ratios onto
        /// the selected base reference pass (defaulting to the midpoint frame when
        /// <paramref name="outputBaseReferenceIndex"/> is <c>null</c>).
        /// </summary>
        public void CompositePerFrameReference(
            IReadOnlyList<KeyValuePair<string, string>> frameAndReferencePaths,
            string outputPngPath,
            int? outputBaseReferenceIndex = null)
        {
            if (frameAndReferencePaths == null || frameAndReferencePaths.Count == 0)
                throw new ArgumentException("At least one frame/reference pair is required.", nameof(frameAndReferencePaths));

            int rawIndex = outputBaseReferenceIndex ?? (frameAndReferencePaths.Count / 2);
            int clampedIndex = Math.Max(0, Math.Min(frameAndReferencePaths.Count - 1, rawIndex));
            string baseReferencePath = frameAndReferencePaths[clampedIndex].Value;
            CompositeInternal(baseReferencePath, frameAndReferencePaths, outputPngPath);
        }

        private static void CompositeInternal(
            string baseReferencePath,
            IReadOnlyList<KeyValuePair<string, string>> frameAndReferencePaths,
            string outputPngPath)
        {
            if (string.IsNullOrWhiteSpace(baseReferencePath) || !File.Exists(baseReferencePath))
                throw new FileNotFoundException("Reference PNG file not found.", baseReferencePath);
            if (string.IsNullOrWhiteSpace(outputPngPath))
                throw new ArgumentException("Output PNG path is required.", nameof(outputPngPath));

            byte[] basePixels = DecodeBgra32(baseReferencePath, out int width, out int height);
            int pixelCount = width * height;
            double[] ratioAccumulator = new double[pixelCount * 3];
            for (int i = 0; i < ratioAccumulator.Length; i++)
            {
                ratioAccumulator[i] = 1.0;
            }

            string? cachedRefPath = baseReferencePath;
            byte[] cachedRefPixels = basePixels;

            foreach (var item in frameAndReferencePaths)
            {
                string framePath = item.Key;
                string refPath = item.Value;

                if (string.IsNullOrWhiteSpace(framePath) || !File.Exists(framePath))
                    throw new FileNotFoundException("Frame PNG file not found.", framePath);

                byte[] refPixels;
                if (string.Equals(refPath, cachedRefPath, StringComparison.OrdinalIgnoreCase))
                {
                    refPixels = cachedRefPixels;
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(refPath) || !File.Exists(refPath))
                        throw new FileNotFoundException("Per-frame reference PNG file not found.", refPath);

                    refPixels = DecodeBgra32(refPath, out int refW, out int refH);
                    if (refW != width || refH != height)
                    {
                        throw new InvalidOperationException(
                            $"Reference PNG '{refPath}' dimensions ({refW}x{refH}) do not match base dimensions ({width}x{height}).");
                    }

                    cachedRefPath = refPath;
                    cachedRefPixels = refPixels;
                }

                byte[] framePixels = DecodeBgra32(framePath, out int frameW, out int frameH);
                if (frameW != width || frameH != height)
                {
                    throw new InvalidOperationException(
                        $"Frame PNG '{framePath}' dimensions ({frameW}x{frameH}) do not match reference dimensions ({width}x{height}).");
                }

                for (int p = 0; p < pixelCount; p++)
                {
                    int bIdx = p * 4;
                    int rIdx = p * 3;

                    for (int c = 0; c < 3; c++)
                    {
                        byte bRef = refPixels[bIdx + c];
                        byte bFrame = framePixels[bIdx + c];
                        double ratio = bRef == 0 ? 1.0 : Math.Min(1.0, (double)bFrame / bRef);
                        ratioAccumulator[rIdx + c] *= ratio;
                    }
                }
            }

            byte[] outPixels = new byte[pixelCount * 4];
            for (int p = 0; p < pixelCount; p++)
            {
                int bIdx = p * 4;
                int rIdx = p * 3;

                outPixels[bIdx + 0] = ClampByte(Math.Round(basePixels[bIdx + 0] * ratioAccumulator[rIdx + 0]));
                outPixels[bIdx + 1] = ClampByte(Math.Round(basePixels[bIdx + 1] * ratioAccumulator[rIdx + 1]));
                outPixels[bIdx + 2] = ClampByte(Math.Round(basePixels[bIdx + 2] * ratioAccumulator[rIdx + 2]));
                outPixels[bIdx + 3] = 255;
            }

            string? outDir = Path.GetDirectoryName(outputPngPath);
            if (!string.IsNullOrEmpty(outDir))
            {
                Directory.CreateDirectory(outDir);
            }

            BitmapSource resultBitmap = BitmapSource.Create(
                width,
                height,
                96.0,
                96.0,
                PixelFormats.Bgra32,
                null,
                outPixels,
                width * 4);
            resultBitmap.Freeze();

            using (var outStream = new FileStream(outputPngPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(resultBitmap));
                encoder.Save(outStream);
            }
        }

        private static byte[] DecodeBgra32(string path, out int width, out int height)
        {
            BitmapSource source;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var decoder = new PngBitmapDecoder(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                if (decoder.Frames.Count == 0)
                {
                    throw new InvalidDataException($"PNG file '{path}' contains no frames.");
                }

                BitmapFrame frame = decoder.Frames[0];
                if (frame.Format != PixelFormats.Bgra32)
                {
                    var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
                    converted.Freeze();
                    source = converted;
                }
                else
                {
                    frame.Freeze();
                    source = frame;
                }
            }

            width = source.PixelWidth;
            height = source.PixelHeight;
            int stride = width * 4;
            byte[] pixels = new byte[stride * height];
            source.CopyPixels(pixels, stride, 0);
            return pixels;
        }

        private static byte ClampByte(double value)
        {
            if (value <= 0.0) return 0;
            if (value >= 255.0) return 255;
            return (byte)value;
        }
    }
}
