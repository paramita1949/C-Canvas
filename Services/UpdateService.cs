using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
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
        // 多个下载地址（按优先级排序 - 优先使用代理服务器）
        private static readonly string[] DOWNLOAD_BASE_URLS = new[]
        {
            "http://106.14.145.43:23414",         // 优先2（代理服务器 - HTTP 备用）
            "http://106.14.145.43:23413",         // 优先3（代理服务器 - HTTP 备用）
            "http://139.159.157.28:45852",       // 优先4（代理服务器 - HTTP 备用）
            "http://139.159.157.28:45853",       // 优先5（代理服务器 - HTTP 备用）
            "https://pan.019890311.xyz/raw",      // 优先2（备用 - 特殊：文件在/raw路径下）
            "https://pan.jiucai.org.cn/raw",      // 优先3（备用 - 特殊：文件在/raw路径下）
            "https://pan.xian.edu.kg/raw",         // 优先4（备用 - 特殊：文件在/raw路径下）
            "https://updata.jiucai.org.cn",       // 优先5（备用）
            "https://updata.pan.xian.edu.kg",     // 优先6（备用）
            "https://canvas.019890311.xyz",       // 优先7（备用）
            "https://pub-64a8ccc2b61d44e2a8ebb27ee3f2f35c.r2.dev" // 优先8（备用）
        };

        // 当前使用的基础URL（动态选择）
        private static string _currentBaseUrl = DOWNLOAD_BASE_URLS[0];

        private static string LATEST_VERSION_URL => _currentBaseUrl + "/latest.txt";
        private static string FILES_LIST_URL_TEMPLATE => _currentBaseUrl + "/v{version}/files.txt";
        
        // 尝试自动发现的文件名列表（如果 files.txt 不存在时使用）
        // 优先级：压缩包 > DLL > 资源文件 > 配置文件
        private static readonly string[] COMMON_UPDATE_FILES = new[]
        {
            // 压缩包（优先）
            "update.zip",
            "update.7z",
            "update.rar",
            "updata.zip",
            "updata.7z",
            "updata.rar",
            "Canvas-Update.zip",
            "Canvas-Update.7z",
            "Canvas-Update.rar",

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
        
        // 保存最新检查到的版本信息（用于显示更新提示）
        private static VersionInfo? _lastCheckedVersionInfo = null;

        static UpdateService()
        {
            // 强制启用 TLS 1.2 和 TLS 1.3（兼容 Cloudflare SSL）
            System.Net.ServicePointManager.SecurityProtocol =
                System.Net.SecurityProtocolType.Tls12 |
                System.Net.SecurityProtocolType.Tls13;

            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Canvas-Cast-Updater");
            _httpClient.Timeout = TimeSpan.FromMinutes(5);
        }
        
        /// <summary>
        /// 获取最后一次检查到的版本信息（如果有新版本）
        /// </summary>
        public static VersionInfo? GetLastCheckedVersionInfo() => _lastCheckedVersionInfo;

        /// <summary>
        /// 获取当前应用程序版本（从程序集属性读取）
        /// </summary>
        public static string GetCurrentVersion()
        {
            try
            {
                // 从程序集版本属性获取（由 .csproj 中的 Version 定义）
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                var version = assembly.GetName().Version;
                
                if (version != null)
                {
                    // 支持3位或4位版本号（例如：5.3.5 或 5.8.6.3）
                    if (version.Revision > 0)
                    {
                        return $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
                    }
                    else
                    {
                        return $"{version.Major}.{version.Minor}.{version.Build}";
                    }
                }

                // 备用方案：尝试从 AssemblyInformationalVersion 获取
                var infoVersionAttr = assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
                    .FirstOrDefault() as System.Reflection.AssemblyInformationalVersionAttribute;
                
                if (infoVersionAttr != null && !string.IsNullOrEmpty(infoVersionAttr.InformationalVersion))
                {
                    // 移除可能的版本后缀（如 +hash）
                    var versionStr = infoVersionAttr.InformationalVersion.Split('+')[0];
                    return versionStr;
                }

                // 如果都失败，返回默认版本
                return "5.3.5";
            }
            catch
            {
                return "5.3.5";
            }
        }

        /// <summary>
        /// 检查是否有新版本（带重试机制和多地址切换）
        /// </summary>
        public static async Task<VersionInfo?> CheckForUpdatesAsync()
        {
            // 尝试所有下载地址（境外服务器，采用渐进式超时策略）
            Exception? lastException = null;

            // 每个地址的超时时间（秒）- 考虑境外网络延迟
            var timeouts = new[] { 30, 20, 20 };

            for (int i = 0; i < DOWNLOAD_BASE_URLS.Length; i++)
            {
                var baseUrl = DOWNLOAD_BASE_URLS[i];
                var timeout = timeouts[i];

                // 每个地址重试2次
                for (int retry = 0; retry < 2; retry++)
                {
                    try
                    {
                        _currentBaseUrl = baseUrl;
                        // 为每个地址设置独立超时
                        using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeout)))
                        {
                            var result = await CheckForUpdatesInternalAsync(cts.Token);

                            // 成功获取到结果（无论是有新版本还是已是最新版本）
                            return result;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        lastException = new TimeoutException($"请求超时({timeout}秒)");

                        // 如果是最后一次重试，跳出重试循环，尝试下一个地址
                        if (retry == 1)
                            break;

                        // 否则等待2秒后重试当前地址
                        await Task.Delay(2000);
                    }
                    catch (Exception ex)
                    {
                        lastException = ex;
                        // 网络错误，跳出重试循环，尝试下一个地址
                        break;
                    }
                }
            }

            // 所有地址都失败
            return null;
        }

        /// <summary>
        /// 检查是否有新版本（内部实现）
        /// </summary>
        private static async Task<VersionInfo?> CheckForUpdatesInternalAsync(CancellationToken cancellationToken = default)
        {
            // 下载 latest.txt 获取最新版本号
            var latestVersion = await _httpClient.GetStringAsync(LATEST_VERSION_URL, cancellationToken);
            latestVersion = latestVersion.Trim();

            // 验证版本号格式（支持3位或4位版本号，如 5.3.5 或 5.8.6.3）
            if (!Regex.IsMatch(latestVersion, @"^\d+\.\d+\.\d+(\.\d+)?$"))
            {
                return null;
            }

            // 比较版本号
            var currentVersion = GetCurrentVersion();

            if (CompareVersions(latestVersion, currentVersion) > 0)
            {
                // 获取更新文件列表
                var files = await GetUpdateFilesListAsync(latestVersion);
                
                var versionInfo = new VersionInfo
                {
                    Version = latestVersion,
                    Files = files
                };
                
                // 保存版本信息供UI使用
                _lastCheckedVersionInfo = versionInfo;
                
                return versionInfo;
            }
            
            // 没有新版本，清除缓存
            _lastCheckedVersionInfo = null;
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
                    var result = await operation();
                    return result;
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    
                    // 如果不是最后一次尝试，等待后重试
                    if (attempt < maxRetries)
                    {
                        await Task.Delay(retryDelayMs);
                    }
                }
            }

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
                    
                    // 如果不是最后一次尝试，等待后重试
                    if (attempt < maxRetries)
                    {
                        await Task.Delay(retryDelayMs);
                    }
                }
            }

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
                
                foreach (var fileName in fileNames)
                {
                    var trimmedName = fileName.Trim();
                    if (!string.IsNullOrEmpty(trimmedName))
                    {
                        files.Add(new UpdateFileInfo
                        {
                            FileName = trimmedName,
                            DownloadUrl = $"{_currentBaseUrl}/v{version}/{trimmedName}",
                            FileSize = 0
                        });
                    }
                }
            }
            catch (HttpRequestException)
            {
                // HTTP请求异常（网络错误、SSL错误等），尝试自动发现文件
                files = await DiscoverFilesAsync(version);
            }
            catch (Exception)
            {
                // 其他异常（超时、格式错误等），尝试自动发现文件
                files = await DiscoverFilesAsync(version);
            }
            
            return files;
        }

        /// <summary>
        /// 自动发现版本文件夹中的文件（按优先级尝试地址，找到文件后立即返回）
        /// </summary>
        private static async Task<List<UpdateFileInfo>> DiscoverFilesAsync(string version)
        {
            var files = new List<UpdateFileInfo>();
            
            // 过滤掉通配符模式（无法通过HTTP HEAD请求检查）
            var concreteFileNames = COMMON_UPDATE_FILES
                .Where(f => !f.Contains("*"))
                .ToList();
            
            // 按优先级尝试下载地址，一旦找到文件就立即返回
            for (int i = 0; i < DOWNLOAD_BASE_URLS.Length; i++)
            {
                var baseUrl = DOWNLOAD_BASE_URLS[i];
                var basePath = baseUrl.EndsWith("/raw") ? $"{baseUrl}/v{version}/" : $"{baseUrl}/v{version}/";
                
                var addressHasError = false;
                
                // 尝试发现常见文件
                foreach (var fileName in concreteFileNames)
                {
                    var fileUrl = basePath + fileName;
                    try
                    {
                        // 使用HEAD请求检查文件是否存在（设置超时）
                        using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
                        using (var request = new HttpRequestMessage(HttpMethod.Head, fileUrl))
                        {
                            var response = await _httpClient.SendAsync(request, cts.Token);
                            if (response.IsSuccessStatusCode)
                            {
                                files.Add(new UpdateFileInfo
                                {
                                    FileName = fileName,
                                    DownloadUrl = fileUrl,
                                    FileSize = response.Content.Headers.ContentLength ?? 0
                                });
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // 超时
                        addressHasError = true;
                        // 如果这个地址超时，跳过剩余文件，尝试下一个地址
                        break;
                    }
                    catch (HttpRequestException)
                    {
                        // HTTP请求异常（网络错误、SSL错误等）
                        addressHasError = true;
                        // 如果这个地址有网络问题，跳过剩余文件，尝试下一个地址
                        break;
                    }
                    catch (Exception)
                    {
                        // 其他异常
                        addressHasError = true;
                        // 如果这个地址有问题，跳过剩余文件，尝试下一个地址
                        break;
                    }
                }
                
                // 如果这个地址有网络错误，记录并继续尝试下一个地址
                if (addressHasError && files.Count == 0)
                {
                    continue;
                }
                
                // 如果在这个地址找到了文件，立即返回（不再尝试其他地址）
                // 下载时会自动轮询多个地址
                if (files.Count > 0)
                {
                    return files;
                }
            }
            
            // 所有地址都尝试完，未找到任何文件
            return files;
        }

        /// <summary>
        /// 根据基础URL构建文件下载地址
        /// </summary>
        private static string BuildFileDownloadUrl(string baseUrl, string version, string fileName)
        {
            // 处理特殊路径：/raw路径下的文件
            if (baseUrl.EndsWith("/raw"))
            {
                return $"{baseUrl}/v{version}/{fileName}";
            }
            else
            {
                return $"{baseUrl}/v{version}/{fileName}";
            }
        }

        /// <summary>
        /// 从多个地址尝试下载单个文件（带轮询）
        /// </summary>
        /// <returns>返回 (是否成功, 下载的字节数)</returns>
        private static async Task<(bool success, long bytesDownloaded)> DownloadFileWithMultiSourceAsync(string fileName, string version, string filePath, long totalBytes, long currentDownloadedBytes, IProgress<(long, long)>? progress)
        {
            // 尝试所有下载地址
            Exception? lastException = null;
            
            for (int i = 0; i < DOWNLOAD_BASE_URLS.Length; i++)
            {
                var baseUrl = DOWNLOAD_BASE_URLS[i];
                var downloadUrl = BuildFileDownloadUrl(baseUrl, version, fileName);
                
                // 每个地址重试2次
                for (int retry = 0; retry < 2; retry++)
                {
                    try
                    {
                        using (var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
                        {
                            // 如果文件不存在（404），尝试下一个地址
                            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                            {
                                break; // 跳出重试循环，尝试下一个地址
                            }
                            
                            response.EnsureSuccessStatusCode();

                            long fileDownloadedBytes = 0;
                            using (var contentStream = await response.Content.ReadAsStreamAsync())
                            using (var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                            {
                                var buffer = new byte[8192];
                                int bytesRead;

                                while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                                {
                                    await fileStream.WriteAsync(buffer, 0, bytesRead);
                                    fileDownloadedBytes += bytesRead;
                                    var newDownloadedBytes = currentDownloadedBytes + fileDownloadedBytes;
                                    progress?.Report((newDownloadedBytes, totalBytes));
                                }
                            }
                            
                            return (true, fileDownloadedBytes); // 下载成功，返回下载的字节数
                        }
                    }
                    catch (Exception ex)
                    {
                        lastException = ex;
                        
                        // 如果是最后一次重试，跳出重试循环，尝试下一个地址
                        if (retry == 1)
                            break;
                        
                        // 否则等待1秒后重试当前地址
                        await Task.Delay(1000);
                    }
                }
            }
            
            // 所有地址都失败
            return (false, 0);
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

                // 计算总大小（先获取每个文件的大小，尝试从多个地址获取）
                long totalBytes = 0;
                foreach (var file in versionInfo.Files)
                {
                    // 尝试从多个地址获取文件大小（使用HEAD请求，只获取响应头）
                    for (int i = 0; i < DOWNLOAD_BASE_URLS.Length; i++)
                    {
                        var baseUrl = DOWNLOAD_BASE_URLS[i];
                        var downloadUrl = BuildFileDownloadUrl(baseUrl, versionInfo.Version, file.FileName);
                        
                        try
                        {
                            using (var request = new HttpRequestMessage(HttpMethod.Head, downloadUrl))
                            using (var response = await _httpClient.SendAsync(request))
                            {
                                if (response.IsSuccessStatusCode)
                                {
                                    file.FileSize = response.Content.Headers.ContentLength ?? 0;
                                    totalBytes += file.FileSize;
                                    break; // 成功获取大小，跳出循环
                                }
                            }
                        }
                        catch { }
                    }
                }

                long downloadedBytes = 0;

                // 下载所有文件
                var successfulFiles = 0;
                var failedFiles = 0;
                
                foreach (var file in versionInfo.Files)
                {
                    var filePath = Path.Combine(tempDir, file.FileName);
                    
                    // 使用多地址轮询下载单个文件
                    var (downloadSuccess, bytesDownloaded) = await DownloadFileWithMultiSourceAsync(
                        file.FileName, 
                        versionInfo.Version, 
                        filePath, 
                        totalBytes, 
                        downloadedBytes, 
                        progress);
                    
                    if (!downloadSuccess)
                    {
                        failedFiles++;
                        continue;
                    }
                    
                    // 更新已下载字节数
                    downloadedBytes += bytesDownloaded;
                    successfulFiles++;

                    // 如果是压缩包，自动解压
                    if (IsCompressedFile(file.FileName))
                    {
                        await ExtractCompressedFileAsync(filePath, tempDir);
                        
                        // 删除压缩包文件
                        File.Delete(filePath);
                    }
                }
                
                // 如果没有任何文件成功下载，返回失败
                if (successfulFiles == 0)
                {
                    return null;
                }

                return tempDir; // 返回临时目录路径
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// 应用更新（可能重启程序或启动独立程序）
        /// 如果更新包含独立的 EXE 文件，则启动该文件；否则正常更新并重启
        /// </summary>
        public static bool ApplyUpdate(string updateDir)
        {
            try
            {
                if (!Directory.Exists(updateDir))
                {
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
                    return false;
                }

                // 🔧 检查是否有独立的 EXE 文件（特殊处理）
                var standaloneExeFiles = updateFiles
                    .Where(f => f.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    .Where(f => !Path.GetFileName(f).Equals("CanvasCast.exe", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                
                if (standaloneExeFiles.Count > 0)
                {
                    // 有独立的 EXE 文件，直接启动它
                    var exeToRun = standaloneExeFiles[0]; // 取第一个 EXE 文件

                    try
                    {
                        // 启动独立的 EXE 文件
                        var exeProcessInfo = new ProcessStartInfo
                        {
                            FileName = exeToRun,
                            UseShellExecute = true,
                            WorkingDirectory = Path.GetDirectoryName(exeToRun)
                        };

                        Process.Start(exeProcessInfo);

                        // 退出当前程序
                        System.Windows.Application.Current.Shutdown();
                        return true;
                    }
                    catch (Exception)
                    {
                        return false;
                    }
                }

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

                // 获取当前进程ID和EXE文件名
                var currentProcessId = Process.GetCurrentProcess().Id;
                var exeFileName = Path.GetFileName(currentExePath);

                // 创建更新脚本（静默模式，无窗口）
                var scriptContent = $@"@echo off
chcp 65001 > nul 2>&1

REM 等待主程序进程完全退出（最多等待30秒）
echo Waiting for process {currentProcessId} to exit...
set WAIT_COUNT=0
:WAIT_LOOP
tasklist /FI ""PID eq {currentProcessId}"" 2>nul | find ""{currentProcessId}"" >nul
if %errorlevel% equ 0 (
    if %WAIT_COUNT% lss 30 (
        timeout /t 1 /nobreak > nul 2>&1
        set /a WAIT_COUNT+=1
        goto WAIT_LOOP
    ) else (
        echo Process still running after 30 seconds, attempting update anyway...
    )
)

REM 额外等待确保文件句柄释放
timeout /t 1 /nobreak > nul 2>&1

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
            catch (Exception)
            {
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
                
                if (extension == ".zip")
                {
                    // 使用 .NET 内置的 ZIP 解压
                    ZipFile.ExtractToDirectory(archivePath, extractPath, true);
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
                            
                            if (process?.ExitCode != 0)
                            {
                                throw new Exception($"解压失败，退出码: {process?.ExitCode}");
                            }
                        }
                    }
                    else
                    {
                        throw new FileNotFoundException("未找到 7z.exe，无法解压此格式的压缩包");
                    }
                }
            });
        }
    }
}


