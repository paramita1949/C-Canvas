using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ImageColorChanger.Utils;
using SkiaSharp;
using Xunit;

namespace Canvas.TextEditor.Tests.Utils
{
    public class SkiaWpfHelperTests
    {
        [Theory]
        [InlineData(SKColorType.Bgra8888, SKAlphaType.Premul)]
        [InlineData(SKColorType.Rgba8888, SKAlphaType.Premul)]
        [InlineData(SKColorType.Rgba8888, SKAlphaType.Unpremul)]
        [InlineData(SKColorType.Bgra8888, SKAlphaType.Unpremul)]
        [InlineData(SKColorType.Bgra8888, SKAlphaType.Opaque)]
        [InlineData(SKColorType.Rgb888x, SKAlphaType.Opaque)]
        [InlineData(SKColorType.Rgb565, SKAlphaType.Opaque)]
        [InlineData(SKColorType.Gray8, SKAlphaType.Opaque)]
        public void CommonSkiaFormatsUseOwnedFrozenPremultipliedPixels(SKColorType colorType, SKAlphaType alphaType)
        {
            OnSta(() =>
            {
                using var bitmap = CreateSkia(colorType, alphaType);
                var legacy = LegacyToWpf(bitmap);
                var actual = SkiaWpfHelper.ConvertToWpfBitmap(bitmap);
                Assert.NotNull(actual);
                Assert.Equal(3, actual.PixelWidth);
                Assert.Equal(2, actual.PixelHeight);
                Assert.Equal(PixelFormats.Pbgra32, actual.Format);
                Assert.IsNotType<BitmapImage>(actual);
                Assert.True(actual.IsFrozen);
                AssertPixelsNear(legacy, actual, 1);
                var before = Pixels(actual);
                bitmap.Erase(SKColors.Black);
                bitmap.Dispose();
                Assert.Equal(before, Pixels(actual));
            });
        }

        [Fact]
        public void OpaquePixelsWithUnusedAlphaBecomeFullyOpaque()
        {
            OnSta(() =>
            {
                using var source = new SKBitmap(new SKImageInfo(1, 1, SKColorType.Bgra8888, SKAlphaType.Opaque));
                Marshal.Copy(new byte[] { 7, 19, 31, 0 }, 0, source.GetPixels(), 4);
                var actual = SkiaWpfHelper.ConvertToWpfBitmap(source);
                Assert.Equal(new byte[] { 7, 19, 31, 255 }, Pixels(actual));
            });
        }

        [Fact]
        public void PaddedSourceStrideDoesNotBleedIntoNextRow()
        {
            OnSta(() =>
            {
                using var source = new SKBitmap();
                Assert.True(source.TryAllocPixels(new SKImageInfo(3, 2, SKColorType.Bgra8888, SKAlphaType.Premul), 32));
                source.Erase(SKColors.Transparent);
                source.SetPixel(0, 0, SKColors.Red);
                source.SetPixel(0, 1, SKColors.Blue);
                var actual = SkiaWpfHelper.ConvertToWpfBitmap(source);
                var legacy = LegacyToWpf(source);
                AssertPixelsNear(legacy, actual, 0);
            });
        }

        [Theory]
        [InlineData("Rgb24")]
        [InlineData("Bgra32")]
        [InlineData("Pbgra32")]
        [InlineData("Gray8")]
        [InlineData("Indexed8")]
        public void WpfFormatsNormalizeToPremultipliedSkiaWithoutPng(string name)
        {
            OnSta(() =>
            {
                var input = CreateWpf(name);
                var expected = Pixels(input);
                using var actual = SkiaWpfHelper.ConvertToSkBitmap(input);
                Assert.NotNull(actual);
                Assert.Equal(SKColorType.Bgra8888, actual.ColorType);
                Assert.Equal(SKAlphaType.Premul, actual.AlphaType);
                using var pixmap = actual.PeekPixels();
                byte[] bytes = new byte[expected.Length];
                Marshal.Copy(pixmap.GetPixels(), bytes, 0, bytes.Length);
                Assert.Equal(expected, bytes);
                Assert.Equal(input.PixelWidth, actual.Width);
                Assert.Equal(input.PixelHeight, actual.Height);
            });
        }

