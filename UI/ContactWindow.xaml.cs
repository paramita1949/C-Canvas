using System.Windows;
using System.Windows.Media.Imaging;
using ImageColorChanger.Core;
using System;

namespace ImageColorChanger.UI
{
    public partial class ContactWindow : Window
    {
        public ContactWindow()
        {
            InitializeComponent();
            LoadImages();
        }
        
        /// <summary>
        /// 从PAK或文件系统加载图片资源
        /// </summary>
        private void LoadImages()
        {
            // 加载微信二维码
            var weixinImage = ResourceLoader.LoadImage("weixin.png");
            if (weixinImage != null && WeixinImage != null)
            {
                WeixinImage.Source = NormalizeTo96Dpi(weixinImage);
            }
            
            // 加载赞助二维码
            var payImage = ResourceLoader.LoadImage("pay.png");
            if (payImage != null && PayImage != null)
            {
                PayImage.Source = NormalizeTo96Dpi(payImage);
            }
        }

        private static BitmapSource NormalizeTo96Dpi(BitmapSource source)
        {
            if (source == null)
            {
                return null;
            }

            // WPF 按 DPI 解释位图尺寸。将非 96 DPI 位图标准化，可避免隐式缩放导致发糊。
            if (Math.Abs(source.DpiX - 96.0) < 0.01 && Math.Abs(source.DpiY - 96.0) < 0.01)
            {
                return source;
            }

            int bitsPerPixel = source.Format.BitsPerPixel;
            if (bitsPerPixel <= 0)
            {
                return source;
            }

            int stride = (source.PixelWidth * bitsPerPixel + 7) / 8;
            byte[] pixels = new byte[stride * source.PixelHeight];
            source.CopyPixels(pixels, stride, 0);

            var normalized = BitmapSource.Create(
                source.PixelWidth,
                source.PixelHeight,
                96.0,
                96.0,
                source.Format,
                source.Palette,
                pixels,
                stride);

            normalized.Freeze();
            return normalized;
        }
    }
}

