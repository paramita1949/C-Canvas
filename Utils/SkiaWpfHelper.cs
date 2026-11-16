using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SkiaSharp;

namespace ImageColorChanger.Utils
{
    /// <summary>
    /// SkiaSharp 与 WPF 互转辅助类
    /// </summary>
    public static class SkiaWpfHelper
    {
        /// <summary>
        /// 将SKBitmap转换为WPF的BitmapSource
        /// </summary>
        public static BitmapSource ConvertToWpfBitmap(SKBitmap skBitmap)
        {
            if (skBitmap == null)
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine(" [SkiaWpfHelper] SKBitmap为空，返回null");
#endif
                return null;
            }
            
            try
            {
                if (RequiresEncodedCompatibility(skBitmap))
                {
                    return ConvertToWpfBitmapEncoded(skBitmap);
                }

                // BitmapSource.Create 复制像素；不能把已释放的 Skia 内存留给 WPF。
                if (skBitmap.ColorType == SKColorType.Bgra8888 && skBitmap.AlphaType == SKAlphaType.Premul)
                {
                    return CopyPremultipliedPixels(skBitmap);
                }

                // Opaque/Rgb888x 的 alpha 字节可能未定义；必须真正转换而非仅换格式标签。
                using var normalized = new SKBitmap(new SKImageInfo(
                    skBitmap.Width, skBitmap.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
                using var pixels = skBitmap.PeekPixels();
                if (pixels == null || !pixels.ReadPixels(normalized.Info, normalized.GetPixels(), normalized.RowBytes))
                {
                    return ConvertToWpfBitmapEncoded(skBitmap);
                }

                if (skBitmap.AlphaType == SKAlphaType.Opaque)
                {
                    SetOpaqueAlpha(normalized);
                }

                return CopyPremultipliedPixels(normalized);
            }
            catch (Exception ex)
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine($" [SkiaWpfHelper] 转换失败: {ex.Message}");
#else
                _ = ex;
#endif
                return null;
            }
        }

        /// <summary>
        /// 使用 SkiaSharp 解码文件，再转换为冻结的 WPF BitmapSource。
        /// WPF 自身缺少 WEBP 编解码器时也能通过此入口加载。
        /// </summary>
        public static BitmapSource LoadBitmapSource(string imagePath)
        {
            if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
            {
                return null;
            }

            try
            {
                using var skBitmap = SKBitmap.Decode(imagePath);
                return ConvertToWpfBitmap(skBitmap);
            }
            catch (Exception ex)
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine($" [SkiaWpfHelper] 文件解码失败: {ex.Message}");
#else
                _ = ex;
#endif
                return null;
            }
        }
        
        /// <summary>
        /// 将WPF的BitmapSource转换为SKBitmap
        /// </summary>
        public static SKBitmap ConvertToSkBitmap(BitmapSource bitmapSource)
        {
            if (bitmapSource == null)
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine(" [SkiaWpfHelper] BitmapSource为空，返回null");
#endif
                return null;
            }
            