        [Fact]
        public void TransparentPixelsMatchLegacyOnBlackAndWhiteBackgrounds()
        {
            OnSta(() =>
            {
                using var source = CreateSkia(SKColorType.Rgba8888, SKAlphaType.Unpremul);
                var actual = SkiaWpfHelper.ConvertToWpfBitmap(source);
                var expected = LegacyToWpf(source);
                foreach (byte background in new byte[] { 0, 255 })
                {
                    var a = Composite(Pixels(actual), background);
                    var e = Composite(Pixels(expected), background);
                    for (int i = 0; i < a.Length; i++) Assert.InRange(Math.Abs(a[i] - e[i]), 0, 1);
                }
            });
        }

        [Fact]
        public void FrozenResultCanBeReadOnAnotherThreadAfterSourceDisposal()
        {
            BitmapSource output = null;
            byte[] expected = null;
            OnSta(() =>
            {
                using var source = CreateSkia(SKColorType.Bgra8888, SKAlphaType.Premul);
                output = SkiaWpfHelper.ConvertToWpfBitmap(source);
                expected = Pixels(output);
            });
            Exception error = null;
            var thread = new Thread(() => { try { Assert.Equal(expected, Pixels(output)); } catch (Exception ex) { error = ex; } });
            thread.Start(); thread.Join();
            if (error != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
        }

        [Fact]
        public void NullEmptyMissingAndCorruptInputsRemainSafe()
        {
            OnSta(() =>
            {
                Assert.Null(SkiaWpfHelper.ConvertToWpfBitmap(null));
                Assert.Null(SkiaWpfHelper.ConvertToSkBitmap(null));
                Assert.Null(SkiaWpfHelper.LoadBitmapSource(null));
                Assert.Null(SkiaWpfHelper.LoadBitmapSource(" "));
                Assert.Null(SkiaWpfHelper.LoadBitmapSource(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".missing")));
                string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".png");
                try { File.WriteAllBytes(path, new byte[] { 1, 2, 3 }); Assert.Null(SkiaWpfHelper.LoadBitmapSource(path)); }
                finally { File.Delete(path); }
            });
        }

