using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using ImageColorChanger.Database;
using ImageColorChanger.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace ImageColorChanger.Services
{
    public enum DatabaseMigrationMessageLevel
    {
        Info,
        Warning,
        Error
    }

    public enum DatabaseImportMode
    {
        Replace = 0,
        IncrementalMerge = 1
    }

    public class DatabaseMigrationResult
    {
        public bool Success { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public DatabaseMigrationMessageLevel Level { get; set; } = DatabaseMigrationMessageLevel.Info;
        public bool RequiresRestart { get; set; }

        public static DatabaseMigrationResult Ok(string title, string message, bool requiresRestart = false)
        {
            return new DatabaseMigrationResult
            {
                Success = true,
                Title = title,
                Message = message,
                Level = DatabaseMigrationMessageLevel.Info,
                RequiresRestart = requiresRestart
            };
        }

        public static DatabaseMigrationResult Fail(string title, string message, DatabaseMigrationMessageLevel level = DatabaseMigrationMessageLevel.Error)
        {
            return new DatabaseMigrationResult
            {
                Success = false,
                Title = title,
                Message = message,
                Level = level,
                RequiresRestart = false
            };
        }
    }

    /// <summary>
    /// 数据库迁移服务 - 提供数据库、缩略图和配置文件的导入导出功能
    /// </summary>
    public class DatabaseMigrationService
    {
        private const string LogPrefix = "[数据库导入导出]";
        private readonly string _defaultDbPath;
        private readonly string _pendingImportDbPath;
        private readonly string _thumbnailsDir;
        private readonly string _configFilePath;
        private readonly string _watermarksDir;

        private static void LogInfo(string message)
        {
            // System.Diagnostics.Debug.WriteLine($"{LogPrefix} {message}");
        }

        private static void LogWarn(string message)
        {
            // System.Diagnostics.Debug.WriteLine($"{LogPrefix} [WARN] {message}");
        }

        private static void LogError(string message)
        {
            // System.Diagnostics.Debug.WriteLine($"{LogPrefix} [ERROR] {message}");
        }

        /// <summary>
        /// 构造函数
        /// </summary>
        public DatabaseMigrationService()
        {
            // 默认数据库路径：主程序目录/pyimages.db
            _defaultDbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "pyimages.db");
            _pendingImportDbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "pyimages.db.import_pending");
            // 缩略图文件夹路径：主程序目录/Thumbnails
            _thumbnailsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Thumbnails");
            // 配置文件路径：主程序目录/config.json
            _configFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
            // 歌词水印目录：主程序目录/data/watermarks
            _watermarksDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "watermarks");
        }

        /// <summary>
        /// 导出数据库到指定位置（压缩包格式，包含数据库、缩略图和配置文件）
        /// </summary>
        /// <param name="targetPath">目标压缩包文件路径</param>
        /// <returns>导出结果</returns>
        public async Task<DatabaseMigrationResult> ExportDatabaseAsync(string targetPath)
        {
            LogInfo($"[Export-Begin] db={_defaultDbPath}, target={targetPath}");
            try
            {
                // 检查源数据库是否存在
                if (!File.Exists(_defaultDbPath))
                {
                    LogWarn("[Export-Skip] source db missing");
                    return DatabaseMigrationResult.Fail("导出失败", "数据库文件不存在，无法导出。", DatabaseMigrationMessageLevel.Warning);
                }

                var exportStats = ReadDatabaseStats(_defaultDbPath);
                LogInfo($"[Export] source stats: folders={exportStats.Folders}, files={exportStats.Files}, lyricsGroups={exportStats.LyricsGroups}, lyricsProjects={exportStats.LyricsProjects}");

                // 确保目标目录存在
                var targetDir = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                }

                // 执行 checkpoint 操作，将 WAL 文件合并到主数据库
                await Task.Run(() =>
                {
                    using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_defaultDbPath}"))
                    {
                        connection.Open();
                        using (var command = connection.CreateCommand())
                        {
                            command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                            command.ExecuteNonQuery();
                        }
                    }
                });

                LogInfo("[Export] WAL checkpoint completed");

                // 创建临时目录用于打包
                var tempDir = Path.Combine(Path.GetTempPath(), $"db_export_{Guid.NewGuid()}");
                Directory.CreateDirectory(tempDir);

                try
                {
                    // 复制数据库文件到临时目录
                    var tempDbPath = Path.Combine(tempDir, "pyimages.db");
                    await Task.Run(() => File.Copy(_defaultDbPath, tempDbPath, overwrite: true));
                    LogInfo("[Export] copied db to temp");

                    // 复制 Thumbnails 文件夹到临时目录（如果存在）
                    if (Directory.Exists(_thumbnailsDir))
                    {
                        var tempThumbnailsDir = Path.Combine(tempDir, "Thumbnails");
                        await Task.Run(() => CopyDirectory(_thumbnailsDir, tempThumbnailsDir));
                        LogInfo($"[Export] copied thumbnails, files={Directory.GetFiles(tempThumbnailsDir).Length}");
                    }

                    // 复制配置文件到临时目录（如果存在）
                    if (File.Exists(_configFilePath))
                    {
                        var tempConfigPath = Path.Combine(tempDir, "config.json");
                        await Task.Run(() => File.Copy(_configFilePath, tempConfigPath, overwrite: true));
                        LogInfo("[Export] copied config");
                    }

                    // 复制歌词水印目录到临时目录（如果存在）
                    if (Directory.Exists(_watermarksDir))
                    {
                        var tempWatermarksDir = Path.Combine(tempDir, "data", "watermarks");
                        await Task.Run(() => CopyDirectory(_watermarksDir, tempWatermarksDir));
                        LogInfo($"[Export] copied watermarks, files={Directory.GetFiles(tempWatermarksDir, "*", SearchOption.AllDirectories).Length}");
                    }

                    // 删除目标文件（如果存在）
                    if (File.Exists(targetPath))
                    {
                        File.Delete(targetPath);
                    }

                    // 创建压缩包
                    await Task.Run(() => ZipFile.CreateFromDirectory(tempDir, targetPath, CompressionLevel.Optimal, false));
                    LogInfo($"[Export-End] zip created: {targetPath}");

                    return DatabaseMigrationResult.Ok("导出成功", $"数据库导出成功！\n\n导出位置：{targetPath}");
                }
                finally
                {
                    // 清理临时目录
                    if (Directory.Exists(tempDir))
                    {
                        Directory.Delete(tempDir, recursive: true);
                        LogInfo("[Export] temp dir cleaned");
                    }
                }
            }
            catch (Exception ex)
            {
                LogError($"[Export-Fail] {ex}");
                return DatabaseMigrationResult.Fail("导出失败", $"数据库导出失败：{ex.Message}");
            }
        }

        /// <summary>
        /// 从指定位置导入数据库（支持压缩包格式）
        /// </summary>
        /// <param name="sourcePath">源文件路径（.zip 压缩包或 .db 文件）</param>
        /// <returns>导入结果</returns>
        public async Task<DatabaseMigrationResult> ImportDatabaseAsync(string sourcePath, DatabaseImportMode importMode = DatabaseImportMode.Replace)
        {
            if (importMode == DatabaseImportMode.IncrementalMerge)
            {
                return await ImportDatabaseIncrementalAsync(sourcePath);
            }

            LogInfo($"[Import-Begin] source={sourcePath}, db={_defaultDbPath}");
            string backupPath = string.Empty;
            bool stagedForRestart = false;
            var importedCompanionArtifacts = new List<string>();
            try
            {
                // 检查源文件是否存在
                if (!File.Exists(sourcePath))
                {
                    LogWarn($"[Import-Skip] source missing: {sourcePath}");
                    return DatabaseMigrationResult.Fail("导入失败", "选择的数据库文件不存在。", DatabaseMigrationMessageLevel.Warning);
                }

                // 确保 backdb 文件夹存在
                var backdbDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "backdb");
                if (!Directory.Exists(backdbDir))
                {
                    Directory.CreateDirectory(backdbDir);
                    LogInfo($"[Import] created backup dir: {backdbDir}");
                }

                // 备份当前数据库到 backdb 文件夹
                if (File.Exists(_defaultDbPath))
                {
                    var backupFileName = $"pyimages.db.backup_{DateTime.Now:yyyyMMdd_HHmmss}";
                    backupPath = Path.Combine(backdbDir, backupFileName);
                    await Task.Run(() => File.Copy(_defaultDbPath, backupPath, overwrite: true));
                    LogInfo($"[Import] backup db: {backupPath}");
                }

                // 备份当前缩略图文件夹到 backdb 文件夹
                if (Directory.Exists(_thumbnailsDir))
                {
                    var backupThumbnailsDir = Path.Combine(backdbDir, $"Thumbnails.backup_{DateTime.Now:yyyyMMdd_HHmmss}");
                    await Task.Run(() => CopyDirectory(_thumbnailsDir, backupThumbnailsDir));
                    LogInfo($"[Import] backup thumbnails: {backupThumbnailsDir}");
                }

                // 备份当前配置文件到 backdb 文件夹
                if (File.Exists(_configFilePath))
                {
                    var backupConfigFileName = $"config.json.backup_{DateTime.Now:yyyyMMdd_HHmmss}";
                    var backupConfigPath = Path.Combine(backdbDir, backupConfigFileName);
                    await Task.Run(() => File.Copy(_configFilePath, backupConfigPath, overwrite: true));
                    LogInfo($"[Import] backup config: {backupConfigPath}");
                }

                // 备份当前歌词水印目录到 backdb 文件夹
                if (Directory.Exists(_watermarksDir))
                {
                    var backupWatermarksDir = Path.Combine(backdbDir, $"watermarks.backup_{DateTime.Now:yyyyMMdd_HHmmss}");
                    await Task.Run(() => CopyDirectory(_watermarksDir, backupWatermarksDir));
                    LogInfo($"[Import] backup watermarks: {backupWatermarksDir}");
                }

                // 关闭所有数据库连接
                CloseAllDatabaseConnections();

                // 判断文件类型并导入
                var fileExtension = Path.GetExtension(sourcePath).ToLower();
                LogInfo($"[Import] source extension={fileExtension}");

                if (fileExtension == ".zip")
                {
                    // 导入压缩包格式
                    stagedForRestart = await ImportFromZipAsync(sourcePath);
                }
                else if (fileExtension == ".db")
                {
                    // 导入单个数据库文件（兼容旧格式）
                    if (!IsValidSqliteDatabase(sourcePath))
                    {
                        LogWarn("[Import-Skip] invalid sqlite db file");
                        return DatabaseMigrationResult.Fail("导入失败", "选择的文件不是有效的SQLite数据库文件。", DatabaseMigrationMessageLevel.Warning);
                    }
                    stagedForRestart = await Task.Run(() => ReplaceDatabaseFileOrStagePending(sourcePath));
                    LogInfo(stagedForRestart
                        ? $"[Import] db staged for restart: {_pendingImportDbPath}"
                        : "[Import] db copied in single-file mode");

                    // 兼容旧流程：当用户直接导入 .db 时，尝试从同目录加载配置/资源侧载文件，避免自定义设置丢失。
                    importedCompanionArtifacts = await TryImportCompanionArtifactsForDbAsync(sourcePath);
                }
                else
                {
                    return DatabaseMigrationResult.Fail("导入失败", "不支持的文件格式，请选择 .zip 或 .db 文件。", DatabaseMigrationMessageLevel.Warning);
                }

                if (!stagedForRestart && !IsDatabaseIntegrityOk(_defaultDbPath, out string integrityMessage))
                {
                    LogError($"[Import-IntegrityFail] {integrityMessage}");
                    if (!string.IsNullOrWhiteSpace(backupPath) && File.Exists(backupPath))
                    {
                        try
                        {
                            ReplaceDatabaseFile(backupPath);
                            LogWarn($"[Import-Rollback] restored backup db: {backupPath}");
                            return DatabaseMigrationResult.Fail("导入失败", $"导入后的数据库校验失败：{integrityMessage}\n\n已自动回滚到导入前备份。");
                        }
                        catch (Exception rollbackEx)
                        {
                            LogError($"[Import-RollbackFail] {rollbackEx}");
                            return DatabaseMigrationResult.Fail("导入失败", $"导入后的数据库校验失败：{integrityMessage}\n\n自动回滚也失败：{rollbackEx.Message}");
                        }
                    }

                    return DatabaseMigrationResult.Fail("导入失败", $"导入后的数据库校验失败：{integrityMessage}");
                }

                if (stagedForRestart)
                {
                    var sourceStats = ReadDatabaseStats(_pendingImportDbPath);
                    LogWarn($"[Import-Staged] pending={_pendingImportDbPath}, folders={sourceStats.Folders}, files={sourceStats.Files}, lyricsGroups={sourceStats.LyricsGroups}, lyricsProjects={sourceStats.LyricsProjects}");
                    string companionMessage = BuildCompanionImportMessage(importedCompanionArtifacts);
                    return DatabaseMigrationResult.Ok(
                        "导入成功（待重启应用）",
                        $"数据库文件当前被占用，已暂存导入。\n\n重启应用后将自动应用该数据库。\n\n待导入统计：\n文件夹: {sourceStats.Folders}\n文件: {sourceStats.Files}\n歌词库: {sourceStats.LyricsGroups}\n歌词: {sourceStats.LyricsProjects}{companionMessage}",
                        requiresRestart: true);
                }

                var stats = ReadDatabaseStats(_defaultDbPath);
                LogInfo($"[Import-End] success=true, folders={stats.Folders}, files={stats.Files}, lyricsGroups={stats.LyricsGroups}, lyricsProjects={stats.LyricsProjects}");
                string importedCompanionSummary = BuildCompanionImportMessage(importedCompanionArtifacts);
                return DatabaseMigrationResult.Ok(
                    "导入成功",
                    $"数据库导入成功！\n\n文件夹: {stats.Folders}\n文件: {stats.Files}\n歌词库: {stats.LyricsGroups}\n歌词: {stats.LyricsProjects}{importedCompanionSummary}\n\n为使更改生效，需要重启应用程序。",
                    requiresRestart: true);
            }
            catch (Exception ex)
            {
                LogError($"[Import-Fail] {ex}");
                return DatabaseMigrationResult.Fail("导入失败", $"数据库导入失败：{ex.Message}");
            }
        }

        /// <summary>
        /// 从压缩包导入数据库、缩略图和配置文件
        /// </summary>
        private async Task<bool> ImportFromZipAsync(string zipPath)
        {
            var tempDir = Path.Combine(Path.GetTempPath(), $"db_import_{Guid.NewGuid()}");
            bool stagedForRestart = false;
            LogInfo($"[ImportZip-Begin] source={zipPath}, tempDir={tempDir}");

            try
            {
                // 解压缩到临时目录
                await Task.Run(() => ZipFile.ExtractToDirectory(zipPath, tempDir));
                LogInfo($"[ImportZip] extracted to {tempDir}");

                // 导入数据库文件（优先 pyimages.db，其次自动扫描压缩包内可用 .db）
                var tempDbPath = FindDatabaseFileFromExtractedPackage(tempDir);
                if (string.IsNullOrWhiteSpace(tempDbPath))
                {
                    throw new Exception("压缩包中未找到可用的数据库文件（.db）");
                }

                var sourceStats = ReadDatabaseStats(tempDbPath);
                LogInfo($"[ImportZip] source stats: folders={sourceStats.Folders}, files={sourceStats.Files}, lyricsGroups={sourceStats.LyricsGroups}, lyricsProjects={sourceStats.LyricsProjects}");

                stagedForRestart = await Task.Run(() => ReplaceDatabaseFileOrStagePending(tempDbPath));
                LogInfo(stagedForRestart
                    ? $"[ImportZip] db staged for restart from {tempDbPath}"
                    : $"[ImportZip] db copied from {tempDbPath}");

                // 导入缩略图文件夹（如果存在）
                var tempThumbnailsDir = Path.Combine(tempDir, "Thumbnails");
                if (Directory.Exists(tempThumbnailsDir))
                {
                    // 删除现有缩略图文件夹
                    if (Directory.Exists(_thumbnailsDir))
                    {
                        Directory.Delete(_thumbnailsDir, recursive: true);
                    }

                    // 复制新的缩略图文件夹
                    await Task.Run(() => CopyDirectory(tempThumbnailsDir, _thumbnailsDir));
                    LogInfo($"[ImportZip] thumbnails imported, files={Directory.GetFiles(_thumbnailsDir).Length}");
                }
                else
                {
                    LogWarn("[ImportZip] thumbnails missing in package");
                }

                // 导入配置文件（如果存在）
                var tempConfigPath = Path.Combine(tempDir, "config.json");
                if (File.Exists(tempConfigPath))
                {
                    await Task.Run(() => File.Copy(tempConfigPath, _configFilePath, overwrite: true));
                    LogInfo("[ImportZip] config imported");
                }
                else
                {
                    LogWarn("[ImportZip] config.json missing in package");
                }

                // 导入歌词水印目录（如果存在）
                var tempWatermarksDir = Path.Combine(tempDir, "data", "watermarks");
                if (Directory.Exists(tempWatermarksDir))
                {
                    if (Directory.Exists(_watermarksDir))
                    {
                        Directory.Delete(_watermarksDir, recursive: true);
                    }

                    await Task.Run(() => CopyDirectory(tempWatermarksDir, _watermarksDir));
                    LogInfo($"[ImportZip] watermarks imported, files={Directory.GetFiles(_watermarksDir, "*", SearchOption.AllDirectories).Length}");
                }
                else
                {
                    LogWarn("[ImportZip] data/watermarks missing in package");
                }
            }
            finally
            {
                try
                {
                    // 清理前先释放所有 SQLite 连接池，避免临时数据库文件被句柄占用
                    Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

                    // 清理临时目录（清理失败不应影响导入结果）
                    if (Directory.Exists(tempDir))
                    {
                        Directory.Delete(tempDir, recursive: true);
                        LogInfo("[ImportZip] temp dir cleaned");
                    }
                }
                catch (Exception ex)
                {
                    LogWarn($"[ImportZip] temp cleanup failed: {ex.Message}");
                }
            }

            return stagedForRestart;
        }

        private sealed class IncrementalMergeStats
        {
            public int AddedFolders { get; set; }
            public int AddedMediaFiles { get; set; }
            public int AddedFolderLinks { get; set; }
            public int AddedImageDisplayLocations { get; set; }
            public int AddedManualSortFolders { get; set; }
            public int AddedTextProjects { get; set; }
            public int AddedSlides { get; set; }
            public int AddedTextElements { get; set; }
            public int AddedRichTextSpans { get; set; }
            public int AddedKeyframes { get; set; }
            public int AddedKeyframeTimings { get; set; }
            public int AddedOriginalMarks { get; set; }
            public int AddedOriginalModeTimings { get; set; }
            public int AddedCompositeScripts { get; set; }
            public int AddedLyricsGroups { get; set; }
            public int AddedLyricsProjects { get; set; }
            public int AddedBibleHistoryRecords { get; set; }
            public int AddedSettings { get; set; }
            public int AddedUiSettings { get; set; }
        }

        private async Task<DatabaseMigrationResult> ImportDatabaseIncrementalAsync(string sourcePath)
        {
            LogInfo($"[ImportMerge-Begin] source={sourcePath}, db={_defaultDbPath}");
            string tempDir = string.Empty;
            try
            {
                if (!File.Exists(sourcePath))
                {
                    return DatabaseMigrationResult.Fail("增量导入失败", "选择的数据库文件不存在。", DatabaseMigrationMessageLevel.Warning);
                }

                string extension = Path.GetExtension(sourcePath).ToLowerInvariant();
                string sourceDbPath = sourcePath;

                if (extension == ".zip")
                {
                    tempDir = Path.Combine(Path.GetTempPath(), $"db_merge_{Guid.NewGuid()}");
                    Directory.CreateDirectory(tempDir);
                    await Task.Run(() => ZipFile.ExtractToDirectory(sourcePath, tempDir));
                    sourceDbPath = FindDatabaseFileFromExtractedPackage(tempDir);
                    if (string.IsNullOrWhiteSpace(sourceDbPath))
                    {
                        return DatabaseMigrationResult.Fail("增量导入失败", "压缩包中未找到可用的数据库文件（.db）。");
                    }
                }
                else if (extension != ".db")
                {
                    return DatabaseMigrationResult.Fail("增量导入失败", "不支持的文件格式，请选择 .zip 或 .db 文件。", DatabaseMigrationMessageLevel.Warning);
                }

                if (!IsValidSqliteDatabase(sourceDbPath))
                {
                    return DatabaseMigrationResult.Fail("增量导入失败", "选择的文件不是有效的SQLite数据库文件。", DatabaseMigrationMessageLevel.Warning);
                }

                string sourceFullPath = Path.GetFullPath(sourceDbPath);
                string targetFullPath = Path.GetFullPath(_defaultDbPath);
                if (string.Equals(sourceFullPath, targetFullPath, StringComparison.OrdinalIgnoreCase))
                {
                    return DatabaseMigrationResult.Fail("增量导入失败", "源数据库与当前数据库相同，无需导入。", DatabaseMigrationMessageLevel.Warning);
                }

                CloseAllDatabaseConnections();

                IncrementalMergeStats stats = await MergeDatabaseIncrementallyAsync(sourceDbPath);
                List<string> companionImported = await TryImportCompanionArtifactsForDbAsync(sourceDbPath);
                string companionMessage = BuildCompanionImportMessage(companionImported);

                string message =
                    "增量导入完成（保留本机数据）。\n\n" +
                    $"新增文件夹: {stats.AddedFolders}\n" +
                    $"新增媒体: {stats.AddedMediaFiles}\n" +
                    $"新增目录素材关联: {stats.AddedFolderLinks}\n" +
                    $"新增显示位置: {stats.AddedImageDisplayLocations}\n" +
                    $"新增手动排序目录: {stats.AddedManualSortFolders}\n" +
                    $"新增幻灯片项目: {stats.AddedTextProjects}\n" +
                    $"新增幻灯片: {stats.AddedSlides}\n" +
                    $"新增文本元素: {stats.AddedTextElements}\n" +
                    $"新增富文本片段: {stats.AddedRichTextSpans}\n" +
                    $"新增关键帧: {stats.AddedKeyframes}\n" +
                    $"新增关键帧时序: {stats.AddedKeyframeTimings}\n" +
                    $"新增原图标记: {stats.AddedOriginalMarks}\n" +
                    $"新增原图模式时序: {stats.AddedOriginalModeTimings}\n" +
                    $"新增合成脚本: {stats.AddedCompositeScripts}\n" +
                    $"新增歌词分组: {stats.AddedLyricsGroups}\n" +
                    $"新增歌词: {stats.AddedLyricsProjects}\n" +
                    $"新增经文历史槽位: {stats.AddedBibleHistoryRecords}\n" +
                    $"新增通用设置键: {stats.AddedSettings}\n" +
                    $"新增UI设置键: {stats.AddedUiSettings}" +
                    companionMessage +
                    "\n\n建议重启应用以刷新缓存和界面状态。";

                return DatabaseMigrationResult.Ok("增量导入成功", message, requiresRestart: true);
            }
            catch (Exception ex)
            {
                LogError($"[ImportMerge-Fail] {ex}");
                return DatabaseMigrationResult.Fail("增量导入失败", $"增量导入失败：{ex.Message}");
            }
            finally
            {
                if (!string.IsNullOrWhiteSpace(tempDir) && Directory.Exists(tempDir))
                {
                    try
                    {
                        Directory.Delete(tempDir, recursive: true);
                    }
                    catch (Exception ex)
                    {
                        LogWarn($"[ImportMerge] temp cleanup failed: {ex.Message}");
                    }
                }
            }
        }

        private async Task<IncrementalMergeStats> MergeDatabaseIncrementallyAsync(string sourceDbPath)
        {
            var stats = new IncrementalMergeStats();
            var pathComparer = StringComparer.OrdinalIgnoreCase;
            var sourceToTargetFolderMap = new Dictionary<int, int>();
            var sourceToTargetMediaMap = new Dictionary<int, int>();
            var sourceToTargetProjectMap = new Dictionary<int, int>();
            var sourceToTargetSlideMap = new Dictionary<int, int>();
            var sourceToTargetElementMap = new Dictionary<int, int>();
            var sourceToTargetLyricsGroupMap = new Dictionary<int, int>();
            var sourceToTargetKeyframeMap = new Dictionary<int, int>();

            await using var target = new CanvasDbContext(_defaultDbPath);
            await using var source = new CanvasDbContext(sourceDbPath);

            await target.Database.OpenConnectionAsync();
            await source.Database.OpenConnectionAsync();

            // folders + media_files
            var targetFolders = await target.Folders.ToListAsync();
            var targetFoldersByPath = targetFolders
                .Where(f => !string.IsNullOrWhiteSpace(f.Path))
                .GroupBy(f => f.Path, pathComparer)
                .ToDictionary(g => g.Key, g => g.First(), pathComparer);

            var sourceFolders = await source.Folders.AsNoTracking().ToListAsync();
            foreach (var sourceFolder in sourceFolders)
            {
                Folder targetFolder = null;
                if (!string.IsNullOrWhiteSpace(sourceFolder.Path) &&
                    targetFoldersByPath.TryGetValue(sourceFolder.Path, out var existing))
                {
                    targetFolder = existing;
                }

                if (targetFolder == null)
                {
                    targetFolder = new Folder
                    {
                        Name = sourceFolder.Name,
                        Path = sourceFolder.Path,
                        OrderIndex = sourceFolder.OrderIndex,
                        CreatedTime = sourceFolder.CreatedTime,
                        VideoPlayMode = sourceFolder.VideoPlayMode,
                        AutoColorEffect = sourceFolder.AutoColorEffect,
                        HighlightColor = sourceFolder.HighlightColor,
                        NormalizedPath = sourceFolder.NormalizedPath,
                        ScanPolicy = sourceFolder.ScanPolicy,
                        LastScanTime = sourceFolder.LastScanTime,
                        LastScanStatus = sourceFolder.LastScanStatus,
                        LastScanError = sourceFolder.LastScanError
                    };
                    target.Folders.Add(targetFolder);
                    await target.SaveChangesAsync();
                    targetFoldersByPath[sourceFolder.Path] = targetFolder;
                    stats.AddedFolders++;
                }

                sourceToTargetFolderMap[sourceFolder.Id] = targetFolder.Id;
            }

            var targetMedia = await target.MediaFiles.ToListAsync();
            var targetMediaByPath = targetMedia
                .Where(m => !string.IsNullOrWhiteSpace(m.Path))
                .GroupBy(m => m.Path, pathComparer)
                .ToDictionary(g => g.Key, g => g.First(), pathComparer);

            var sourceMediaFiles = await source.MediaFiles.AsNoTracking().ToListAsync();
            foreach (var sourceMedia in sourceMediaFiles)
            {
                MediaFile targetMediaFile = null;
                if (!string.IsNullOrWhiteSpace(sourceMedia.Path) &&
                    targetMediaByPath.TryGetValue(sourceMedia.Path, out var existingMedia))
                {
                    targetMediaFile = existingMedia;
                }

                if (targetMediaFile == null)
                {
                    targetMediaFile = new MediaFile
                    {
                        Name = sourceMedia.Name,
                        Path = sourceMedia.Path,
                        FolderId = sourceMedia.FolderId.HasValue && sourceToTargetFolderMap.TryGetValue(sourceMedia.FolderId.Value, out int mappedFolderId) ? mappedFolderId : null,
                        LastModified = sourceMedia.LastModified,
                        OrderIndex = sourceMedia.OrderIndex,
                        FileTypeString = sourceMedia.FileTypeString,
                        CompositePlaybackEnabled = sourceMedia.CompositePlaybackEnabled
                    };
                    target.MediaFiles.Add(targetMediaFile);
                    await target.SaveChangesAsync();
                    targetMediaByPath[sourceMedia.Path] = targetMediaFile;
                    stats.AddedMediaFiles++;
                }

                sourceToTargetMediaMap[sourceMedia.Id] = targetMediaFile.Id;
            }

            // folder_images
            var targetFolderLinks = await target.FolderImages.AsNoTracking()
                .Select(fi => new { fi.FolderId, fi.ImageId })
                .ToListAsync();
            var linkKeySet = new HashSet<string>(targetFolderLinks.Select(v => $"{v.FolderId}:{v.ImageId}"), StringComparer.Ordinal);
            var sourceFolderLinks = await source.FolderImages.AsNoTracking().ToListAsync();

            foreach (var sourceLink in sourceFolderLinks)
            {
                if (!sourceToTargetFolderMap.TryGetValue(sourceLink.FolderId, out int mappedFolderId) ||
                    !sourceToTargetMediaMap.TryGetValue(sourceLink.ImageId, out int mappedMediaId))
                {
                    continue;
                }

                string key = $"{mappedFolderId}:{mappedMediaId}";
                if (linkKeySet.Contains(key))
                {
                    continue;
                }

                target.FolderImages.Add(new FolderImage
                {
                    FolderId = mappedFolderId,
                    ImageId = mappedMediaId,
                    OrderIndex = sourceLink.OrderIndex
                });
                await target.SaveChangesAsync();
                linkKeySet.Add(key);
                stats.AddedFolderLinks++;
            }

            // image_display_locations
            var targetDisplayLocationKeys = new HashSet<string>(
                (await target.ImageDisplayLocations.AsNoTracking()
                    .Select(l => new { l.ImageId, l.LocationTypeString, l.FolderId })
                    .ToListAsync())
                .Select(v => $"{v.ImageId}:{(v.LocationTypeString ?? string.Empty).Trim().ToLowerInvariant()}:{(v.FolderId.HasValue ? v.FolderId.Value.ToString() : "null")}"),
                StringComparer.Ordinal);
            var sourceDisplayLocations = await source.ImageDisplayLocations.AsNoTracking().ToListAsync();
            foreach (var sourceLocation in sourceDisplayLocations)
            {
                if (!sourceToTargetMediaMap.TryGetValue(sourceLocation.ImageId, out int mappedImageId))
                {
                    continue;
                }

                int? mappedFolderId = null;
                if (sourceLocation.FolderId.HasValue)
                {
                    if (!sourceToTargetFolderMap.TryGetValue(sourceLocation.FolderId.Value, out int mappedFolder))
                    {
                        continue;
                    }
                    mappedFolderId = mappedFolder;
                }

                string normalizedLocationType = (sourceLocation.LocationTypeString ?? string.Empty).Trim().ToLowerInvariant();
                string displayLocationKey = $"{mappedImageId}:{normalizedLocationType}:{(mappedFolderId.HasValue ? mappedFolderId.Value.ToString() : "null")}";
                if (targetDisplayLocationKeys.Contains(displayLocationKey))
                {
                    continue;
                }

                target.ImageDisplayLocations.Add(new ImageDisplayLocation
                {
                    ImageId = mappedImageId,
                    LocationTypeString = sourceLocation.LocationTypeString,
                    FolderId = mappedFolderId,
                    OrderIndex = sourceLocation.OrderIndex,
                    CreatedTime = sourceLocation.CreatedTime
                });
                await target.SaveChangesAsync();
                targetDisplayLocationKeys.Add(displayLocationKey);
                stats.AddedImageDisplayLocations++;
            }

            // manual_sort_folders
            var targetManualSortFolderIds = new HashSet<int>(
                await target.ManualSortFolders.AsNoTracking().Select(m => m.FolderId).ToListAsync());
            var sourceManualSortFolders = await source.ManualSortFolders.AsNoTracking().ToListAsync();
            foreach (var sourceManualSort in sourceManualSortFolders)
            {
                if (!sourceToTargetFolderMap.TryGetValue(sourceManualSort.FolderId, out int mappedFolderId) ||
                    targetManualSortFolderIds.Contains(mappedFolderId))
                {
                    continue;
                }

                target.ManualSortFolders.Add(new ManualSortFolder
                {
                    FolderId = mappedFolderId,
                    IsManualSort = sourceManualSort.IsManualSort,
                    LastManualSortTime = sourceManualSort.LastManualSortTime
                });
                await target.SaveChangesAsync();
                targetManualSortFolderIds.Add(mappedFolderId);
                stats.AddedManualSortFolders++;
            }

            // original_marks
            var targetOriginalMarkKeys = new HashSet<string>(
                (await target.OriginalMarks.AsNoTracking()
                    .Select(m => new { m.ItemTypeString, m.ItemId })
                    .ToListAsync())
                .Select(v => $"{(v.ItemTypeString ?? string.Empty).Trim().ToLowerInvariant()}:{v.ItemId}"),
                StringComparer.Ordinal);
            var sourceOriginalMarks = await source.OriginalMarks.AsNoTracking().ToListAsync();
            foreach (var sourceMark in sourceOriginalMarks)
            {
                string itemType = (sourceMark.ItemTypeString ?? string.Empty).Trim().ToLowerInvariant();
                int mappedItemId;
                if (itemType == "folder")
                {
                    if (!sourceToTargetFolderMap.TryGetValue(sourceMark.ItemId, out mappedItemId))
                    {
                        continue;
                    }
                }
                else
                {
                    if (!sourceToTargetMediaMap.TryGetValue(sourceMark.ItemId, out mappedItemId))
                    {
                        continue;
                    }
                }

                string key = $"{itemType}:{mappedItemId}";
                if (targetOriginalMarkKeys.Contains(key))
                {
                    continue;
                }

                target.OriginalMarks.Add(new OriginalMark
                {
                    ItemTypeString = sourceMark.ItemTypeString,
                    ItemId = mappedItemId,
                    MarkTypeString = sourceMark.MarkTypeString,
                    CreatedTime = sourceMark.CreatedTime
                });
                await target.SaveChangesAsync();
                targetOriginalMarkKeys.Add(key);
                stats.AddedOriginalMarks++;
            }

            // keyframes
            var targetKeyframeKeys = new HashSet<string>(
                (await target.Keyframes.AsNoTracking()
                    .Select(k => new { k.ImageId, k.Position, k.YPosition, k.OrderIndex })
                    .ToListAsync())
                .Select(v => $"{v.ImageId}:{v.Position:R}:{v.YPosition}:{(v.OrderIndex.HasValue ? v.OrderIndex.Value.ToString() : "null")}"),
                StringComparer.Ordinal);
            var sourceKeyframes = await source.Keyframes.AsNoTracking().ToListAsync();
            foreach (var sourceKeyframe in sourceKeyframes)
            {
                if (!sourceToTargetMediaMap.TryGetValue(sourceKeyframe.ImageId, out int mappedImageId))
                {
                    continue;
                }

                string key = $"{mappedImageId}:{sourceKeyframe.Position:R}:{sourceKeyframe.YPosition}:{(sourceKeyframe.OrderIndex.HasValue ? sourceKeyframe.OrderIndex.Value.ToString() : "null")}";
                Keyframe targetKeyframe = null;
                if (targetKeyframeKeys.Contains(key))
                {
                    targetKeyframe = await target.Keyframes.FirstOrDefaultAsync(k =>
                        k.ImageId == mappedImageId &&
                        k.Position == sourceKeyframe.Position &&
                        k.YPosition == sourceKeyframe.YPosition &&
                        k.OrderIndex == sourceKeyframe.OrderIndex);
                }

                if (targetKeyframe == null)
                {
                    targetKeyframe = new Keyframe
                    {
                        ImageId = mappedImageId,
                        Position = sourceKeyframe.Position,
                        YPosition = sourceKeyframe.YPosition,
                        OrderIndex = sourceKeyframe.OrderIndex,
                        LoopCount = sourceKeyframe.LoopCount,
                        AutoPause = sourceKeyframe.AutoPause
                    };

                    target.Keyframes.Add(targetKeyframe);
                    await target.SaveChangesAsync();
                    targetKeyframeKeys.Add(key);
                    stats.AddedKeyframes++;
                }

                sourceToTargetKeyframeMap[sourceKeyframe.Id] = targetKeyframe.Id;
            }

            // keyframe_timings
            var targetTimingKeys = new HashSet<string>(
                (await target.KeyframeTimings.AsNoTracking()
                    .Select(t => new { t.ImageId, t.KeyframeId, t.SequenceOrder })
                    .ToListAsync())
                .Select(v => $"{v.ImageId}:{v.KeyframeId}:{v.SequenceOrder}"),
                StringComparer.Ordinal);
            var sourceTimings = await source.KeyframeTimings.AsNoTracking().ToListAsync();
            foreach (var sourceTiming in sourceTimings)
            {
                if (!sourceToTargetMediaMap.TryGetValue(sourceTiming.ImageId, out int mappedImageId) ||
                    !sourceToTargetKeyframeMap.TryGetValue(sourceTiming.KeyframeId, out int mappedKeyframeId))
                {
                    continue;
                }

                string timingKey = $"{mappedImageId}:{mappedKeyframeId}:{sourceTiming.SequenceOrder}";
                if (targetTimingKeys.Contains(timingKey))
                {
                    continue;
                }

                target.KeyframeTimings.Add(new KeyframeTiming
                {
                    ImageId = mappedImageId,
                    KeyframeId = mappedKeyframeId,
                    Duration = sourceTiming.Duration,
                    SequenceOrder = sourceTiming.SequenceOrder,
                    CreatedAt = sourceTiming.CreatedAt
                });
                stats.AddedKeyframeTimings++;
                targetTimingKeys.Add(timingKey);
            }
            await target.SaveChangesAsync();

            // original_mode_timings
            var targetOriginalTimingKeys = new HashSet<string>(
                (await target.OriginalModeTimings.AsNoTracking()
                    .Select(t => new { t.BaseImageId, t.FromImageId, t.ToImageId, t.SequenceOrder, t.MarkTypeString })
                    .ToListAsync())
                .Select(v => $"{v.BaseImageId}:{v.FromImageId}:{v.ToImageId}:{v.SequenceOrder}:{(v.MarkTypeString ?? string.Empty).Trim().ToLowerInvariant()}"),
                StringComparer.Ordinal);
            var sourceOriginalTimings = await source.OriginalModeTimings.AsNoTracking().ToListAsync();
            foreach (var sourceTiming in sourceOriginalTimings)
            {
                if (!sourceToTargetMediaMap.TryGetValue(sourceTiming.BaseImageId, out int mappedBaseId) ||
                    !sourceToTargetMediaMap.TryGetValue(sourceTiming.FromImageId, out int mappedFromId) ||
                    !sourceToTargetMediaMap.TryGetValue(sourceTiming.ToImageId, out int mappedToId))
                {
                    continue;
                }

                string key = $"{mappedBaseId}:{mappedFromId}:{mappedToId}:{sourceTiming.SequenceOrder}:{(sourceTiming.MarkTypeString ?? string.Empty).Trim().ToLowerInvariant()}";
                if (targetOriginalTimingKeys.Contains(key))
                {
                    continue;
                }

                target.OriginalModeTimings.Add(new OriginalModeTiming
                {
                    BaseImageId = mappedBaseId,
                    FromImageId = mappedFromId,
                    ToImageId = mappedToId,
                    Duration = sourceTiming.Duration,
                    SequenceOrder = sourceTiming.SequenceOrder,
                    MarkTypeString = sourceTiming.MarkTypeString,
                    CreatedAt = sourceTiming.CreatedAt
                });
                stats.AddedOriginalModeTimings++;
                targetOriginalTimingKeys.Add(key);
            }
            await target.SaveChangesAsync();

            // composite_scripts
            var targetCompositeScriptImageIds = new HashSet<int>(
                await target.CompositeScripts.AsNoTracking().Select(s => s.ImageId).ToListAsync());
            var sourceCompositeScripts = await source.CompositeScripts.AsNoTracking().ToListAsync();
            foreach (var sourceScript in sourceCompositeScripts)
            {
                if (!sourceToTargetMediaMap.TryGetValue(sourceScript.ImageId, out int mappedImageId) ||
                    targetCompositeScriptImageIds.Contains(mappedImageId))
                {
                    continue;
                }

                target.CompositeScripts.Add(new CompositeScript
                {
                    ImageId = mappedImageId,
                    TotalDuration = sourceScript.TotalDuration,
                    AutoCalculate = sourceScript.AutoCalculate,
                    CreatedAt = sourceScript.CreatedAt,
                    UpdatedAt = sourceScript.UpdatedAt
                });
                stats.AddedCompositeScripts++;
                targetCompositeScriptImageIds.Add(mappedImageId);
            }
            await target.SaveChangesAsync();

            // text projects + slides + text elements + rich text spans
            var targetProjectNames = new HashSet<string>(
                await target.TextProjects.AsNoTracking().Select(p => p.Name).ToListAsync(),
                StringComparer.OrdinalIgnoreCase);

            var sourceProjects = await source.TextProjects.AsNoTracking().ToListAsync();
            foreach (var sourceProject in sourceProjects)
            {
                string uniqueProjectName = GetUniqueName(sourceProject.Name, targetProjectNames);
                var newProject = new TextProject
                {
                    Name = uniqueProjectName,
                    BackgroundImagePath = sourceProject.BackgroundImagePath,
                    CanvasWidth = sourceProject.CanvasWidth,
                    CanvasHeight = sourceProject.CanvasHeight,
                    CreatedTime = sourceProject.CreatedTime,
                    ModifiedTime = sourceProject.ModifiedTime,
                    SortOrder = sourceProject.SortOrder
                };

                target.TextProjects.Add(newProject);
                await target.SaveChangesAsync();
                sourceToTargetProjectMap[sourceProject.Id] = newProject.Id;
                targetProjectNames.Add(uniqueProjectName);
                stats.AddedTextProjects++;
            }

            var sourceSlides = await source.Slides.AsNoTracking()
                .OrderBy(s => s.ProjectId)
                .ThenBy(s => s.SortOrder)
                .ToListAsync();
            foreach (var sourceSlide in sourceSlides)
            {
                if (!sourceToTargetProjectMap.TryGetValue(sourceSlide.ProjectId, out int mappedProjectId))
                {
                    continue;
                }

                var newSlide = new Slide
                {
                    ProjectId = mappedProjectId,
                    Title = sourceSlide.Title,
                    SortOrder = sourceSlide.SortOrder,
                    BackgroundImagePath = sourceSlide.BackgroundImagePath,
                    BackgroundColor = sourceSlide.BackgroundColor,
                    BackgroundGradientEnabled = sourceSlide.BackgroundGradientEnabled,
                    BackgroundGradientStartColor = sourceSlide.BackgroundGradientStartColor,
                    BackgroundGradientEndColor = sourceSlide.BackgroundGradientEndColor,
                    BackgroundGradientDirection = sourceSlide.BackgroundGradientDirection,
                    BackgroundOpacity = sourceSlide.BackgroundOpacity,
                    SplitMode = sourceSlide.SplitMode,
                    SplitRegionsData = sourceSlide.SplitRegionsData,
                    SplitStretchMode = sourceSlide.SplitStretchMode,
                    VideoBackgroundEnabled = sourceSlide.VideoBackgroundEnabled,
                    VideoLoopEnabled = sourceSlide.VideoLoopEnabled,
                    VideoVolume = sourceSlide.VideoVolume,
                    OutputMode = sourceSlide.OutputMode,
                    CreatedTime = sourceSlide.CreatedTime,
                    ModifiedTime = sourceSlide.ModifiedTime
                };

                target.Slides.Add(newSlide);
                await target.SaveChangesAsync();
                sourceToTargetSlideMap[sourceSlide.Id] = newSlide.Id;
                stats.AddedSlides++;
            }

            var sourceElements = await source.TextElements.AsNoTracking()
                .Where(e => e.SlideId.HasValue)
                .ToListAsync();
            foreach (var sourceElement in sourceElements)
            {
                if (!sourceElement.SlideId.HasValue || !sourceToTargetSlideMap.TryGetValue(sourceElement.SlideId.Value, out int mappedSlideId))
                {
                    continue;
                }

                var newElement = new TextElement
                {
                    ProjectId = null,
                    SlideId = mappedSlideId,
                    X = sourceElement.X,
                    Y = sourceElement.Y,
                    Width = sourceElement.Width,
                    Height = sourceElement.Height,
                    ZIndex = sourceElement.ZIndex,
                    Content = sourceElement.Content,
                    ComponentType = sourceElement.ComponentType,
                    ComponentConfigJson = sourceElement.ComponentConfigJson,
                    FontFamily = sourceElement.FontFamily,
                    FontSize = sourceElement.FontSize,
                    FontColor = sourceElement.FontColor,
                    IsBold = sourceElement.IsBold,
                    TextAlign = sourceElement.TextAlign,
                    TextVerticalAlign = sourceElement.TextVerticalAlign,
                    IsUnderline = sourceElement.IsUnderline,
                    IsItalic = sourceElement.IsItalic,
                    BorderColor = sourceElement.BorderColor,
                    BorderWidth = sourceElement.BorderWidth,
                    BorderRadius = sourceElement.BorderRadius,
                    BorderOpacity = sourceElement.BorderOpacity,
                    BackgroundColor = sourceElement.BackgroundColor,
                    BackgroundRadius = sourceElement.BackgroundRadius,
                    BackgroundOpacity = sourceElement.BackgroundOpacity,
                    ShadowType = sourceElement.ShadowType,
                    ShadowPreset = sourceElement.ShadowPreset,
                    ShadowColor = sourceElement.ShadowColor,
                    ShadowOffsetX = sourceElement.ShadowOffsetX,
                    ShadowOffsetY = sourceElement.ShadowOffsetY,
                    ShadowBlur = sourceElement.ShadowBlur,
                    ShadowOpacity = sourceElement.ShadowOpacity,
                    LineSpacing = sourceElement.LineSpacing,
                    LetterSpacing = sourceElement.LetterSpacing,
                    IsSymmetric = sourceElement.IsSymmetric,
                    SymmetricType = sourceElement.SymmetricType
                };

                target.TextElements.Add(newElement);
                await target.SaveChangesAsync();
                sourceToTargetElementMap[sourceElement.Id] = newElement.Id;
                stats.AddedTextElements++;
            }

            foreach (var sourceElement in sourceElements)
            {
                if (!sourceElement.SymmetricPairId.HasValue ||
                    !sourceToTargetElementMap.TryGetValue(sourceElement.Id, out int mappedElementId) ||
                    !sourceToTargetElementMap.TryGetValue(sourceElement.SymmetricPairId.Value, out int mappedSymmetricId))
                {
                    continue;
                }

                var targetElement = await target.TextElements.FirstOrDefaultAsync(e => e.Id == mappedElementId);
                if (targetElement == null)
                {
                    continue;
                }

                targetElement.SymmetricPairId = mappedSymmetricId;
                targetElement.SymmetricType = sourceElement.SymmetricType;
                await target.SaveChangesAsync();
            }

            var sourceSpans = await source.RichTextSpans.AsNoTracking().ToListAsync();
            foreach (var sourceSpan in sourceSpans)
            {
                if (!sourceToTargetElementMap.TryGetValue(sourceSpan.TextElementId, out int mappedElementId))
                {
                    continue;
                }

                target.RichTextSpans.Add(new RichTextSpan
                {
                    TextElementId = mappedElementId,
                    SpanOrder = sourceSpan.SpanOrder,
                    Text = sourceSpan.Text,
                    ParagraphIndex = sourceSpan.ParagraphIndex,
                    RunIndex = sourceSpan.RunIndex,
                    FormatVersion = sourceSpan.FormatVersion,
                    FontFamily = sourceSpan.FontFamily,
                    FontSize = sourceSpan.FontSize,
                    FontColor = sourceSpan.FontColor,
                    IsBold = sourceSpan.IsBold,
                    IsItalic = sourceSpan.IsItalic,
                    IsUnderline = sourceSpan.IsUnderline,
                    BorderColor = sourceSpan.BorderColor,
                    BorderWidth = sourceSpan.BorderWidth,
                    BorderRadius = sourceSpan.BorderRadius,
                    BorderOpacity = sourceSpan.BorderOpacity,
                    BackgroundColor = sourceSpan.BackgroundColor,
                    BackgroundRadius = sourceSpan.BackgroundRadius,
                    BackgroundOpacity = sourceSpan.BackgroundOpacity,
                    ShadowColor = sourceSpan.ShadowColor,
                    ShadowOffsetX = sourceSpan.ShadowOffsetX,
                    ShadowOffsetY = sourceSpan.ShadowOffsetY,
                    ShadowBlur = sourceSpan.ShadowBlur,
                    ShadowOpacity = sourceSpan.ShadowOpacity
                });
                stats.AddedRichTextSpans++;
            }
            await target.SaveChangesAsync();

            // lyrics groups + lyrics projects
            var targetGroups = await target.LyricsGroups.ToListAsync();
            var targetGroupByExternalId = targetGroups
                .Where(g => !string.IsNullOrWhiteSpace(g.ExternalId))
                .GroupBy(g => g.ExternalId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            var targetGroupNames = new HashSet<string>(targetGroups.Select(g => g.Name), StringComparer.OrdinalIgnoreCase);

            var sourceGroups = await source.LyricsGroups.AsNoTracking().ToListAsync();
            foreach (var sourceGroup in sourceGroups)
            {
                LyricsGroup targetGroup = null;
                if (!string.IsNullOrWhiteSpace(sourceGroup.ExternalId) &&
                    targetGroupByExternalId.TryGetValue(sourceGroup.ExternalId, out var existingGroup))
                {
                    targetGroup = existingGroup;
                }
                else
                {
                    string uniqueGroupName = GetUniqueName(sourceGroup.Name, targetGroupNames);
                    targetGroup = new LyricsGroup
                    {
                        Name = uniqueGroupName,
                        ExternalId = string.IsNullOrWhiteSpace(sourceGroup.ExternalId) ? Guid.NewGuid().ToString() : sourceGroup.ExternalId,
                        SortOrder = sourceGroup.SortOrder,
                        CreatedTime = sourceGroup.CreatedTime,
                        ModifiedTime = sourceGroup.ModifiedTime,
                        HighlightColor = sourceGroup.HighlightColor,
                        IsSystem = sourceGroup.IsSystem
                    };
                    target.LyricsGroups.Add(targetGroup);
                    await target.SaveChangesAsync();
                    targetGroupNames.Add(uniqueGroupName);
                    if (!string.IsNullOrWhiteSpace(targetGroup.ExternalId))
                    {
                        targetGroupByExternalId[targetGroup.ExternalId] = targetGroup;
                    }
                    stats.AddedLyricsGroups++;
                }

                sourceToTargetLyricsGroupMap[sourceGroup.Id] = targetGroup.Id;
            }

            var targetLyricsExternalIds = new HashSet<string>(
                (await target.LyricsProjects.AsNoTracking()
                    .Where(p => !string.IsNullOrWhiteSpace(p.ExternalId))
                    .Select(p => p.ExternalId)
                    .ToListAsync()),
                StringComparer.OrdinalIgnoreCase);

            var sourceLyrics = await source.LyricsProjects.AsNoTracking().ToListAsync();
            foreach (var sourceLyric in sourceLyrics)
            {
                if (!string.IsNullOrWhiteSpace(sourceLyric.ExternalId) &&
                    targetLyricsExternalIds.Contains(sourceLyric.ExternalId))
                {
                    continue;
                }

                int? mappedGroupId = null;
                if (sourceLyric.GroupId.HasValue && sourceToTargetLyricsGroupMap.TryGetValue(sourceLyric.GroupId.Value, out int mappedGroup))
                {
                    mappedGroupId = mappedGroup;
                }

                int? mappedImageId = null;
                if (sourceLyric.ImageId.HasValue && sourceToTargetMediaMap.TryGetValue(sourceLyric.ImageId.Value, out int mappedMedia))
                {
                    mappedImageId = mappedMedia;
                }

                var newLyric = new LyricsProject
                {
                    Name = sourceLyric.Name,
                    ImageId = mappedImageId,
                    GroupId = mappedGroupId,
                    ExternalId = string.IsNullOrWhiteSpace(sourceLyric.ExternalId) ? Guid.NewGuid().ToString() : sourceLyric.ExternalId,
                    SortOrder = sourceLyric.SortOrder,
                    SourceType = sourceLyric.SourceType,
                    Content = sourceLyric.Content,
                    FontSize = sourceLyric.FontSize,
                    TextAlign = sourceLyric.TextAlign,
                    ViewMode = sourceLyric.ViewMode,
                    ProjectionWatermarkPath = sourceLyric.ProjectionWatermarkPath,
                    CreatedTime = sourceLyric.CreatedTime,
                    ModifiedTime = sourceLyric.ModifiedTime
                };

                target.LyricsProjects.Add(newLyric);
                await target.SaveChangesAsync();
                if (!string.IsNullOrWhiteSpace(newLyric.ExternalId))
                {
                    targetLyricsExternalIds.Add(newLyric.ExternalId);
                }
                stats.AddedLyricsProjects++;
            }

            // bible_history：仅补充缺失槽位；已有槽位仅在本机为空且导入值非空时补齐
            var targetHistoryBySlot = (await target.BibleHistory.AsNoTracking().ToListAsync())
                .ToDictionary(r => r.SlotIndex, r => r);
            var sourceHistory = await source.BibleHistory.AsNoTracking().ToListAsync();
            foreach (var sourceRecord in sourceHistory)
            {
                if (!targetHistoryBySlot.TryGetValue(sourceRecord.SlotIndex, out var existingRecord))
                {
                    target.BibleHistory.Add(new BibleHistoryRecord
                    {
                        SlotIndex = sourceRecord.SlotIndex,
                        DisplayText = sourceRecord.DisplayText,
                        BookId = sourceRecord.BookId,
                        Chapter = sourceRecord.Chapter,
                        StartVerse = sourceRecord.StartVerse,
                        EndVerse = sourceRecord.EndVerse,
                        IsChecked = sourceRecord.IsChecked,
                        IsLocked = sourceRecord.IsLocked,
                        UpdatedTime = sourceRecord.UpdatedTime
                    });
                    stats.AddedBibleHistoryRecords++;
                    continue;
                }

                bool targetDisplayEmpty = string.IsNullOrWhiteSpace(existingRecord.DisplayText);
                bool sourceDisplayHasValue = !string.IsNullOrWhiteSpace(sourceRecord.DisplayText);
                if (!targetDisplayEmpty || !sourceDisplayHasValue)
                {
                    continue;
                }

                var updateRecord = await target.BibleHistory.FirstOrDefaultAsync(r => r.SlotIndex == sourceRecord.SlotIndex);
                if (updateRecord == null)
                {
                    continue;
                }

                updateRecord.DisplayText = sourceRecord.DisplayText;
                updateRecord.BookId = sourceRecord.BookId;
                updateRecord.Chapter = sourceRecord.Chapter;
                updateRecord.StartVerse = sourceRecord.StartVerse;
                updateRecord.EndVerse = sourceRecord.EndVerse;
                updateRecord.IsChecked = sourceRecord.IsChecked;
                updateRecord.IsLocked = sourceRecord.IsLocked;
                updateRecord.UpdatedTime = sourceRecord.UpdatedTime;
            }
            await target.SaveChangesAsync();

            // settings / ui_settings：仅补充缺失键，不覆盖本机值
            var targetSettingsKeys = new HashSet<string>(
                await target.Settings.AsNoTracking().Select(s => s.Key).ToListAsync(),
                StringComparer.OrdinalIgnoreCase);
            var sourceSettings = await source.Settings.AsNoTracking().ToListAsync();
            foreach (var sourceSetting in sourceSettings)
            {
                if (string.IsNullOrWhiteSpace(sourceSetting.Key) || targetSettingsKeys.Contains(sourceSetting.Key))
                {
                    continue;
                }

                target.Settings.Add(new Setting
                {
                    Key = sourceSetting.Key,
                    Value = sourceSetting.Value ?? string.Empty
                });
                targetSettingsKeys.Add(sourceSetting.Key);
                stats.AddedSettings++;
            }

            var targetUiSettingKeys = new HashSet<string>(
                await target.UISettings.AsNoTracking().Select(s => s.Key).ToListAsync(),
                StringComparer.OrdinalIgnoreCase);
            var sourceUiSettings = await source.UISettings.AsNoTracking().ToListAsync();
            foreach (var sourceUiSetting in sourceUiSettings)
            {
                if (string.IsNullOrWhiteSpace(sourceUiSetting.Key) || targetUiSettingKeys.Contains(sourceUiSetting.Key))
                {
                    continue;
                }

                target.UISettings.Add(new UISetting
                {
                    Key = sourceUiSetting.Key,
                    Value = sourceUiSetting.Value ?? string.Empty
                });
                targetUiSettingKeys.Add(sourceUiSetting.Key);
                stats.AddedUiSettings++;
            }

            await target.SaveChangesAsync();
            return stats;
        }

        private static string GetUniqueName(string baseName, HashSet<string> existingNames)
        {
            string normalizedBase = string.IsNullOrWhiteSpace(baseName) ? "未命名" : baseName.Trim();
            if (!existingNames.Contains(normalizedBase))
            {
                return normalizedBase;
            }

            int suffix = 1;
            string candidate;
            do
            {
                candidate = $"{normalizedBase} ({suffix})";
                suffix++;
            } while (existingNames.Contains(candidate));

            return candidate;
        }

        private async Task<List<string>> TryImportCompanionArtifactsForDbAsync(string sourceDbPath)
        {
            var imported = new List<string>();
            if (string.IsNullOrWhiteSpace(sourceDbPath) || !File.Exists(sourceDbPath))
            {
                return imported;
            }

            string sourceDir = Path.GetDirectoryName(sourceDbPath);
            if (string.IsNullOrWhiteSpace(sourceDir) || !Directory.Exists(sourceDir))
            {
                return imported;
            }

            string sourceFileName = Path.GetFileName(sourceDbPath) ?? string.Empty;
            string sourceBaseName = Path.GetFileNameWithoutExtension(sourceDbPath) ?? string.Empty;
            bool sourceLooksLikeMainDb = string.Equals(sourceFileName, "pyimages.db", StringComparison.OrdinalIgnoreCase);

            string[] configCandidates = sourceLooksLikeMainDb
                ? new[]
                {
                    Path.Combine(sourceDir, $"{sourceBaseName}.config.json"),
                    Path.Combine(sourceDir, "config.json")
                }
                : new[]
                {
                    Path.Combine(sourceDir, $"{sourceBaseName}.config.json")
                };

            foreach (string candidate in configCandidates)
            {
                if (!File.Exists(candidate))
                {
                    continue;
                }

                await Task.Run(() => File.Copy(candidate, _configFilePath, overwrite: true));
                imported.Add("config.json");
                LogInfo($"[ImportDB-Companion] config imported from {candidate}");
                break;
            }

            if (sourceLooksLikeMainDb)
            {
                string sourceThumbnailsDir = Path.Combine(sourceDir, "Thumbnails");
                if (Directory.Exists(sourceThumbnailsDir))
                {
                    if (Directory.Exists(_thumbnailsDir))
                    {
                        Directory.Delete(_thumbnailsDir, recursive: true);
                    }

                    await Task.Run(() => CopyDirectory(sourceThumbnailsDir, _thumbnailsDir));
                    imported.Add("Thumbnails");
                    LogInfo($"[ImportDB-Companion] thumbnails imported from {sourceThumbnailsDir}");
                }

                string sourceWatermarksDir = Path.Combine(sourceDir, "data", "watermarks");
                if (Directory.Exists(sourceWatermarksDir))
                {
                    if (Directory.Exists(_watermarksDir))
                    {
                        Directory.Delete(_watermarksDir, recursive: true);
                    }

                    await Task.Run(() => CopyDirectory(sourceWatermarksDir, _watermarksDir));
                    imported.Add("data/watermarks");
                    LogInfo($"[ImportDB-Companion] watermarks imported from {sourceWatermarksDir}");
                }
            }

            return imported;
        }

        private static string BuildCompanionImportMessage(IReadOnlyCollection<string> importedCompanionArtifacts)
        {
            if (importedCompanionArtifacts == null || importedCompanionArtifacts.Count == 0)
            {
                return string.Empty;
            }

            return $"\n\n同步导入配置资源：{string.Join("、", importedCompanionArtifacts)}";
        }

        /// <summary>
        /// 验证文件是否为有效的SQLite数据库
        /// </summary>
        private bool IsValidSqliteDatabase(string filePath)
        {
            try
            {
                // SQLite数据库文件头标识：SQLite format 3
                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                {
                    var header = new byte[16];
                    fs.Read(header, 0, 16);
                    var headerString = System.Text.Encoding.ASCII.GetString(header);
                    return headerString.StartsWith("SQLite format 3");
                }
            }
            catch
            {
                return false;
            }
        }

        private void ReplaceDatabaseFile(string sourceDbPath)
        {
            if (string.IsNullOrWhiteSpace(sourceDbPath) || !File.Exists(sourceDbPath))
            {
                throw new FileNotFoundException("源数据库文件不存在", sourceDbPath);
            }

            CleanupSqliteSidecarFiles(_defaultDbPath);
            if (File.Exists(_defaultDbPath))
            {
                File.Delete(_defaultDbPath);
            }

            File.Copy(sourceDbPath, _defaultDbPath, overwrite: true);
            CleanupSqliteSidecarFiles(_defaultDbPath);
        }

        private bool ReplaceDatabaseFileOrStagePending(string sourceDbPath)
        {
            try
            {
                ReplaceDatabaseFile(sourceDbPath);
                return false;
            }
            catch (IOException ioEx) when (IsLikelyFileInUse(ioEx))
            {
                File.Copy(sourceDbPath, _pendingImportDbPath, overwrite: true);
                LogWarn($"[Import-StagePending] db in use, staged pending file: {_pendingImportDbPath}");
                return true;
            }
        }

        private static bool IsLikelyFileInUse(IOException ex)
        {
            if (ex == null)
            {
                return false;
            }

            string message = ex.Message ?? string.Empty;
            return message.Contains("being used by another process", StringComparison.OrdinalIgnoreCase)
                || message.Contains("另一个进程", StringComparison.OrdinalIgnoreCase);
        }

        private void CleanupSqliteSidecarFiles(string dbPath)
        {
            if (string.IsNullOrWhiteSpace(dbPath))
            {
                return;
            }

            var walPath = dbPath + "-wal";
            var shmPath = dbPath + "-shm";
            try
            {
                if (File.Exists(walPath))
                {
                    File.Delete(walPath);
                    LogInfo($"[SQLite] deleted sidecar: {walPath}");
                }

                if (File.Exists(shmPath))
                {
                    File.Delete(shmPath);
                    LogInfo($"[SQLite] deleted sidecar: {shmPath}");
                }
            }
            catch (Exception ex)
            {
                LogWarn($"[SQLite] cleanup sidecar warning: {ex.Message}");
            }
        }

        private bool IsDatabaseIntegrityOk(string dbPath, out string message)
        {
            message = "ok";
            try
            {
                using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "PRAGMA quick_check;";
                object result = command.ExecuteScalar();
                string text = (result == null || result == DBNull.Value) ? string.Empty : result.ToString();
                if (!string.Equals(text, "ok", StringComparison.OrdinalIgnoreCase))
                {
                    message = string.IsNullOrWhiteSpace(text) ? "quick_check returned empty result" : text;
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return false;
            }
        }

        private string FindDatabaseFileFromExtractedPackage(string tempDir)
        {
            var preferred = Path.Combine(tempDir, "pyimages.db");
            if (File.Exists(preferred) && IsValidSqliteDatabase(preferred))
            {
                return preferred;
            }

            foreach (var dbFile in Directory.GetFiles(tempDir, "*.db", SearchOption.AllDirectories))
            {
                if (IsValidSqliteDatabase(dbFile))
                {
                    return dbFile;
                }
            }

            return string.Empty;
        }

        private (int Folders, int Files, int LyricsGroups, int LyricsProjects) ReadDatabaseStats(string dbPath)
        {
            try
            {
                using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
                connection.Open();

                int CountByCandidates(params string[] tables)
                {
                    foreach (var table in tables)
                    {
                        try
                        {
                            using var cmd = connection.CreateCommand();
                            cmd.CommandText = $"SELECT COUNT(1) FROM {table};";
                            object value = cmd.ExecuteScalar();
                            return value == null || value == DBNull.Value ? 0 : Convert.ToInt32(value);
                        }
                        catch
                        {
                            // 尝试下一个候选表名
                        }
                    }

                    return 0;
                }

                return (
                    CountByCandidates("folders", "Folders"),
                    CountByCandidates("images", "media_files", "MediaFiles"),
                    CountByCandidates("lyrics_groups", "LyricsGroups"),
                    CountByCandidates("lyrics_projects", "LyricsProjects"));
            }
            catch
            {
                return (0, 0, 0, 0);
            }
        }

        /// <summary>
        /// 递归复制目录及其所有内容
        /// </summary>
        private void CopyDirectory(string sourceDir, string targetDir)
        {
            // 创建目标目录
            Directory.CreateDirectory(targetDir);

            // 复制所有文件
            foreach (var file in Directory.GetFiles(sourceDir))
            {
                var fileName = Path.GetFileName(file);
                var targetFile = Path.Combine(targetDir, fileName);
                File.Copy(file, targetFile, overwrite: true);
            }

            // 递归复制所有子目录
            foreach (var subDir in Directory.GetDirectories(sourceDir))
            {
                var dirName = Path.GetFileName(subDir);
                var targetSubDir = Path.Combine(targetDir, dirName);
                CopyDirectory(subDir, targetSubDir);
            }
        }

        /// <summary>
        /// 关闭所有数据库连接
        /// </summary>
        private void CloseAllDatabaseConnections()
        {
            try
            {
                // 强制垃圾回收，释放所有数据库连接
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                // 清除SQLite连接池
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

                LogInfo("[Import] all db connections closed");
            }
            catch (Exception ex)
            {
                LogWarn($"[Import] closing db connections warning: {ex.Message}");
            }
        }

    }
}

