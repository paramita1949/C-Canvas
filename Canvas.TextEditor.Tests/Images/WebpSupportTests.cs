using System;
using System.IO;
using System.Linq;
using ImageColorChanger.Database;
using ImageColorChanger.Database.Models;
using ImageColorChanger.Database.Repositories;
using ImageColorChanger.Managers;
using ImageColorChanger.Utils;
using Microsoft.Data.Sqlite;
using SkiaSharp;

namespace ImageColorChanger.CanvasTextEditor.Tests.Images
{
    public sealed class WebpSupportTests
    {
        [Fact]
        public void Webp_IsRegisteredAsAnImageFormat()
        {
            Assert.Contains(".webp", ImportManager.ImageExtensions, StringComparer.OrdinalIgnoreCase);
            Assert.True(ImageFileSupport.IsImageExtension(".WEBP"));
            Assert.Contains("*.webp", ImportManager.GetFileDialogFilter(), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Webp_IsIncludedInTheBatchSlideImportFilter()
        {
            var sourcePath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "UI", "MainWindow.TextEditor.Toolbar.Actions.cs");
            var source = File.ReadAllText(Path.GetFullPath(sourcePath));

            Assert.Contains("Filter = ImageFileSupport.BuildImageDialogFilter()", source, StringComparison.Ordinal);
        }

        [Fact]
        public void Webp_IsClassifiedAsAnImageByMediaRepository()
        {
            var dbPath = Path.Combine(Path.GetTempPath(), $"canvas-webp-{Guid.NewGuid():N}.db");

            try
            {
                using var context = new CanvasDbContext(dbPath);
                context.InitializeDatabase();

                var mediaFile = new MediaRepository(context).AddMediaFile(
                    Path.Combine(Path.GetTempPath(), "sample.webp"));

                Assert.Equal(FileType.Image, mediaFile.FileType);
            }
            finally
            {
                SqliteConnection.ClearAllPools();
                if (File.Exists(dbPath))
                {
                    File.Delete(dbPath);
                }
            }
        }

        [Fact]
        public void Webp_CanBeDecodedIntoAFrozenWpfBitmapSource()
        {
            var path = Path.Combine(Path.GetTempPath(), $"canvas-webp-{Guid.NewGuid():N}.webp");

            try
            {
                using (var bitmap = new SKBitmap(2, 3))
                using (var image = SKImage.FromBitmap(bitmap))
                using (var data = image.Encode(SKEncodedImageFormat.Webp, 100))
                {
                    File.WriteAllBytes(path, data.ToArray());
                }

                var source = SkiaWpfHelper.LoadBitmapSource(path);

                Assert.NotNull(source);
                Assert.Equal(2, source.PixelWidth);
                Assert.Equal(3, source.PixelHeight);
                Assert.True(source.IsFrozen);
            }
            finally
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
    }
}
