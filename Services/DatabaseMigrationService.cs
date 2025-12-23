using System;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;

namespace ImageColorChanger.Services
{
    /// <summary>
    /// 数据库迁移服务 - 提供数据库、缩略图和配置文件的导入导出功能
    /// </summary>
    public class DatabaseMigrationService
    {
        private readonly string _defaultDbPath;
        private readonly string _thumbnailsDir;
        private readonly string _configFilePath;

        /// <summary>
        /// 构造函数
        /// </summary>
        public DatabaseMigrationService()
        {
            // 默认数据库路径：主程序目录/pyimages.db
            _defaultDbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "pyimages.db");
            // 缩略图文件夹路径：主程序目录/Thumbnails
            _thumbnailsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Thumbnails");
            // 配置文件路径：主程序目录/config.json
            _configFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
        }

        /// <summary>
        /// 导出数据库到指定位置（压缩包格式，包含数据库、缩略图和配置文件）
        /// </summary>
        /// <param name="targetPath">目标压缩包文件路径</param>
        /// <returns>是否成功</returns>
        public async Task<bool> ExportDatabaseAsync(string targetPath)
        {
            try
            {
                // 检查源数据库是否存在
                if (!File.Exists(_defaultDbPath))
                {
                    System.Windows.MessageBox.Show("数据库文件不存在，无法导出。", "导出失败", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                    return false;
                }

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

                System.Diagnostics.Debug.WriteLine("✅ WAL checkpoint 完成");

                // 创建临时目录用于打包
                var tempDir = Path.Combine(Path.GetTempPath(), $"db_export_{Guid.NewGuid()}");
                Directory.CreateDirectory(tempDir);

                try
                {
                    // 复制数据库文件到临时目录
                    var tempDbPath = Path.Combine(tempDir, "pyimages.db");
                    await Task.Run(() => File.Copy(_defaultDbPath, tempDbPath, overwrite: true));
                    System.Diagnostics.Debug.WriteLine("✅ 数据库文件已复制到临时目录");

                    // 复制 Thumbnails 文件夹到临时目录（如果存在）
                    if (Directory.Exists(_thumbnailsDir))
                    {
                        var tempThumbnailsDir = Path.Combine(tempDir, "Thumbnails");
                        await Task.Run(() => CopyDirectory(_thumbnailsDir, tempThumbnailsDir));
                        System.Diagnostics.Debug.WriteLine($"✅ 缩略图文件夹已复制到临时目录（{Directory.GetFiles(tempThumbnailsDir).Length} 个文件）");
                    }

                    // 复制配置文件到临时目录（如果存在）
                    if (File.Exists(_configFilePath))
                    {
                        var tempConfigPath = Path.Combine(tempDir, "config.json");
                        await Task.Run(() => File.Copy(_configFilePath, tempConfigPath, overwrite: true));
                        System.Diagnostics.Debug.WriteLine("✅ 配置文件已复制到临时目录");
                    }

                    // 删除目标文件（如果存在）
                    if (File.Exists(targetPath))
                    {
                        File.Delete(targetPath);
                    }

                    // 创建压缩包
                    await Task.Run(() => ZipFile.CreateFromDirectory(tempDir, targetPath, CompressionLevel.Optimal, false));
                    System.Diagnostics.Debug.WriteLine($"✅ 压缩包创建成功: {targetPath}");

                    System.Windows.MessageBox.Show($"数据库导出成功！\n\n导出位置：{targetPath}", "导出成功", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                    return true;
                }
                finally
                {
                    // 清理临时目录
                    if (Directory.Exists(tempDir))
                    {
                        Directory.Delete(tempDir, recursive: true);
                        System.Diagnostics.Debug.WriteLine("✅ 临时目录已清理");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"数据库导出失败：{ex.Message}", "导出失败", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                System.Diagnostics.Debug.WriteLine($"❌ 数据库导出异常: {ex}");
                return false;
            }
        }

        /// <summary>
        /// 从指定位置导入数据库（支持压缩包格式）
        /// </summary>
        /// <param name="sourcePath">源文件路径（.zip 压缩包或 .db 文件）</param>
        /// <returns>是否成功</returns>
        public async Task<bool> ImportDatabaseAsync(string sourcePath)
        {
            try
            {
                // 检查源文件是否存在
                if (!File.Exists(sourcePath))
                {
                    System.Windows.MessageBox.Show("选择的数据库文件不存在。", "导入失败", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                    return false;
                }

                // 确保 backdb 文件夹存在
                var backdbDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "backdb");
                if (!Directory.Exists(backdbDir))
                {
                    Directory.CreateDirectory(backdbDir);
                    System.Diagnostics.Debug.WriteLine($"✅ 已创建备份文件夹: {backdbDir}");
                }

                // 备份当前数据库到 backdb 文件夹
                if (File.Exists(_defaultDbPath))
                {
                    var backupFileName = $"pyimages.db.backup_{DateTime.Now:yyyyMMdd_HHmmss}";
                    var backupPath = Path.Combine(backdbDir, backupFileName);
                    await Task.Run(() => File.Copy(_defaultDbPath, backupPath, overwrite: true));
                    System.Diagnostics.Debug.WriteLine($"✅ 已备份当前数据库到: {backupPath}");
                }

                // 备份当前缩略图文件夹到 backdb 文件夹
                if (Directory.Exists(_thumbnailsDir))
                {
                    var backupThumbnailsDir = Path.Combine(backdbDir, $"Thumbnails.backup_{DateTime.Now:yyyyMMdd_HHmmss}");
                    await Task.Run(() => CopyDirectory(_thumbnailsDir, backupThumbnailsDir));
                    System.Diagnostics.Debug.WriteLine($"✅ 已备份当前缩略图文件夹到: {backupThumbnailsDir}");
                }

                // 备份当前配置文件到 backdb 文件夹
                if (File.Exists(_configFilePath))
                {
                    var backupConfigFileName = $"config.json.backup_{DateTime.Now:yyyyMMdd_HHmmss}";
                    var backupConfigPath = Path.Combine(backdbDir, backupConfigFileName);
                    await Task.Run(() => File.Copy(_configFilePath, backupConfigPath, overwrite: true));
                    System.Diagnostics.Debug.WriteLine($"✅ 已备份当前配置文件到: {backupConfigPath}");
                }

                // 关闭所有数据库连接
                CloseAllDatabaseConnections();

                // 判断文件类型并导入
                var fileExtension = Path.GetExtension(sourcePath).ToLower();

                if (fileExtension == ".zip")
                {
                    // 导入压缩包格式
                    await ImportFromZipAsync(sourcePath);
                }
                else if (fileExtension == ".db")
                {
                    // 导入单个数据库文件（兼容旧格式）
                    if (!IsValidSqliteDatabase(sourcePath))
                    {
                        System.Windows.MessageBox.Show("选择的文件不是有效的SQLite数据库文件。", "导入失败", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                        return false;
                    }
                    await Task.Run(() => File.Copy(sourcePath, _defaultDbPath, overwrite: true));
                    System.Diagnostics.Debug.WriteLine("✅ 数据库文件导入成功（单文件模式）");
                }
                else
                {
                    System.Windows.MessageBox.Show("不支持的文件格式，请选择 .zip 或 .db 文件。", "导入失败", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                    return false;
                }

                // 提示重启应用程序
                var result = System.Windows.MessageBox.Show(
                    "数据库导入成功！\n\n为使更改生效，需要重启应用程序。\n是否立即重启？",
                    "导入成功",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Information);

                if (result == System.Windows.MessageBoxResult.Yes)
                {
                    RestartApplication();
                }

                return true;
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"数据库导入失败：{ex.Message}", "导入失败", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                System.Diagnostics.Debug.WriteLine($"❌ 数据库导入异常: {ex}");
                return false;
            }
        }

        /// <summary>
        /// 从压缩包导入数据库、缩略图和配置文件
        /// </summary>
        private async Task ImportFromZipAsync(string zipPath)
        {
            var tempDir = Path.Combine(Path.GetTempPath(), $"db_import_{Guid.NewGuid()}");

            try
            {
                // 解压缩到临时目录
                await Task.Run(() => ZipFile.ExtractToDirectory(zipPath, tempDir));
                System.Diagnostics.Debug.WriteLine($"✅ 压缩包已解压到临时目录: {tempDir}");

                // 导入数据库文件
                var tempDbPath = Path.Combine(tempDir, "pyimages.db");
                if (!File.Exists(tempDbPath))
                {
                    throw new Exception("压缩包中未找到 pyimages.db 文件");
                }

                if (!IsValidSqliteDatabase(tempDbPath))
                {
                    throw new Exception("压缩包中的数据库文件格式无效");
                }

                await Task.Run(() => File.Copy(tempDbPath, _defaultDbPath, overwrite: true));
                System.Diagnostics.Debug.WriteLine("✅ 数据库文件已导入");

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
                    System.Diagnostics.Debug.WriteLine($"✅ 缩略图文件夹已导入（{Directory.GetFiles(_thumbnailsDir).Length} 个文件）");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("⚠️ 压缩包中未找到 Thumbnails 文件夹");
                }

                // 导入配置文件（如果存在）
                var tempConfigPath = Path.Combine(tempDir, "config.json");
                if (File.Exists(tempConfigPath))
                {
                    await Task.Run(() => File.Copy(tempConfigPath, _configFilePath, overwrite: true));
                    System.Diagnostics.Debug.WriteLine("✅ 配置文件已导入");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("⚠️ 压缩包中未找到 config.json 文件");
                }
            }
            finally
            {
                // 清理临时目录
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                    System.Diagnostics.Debug.WriteLine("✅ 临时目录已清理");
                }
            }
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

                System.Diagnostics.Debug.WriteLine("✅ 已关闭所有数据库连接");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ 关闭数据库连接时出现警告: {ex.Message}");
            }
        }

        /// <summary>
        /// 重启应用程序
        /// </summary>
        private void RestartApplication()
        {
            try
            {
                var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
                System.Diagnostics.Process.Start(exePath);
                System.Windows.Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"自动重启失败，请手动重启应用程序。\n\n错误：{ex.Message}", "重启失败", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            }
        }
    }
}

