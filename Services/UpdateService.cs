using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ImageColorChanger.Services
{
    /// <summary>
    /// 更新文件信息
    /// </summary>
    public class UpdateFileInfo
    {
        public string FileName { get; set; } = "";
        public string DownloadUrl { get; set; } = "";
        public long FileSize { get; set; }
    }

    /// <summary>
    /// 版本信息模型
    /// </summary>
    public class VersionInfo
    {
        public string Version { get; set; } = "";
        public List<UpdateFileInfo> Files { get; set; } = new List<UpdateFileInfo>();
        
        // 兼容旧代码
        public string DownloadUrl 
        { 
            get => Files.FirstOrDefault()?.DownloadUrl ?? "";
            set { }
        }
        public long FileSize 
        { 
            get => Files.Sum(f => f.FileSize);
            set { }
        }
    }

#nullable enable

    /// <summary>
    /// 自动更新服务（基于 Cloudflare R2）
    /// </summary>
    public class UpdateService
    {
        // Cloudflare R2 地址
        private const string R2_BASE_URL = "https://canvas.019890311.xyz";
        private const string LATEST_VERSION_URL = R2_BASE_URL + "/latest.txt";
        private const string FILES_LIST_URL_TEMPLATE = R2_BASE_URL + "/v{version}/files.txt";
        
        // 尝试自动发现的文件名列表（如果 files.txt 不存在时使用）
        // 优先级：压缩包 > DLL > 资源文件 > 配置文件
        private static readonly string[] COMMON_UPDATE_FILES = new[]
        {
            // 压缩包（优先）
            "update.zip",
            "update.7z",
            "update.rar",
            "Canvas-Update.zip",
            "Canvas-Full.zip",

            // 主程序文件
            "CanvasCast.dll",
            "CanvasCast.exe",

            // 资源文件
            "Resources.pak",

            // 配置文件
            "appsettings.json",
            "config.json",

            // 其他可能的更新文件
            "*.dll",
            "*.pak",
            "*.exe"
        };
        
        private static readonly HttpClient _httpClient = new HttpClient();

        static UpdateService()
        {
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Canvas-Cast-Updater");
            _httpClient.Timeout = TimeSpan.FromMinutes(5);
        }

        /// <summary>
        /// 获取当前应用程序版本（从 MainWindow 的 Title 属性读取）
        /// </summary>
        public static string GetCurrentVersion()
        {
            try
            {
                // 从运行时的 MainWindow 获取 Title（动态获取，无论程序在什么位置）
                var mainWindow = System.Windows.Application.Current?.MainWindow as ImageColorChanger.UI.MainWindow;
                if (mainWindow != null && !string.IsNullOrEmpty(mainWindow.Title))
                {
                    var match = Regex.Match(mainWindow.Title, @"V(\d+\.\d+\.\d+)");
                    if (match.Success)
                    {
                        return match.Groups[1].Value;
                    }
                }

                // 如果 MainWindow 还未创建（更新检查在窗口加载后执行，这种情况不应该发生），返回默认版本
                return "5.2.8";
            }
            catch
            {
                return "5.2.8";
            }
        }

        /// <summary>
        /// 检查是否有新版本（带重试机制）
        /// </summary>
        public static async Task<VersionInfo?> CheckForUpdatesAsync()
        {
            return await RetryAsync(async () => await CheckForUpdatesInternalAsync(), maxRetries: 3, retryDelayMs: 2000);
        }

        /// <summary>
        /// 检查是否有新版本（内部实现）
        /// </summary>
        private static async Task<VersionInfo?> CheckForUpdatesInternalAsync()
        {
#if DEBUG
            Debug.WriteLine("[UpdateService] 开始检查更新...");
            Debug.WriteLine($"[UpdateService] 检查地址: {LATEST_VERSION_URL}");
#endif
            // 下载 latest.txt 获取最新版本号
            var latestVersion = await _httpClient.GetStringAsync(LATEST_VERSION_URL);
            latestVersion = latestVersion.Trim();

#if DEBUG
            Debug.WriteLine($"[UpdateService] 最新版本: {latestVersion}");
#endif

            // 验证版本号格式
            if (!Regex.IsMatch(latestVersion, @"^\d+\.\d+\.\d+$"))
            {
#if DEBUG
                Debug.WriteLine($"[UpdateService] 版本号格式无效: {latestVersion}");
#endif
                return null;
            }

            // 比较版本号
            var currentVersion = GetCurrentVersion();
#if DEBUG
            Debug.WriteLine($"[UpdateService] 当前版本: {currentVersion}");
#endif

            if (CompareVersions(latestVersion, currentVersion) > 0)
            {
#if DEBUG
                Debug.WriteLine($"[UpdateService] 发现新版本: {latestVersion}");
#endif

                // 获取更新文件列表
                var files = await GetUpdateFilesListAsync(latestVersion);
                
                return new VersionInfo
                {
                    Version = latestVersion,
                    Files = files
                };
            }

#if DEBUG
            Debug.WriteLine($"[UpdateService] 已是最新版本");
#endif
            return null;
        }

        /// <summary>
        /// 通用重试机制（引用类型）
        /// </summary>
        private static async Task<T?> RetryAsync<T>(Func<Task<T?>> operation, int maxRetries = 3, int retryDelayMs = 2000) where T : class
        {
            int attempt = 0;
            Exception? lastException = null;

            while (attempt < maxRetries)
            {
                try
                {
                    attempt++;
#if DEBUG
                    if (attempt > 1)
                    {
                        Debug.WriteLine($"[UpdateService] 重试 {attempt}/{maxRetries}...");
                    }
#endif
                    var result = await operation();
                    return result;
                }
                catch (Exception ex)
                {
                    lastException = ex;
#if DEBUG
                    Debug.WriteLine($"[UpdateService] 尝试 {attempt}/{maxRetries} 失败: {ex.Message}");
#endif
                    
                    // 如果不是最后一次尝试，等待后重试
                    if (attempt < maxRetries)
                    {
#if DEBUG
                        Debug.WriteLine($"[UpdateService] 等待 {retryDelayMs}ms 后重试...");
#endif
                        await Task.Delay(retryDelayMs);
                    }
                }
            }

#if DEBUG
            Debug.WriteLine($"[UpdateService] 所有重试均失败，最后错误: {lastException?.Message}");
#endif
            return null;
        }

        /// <summary>
        /// 通用重试机制（布尔返回值）
        /// </summary>
        private static async Task<bool> RetryAsync(Func<Task<bool>> operation, int maxRetries = 3, int retryDelayMs = 2000)
        {
            int attempt = 0;
            Exception? lastException = null;

            while (attempt < maxRetries)
            {
                try
                {
                    attempt++;
#if DEBUG
                    if (attempt > 1)
                    {
                        Debug.WriteLine($"[UpdateService] 重试 {attempt}/{maxRetries}...");
                    }
#endif
                    var result = await operation();
                    if (result)
                    {
                        return true;
                    }
                    
                    // 如果返回false但没有异常，不重试（比如404情况）
                    return false;
                }
                catch (Exception ex)
                {
                    lastException = ex;
#if DEBUG
                    Debug.WriteLine($"[UpdateService] 尝试 {attempt}/{maxRetries} 失败: {ex.Message}");
#endif
                    
                    // 如果不是最后一次尝试，等待后重试
                    if (attempt < maxRetries)
                    {
#if DEBUG
                        Debug.WriteLine($"[UpdateService] 等待 {retryDelayMs}ms 后重试...");
#endif
                        await Task.Delay(retryDelayMs);
                    }
                }
            }

#if DEBUG
            Debug.WriteLine($"[UpdateService] 所有重试均失败，最后错误: {lastException?.Message}");
#endif
            return false;
        }

        /// <summary>
        /// 获取更新文件列表
        /// </summary>
        private static async Task<List<UpdateFileInfo>> GetUpdateFilesListAsync(string version)
        {
            var files = new List<UpdateFileInfo>();
            
            try
            {
                // 优先尝试从服务器获取文件列表
                var filesListUrl = FILES_LIST_URL_TEMPLATE.Replace("{version}", version);
                var filesList = await _httpClient.GetStringAsync(filesListUrl);
                var fileNames = filesList.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                
#if DEBUG
                Debug.WriteLine($"[UpdateService] 从服务器获取到文件列表: {string.Join(", ", fileNames)}");
#endif
                
                foreach (var fileName in fileNames)
                {
                    var trimmedName = fileName.Trim();
                    if (!string.IsNullOrEmpty(trimmedName))
                    {
                        files.Add(new UpdateFileInfo
                        {
                            FileName = trimmedName,
                            DownloadUrl = $"{R2_BASE_URL}/v{version}/{trimmedName}",
                            FileSize = 0
                        });
                    }
                }
            }
            catch
            {
                // 如果 files.txt 不存在，尝试自动发现文件（通过HEAD请求检查文件是否存在）
#if DEBUG
                Debug.WriteLine($"[UpdateService] files.txt 不存在，尝试自动发现文件");
#endif
                files = await DiscoverFilesAsync(version);
            }
            
            return files;
        }

        /// <summary>
        /// 自动发现版本文件夹中的文件
        /// </summary>
        private static async Task<List<UpdateFileInfo>> DiscoverFilesAsync(string version)
        {
            var files = new List<UpdateFileInfo>();
            var baseUrl = $"{R2_BASE_URL}/v{version}/";
            
            // 尝试发现常见文件
            foreach (var fileName in COMMON_UPDATE_FILES)
            {
                var fileUrl = baseUrl + fileName;
                try
                {
                    // 使用HEAD请求检查文件是否存在
                    using (var request = new HttpRequestMessage(HttpMethod.Head, fileUrl))
                    {
                        var response = await _httpClient.SendAsync(request);
                        if (response.IsSuccessStatusCode)
                        {
                            files.Add(new UpdateFileInfo
                            {
                                FileName = fileName,
                                DownloadUrl = fileUrl,
                                FileSize = response.Content.Headers.ContentLength ?? 0
                            });
#if DEBUG
                            Debug.WriteLine($"[UpdateService] 发现文件: {fileName}");
#endif
                        }
                    }
                }
                catch
                {
                    // 文件不存在，跳过
                }
            }
            
#if DEBUG
            if (files.Count == 0)
            {
                Debug.WriteLine($"[UpdateService] 未发现任何文件，建议在服务器上创建 files.txt");
            }
            else
            {
                Debug.WriteLine($"[UpdateService] 自动发现 {files.Count} 个文件");
            }
#endif
            
            return files;
        }

        /// <summary>
        /// 下载更新文件
        /// </summary>
        public static async Task<string?> DownloadUpdateAsync(VersionInfo versionInfo, IProgress<(long, long)>? progress = null)
        {
            try
            {
                // 检查文件列表是否为空
                if (versionInfo.Files == null || versionInfo.Files.Count == 0)
                {
#if DEBUG
                    Debug.WriteLine($"[UpdateService] 更新文件列表为空");
#endif
                    throw new Exception("未找到更新文件。\n\n请在服务器上创建 files.txt 文件，列出需要更新的文件。\n例如：\nupdate.zip\nCanvas Cast.dll");
                }

                // 创建临时目录
                var tempDir = Path.Combine(Path.GetTempPath(), "CanvasCastUpdate");
                if (Directory.Exists(tempDir))
                {
                    // 清空旧文件
                    Directory.Delete(tempDir, true);
                }
                Directory.CreateDirectory(tempDir);

#if DEBUG
                Debug.WriteLine($"[UpdateService] 开始下载 {versionInfo.Files.Count} 个更新文件");
#endif

                // 计算总大小（先获取每个文件的大小）
                long totalBytes = 0;
                foreach (var file in versionInfo.Files)
                {
                    try
                    {
                        using (var response = await _httpClient.GetAsync(file.DownloadUrl, HttpCompletionOption.ResponseHeadersRead))
                        {
                            if (response.IsSuccessStatusCode)
                            {
                                file.FileSize = response.Content.Headers.ContentLength ?? 0;
                                totalBytes += file.FileSize;
                            }
                        }
                    }
                    catch { }
                }

                long downloadedBytes = 0;

                // 下载所有文件
                var successfulFiles = 0;
                var failedFiles = 0;
                
                foreach (var file in versionInfo.Files)
                {
#if DEBUG
                    Debug.WriteLine($"[UpdateService] 下载文件: {file.FileName}");
#endif
                    var filePath = Path.Combine(tempDir, file.FileName);
                    
                    // 使用重试机制下载单个文件
                    bool downloadSuccess = await RetryAsync(async () =>
                    {
                        using (var response = await _httpClient.GetAsync(file.DownloadUrl, HttpCompletionOption.ResponseHeadersRead))
                        {
                            // 如果文件不存在（404），返回false表示跳过
                            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                            {
#if DEBUG
                                Debug.WriteLine($"[UpdateService] 文件不存在，跳过: {file.FileName} (404)");
#endif
                                return false;
                            }
                            
                            response.EnsureSuccessStatusCode();

                            using (var contentStream = await response.Content.ReadAsStreamAsync())
                            using (var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                            {
                                var buffer = new byte[8192];
                                int bytesRead;
                                long fileDownloadedBytes = 0;

                                while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                                {
                                    await fileStream.WriteAsync(buffer, 0, bytesRead);
                                    fileDownloadedBytes += bytesRead;
                                    downloadedBytes += bytesRead;
                                    progress?.Report((downloadedBytes, totalBytes));
                                }
                            }
                            
                            return true;
                        }
                    }, maxRetries: 3, retryDelayMs: 2000);
                    
                    if (!downloadSuccess)
                    {
                        failedFiles++;
                        continue;
                    }
                    
                    successfulFiles++;
#if DEBUG
                    Debug.WriteLine($"[UpdateService] 完成: {file.FileName}");
#endif

                    // 如果是压缩包，自动解压
                    if (IsCompressedFile(file.FileName))
                    {
#if DEBUG
                        Debug.WriteLine($"[UpdateService] 检测到压缩包，开始解压: {file.FileName}");
#endif
                        await ExtractCompressedFileAsync(filePath, tempDir);
                        
                        // 删除压缩包文件
                        File.Delete(filePath);
#if DEBUG
                        Debug.WriteLine($"[UpdateService] 解压完成，已删除压缩包");
#endif
                    }
                }
                
#if DEBUG
                Debug.WriteLine($"[UpdateService] 下载完成: 成功 {successfulFiles} 个，失败/跳过 {failedFiles} 个");
                
                if (successfulFiles == 0)
                {
                    Debug.WriteLine($"[UpdateService] 警告: 没有成功下载任何文件！");
                }
#endif
                
                // 如果没有任何文件成功下载，返回失败
                if (successfulFiles == 0)
                {
                    return null;
                }

#if DEBUG
                Debug.WriteLine($"[UpdateService] 所有文件下载完成: {tempDir}");
#endif
                return tempDir; // 返回临时目录路径
            }
            catch (Exception ex)
            {
#if DEBUG
                Debug.WriteLine($"[UpdateService] 下载更新失败: {ex.Message}");
#else
                _ = ex; // 避免Release模式下的未使用警告
#endif
                return null;
            }
        }

        /// <summary>
        /// 应用更新（需要重启程序）
        /// </summary>
        public static bool ApplyUpdate(string updateDir)
        {
            try
            {
                if (!Directory.Exists(updateDir))
                {
#if DEBUG
                    Debug.WriteLine($"[UpdateService] 更新目录不存在: {updateDir}");
#endif
                    return false;
                }

                // 获取当前程序目录和主程序路径
                var currentDir = AppDomain.CurrentDomain.BaseDirectory;
                var currentExePath = Process.GetCurrentProcess().MainModule?.FileName ??
                                    Path.Combine(currentDir, "CanvasCast.exe");
                var updaterScript = Path.Combine(Path.GetTempPath(), "update_canvas.bat");

                // 获取所有需要更新的文件（包括子目录）
                var updateFiles = Directory.GetFiles(updateDir, "*.*", SearchOption.AllDirectories);
                if (updateFiles.Length == 0)
                {
#if DEBUG
                    Debug.WriteLine($"[UpdateService] 更新目录为空");
#endif
                    return false;
                }

#if DEBUG
                Debug.WriteLine($"[UpdateService] 当前程序: {currentExePath}");
                Debug.WriteLine($"[UpdateService] 发现 {updateFiles.Length} 个更新文件:");
                foreach (var file in updateFiles)
                {
                    Debug.WriteLine($"  - {Path.GetFileName(file)}");
                }
#endif

                // 构建备份命令
                var backupCommands = new System.Text.StringBuilder();
                var updateCommands = new System.Text.StringBuilder();
                var restoreCommands = new System.Text.StringBuilder();
                var cleanupCommands = new System.Text.StringBuilder();

                foreach (var sourceFile in updateFiles)
                {
                    // 计算相对路径，支持子目录结构
                    var relativePath = Path.GetRelativePath(updateDir, sourceFile);
                    var targetFile = Path.Combine(currentDir, relativePath);
                    var targetDir = Path.GetDirectoryName(targetFile);
                    var backupFile = targetFile + ".bak";

                    // 如果目标文件在子目录中，确保目录存在
                    if (!string.IsNullOrEmpty(targetDir) && targetDir != currentDir)
                    {
                        updateCommands.AppendLine($"if not exist \"{targetDir}\" mkdir \"{targetDir}\"");
                    }

                    // 备份旧文件
                    backupCommands.AppendLine($"if exist \"{targetFile}\" (");
                    backupCommands.AppendLine($"    copy \"{targetFile}\" \"{backupFile}\" > nul 2>&1");
                    backupCommands.AppendLine($")");

                    // 复制新文件
                    updateCommands.AppendLine($"copy /Y \"{sourceFile}\" \"{targetFile}\" > nul 2>&1");
                    updateCommands.AppendLine($"if %errorlevel% neq 0 set UPDATE_FAILED=1");

                    // 恢复备份（失败时）
                    restoreCommands.AppendLine($"if exist \"{backupFile}\" (");
                    restoreCommands.AppendLine($"    copy /Y \"{backupFile}\" \"{targetFile}\" > nul 2>&1");
                    restoreCommands.AppendLine($")");

                    // 清理备份
                    cleanupCommands.AppendLine($"del \"{backupFile}\" > nul 2>&1");
                }

                // 创建更新脚本（静默模式，无窗口）
                var scriptContent = $@"@echo off
chcp 65001 > nul 2>&1

REM 等待主程序退出
timeout /t 2 /nobreak > nul 2>&1

REM 备份旧文件
{backupCommands}

REM 复制新文件
set UPDATE_FAILED=0
{updateCommands}

if %UPDATE_FAILED% neq 0 (
    REM 更新失败，恢复备份
{restoreCommands}
    exit /b 1
)

REM 清理备份和临时文件
{cleanupCommands}
rd /s /q ""{updateDir}"" > nul 2>&1

REM 重启程序（使用当前运行的程序路径）
timeout /t 1 /nobreak > nul 2>&1
start """" ""{currentExePath}""

REM 删除自身
(goto) 2>nul & del ""%~f0"" > nul 2>&1
exit
";

                File.WriteAllText(updaterScript, scriptContent, System.Text.Encoding.UTF8);

#if DEBUG
                Debug.WriteLine($"[UpdateService] 创建更新脚本: {updaterScript}");
                Debug.WriteLine($"[UpdateService] 将更新 {updateFiles.Length} 个文件");
                Debug.WriteLine($"[UpdateService] 更新完成后将重启: {currentExePath}");
#endif

                // 启动更新脚本（隐藏窗口，静默执行）
                var processInfo = new ProcessStartInfo
                {
                    FileName = updaterScript,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                Process.Start(processInfo);

                // 退出当前程序
                System.Windows.Application.Current.Shutdown();
                return true;
            }
            catch (Exception ex)
            {
#if DEBUG
                Debug.WriteLine($"[UpdateService] 应用更新失败: {ex.Message}");
#else
                _ = ex; // 避免Release模式下的未使用警告
#endif
                return false;
            }
        }

        /// <summary>
        /// 比较版本号
        /// </summary>
        private static int CompareVersions(string version1, string version2)
        {
            try
            {
                var v1Parts = version1.Split('.');
                var v2Parts = version2.Split('.');
                var maxLength = Math.Max(v1Parts.Length, v2Parts.Length);

                for (int i = 0; i < maxLength; i++)
                {
                    var v1Part = i < v1Parts.Length ? int.Parse(v1Parts[i]) : 0;
                    var v2Part = i < v2Parts.Length ? int.Parse(v2Parts[i]) : 0;

                    if (v1Part > v2Part) return 1;
                    if (v1Part < v2Part) return -1;
                }

                return 0;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// 格式化文件大小
        /// </summary>
        public static string FormatFileSize(long bytes)
        {
            if (bytes == 0) return "未知大小";
            
            string[] sizes = { "B", "KB", "MB", "GB" };
            double len = bytes;
            int order = 0;

            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }

            return $"{len:0.##} {sizes[order]}";
        }

        /// <summary>
        /// 判断是否为压缩文件
        /// </summary>
        private static bool IsCompressedFile(string fileName)
        {
            var extension = Path.GetExtension(fileName).ToLowerInvariant();
            return extension == ".zip" || extension == ".7z" || extension == ".rar";
        }

        /// <summary>
        /// 解压压缩文件
        /// </summary>
        private static async Task ExtractCompressedFileAsync(string archivePath, string extractPath)
        {
            await Task.Run(() =>
            {
                var extension = Path.GetExtension(archivePath).ToLowerInvariant();
                
                try
                {
                    if (extension == ".zip")
                    {
                        // 使用 .NET 内置的 ZIP 解压
                        ZipFile.ExtractToDirectory(archivePath, extractPath, true);
#if DEBUG
                        Debug.WriteLine($"[UpdateService] ZIP 解压成功");
#endif
                    }
                    else if (extension == ".7z" || extension == ".rar")
                    {
                        // 7z 和 RAR 需要外部工具，这里尝试使用 7z.exe
                        var sevenZipPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "7z.exe");
                        
                        if (File.Exists(sevenZipPath))
                        {
                            var processInfo = new ProcessStartInfo
                            {
                                FileName = sevenZipPath,
                                Arguments = $"x \"{archivePath}\" -o\"{extractPath}\" -y",
                                UseShellExecute = false,
                                CreateNoWindow = true,
                                RedirectStandardOutput = true,
                                RedirectStandardError = true
                            };

                            using (var process = Process.Start(processInfo))
                            {
                                process?.WaitForExit();
                                
                                if (process?.ExitCode == 0)
                                {
#if DEBUG
                                    Debug.WriteLine($"[UpdateService] 7z/RAR 解压成功");
#endif
                                }
                                else
                                {
#if DEBUG
                                    Debug.WriteLine($"[UpdateService] 7z/RAR 解压失败，退出码: {process?.ExitCode}");
#endif
                                    throw new Exception($"解压失败，退出码: {process?.ExitCode}");
                                }
                            }
                        }
                        else
                        {
#if DEBUG
                            Debug.WriteLine($"[UpdateService] 未找到 7z.exe，无法解压 {extension} 格式");
#endif
                            throw new FileNotFoundException("未找到 7z.exe，无法解压此格式的压缩包");
                        }
                    }
                }
                catch (Exception ex)
                {
#if DEBUG
                    Debug.WriteLine($"[UpdateService] 解压失败: {ex.Message}");
#else
                    _ = ex; // 避免Release模式下的未使用警告
#endif
                    throw;
                }
            });
        }
    }
}

