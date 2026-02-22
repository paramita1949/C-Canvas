using System;
using System.IO;
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
                using var image = SKImage.FromBitmap(skBitmap);
                using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                
                var bitmapImage = new BitmapImage();
                bitmapImage.BeginInit();
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapImage.StreamSource = data.AsStream();
                bitmapImage.EndInit();
                bitmapImage.Freeze(); //  线程安全：冻结对象，允许跨线程访问
                
                return bitmapImage;
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
                // 将BitmapSource编码为PNG
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmapSource));
                
                using var memoryStream = new MemoryStream();
                encoder.Save(memoryStream);
                memoryStream.Seek(0, SeekOrigin.Begin);
                
                // 使用SkiaSharp解码
                return SKBitmap.Decode(memoryStream);
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


