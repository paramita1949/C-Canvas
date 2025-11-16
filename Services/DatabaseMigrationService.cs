using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;

namespace ImageColorChanger.Services
{
    /// <summary>
    /// 数据库迁移服务 - 提供数据库导入导出功能
    /// </summary>
    public class DatabaseMigrationService
    {
        private readonly string _defaultDbPath;

        /// <summary>
        /// 构造函数
        /// </summary>
        public DatabaseMigrationService()
        {
            // 默认数据库路径：主程序目录/pyimages.db
            _defaultDbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "pyimages.db");
        }

        /// <summary>
        /// 导出数据库到指定位置
        /// </summary>
        /// <param name="targetPath">目标文件路径</param>
        /// <returns>是否成功</returns>
        public async Task<bool> ExportDatabaseAsync(string targetPath)
        {
            try
            {
                // 检查源数据库是否存在
                if (!File.Exists(_defaultDbPath))
                {
                    MessageBox.Show("数据库文件不存在，无法导出。", "导出失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }

                // 确保 backdb 文件夹存在
                var backdbDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "backdb");
                if (!Directory.Exists(backdbDir))
                {
                    Directory.CreateDirectory(backdbDir);
                    System.Diagnostics.Debug.WriteLine($"✅ 已创建备份文件夹: {backdbDir}");
                }

                // 将目标路径修改为 backdb 文件夹下
                var fileName = Path.GetFileName(targetPath);
                var finalTargetPath = Path.Combine(backdbDir, fileName);

                // 复制数据库文件
                await Task.Run(() => File.Copy(_defaultDbPath, finalTargetPath, overwrite: true));

                MessageBox.Show($"数据库导出成功！\n\n导出位置：{finalTargetPath}", "导出成功", MessageBoxButton.OK, MessageBoxImage.Information);
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"数据库导出失败：{ex.Message}", "导出失败", MessageBoxButton.OK, MessageBoxImage.Error);
                System.Diagnostics.Debug.WriteLine($"❌ 数据库导出异常: {ex}");
                return false;
            }
        }

        /// <summary>
        /// 从指定位置导入数据库
        /// </summary>
        /// <param name="sourcePath">源文件路径</param>
        /// <returns>是否成功</returns>
        public async Task<bool> ImportDatabaseAsync(string sourcePath)
        {
            try
            {
                // 检查源文件是否存在
                if (!File.Exists(sourcePath))
                {
                    MessageBox.Show("选择的数据库文件不存在。", "导入失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }

                // 验证文件是否为有效的SQLite数据库
                if (!IsValidSqliteDatabase(sourcePath))
                {
                    MessageBox.Show("选择的文件不是有效的SQLite数据库文件。", "导入失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }

                // 备份当前数据库
                if (File.Exists(_defaultDbPath))
                {
                    var backupPath = $"{_defaultDbPath}.backup_{DateTime.Now:yyyyMMdd_HHmmss}";
                    await Task.Run(() => File.Copy(_defaultDbPath, backupPath, overwrite: true));
                    System.Diagnostics.Debug.WriteLine($"✅ 已备份当前数据库到: {backupPath}");
                }

                // 导入新数据库（覆盖当前数据库）
                await Task.Run(() => File.Copy(sourcePath, _defaultDbPath, overwrite: true));

                var result = MessageBox.Show(
                    "数据库导入成功！\n\n为使更改生效，需要重启应用程序。\n是否立即重启？",
                    "导入成功",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);

                if (result == MessageBoxResult.Yes)
                {
                    RestartApplication();
                }

                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"数据库导入失败：{ex.Message}", "导入失败", MessageBoxButton.OK, MessageBoxImage.Error);
                System.Diagnostics.Debug.WriteLine($"❌ 数据库导入异常: {ex}");
                return false;
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
        /// 重启应用程序
        /// </summary>
        private void RestartApplication()
        {
            try
            {
                var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
                System.Diagnostics.Process.Start(exePath);
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"自动重启失败，请手动重启应用程序。\n\n错误：{ex.Message}", "重启失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}

