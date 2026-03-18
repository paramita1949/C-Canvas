using System;
using System.IO;
using System.Linq;
using ImageColorChanger.Database;
using ImageColorChanger.Database.Models;
using ImageColorChanger.Database.Repositories;
using Microsoft.Data.Sqlite;
using Xunit;

namespace ImageColorChanger.CanvasTextEditor.Tests.Database
{
    public sealed class FolderRepositoryDeleteTests
    {
        [Fact]
        public void DeleteFolder_RemovesUnreferencedOrphanImage()
        {
            var dbPath = Path.Combine(Path.GetTempPath(), $"canvas-folder-delete-{Guid.NewGuid():N}.db");

            try
            {
                int folderId;
                int imageId;

                using (var context = new CanvasDbContext(dbPath))
                {
                    context.InitializeDatabase();
                    var setup = SeedFolderWithImage(context);
                    folderId = setup.folderId;
                    imageId = setup.imageId;

                    var repo = new FolderRepository(context);
                    repo.DeleteFolder(folderId, forceDelete: false);
                }

                using (var verify = new CanvasDbContext(dbPath))
                {
                    verify.InitializeDatabase();
                    Assert.False(verify.Folders.Any(f => f.Id == folderId));
                    Assert.False(verify.MediaFiles.Any(m => m.Id == imageId));
                }
            }
            finally
            {
                DeleteTempDb(dbPath);
            }
        }

        [Fact]
        public void DeleteFolder_KeepsImage_WhenOriginalModeTimingReferencesIt_AndForceDeleteFalse()
        {
            var dbPath = Path.Combine(Path.GetTempPath(), $"canvas-folder-delete-{Guid.NewGuid():N}.db");

            try
            {
                int folderId;
                int imageId;

                using (var context = new CanvasDbContext(dbPath))
                {
                    context.InitializeDatabase();
                    var setup = SeedFolderWithImage(context);
                    folderId = setup.folderId;
                    imageId = setup.imageId;

                    context.OriginalModeTimings.Add(new OriginalModeTiming
                    {
                        BaseImageId = imageId,
                        FromImageId = imageId,
                        ToImageId = imageId,
                        Duration = 1.0,
                        SequenceOrder = 0,
                        CreatedAt = DateTime.Now
                    });
                    context.SaveChanges();

                    var repo = new FolderRepository(context);
                    repo.DeleteFolder(folderId, forceDelete: false);
                }

                using (var verify = new CanvasDbContext(dbPath))
                {
                    verify.InitializeDatabase();
                    Assert.False(verify.Folders.Any(f => f.Id == folderId));
                    Assert.True(verify.MediaFiles.Any(m => m.Id == imageId));
                    Assert.True(verify.OriginalModeTimings.Any(t => t.BaseImageId == imageId));
                }
            }
            finally
            {
                DeleteTempDb(dbPath);
            }
        }

        [Fact]
        public void DeleteFolder_ForceDelete_RemovesImageAndNonFkImageScopedRows()
        {
            var dbPath = Path.Combine(Path.GetTempPath(), $"canvas-folder-delete-{Guid.NewGuid():N}.db");

            try
            {
                int folderId;
                int imageId;

                using (var context = new CanvasDbContext(dbPath))
                {
                    context.InitializeDatabase();
                    var setup = SeedFolderWithImage(context);
                    folderId = setup.folderId;
                    imageId = setup.imageId;

                    context.OriginalMarks.Add(new OriginalMark
                    {
                        ItemTypeString = "image",
                        ItemId = imageId,
                        MarkTypeString = "loop",
                        CreatedTime = DateTime.Now
                    });
                    context.OriginalModeTimings.Add(new OriginalModeTiming
                    {
                        BaseImageId = imageId,
                        FromImageId = imageId,
                        ToImageId = imageId,
                        Duration = 1.0,
                        SequenceOrder = 0,
                        CreatedAt = DateTime.Now
                    });
                    context.SaveChanges();

                    var repo = new FolderRepository(context);
                    repo.DeleteFolder(folderId, forceDelete: true);
                }

                using (var verify = new CanvasDbContext(dbPath))
                {
                    verify.InitializeDatabase();
                    Assert.False(verify.Folders.Any(f => f.Id == folderId));
                    Assert.False(verify.MediaFiles.Any(m => m.Id == imageId));
                    Assert.False(verify.OriginalMarks.Any(m => m.ItemTypeString == "image" && m.ItemId == imageId));
                    Assert.False(verify.OriginalModeTimings.Any(t =>
                        t.BaseImageId == imageId || t.FromImageId == imageId || t.ToImageId == imageId));
                }
            }
            finally
            {
                DeleteTempDb(dbPath);
            }
        }

        private static (int folderId, int imageId) SeedFolderWithImage(CanvasDbContext context)
        {
            var folder = new Folder
            {
                Name = "folder",
                Path = $@"D:\fake\folder-{Guid.NewGuid():N}",
                NormalizedPath = $@"d:\fake\folder-{Guid.NewGuid():N}",
                CreatedTime = DateTime.Now,
                VideoPlayMode = "random",
                ScanPolicy = "full",
                LastScanTime = DateTime.Now,
                LastScanStatus = "success"
            };
            context.Folders.Add(folder);
            context.SaveChanges();

            var image = new MediaFile
            {
                Name = "image",
                Path = $@"D:\fake\image-{Guid.NewGuid():N}.jpg",
                FolderId = folder.Id,
                FileTypeString = "image"
            };
            context.MediaFiles.Add(image);
            context.SaveChanges();

            context.FolderImages.Add(new FolderImage
            {
                FolderId = folder.Id,
                ImageId = image.Id,
                OrderIndex = 1,
                DiscoveredAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            });
            context.SaveChanges();

            return (folder.Id, image.Id);
        }

        private static void DeleteTempDb(string dbPath)
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath))
            {
                for (var i = 0; i < 5; i++)
                {
                    try
                    {
                        File.Delete(dbPath);
                        break;
                    }
                    catch (IOException) when (i < 4)
                    {
                        System.Threading.Thread.Sleep(50);
                    }
                }
            }
        }
    }
}
