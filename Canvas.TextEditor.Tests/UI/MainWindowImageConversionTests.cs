using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ImageColorChanger.UI;
using SkiaSharp;
using Xunit;

namespace Canvas.TextEditor.Tests.UI
{
    public class MainWindowImageConversionTests
    {
        [Theory]
        [InlineData("Rgb24")]
        [InlineData("Bgra32")]
        [InlineData("Pbgra32")]
        public void SlideReverseConversionUsesActualWpfFormat(string formatName)
        {
            Exception error = null;
            var thread = new Thread(() =>
            {
                try
                {
                    PixelFormat format = formatName == "Rgb24" ? PixelFormats.Rgb24 : formatName == "Bgra32" ? PixelFormats.Bgra32 : PixelFormats.Pbgra32;
                    byte[] input = formatName == "Rgb24" ? new byte[] { 201, 47, 83, 31, 73, 159 }
                        : formatName == "Bgra32" ? new byte[] { 83, 47, 201, 128, 159, 73, 31, 255 }
                        : new byte[] { 42, 24, 101, 128, 159, 73, 31, 255 };
                    var source = BitmapSource.Create(2, 1, 96, 96, format, null, input, input.Length);
                    source.Freeze();
                    var normalized = new FormatConvertedBitmap(source, PixelFormats.Pbgra32, null, 0);
                    byte[] expected = new byte[8]; normalized.CopyPixels(expected, 8, 0);
                    // 这个私有方法无实例状态依赖；不构造业务窗口、启动应用或打开数据库。
                    var instance = RuntimeHelpers.GetUninitializedObject(typeof(MainWindow));
                    var method = typeof(MainWindow).GetMethod("ConvertBitmapSourceToSKBitmap", BindingFlags.Instance | BindingFlags.NonPublic);
                    using var actual = (SKBitmap)method.Invoke(instance, new object[] { source });
                    byte[] pixels = new byte[8]; Marshal.Copy(actual.GetPixels(), pixels, 0, pixels.Length);
                    Assert.Equal(SKColorType.Bgra8888, actual.ColorType);
                    Assert.Equal(SKAlphaType.Premul, actual.AlphaType);
                    Assert.Equal(expected, pixels);
                }
                catch (Exception ex) { error = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
            if (error != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
        }
    }
}