        [Theory]
        [InlineData(SKEncodedImageFormat.Jpeg)]
        [InlineData(SKEncodedImageFormat.Png)]
        [InlineData(SKEncodedImageFormat.Webp)]
        public void ExistingFileDecoderKeepsDimensionsAndFrozenOutput(SKEncodedImageFormat format)
        {
            OnSta(() =>
            {
                using var source = CreateSkia(SKColorType.Bgra8888, SKAlphaType.Premul);
                using var image = SKImage.FromBitmap(source);
                using var encoded = image.Encode(format, 95);
                string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".image");
                try
                {
                    File.WriteAllBytes(path, encoded.ToArray());
                    var result = SkiaWpfHelper.LoadBitmapSource(path);
                    Assert.NotNull(result); Assert.Equal(3, result.PixelWidth); Assert.Equal(2, result.PixelHeight);
                    Assert.True(result.IsFrozen);
                }
                finally { File.Delete(path); }
            });
        }

        [Fact]
        public void HighPrecisionSkiaInputRetainsLegacyCompatibilityPath()
        {
            OnSta(() =>
            {
                using var source = CreateSkia(SKColorType.RgbaF16, SKAlphaType.Premul);
                var expected = LegacyToWpf(source);
                var actual = SkiaWpfHelper.ConvertToWpfBitmap(source);
                Assert.NotNull(actual); Assert.IsType<BitmapImage>(actual);
                AssertPixelsNear(expected, actual, 0);
            });
        }

        [Fact]
        public void NonSrgbColorSpaceRetainsLegacyCompatibilityPath()
        {
            OnSta(() =>
            {
                using var colorSpace = SKColorSpace.CreateSrgbLinear();
                using var source = new SKBitmap(new SKImageInfo(3, 2, SKColorType.Bgra8888, SKAlphaType.Premul, colorSpace));
                source.Erase(new SKColor(83, 121, 201, 128));
                var expected = LegacyToWpf(source);
                var actual = SkiaWpfHelper.ConvertToWpfBitmap(source);
                Assert.NotNull(actual); Assert.IsType<BitmapImage>(actual);
                AssertPixelsNear(expected, actual, 0);
            });
        }

        [Fact]
        public void Gray16WpfInputRetainsLegacyPrecisionPath()
        {
            OnSta(() =>
            {
                var input = BitmapSource.Create(2, 1, 96, 96, PixelFormats.Gray16, null, new byte[] { 0, 0, 255, 255 }, 4);
                input.Freeze();
                using var actual = SkiaWpfHelper.ConvertToSkBitmap(input);
                using var expected = LegacyToSkia(input);
                Assert.NotNull(actual); Assert.Equal(expected.Info, actual.Info);
                Assert.Equal(expected.Bytes, actual.Bytes);
            });
        }

        private static SKBitmap CreateSkia(SKColorType type, SKAlphaType alpha)
        {
            var bitmap = new SKBitmap(new SKImageInfo(3, 2, type, alpha));
            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(SKColors.Transparent);
            using var paint = new SKPaint { BlendMode = SKBlendMode.Src };
            SKColor[] colors = { new SKColor(201, 47, 83, 128), SKColors.Transparent, SKColors.Red, SKColors.Blue, SKColors.White, new SKColor(41, 153, 231, 64) };
            for (int i = 0; i < colors.Length; i++) { paint.Color = colors[i]; canvas.DrawRect(i % 3, i / 3, 1, 1, paint); }
            return bitmap;
        }

        private static BitmapSource CreateWpf(string name)
        {
            PixelFormat format; byte[] data; int stride; BitmapPalette palette = null;
            switch (name)
            {
                case "Rgb24": format = PixelFormats.Rgb24; data = new byte[] { 255, 0, 0, 0, 0, 255 }; stride = 6; break;
                case "Bgra32": format = PixelFormats.Bgra32; data = new byte[] { 83, 47, 201, 128, 0, 0, 255, 255 }; stride = 8; break;
                case "Pbgra32": format = PixelFormats.Pbgra32; data = new byte[] { 42, 24, 101, 128, 0, 0, 255, 255 }; stride = 8; break;
                case "Gray8": format = PixelFormats.Gray8; data = new byte[] { 50, 230 }; stride = 2; break;
                default: format = PixelFormats.Indexed8; data = new byte[] { 0, 1 }; stride = 2; palette = new BitmapPalette(new[] { Colors.Transparent, Colors.Red }); break;
            }
            var bitmap = BitmapSource.Create(2, 1, 96, 96, format, palette, data, stride); bitmap.Freeze(); return bitmap;
        }

        private static BitmapSource LegacyToWpf(SKBitmap bitmap)
        {
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            using var stream = data.AsStream();
            var result = new BitmapImage(); result.BeginInit(); result.CacheOption = BitmapCacheOption.OnLoad;
            result.StreamSource = stream; result.EndInit(); result.Freeze(); return result;
        }

        private static SKBitmap LegacyToSkia(BitmapSource input)
        {
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(input));
            using var stream = new MemoryStream(); encoder.Save(stream); stream.Position = 0; return SKBitmap.Decode(stream);
        }

        private static byte[] Pixels(BitmapSource bitmap)
        {
            var normalized = bitmap.Format == PixelFormats.Pbgra32 ? bitmap : new FormatConvertedBitmap(bitmap, PixelFormats.Pbgra32, null, 0);
            byte[] pixels = new byte[normalized.PixelWidth * normalized.PixelHeight * 4]; normalized.CopyPixels(pixels, normalized.PixelWidth * 4, 0); return pixels;
        }

        private static void AssertPixelsNear(BitmapSource expected, BitmapSource actual, int tolerance)
        {
            var e = Pixels(expected); var a = Pixels(actual); Assert.Equal(e.Length, a.Length);
            for (int i = 0; i < a.Length; i++) Assert.InRange(Math.Abs(e[i] - a[i]), 0, tolerance);
        }

        private static byte[] Composite(byte[] pixels, byte background)
        {
            byte[] output = new byte[pixels.Length / 4 * 3];
            for (int i = 0; i < pixels.Length / 4; i++) for (int c = 0; c < 3; c++) output[i * 3 + c] = (byte)Math.Min(255, pixels[i * 4 + c] + (background * (255 - pixels[i * 4 + 3]) + 127) / 255);
            return output;
        }

        private static void OnSta(Action action)
        {
            Exception error = null;
            var thread = new Thread(() => { try { action(); } catch (Exception ex) { error = ex; } });
            thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
            if (error != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
        }
    }
}
