using System;
using System.IO;
using System.Linq;
using ImageColorChanger.Database;
using ImageColorChanger.Managers;
using Microsoft.Data.Sqlite;
using Xunit;

namespace ImageColorChanger.CanvasTextEditor.Tests.Managers
{
    public sealed class ImportManagerDeleteOnlySyncTests
    {
        [Fact]
        public void SyncAllFoldersRemovalsOnly_DoesNotImportNewFiles()
        {
            string root = Path.Combine(Path.GetTempPath(), $"canvas-delete-only-{Guid.NewGuid():N}");
            string dbPath = Path.Combine(root, "test.db");
            string folder = Path.Combine(root, "media");
            Directory.CreateDirectory(folder);

            string existing = Path.Combine(folder, "a.jpg");
            string addedLater = Path.Combine(folder, "b.jpg");
            File.WriteAllText(existing, "a");

            try
            {
                using var db = new DatabaseManager(dbPath);
                var manager = new ImportManager(db, new SortManager());
                var importResult = manager.ImportFolder(folder);
                Assert.NotNull(importResult.folder);
                Assert.Single(db.GetMediaFilesByFolder(importResult.folder.Id));

                File.WriteAllText(addedLater, "b");

                var (added, removed, _) = manager.SyncAllFoldersRemovalsOnly();
                Assert.Equal(0, added);
                Assert.Equal(0, removed);

                var files = db.GetMediaFilesByFolder(importResult.folder.Id);
                Assert.Single(files);
                Assert.Equal("a.jpg", Path.GetFileName(files[0].Path));
            }
            finally
            {
                SqliteConnection.ClearAllPools();
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
        }

        [Fact]
        public void SyncAllFoldersRemovalsOnly_RemovesDeletedFiles()
        {
            string root = Path.Combine(Path.GetTempPath(), $"canvas-delete-only-{Guid.NewGuid():N}");
            string dbPath = Path.Combine(root, "test.db");
            string folder = Path.Combine(root, "media");
            Directory.CreateDirectory(folder);

            string existing = Path.Combine(folder, "a.jpg");
            File.WriteAllText(existing, "a");

            try
            {
                using var db = new DatabaseManager(dbPath);
                var manager = new ImportManager(db, new SortManager());
                var importResult = manager.ImportFolder(folder);
                Assert.NotNull(importResult.folder);
                Assert.Single(db.GetMediaFilesByFolder(importResult.folder.Id));

                File.Delete(existing);

                var (added, removed, _) = manager.SyncAllFoldersRemovalsOnly();
                Assert.Equal(0, added);
                Assert.Equal(1, removed);
                Assert.Empty(db.GetMediaFilesByFolder(importResult.folder.Id));
            }
            finally
            {
                SqliteConnection.ClearAllPools();
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
        }
    }
}

