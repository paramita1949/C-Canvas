using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ImageColorChanger.UI
{
    internal static class StartupSplashImageResolver
    {
        private const string SplashImageFileName = "SOM.png";

        public static bool TryLoad(out ImageSource imageSource)
        {
            imageSource = null;

            foreach (string candidatePath in EnumerateCandidatePaths())
            {
                if (string.IsNullOrWhiteSpace(candidatePath) || !File.Exists(candidatePath))
                {
                    continue;
                }

                imageSource = LoadBitmap(candidatePath);
                return true;
            }

            return false;
        }

        private static string[] EnumerateCandidatePaths()
        {
            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            return new[]
            {
                Path.Combine(baseDirectory, "data", "splash", SplashImageFileName),
                Path.Combine(baseDirectory, SplashImageFileName)
            };
        }

        private static BitmapImage LoadBitmap(string path)
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
    }
}
