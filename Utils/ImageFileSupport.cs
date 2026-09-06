using System;
using System.Collections.Generic;
using System.Linq;

namespace ImageColorChanger.Utils
{
    /// <summary>
    /// 图片格式支持的单一来源。
    /// </summary>
    public static class ImageFileSupport
    {
        public static readonly string[] ImageExtensions =
        {
            ".jpg",
            ".jpeg",
            ".png",
            ".bmp",
            ".gif",
            ".tif",
            ".webp"
        };

        public const string ImageDialogPattern = "*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tif;*.webp";

        private static readonly HashSet<string> ImageExtensionSet =
            new HashSet<string>(ImageExtensions, StringComparer.OrdinalIgnoreCase);

        public static bool IsImageExtension(string extension)
        {
            return !string.IsNullOrWhiteSpace(extension) &&
                   ImageExtensionSet.Contains(extension.StartsWith(".", StringComparison.Ordinal)
                       ? extension
                       : "." + extension);
        }

        public static string BuildImageDialogFilter()
        {
            return $"图片文件|{ImageDialogPattern}";
        }
    }
}
