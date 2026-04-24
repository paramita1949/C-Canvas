using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;

namespace ImageColorChanger.Services
{
    public enum DatabaseMigrationMessageLevel
    {
        Info,
        Warning,
        Error
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
        public async Task<DatabaseMigrationResult> ImportDatabaseAsync(string sourcePath)
        {
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

