using System;
using System.IO;

namespace ImageColorChanger.Core
{
    internal static class ImageFileCacheKey
    {
        // 文件身份只取磁盘元数据，不把UI文字或当前效果当成原图身份。
        public static string GetRawKey(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            try
            {
                var file = new FileInfo(path);
                if (!file.Exists) return null;
                return $"{file.FullName.ToUpperInvariant()}|{file.LastWriteTimeUtc.Ticks}|{file.Length}";
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
            catch (ArgumentException) { return null; }
            catch (NotSupportedException) { return null; }
        }

        public static string GetRenderKey(string rawKey, int width, int height, bool invert)
            => $"{rawKey}|{width}x{height}|{(invert ? "inverted" : "normal")}";
    }
}
