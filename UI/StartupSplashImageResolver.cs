using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ImageColorChanger.Utils;

namespace ImageColorChanger.UI
{
    internal static class StartupSplashImageResolver
    {
        private const string SplashImageFileName = "SOM.png";
        private static readonly string[] SupportedExtensions =
        {
            ".png",
            ".jpg",
            ".jpeg",
            ".webp",
            ".bmp"
        };

        public static bool TryLoad(out ImageSource imageSource)
        {
            imageSource = null;

            foreach (string candidatePath in EnumerateCandidatePaths(Random.Shared))
            {
                if (string.IsNullOrWhiteSpace(candidatePath) || !File.Exists(candidatePath))
                {
                    continue;
                }

                try
                {
                    imageSource = LoadBitmap(candidatePath);
                    return true;
                }
                catch
                {
                    // 单张开屏图损坏时继续尝试其他图片，不能阻断应用启动。
                }
            }

            return false;
        }

        internal static IReadOnlyList<string> GetSupportedSplashImagePaths(string splashDirectory)
        {
            if (string.IsNullOrWhiteSpace(splashDirectory) || !Directory.Exists(splashDirectory))
            {
                return Array.Empty<string>();
            }

            return Directory.EnumerateFiles(splashDirectory, "*.*", SearchOption.TopDirectoryOnly)
                .Where(path => SupportedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        internal static string SelectRandomSplashImagePath(string splashDirectory, Random random)
        {
            IReadOnlyList<string> images = GetSupportedSplashImagePaths(splashDirectory);
            if (images.Count == 0)
            {
                return null;
            }

            random ??= Random.Shared;
            return images[random.Next(images.Count)];
        }

        private static string[] EnumerateCandidatePaths(Random random)
        {
            random ??= Random.Shared;
            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string splashDirectory = Path.Combine(baseDirectory, "data", "splash");
            List<string> candidates = new();
            string selectedSplashImage = SelectRandomSplashImagePath(splashDirectory, random);

            if (!string.IsNullOrWhiteSpace(selectedSplashImage))
            {
                candidates.Add(selectedSplashImage);
                foreach (string imagePath in GetSupportedSplashImagePaths(splashDirectory))
                {
                    if (!string.Equals(imagePath, selectedSplashImage, StringComparison.OrdinalIgnoreCase))
                    {
                        candidates.Add(imagePath);
                    }
                }
            }

            candidates.Add(Path.Combine(baseDirectory, SplashImageFileName));
            return candidates.ToArray();
        }

        private static ImageSource LoadBitmap(string path)
        {
            return SkiaWpfHelper.LoadBitmapSource(path);
        }
    }
}