            try
            {
                // 非8位/色彩管理输入先保持既有编码行为，避免静默截断精度或改变色彩。
                if (!SupportsDirectWpfPixels(bitmapSource.Format) ||
                    bitmapSource is BitmapFrame frame && frame.ColorContexts?.Count > 0)
                {
                    return ConvertToSkBitmapEncoded(bitmapSource);
                }

                var normalized = bitmapSource.Format == PixelFormats.Pbgra32
                    ? bitmapSource
                    : new FormatConvertedBitmap(bitmapSource, PixelFormats.Pbgra32, null, 0);
                var bitmap = new SKBitmap(new SKImageInfo(
                    normalized.PixelWidth, normalized.PixelHeight, SKColorType.Bgra8888, SKAlphaType.Premul));
                try
                {
                    int capacity = checked(bitmap.RowBytes * bitmap.Height);
                    normalized.CopyPixels(Int32Rect.Empty, bitmap.GetPixels(), capacity, bitmap.RowBytes);
                    return bitmap;
                }
                catch
                {
                    bitmap.Dispose();
                    throw;
                }
            }
            catch (Exception ex)
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine($" [SkiaWpfHelper] 转换失败: {ex.Message}");
#else
                _ = ex;
#endif
                return null;
            }
        }

        private static BitmapSource CopyPremultipliedPixels(SKBitmap bitmap)
        {
            int capacity = checked(bitmap.RowBytes * bitmap.Height);
            var result = BitmapSource.Create(
                bitmap.Width, bitmap.Height, 96, 96, PixelFormats.Pbgra32, null,
                bitmap.GetPixels(), capacity, bitmap.RowBytes);
            result.Freeze();
            return result;
        }

        private static bool SupportsDirectWpfPixels(PixelFormat format)
        {
            // 只优化已验证格式；其余格式保持既有编码器/色彩管理语义。
            return format == PixelFormats.Pbgra32 || format == PixelFormats.Bgra32 ||
                format == PixelFormats.Rgb24 || format == PixelFormats.Gray8 ||
                format == PixelFormats.Indexed8;
        }

        private static unsafe void SetOpaqueAlpha(SKBitmap bitmap)
        {
            _ = checked(bitmap.RowBytes * bitmap.Height);
            byte* pixels = (byte*)bitmap.GetPixels();
            for (int y = 0; y < bitmap.Height; y++)
            {
                byte* row = pixels + y * bitmap.RowBytes;
                for (int x = 0; x < bitmap.Width; x++)
                {
                    row[x * 4 + 3] = 255;
                }
            }
        }

        private static bool RequiresEncodedCompatibility(SKBitmap bitmap)
        {
            if (bitmap.Info.ColorSpace != null && !bitmap.Info.ColorSpace.IsSrgb)
            {
                return true;
            }

            return bitmap.ColorType != SKColorType.Bgra8888 &&
                bitmap.ColorType != SKColorType.Rgba8888 &&
                bitmap.ColorType != SKColorType.Rgb888x &&
                bitmap.ColorType != SKColorType.Rgb565 &&
                bitmap.ColorType != SKColorType.Gray8;
        }

        private static BitmapSource ConvertToWpfBitmapEncoded(SKBitmap bitmap)
        {
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            using var stream = data.AsStream();
            var result = new BitmapImage();
            result.BeginInit();
            result.CacheOption = BitmapCacheOption.OnLoad;
            result.StreamSource = stream;
            result.EndInit();
            result.Freeze();
            return result;
        }

        private static SKBitmap ConvertToSkBitmapEncoded(BitmapSource bitmap)
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            stream.Position = 0;
            return SKBitmap.Decode(stream);
        }
        
        /// <summary>
        /// 将SKColor转换为System.Windows.Media.Color
        /// </summary>
        public static System.Windows.Media.Color ToWpfColor(SKColor skColor)
        {
            return System.Windows.Media.Color.FromArgb(
                skColor.Alpha,
                skColor.Red,
                skColor.Green,
                skColor.Blue
            );
        }
        
        /// <summary>
        /// 将System.Windows.Media.Color转换为SKColor
        /// </summary>
        public static SKColor ToSkColor(System.Windows.Media.Color wpfColor)
        {
            return new SKColor(
                wpfColor.R,
                wpfColor.G,
                wpfColor.B,
                wpfColor.A
            );
        }
        
        /// <summary>
        /// 将WPF TextAlignment转换为SKTextAlign
        /// </summary>
        public static SKTextAlign ToSkTextAlign(System.Windows.TextAlignment wpfAlignment)
        {
            return wpfAlignment switch
            {
                System.Windows.TextAlignment.Center => SKTextAlign.Center,
                System.Windows.TextAlignment.Right => SKTextAlign.Right,
                System.Windows.TextAlignment.Justify => SKTextAlign.Left,
                _ => SKTextAlign.Left
            };
        }
        
        /// <summary>
        /// 将字符串对齐方式转换为SKTextAlign
        /// </summary>
        public static SKTextAlign ToSkTextAlign(string alignment)
        {
            return alignment?.ToLower() switch
            {
                "center" => SKTextAlign.Center,
                "right" => SKTextAlign.Right,
                _ => SKTextAlign.Left
            };
        }
    }
}


