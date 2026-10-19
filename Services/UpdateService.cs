using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
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
        public string ReleaseNotes { get; set; } = "";
        
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
        // 唯一永久升级地址：只保留 pan.019890311.xyz，客户端不再内置任何 IP 或其它域名镜像。
        // 网站版本策略下发时覆盖该地址；下发为空或失败时始终回落到这个永久域名。
        public const string PermanentDownloadBaseUrl = "https://pan.019890311.xyz/raw";

        private static readonly object DownloadEndpointGate = new();
        private static string[] _downloadBaseUrls = new[] { PermanentDownloadBaseUrl };
        private static string _currentBaseUrl = PermanentDownloadBaseUrl;

        private static string LATEST_VERSION_URL => _currentBaseUrl + "/latest.txt";
        private static readonly string[] VERSION_DIR_PREFIXES = new[] { "v", "V" };
        private static readonly string[] VERSION_LIST_FILES = new[] { "versions.txt" };
        
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
        
        private static readonly UpdateEndpointSelector EndpointSelector = new();
        private static HttpClient _httpClient = CreateDefaultHttpClient();
        private const int RollbackRequestTimeoutSeconds = 6;

        // 升级检查的时限保护：整体检查与单地址探测都有上限，避免长时间卡在某个地址上无反馈。
        private const int DefaultUpdateCheckOverallTimeoutSeconds = 20;
        private const int DefaultHealthProbeOverallTimeoutSeconds = 8;
        private const int DefaultHealthProbeTimeoutSeconds = 4;
        private static int _updateCheckOverallTimeoutSeconds = DefaultUpdateCheckOverallTimeoutSeconds;
        private static int _healthProbeOverallTimeoutSeconds = DefaultHealthProbeOverallTimeoutSeconds;
        private static int _healthProbeTimeoutSeconds = DefaultHealthProbeTimeoutSeconds;

        // 单次 latest.txt/files.txt 请求的超时（内部实现共用同一个值）。
        private const int MetadataRequestTimeoutSeconds = 8;
        
        // 保存最新检查到的版本信息（用于显示更新提示）
        private static VersionInfo? _lastCheckedVersionInfo = null;

        static UpdateService()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            // 强制启用 TLS 1.2 和 TLS 1.3（兼容 Cloudflare SSL）
            System.Net.ServicePointManager.SecurityProtocol =
                System.Net.SecurityProtocolType.Tls12 |
                System.Net.SecurityProtocolType.Tls13;

        }

        private static HttpClient CreateDefaultHttpClient()
        {
            var client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", "Canvas-Cast-Updater");
            client.Timeout = TimeSpan.FromMinutes(5);
            return client;
        }

        /// <summary>测试注入用：替换内部 HttpClient（传 null 恢复默认）。</summary>
        internal static void SetHttpClientForTests(HttpClient? client)
        {
            _httpClient = client ?? CreateDefaultHttpClient();
        }

        /// <summary>测试注入用：缩短升级检查各段超时（传 null 恢复默认值），保证测试快速且确定。</summary>
        internal static void SetTimeoutsForTests(int? updateCheckOverallSeconds = null, int? healthProbeOverallSeconds = null, int? healthProbeSeconds = null)
        {
            _updateCheckOverallTimeoutSeconds = updateCheckOverallSeconds ?? DefaultUpdateCheckOverallTimeoutSeconds;
            _healthProbeOverallTimeoutSeconds = healthProbeOverallSeconds ?? DefaultHealthProbeOverallTimeoutSeconds;
            _healthProbeTimeoutSeconds = healthProbeSeconds ?? DefaultHealthProbeTimeoutSeconds;
        }

        /// <summary>测试注入用：清空“最近一次检查到的版本”缓存，避免静态状态串场。</summary>
        internal static void ClearLastCheckedVersionInfoForTests()
        {
            _lastCheckedVersionInfo = null;
        }
        
        /// <summary>
        /// 获取最后一次检查到的版本信息（如果有新版本）
        /// </summary>
        public static VersionInfo? GetLastCheckedVersionInfo() => _lastCheckedVersionInfo;

        /// <summary>
        /// 最近一次升级检查是否所有地址都不可达。
        /// true 表示没有任何地址返回过响应（网络/服务器问题），false 表示“已是最新版本”这类正常结论。
        /// </summary>
        public static bool LastCheckFailedToReachAnyEndpoint { get; private set; }

        public static IReadOnlyList<string> GetDownloadBaseUrls()
        {
            return GetDownloadBaseUrlsSnapshot();
        }

        public static void SetDownloadBaseUrls(IEnumerable<string>? baseUrls)
        {
            var normalized = (baseUrls ?? Array.Empty<string>())
                .Select(NormalizeDownloadBaseUrl)
                .Where(url => !string.IsNullOrEmpty(url))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            lock (DownloadEndpointGate)
            {
                // 空列表不再关闭升级链路，而是回到唯一永久地址，保证客户端始终有一个可用升级源。
                var effective = normalized.Length > 0 ? normalized : new[] { PermanentDownloadBaseUrl };
                _downloadBaseUrls = effective;
                _currentBaseUrl = effective[0];
            }

            // 地址集合变了，之前的健康记录不再适用。
            EndpointSelector.Reset();
        }

        private static string[] GetDownloadBaseUrlsSnapshot()
        {
            lock (DownloadEndpointGate)
            {
                return _downloadBaseUrls.ToArray();
            }
        }

        /// <summary>按“最近可用地址优先、失败地址靠后”的顺序取候选地址快照。</summary>
        internal static IReadOnlyList<string> GetOrderedDownloadBaseUrlsSnapshot()
        {
            return EndpointSelector.Order(GetDownloadBaseUrlsSnapshot());
        }

        private static string NormalizeDownloadBaseUrl(string? value)
        {
            if (string.IsNullOrWhiteSpace(value) || !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri))
            {
                return string.Empty;
            }

            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            {
                return string.Empty;
            }

            return uri.AbsoluteUri.TrimEnd('/');
        }

        /// <summary>
        /// 获取可回退版本列表（仅返回小于等于当前版本且存在 files.txt 的版本）
        /// </summary>
        public static async Task<List<string>> GetRollbackVersionsAsync()
        {
            var currentVersion = GetCurrentVersion();
            var versions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // 尝试从版本清单文件中提取版本
            foreach (var listFile in VERSION_LIST_FILES)
            {
                var listLoaded = false;
                var baseUrls = GetDownloadBaseUrlsSnapshot();
                for (int i = 0; i < baseUrls.Length; i++)
                {
                    try
                    {
                        var url = $"{baseUrls[i]}/{listFile}";
                        var content = await TryGetStringWithTimeoutAsync(url, RollbackRequestTimeoutSeconds);
                        if (string.IsNullOrWhiteSpace(content))
                        {
                            continue;
                        }

                        foreach (var v in ExtractVersions(content))
                        {
                            versions.Add(v);
                        }

                        // 同一份清单在多个镜像通常一致，拿到一份即可
                        listLoaded = true;
                        break;
                    }
                    catch
                    {
                        // 单个地址失败不影响整体
                    }
                }

                if (listLoaded)
                {
                    // 优先读取 next list file
                    continue;
                }
            }

            // latest 兜底
            var latestBaseUrls = GetDownloadBaseUrlsSnapshot();
            for (int i = 0; i < latestBaseUrls.Length; i++)
            {
                try
                {
                    var latest = await TryGetStringWithTimeoutAsync($"{latestBaseUrls[i]}/latest.txt", RollbackRequestTimeoutSeconds);
                    var normalized = NormalizeVersion(latest);
                    if (!string.IsNullOrEmpty(normalized))
                    {
                        versions.Add(normalized);
                        break;
                    }
                }
                catch
                {
                    // 忽略
                }
            }

            // 列表阶段只做版本过滤，不预检 files.txt。
            // files.txt 在用户点击回退时，通过 GetVersionInfoAsync/GetUpdateFilesListAsync 再校验。
            var result = versions
                .Where(version => CompareVersions(version, currentVersion) < 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(v => v, Comparer<string>.Create(CompareVersions))
                .ToList();

            return result;
        }

        /// <summary>
        /// 获取指定版本的更新信息（用于手动升级/回退）
        /// </summary>
        public static async Task<VersionInfo?> GetVersionInfoAsync(string version)
        {
            var normalized = NormalizeVersion(version);
            if (string.IsNullOrEmpty(normalized))
            {
                return null;
            }

            var files = await GetUpdateFilesListAsync(normalized);
            if (files.Count == 0)
            {
                return null;
            }

            return new VersionInfo
            {
                Version = normalized,
                Files = files,
                ReleaseNotes = (await GetVersionReadmeAsync(normalized)) ?? ""
            };
        }

        /// <summary>
        /// 获取指定版本的更新说明（read.txt）
        /// </summary>
        public static async Task<string?> GetVersionReadmeAsync(string version)
        {
            var normalized = NormalizeVersion(version);
            if (string.IsNullOrEmpty(normalized))
            {
                return null;
            }

            foreach (var baseUrl in GetOrderedDownloadBaseUrlsSnapshot())
            {
                foreach (var url in BuildReadmeUrls(baseUrl, normalized))
                {
                    var content = await TryGetStringWithTimeoutAsync(url, RollbackRequestTimeoutSeconds);
                    if (!string.IsNullOrWhiteSpace(content))
                    {
                        EndpointSelector.MarkSuccess(baseUrl);
                        return content;
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// 获取当前应用程序版本（最佳实践：优先产品版本 Version/InformationalVersion）
        /// </summary>
        public static string GetCurrentVersion()
        {
            try
            {
                // 优先使用产品版本（通常由 .csproj 的 <Version> 生成）
                var assembly = System.Reflection.Assembly.GetEntryAssembly()
                    ?? System.Reflection.Assembly.GetExecutingAssembly();

                var infoVersionAttr = assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
                    .FirstOrDefault() as System.Reflection.AssemblyInformationalVersionAttribute;

                if (infoVersionAttr != null && !string.IsNullOrWhiteSpace(infoVersionAttr.InformationalVersion))
                {
                    // 去掉可能附带的 +commit/hash 等元数据
                    var versionStr = infoVersionAttr.InformationalVersion.Split('+')[0].Trim();
                    if (!string.IsNullOrWhiteSpace(versionStr))
                    {
                        return versionStr;
                    }
                }

                // 次优先：文件版本（Windows 文件属性）
                var fileVersionAttr = assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyFileVersionAttribute), false)
                    .FirstOrDefault() as System.Reflection.AssemblyFileVersionAttribute;
                if (fileVersionAttr != null && !string.IsNullOrWhiteSpace(fileVersionAttr.Version))
                {
                    return fileVersionAttr.Version.Trim();
                }

                // 最后回退：程序集版本（更偏二进制兼容，不建议作为更新判断主键）
                var assemblyVersion = assembly.GetName().Version;
                if (assemblyVersion != null)
                {
                    // 支持3位或4位版本号（例如：5.3.5 或 5.8.6.3）
                    if (assemblyVersion.Revision > 0)
                    {
                        return $"{assemblyVersion.Major}.{assemblyVersion.Minor}.{assemblyVersion.Build}.{assemblyVersion.Revision}";
                    }
                    else
                    {
                        return $"{assemblyVersion.Major}.{assemblyVersion.Minor}.{assemblyVersion.Build}";
                    }
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
            LastCheckFailedToReachAnyEndpoint = false;

            // 候选地址按“最近可用优先、失败地址靠后”排序，避免每次都从卡住的地址开始。
            var candidates = GetOrderedDownloadBaseUrlsSnapshot();
            if (candidates.Count == 0)
            {
                LastCheckFailedToReachAnyEndpoint = true;
                return null;
            }

            // 整个检查（含健康探测）共用一个硬上限，保证手动“检查升级”一定能在有限时间内给出反馈。
            var overallDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(_updateCheckOverallTimeoutSeconds);

            // 先并行探测各地址的 latest.txt，抢到第一个可用地址；这样不会串行卡死在单个地址上。
            var healthyBaseUrl = await ResolveHealthyBaseUrlAsync(candidates, CancellationToken.None);

            var attemptOrder = new List<string>();
            if (!string.IsNullOrEmpty(healthyBaseUrl))
            {
                attemptOrder.Add(healthyBaseUrl);
            }

            attemptOrder.AddRange(candidates.Where(url =>
                !string.Equals(url, healthyBaseUrl, StringComparison.OrdinalIgnoreCase)));

            // 每个地址的超时时间（秒）
            var timeouts = new[] { 12, 8, 8 };
            var reachedAnyEndpoint = false;

            for (int i = 0; i < attemptOrder.Count; i++)
            {
                if (DateTime.UtcNow >= overallDeadline)
                {
                    break;
                }

                var baseUrl = attemptOrder[i];

                // 每个地址重试2次
                for (int retry = 0; retry < 2; retry++)
                {
                    var remaining = overallDeadline - DateTime.UtcNow;
                    if (remaining <= TimeSpan.Zero)
                    {
                        break;
                    }

                    // 单地址超时同时受整体预算约束，避免重试把总耗时放大到不可控。
                    var plannedTimeout = i < timeouts.Length ? timeouts[i] : 8;
                    var timeout = Math.Max(1, Math.Min(plannedTimeout, (int)Math.Ceiling(remaining.TotalSeconds)));
                    var timeoutBudget = TimeSpan.FromSeconds(timeout);
                    var attemptStartedAt = DateTime.UtcNow;

                    try
                    {
                        _currentBaseUrl = baseUrl;
                        // 为每个地址设置独立超时
                        using (var cts = new CancellationTokenSource(timeoutBudget))
                        {
                            var (result, reachable) = await CheckForUpdatesInternalAsync(cts.Token);

                            if (!reachable)
                            {
                                // 该地址没有任何有效响应（超时/连不上/没有 latest.txt）：进入冷却，换下一个地址。
                                EndpointSelector.MarkFailure(baseUrl);

                                // 已经等满本次请求的实际预算说明是“卡住”而不是“快速失败”，重试同一个地址没有意义。
                                var effectiveBudget = TimeSpan.FromSeconds(Math.Min(timeout, MetadataRequestTimeoutSeconds));
                                var stalled = DateTime.UtcNow - attemptStartedAt >= effectiveBudget - TimeSpan.FromSeconds(1);
                                if (retry == 1 || stalled)
                                    break;

                                await Task.Delay(500);
                                continue;
                            }

                            // 成功获取到结果（无论是有新版本还是已是最新版本）
                            EndpointSelector.MarkSuccess(baseUrl);
                            reachedAnyEndpoint = true;
                            return result;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        EndpointSelector.MarkFailure(baseUrl);

                        // 如果是最后一次重试，跳出重试循环，尝试下一个地址
                        if (retry == 1)
                            break;

                        // 否则短暂等待后重试当前地址
                        await Task.Delay(500);
                    }
                    catch (Exception)
                    {
                        EndpointSelector.MarkFailure(baseUrl);
                        // 网络错误，跳出重试循环，尝试下一个地址
                        break;
                    }
                }
            }

            // 所有地址都失败：调用方可据此区分“已是最新版本”和“网络/服务器不可达”。
            LastCheckFailedToReachAnyEndpoint = !reachedAnyEndpoint;
            return null;
        }

        /// <summary>
        /// 并行探测候选地址的 latest.txt，返回第一个可用地址；全部失败返回 null。
        /// 单地址时直接返回该地址，避免额外探测开销。
        /// </summary>
        private static async Task<string?> ResolveHealthyBaseUrlAsync(IReadOnlyList<string> candidates, CancellationToken cancellationToken)
        {
            if (candidates.Count == 0)
            {
                return null;
            }

            if (candidates.Count == 1)
            {
                return candidates[0];
            }

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linked.CancelAfter(TimeSpan.FromSeconds(_healthProbeOverallTimeoutSeconds));
            var token = linked.Token;

            var probes = candidates
                .Select(async baseUrl =>
                {
                    var latest = await TryGetStringWithTimeoutAsync($"{baseUrl}/latest.txt", _healthProbeTimeoutSeconds, token);
                    return (baseUrl, version: NormalizeVersion(latest ?? string.Empty));
                })
                .ToList();

            while (probes.Count > 0)
            {
                var finished = await Task.WhenAny(probes);
                probes.Remove(finished);

                (string baseUrl, string? version) probe;
                try
                {
                    probe = await finished;
                }
                catch
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(probe.version))
                {
                    EndpointSelector.MarkSuccess(probe.baseUrl);
                    return probe.baseUrl;
                }

                if (!token.IsCancellationRequested)
                {
                    EndpointSelector.MarkFailure(probe.baseUrl);
                }
            }

            return null;
        }

        /// <summary>
        /// 检查是否有新版本（内部实现）。
        /// 返回 (版本信息, 是否读到了该地址的 latest.txt)：
        /// 第二个值用于区分“确实已是最新版本”和“该地址根本连不上/没有 latest.txt”，
        /// 否则超时会被当作“已是最新版本”，UI 就会给出错误反馈。
        /// </summary>
        private static async Task<(VersionInfo? versionInfo, bool reachable)> CheckForUpdatesInternalAsync(CancellationToken cancellationToken = default)
        {
            // 下载 latest.txt 获取最新版本号
            var latestVersion = await TryGetStringWithTimeoutAsync(LATEST_VERSION_URL, MetadataRequestTimeoutSeconds, cancellationToken);
            if (string.IsNullOrWhiteSpace(latestVersion))
            {
                return (null, false);
            }
            latestVersion = NormalizeVersion(latestVersion);

            // 验证版本号格式（支持3位或4位版本号，如 5.3.5 或 5.8.6.3）
            if (string.IsNullOrEmpty(latestVersion))
            {
                return (null, false);
            }

            // 比较版本号
            var currentVersion = GetCurrentVersion();

            if (CompareVersions(latestVersion, currentVersion) > 0)
            {
                // 获取更新文件列表
                var files = await GetUpdateFilesListAsync(latestVersion, cancellationToken);
                
                var versionInfo = new VersionInfo
                {
                    Version = latestVersion,
                    Files = files,
                    ReleaseNotes = (await GetVersionReadmeAsync(latestVersion)) ?? ""
                };
                
                // 保存版本信息供UI使用
                _lastCheckedVersionInfo = versionInfo;
                
                return (versionInfo, true);
            }
            
            // 没有新版本，清除缓存
            _lastCheckedVersionInfo = null;
            return (null, true);
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
        private static async Task<List<UpdateFileInfo>> GetUpdateFilesListAsync(string version, CancellationToken cancellationToken = default)
        {
            var files = new List<UpdateFileInfo>();
            
            try
            {
                // 优先尝试从服务器获取文件列表（同样按“最近可用地址优先”排序）
                string? filesList = null;
                var baseUrls = GetOrderedDownloadBaseUrlsSnapshot();
                foreach (var baseUrl in baseUrls)
                {
                    foreach (var filesListUrl in BuildFilesListUrls(baseUrl, version))
                    {
                        filesList = await TryGetStringWithTimeoutAsync(filesListUrl, MetadataRequestTimeoutSeconds, cancellationToken);
                        if (!string.IsNullOrWhiteSpace(filesList))
                        {
                            _currentBaseUrl = baseUrl;
                            EndpointSelector.MarkSuccess(baseUrl);
                            break;
                        }
                    }
                    if (!string.IsNullOrWhiteSpace(filesList))
                    {
                        break;
                    }
                }

                if (string.IsNullOrWhiteSpace(filesList))
                {
                    throw new HttpRequestException("files.txt not found");
                }

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
                files = await DiscoverFilesAsync(version, cancellationToken);
            }
            catch (Exception)
            {
                // 其他异常（超时、格式错误等），尝试自动发现文件
                files = await DiscoverFilesAsync(version, cancellationToken);
            }
            
            return files;
        }

        /// <summary>
        /// 自动发现版本文件夹中的文件（按优先级尝试地址，找到文件后立即返回）
        /// </summary>
        private static async Task<List<UpdateFileInfo>> DiscoverFilesAsync(string version, CancellationToken cancellationToken = default)
        {
            var files = new List<UpdateFileInfo>();
            
            // 过滤掉通配符模式（无法通过HTTP HEAD请求检查）
            var concreteFileNames = COMMON_UPDATE_FILES
                .Where(f => !f.Contains("*"))
                .ToList();
            
            // 按健康优先顺序尝试下载地址，一旦找到文件就立即返回
            var baseUrls = GetOrderedDownloadBaseUrlsSnapshot();
            for (int i = 0; i < baseUrls.Count; i++)
            {
                // 升级检查的整体预算用尽时不再继续探测，避免把反馈时间拖长。
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                var baseUrl = baseUrls[i];
                var basePaths = BuildVersionBasePaths(baseUrl, version);
                var addressHasError = false;
                
                foreach (var basePath in basePaths)
                {
                    // 尝试发现常见文件
                    foreach (var fileName in concreteFileNames)
                    {
                        var fileUrl = basePath + fileName;
                        try
                        {
                            // 使用HEAD请求检查文件是否存在（设置超时，并受升级检查整体预算约束）
                            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                            using (var request = new HttpRequestMessage(HttpMethod.Head, fileUrl))
                            {
                                cts.CancelAfter(TimeSpan.FromSeconds(10));
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
                }
                
                // 如果这个地址有网络错误，记录并继续尝试下一个地址
                if (addressHasError && files.Count == 0)
                {
                    EndpointSelector.MarkFailure(baseUrl);
                    continue;
                }
                
                // 如果在这个地址找到了文件，立即返回（不再尝试其他地址）
                // 下载时会自动轮询多个地址
                if (files.Count > 0)
                {
                    EndpointSelector.MarkSuccess(baseUrl);
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
            return $"{baseUrl}/v{version}/{fileName}";
        }

        /// <summary>
        /// 从多个地址尝试下载单个文件（带轮询）
        /// </summary>
        /// <returns>返回 (是否成功, 下载的字节数)</returns>
        private static async Task<(bool success, long bytesDownloaded)> DownloadFileWithMultiSourceAsync(string fileName, string version, string filePath, long totalBytes, long currentDownloadedBytes, IProgress<(long, long)>? progress)
        {
            // 尝试所有下载地址（最近可用地址优先，失败地址靠后）
            var baseUrls = GetOrderedDownloadBaseUrlsSnapshot();
            for (int i = 0; i < baseUrls.Count; i++)
            {
                var baseUrl = baseUrls[i];
                var downloadCandidates = BuildFileDownloadCandidateUrls(baseUrl, version, fileName);

                foreach (var downloadUrl in downloadCandidates)
                {
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
                                EndpointSelector.MarkSuccess(baseUrl);
                                return (true, fileDownloadedBytes); // 下载成功，返回下载的字节数
                            }
                        }
                        catch (Exception)
                        {
                        
                            // 如果是最后一次重试，跳出重试循环，尝试下一个地址
                            if (retry == 1)
                                break;
                        
                            // 否则等待1秒后重试当前地址
                            await Task.Delay(1000);
                        }
                    }
                }

                // 该地址的所有候选 URL 都失败，进入冷却期，下次优先使用其它地址。
                EndpointSelector.MarkFailure(baseUrl);
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
                    var sizeResolved = false;
                    var baseUrls = GetDownloadBaseUrlsSnapshot();
                    for (int i = 0; i < baseUrls.Length; i++)
                    {
                        var baseUrl = baseUrls[i];
                        var downloadCandidates = BuildFileDownloadCandidateUrls(baseUrl, versionInfo.Version, file.FileName);

                        foreach (var downloadUrl in downloadCandidates)
                        {
                            try
                            {
                                using (var request = new HttpRequestMessage(HttpMethod.Head, downloadUrl))
                                using (var response = await _httpClient.SendAsync(request))
                                {
                                    if (response.IsSuccessStatusCode)
                                    {
                                        file.FileSize = response.Content.Headers.ContentLength ?? 0;
                                        totalBytes += file.FileSize;
                                        sizeResolved = true;
                                        break; // 成功获取大小，跳出循环
                                    }
                                }
                            }
                            catch { }
                        }

                        if (sizeResolved)
                        {
                            break; // 已获取到该文件大小，不再累加其他镜像
                        }
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

                //  检查是否有独立的 EXE 文件（特殊处理）
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
                var skippedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    // 用户本地配置文件，升级时必须保留，避免用户偏好被更新包默认值覆盖
                    "config.json"
                };

                foreach (var sourceFile in updateFiles)
                {
                    // 计算相对路径，支持子目录结构
                    var relativePath = Path.GetRelativePath(updateDir, sourceFile);
                    if (skippedFiles.Contains(relativePath) ||
                        skippedFiles.Contains(Path.GetFileName(relativePath)))
                    {
                        continue;
                    }

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
                version1 = NormalizeVersion(version1) ?? version1;
                version2 = NormalizeVersion(version2) ?? version2;

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

        private static string? NormalizeVersion(string? version)
        {
            if (string.IsNullOrWhiteSpace(version))
            {
                return null;
            }

            var input = version.Trim();
            if (input.StartsWith("V", StringComparison.OrdinalIgnoreCase))
            {
                input = input.Substring(1);
            }

            if (!Regex.IsMatch(input, @"^\d+\.\d+\.\d+(\.\d+)?$"))
            {
                return null;
            }

            return input;
        }

        private static IEnumerable<string> ExtractVersions(string content)
        {
            var matches = Regex.Matches(content, @"V?(\d+\.\d+\.\d+(?:\.\d+)?)", RegexOptions.IgnoreCase);
            foreach (Match match in matches)
            {
                var normalized = NormalizeVersion(match.Value);
                if (!string.IsNullOrEmpty(normalized))
                {
                    yield return normalized;
                }
            }
        }

        private static List<string> BuildVersionBasePaths(string baseUrl, string version)
        {
            return VERSION_DIR_PREFIXES.Select(prefix => $"{baseUrl}/{prefix}{version}/").ToList();
        }

        private static List<string> BuildFilesListUrls(string baseUrl, string version)
        {
            var urls = new List<string>();

            // 兼容 v5.9.9.8 / V5.9.9.8
            urls.AddRange(VERSION_DIR_PREFIXES.Select(prefix => $"{baseUrl}/{prefix}{version}/files.txt"));
            // 兼容 5.9.9.8（无前缀）
            urls.Add($"{baseUrl}/{version}/files.txt");

            // 兼容大小写文件名差异
            urls.AddRange(VERSION_DIR_PREFIXES.Select(prefix => $"{baseUrl}/{prefix}{version}/Files.txt"));
            urls.Add($"{baseUrl}/{version}/Files.txt");

            return urls.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static List<string> BuildFileDownloadCandidateUrls(string baseUrl, string version, string fileName)
        {
            return VERSION_DIR_PREFIXES
                .Select(prefix => $"{baseUrl}/{prefix}{version}/{fileName}")
                .ToList();
        }

        private static List<string> BuildReadmeUrls(string baseUrl, string version)
        {
            var urls = new List<string>();

            urls.AddRange(VERSION_DIR_PREFIXES.Select(prefix => $"{baseUrl}/{prefix}{version}/read.txt"));
            urls.Add($"{baseUrl}/{version}/read.txt");
            urls.AddRange(VERSION_DIR_PREFIXES.Select(prefix => $"{baseUrl}/{prefix}{version}/Read.txt"));
            urls.Add($"{baseUrl}/{version}/Read.txt");

            return urls.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static async Task<bool> HasFilesListAsync(string version)
        {
            var baseUrls = GetDownloadBaseUrlsSnapshot();
            for (int i = 0; i < baseUrls.Length; i++)
            {
                foreach (var url in BuildFilesListUrls(baseUrls[i], version))
                {
                    try
                    {
                        using var request = new HttpRequestMessage(HttpMethod.Head, url);
                        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(RollbackRequestTimeoutSeconds));
                        using var response = await _httpClient.SendAsync(request, cts.Token);
                        if (response.IsSuccessStatusCode)
                        {
                            return true;
                        }

                        // 某些网盘镜像不支持 HEAD
                        if (response.StatusCode == System.Net.HttpStatusCode.MethodNotAllowed)
                        {
                            using var getRequest = new HttpRequestMessage(HttpMethod.Get, url);
                            using var getCts = new CancellationTokenSource(TimeSpan.FromSeconds(RollbackRequestTimeoutSeconds));
                            using var getResponse = await _httpClient.SendAsync(getRequest, HttpCompletionOption.ResponseHeadersRead, getCts.Token);
                            if (getResponse.IsSuccessStatusCode)
                            {
                                return true;
                            }
                        }
                    }
                    catch
                    {
                        // 忽略单个地址
                    }
                }
            }

            return false;
        }

        private static async Task<string?> TryGetStringWithTimeoutAsync(string url, int timeoutSeconds, CancellationToken externalToken = default)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
                cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                var bytes = await response.Content.ReadAsByteArrayAsync(cts.Token);
                if (bytes == null || bytes.Length == 0)
                {
                    return string.Empty;
                }

                return DecodeTextPayload(bytes);
            }
            catch
            {
                return null;
            }
        }

        private static string DecodeTextPayload(byte[] bytes)
        {
            var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
            try
            {
                return utf8.GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                // Fall through to GB18030 for legacy ANSI/GBK mirror content.
            }

            try
            {
                var gb18030 = Encoding.GetEncoding("GB18030");
                return gb18030.GetString(bytes);
            }
            catch
            {
                return Encoding.UTF8.GetString(bytes);
            }
        }
    }
}




