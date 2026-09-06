using System;
using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ImageColorChanger.Utils;

namespace ImageColorChanger.UI.Converters
{
    /// <summary>
    /// 将缩略图文件路径转换为可绑定的 ImageSource，避免数据库模型依赖 WPF 类型。
    /// </summary>
    public sealed class ThumbnailPathToImageSourceConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not string thumbnailPath || string.IsNullOrWhiteSpace(thumbnailPath) || !File.Exists(thumbnailPath))
            {
                return null;
            }

            try
            {
                return SkiaWpfHelper.LoadBitmapSource(thumbnailPath);
            }
            catch
            {
                return null;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
