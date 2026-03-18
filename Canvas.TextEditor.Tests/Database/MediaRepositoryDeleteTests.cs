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
    public sealed class MediaRepositoryDeleteTests
    {
        [Fact]
        public void DeleteMediaFile_RemovesImageScopedData_AndKeepsUnrelatedRows()
        {
            var dbPath = Path.Combine(Path.GetTempPath(), $"canvas-media-delete-{Guid.NewGuid():N}.db");

            try
            {
                int targetImageId;
                int unrelatedImageId;

                using (var context = new CanvasDbContext(dbPath))
                {
                    context.InitializeDatabase();

                    var targetImage = new MediaFile
                    {
                        Name = "target",
                        Path = @"D:\fake\target.jpg",
                        FileTypeString = "image"
                    };
                    var unrelatedImage = new MediaFile
                    {
                        Name = "other",
                        Path = @"D:\fake\other.jpg",
                        FileTypeString = "image"
                    };

                    context.MediaFiles.AddRange(targetImage, unrelatedImage);
                    context.SaveChanges();

                    targetImageId = targetImage.Id;
                    unrelatedImageId = unrelatedImage.Id;

                    context.CompositeScripts.Add(new CompositeScript
                    {
                        ImageId = targetImageId,
                        TotalDuration = 60,
                        AutoCalculate = false,
                        CreatedAt = DateTime.Now,
                        UpdatedAt = DateTime.Now
                    });

                    context.OriginalMarks.Add(new OriginalMark
                    {
                        ItemTypeString = "image",
                        ItemId = targetImageId,
                        MarkTypeString = "loop",
                        CreatedTime = DateTime.Now
                    });

                    context.OriginalModeTimings.AddRange(
                        new OriginalModeTiming
                        {
                            BaseImageId = targetImageId,
                            FromImageId = targetImageId,
                            ToImageId = unrelatedImageId,
                            Duration = 1.2,
                            SequenceOrder = 0,
                            CreatedAt = DateTime.Now
                        },
                        new OriginalModeTiming
                        {
                            BaseImageId = unrelatedImageId,
                            FromImageId = unrelatedImageId,
                            ToImageId = unrelatedImageId,
                            Duration = 2.3,
                            SequenceOrder = 0,
                            CreatedAt = DateTime.Now
                        });

                    context.SaveChanges();

                    var repository = new MediaRepository(context);
                    repository.DeleteMediaFile(targetImageId);
                }

                using (var verifyContext = new CanvasDbContext(dbPath))
                {
                    verifyContext.InitializeDatabase();

                    Assert.Null(verifyContext.MediaFiles.FirstOrDefault(m => m.Id == targetImageId));
                    Assert.NotNull(verifyContext.MediaFiles.FirstOrDefault(m => m.Id == unrelatedImageId));

                    Assert.False(verifyContext.CompositeScripts.Any(s => s.ImageId == targetImageId));
                    Assert.False(verifyContext.OriginalMarks.Any(m => m.ItemTypeString == "image" && m.ItemId == targetImageId));
                    Assert.False(verifyContext.OriginalModeTimings.Any(t =>
                        t.BaseImageId == targetImageId || t.FromImageId == targetImageId || t.ToImageId == targetImageId));

                    Assert.True(verifyContext.OriginalModeTimings.Any(t =>
                        t.BaseImageId == unrelatedImageId &&
                        t.FromImageId == unrelatedImageId &&
                        t.ToImageId == unrelatedImageId));
                }
            }
            finally
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
}
