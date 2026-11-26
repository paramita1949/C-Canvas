using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ImageColorChanger.Services.OnlineScores
{
    // 当前官方公告/原客户端记录的候选，非永久可用保证。只保存实际下载成功地址。
    public sealed class ScoreDownloadHostCatalog
    {
        // 持久化契约不依赖私有成员或构造参数名称，发布保护后仍能读取同一份缓存。
        private sealed class VerifiedHost
        {
            public VerifiedHost() { }
            [JsonPropertyName("Url")]
            public string Url { get; set; }
            [JsonPropertyName("At")]
            public DateTimeOffset At { get; set; }
        }
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("CanvasCast.ScoreDownloadHosts.v1");
        private readonly IReadOnlyList<Uri> _builtIns;
        private readonly string _path;
        private readonly Dictionary<string, VerifiedHost> _verified = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _sync = new();

        public static IReadOnlyList<Uri> BuiltInHosts()
        {
            // 来源：官方Web /hosts与本机诗歌本公告缓存，2026-11-26逐条复查。
            // Web六条已实测完整JPG；桌面当前公告是.xyz，不再把旧.icu当当前线路。
            // 桌面.xyz当前证书过期，仅保留待官方修复后重测；绝不跳过证书校验。
            var hosts = new List<Uri>();
            foreach (int n in new[] { 11, 23, 31, 32, 33, 34 })
                hosts.Add(new Uri($"https://{n}.400400.icu:9003/api/"));
            foreach (int n in new[] { 11, 23, 31, 32, 33, 34 })
                hosts.Add(new Uri($"https://{n}.hxoihyia.xyz:9013/api/"));
            // 历史候选排在当前公告之后；当前公共DNS返回NXDOMAIN。
            foreach (int n in new[] { 11, 23, 31, 32, 33, 34 })
                hosts.Add(new Uri($"https://{n}.hxoihyia.icu:9013/api/"));
            return hosts;
        }

        public ScoreDownloadHostCatalog(IReadOnlyList<Uri> builtIns, string path)
        {
            _builtIns = (builtIns ?? Array.Empty<Uri>()).Select(Normalize).Where(x => x != null).Distinct().Take(32).ToArray();
            _path = path;
            if (path == null || !File.Exists(path)) return;
            try
            {
                if (new FileInfo(path).Length > 64 * 1024) return;
                // 不能从可篡改的明文缓存新增设备头收件地址；DPAPI同时校验缓存完整性。
                byte[] clear = ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy, DataProtectionScope.CurrentUser);
                try
                {
                    foreach (var item in JsonSerializer.Deserialize<List<VerifiedHost>>(clear) ?? new())
                    {
                        if (item == null || item.At < DateTimeOffset.UtcNow.AddDays(-7) || item.At > DateTimeOffset.UtcNow.AddMinutes(5) ||
                            !Uri.TryCreate(item.Url, UriKind.Absolute, out var uri) || Normalize(uri) is not Uri host) continue;
                        item.Url = host.AbsoluteUri;
                        _verified[host.AbsoluteUri] = item;
                        if (_verified.Count >= 32) break;
                    }
                }
                finally { CryptographicOperations.ZeroMemory(clear); }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException || ex is CryptographicException) { }
        }

        internal static Uri Normalize(Uri host)
        {
            if (host == null || !host.IsAbsoluteUri || host.Scheme != Uri.UriSchemeHttps || host.UserInfo.Length != 0 ||
                host.Query.Length != 0 || host.Fragment.Length != 0) return null;
            // URL本身包含/api；不添加第二段/api，也不将域名改写成裸IP绕过TLS。
            return new Uri(host.AbsoluteUri.TrimEnd('/') + "/");
        }

        public IReadOnlyList<Uri> GetHosts()
        {
            lock (_sync)
                return _verified.Values.Where(x => x.At >= DateTimeOffset.UtcNow.AddDays(-7))
                    .OrderByDescending(x => x.At).Select(x => new Uri(x.Url)).Concat(_builtIns).Distinct().Take(64).ToArray();
        }

        public void RecordSuccess(Uri host)
        {
            host = Normalize(host);
            if (host == null) return;
            lock (_sync)
            {
                _verified[host.AbsoluteUri] = new VerifiedHost { Url = host.AbsoluteUri, At = DateTimeOffset.UtcNow };
                if (_verified.Count > 32) _verified.Remove(_verified.OrderBy(x => x.Value.At).First().Key);
                if (_path == null) return;
                string temp = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(_path));
                    byte[] clear = JsonSerializer.SerializeToUtf8Bytes(_verified.Values.ToArray());
                    try { File.WriteAllBytes(temp, ProtectedData.Protect(clear, Entropy, DataProtectionScope.CurrentUser)); }
                    finally { CryptographicOperations.ZeroMemory(clear); }
                    File.Move(temp, _path, true);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is CryptographicException) { }
                finally { try { if (File.Exists(temp)) File.Delete(temp); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
            }
        }
    }
}
