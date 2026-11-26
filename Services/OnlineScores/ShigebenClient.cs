using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ImageColorChanger.Services.OnlineScores
{
    public enum OnlineScoreSearchMode { Local, Name, Lyrics }

    // 外部 id 不进入本地 MediaFile 的主键域；bId 始终为伴奏标识。
    public sealed class OnlineScoreResult : INotifyPropertyChanged
    {
        public int Id { get; init; }
        public string Name { get; init; }
        public string BookName { get; init; }
        public int? BookId { get; init; }
        public int No { get; init; }
        public int AccompanimentId { get; init; }
        public int Matched { get; init; }
        // 站点曲目编号，不是搜索排序序号；缺失时留空，不编造编号。
        public string NumberLabel => No > 0 ? No.ToString(CultureInfo.InvariantCulture) : "";
        public string BookDisplayName => string.IsNullOrWhiteSpace(BookName) ? "歌本未解析" : BookName;
        public string Description => $"{BookName ?? "歌本未解析"} · {No} 号";
        private bool? _lyricsAvailable;
        public bool HasLyrics => _lyricsAvailable == true;
        internal void SetLyricsAvailability(bool available)
        {
            if (_lyricsAvailable == available) return;
            _lyricsAvailable = available;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasLyrics)));
        }
        public event PropertyChangedEventHandler PropertyChanged;
    }

    public sealed record ScoreTextPart(string Text, bool Highlight);
    public sealed class ScoreIntegrationException : Exception
    {
        public ScoreIntegrationException(string message) : base(message) { }
    }

    internal sealed class ScoreNodeTransportException : HttpRequestException
    {
        public ScoreNodeTransportException(int status) : base("下载节点 HTTP " + status, null, (System.Net.HttpStatusCode)status) { }
    }

    public interface IScoreRequestAuthorizer
    {
        Task AuthorizeAsync(HttpRequestMessage request, CancellationToken cancellationToken);
    }

    // 由站方认可的签名服务按实际请求签发短时请求头；不包含前端应用材料或设备 secret。
    // 签名服务只通过 HTTPS 调用，禁止重定向；凭据不写配置/日志/磁盘缓存。
    public sealed class BrokerScoreAuthorizer : IScoreRequestAuthorizer, IDisposable
    {
        private readonly Uri _endpoint;
        private readonly HttpClient _http = new(new HttpClientHandler { AllowAutoRedirect = false });
        private static readonly string[] Required = { "appid", "timestamp", "token", "X-Device-Id", "X-Device-Timestamp", "X-Device-Nonce", "X-Device-Sign" };
        public BrokerScoreAuthorizer(Uri endpoint)
        {
            if (endpoint.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(endpoint.UserInfo))
                throw new ScoreIntegrationException("签名服务必须是站方认可的 HTTPS 地址。");
            _endpoint = endpoint;
            _http.Timeout = TimeSpan.FromSeconds(12);
        }
        public async Task AuthorizeAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            byte[] body = request.Content == null ? Array.Empty<byte>() : await request.Content.ReadAsByteArrayAsync(cancellationToken);
            using var query = new HttpRequestMessage(HttpMethod.Post, _endpoint);
            query.Content = new StringContent(JsonSerializer.Serialize(new
            {
                method = request.Method.Method, uri = request.RequestUri.AbsoluteUri,
                bodySha256 = Convert.ToHexString(SHA256.HashData(body)).ToLowerInvariant()
            }), Encoding.UTF8, "application/json");
            using var response = await _http.SendAsync(query, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode) throw new ScoreIntegrationException("诗歌本签名服务暂不可用，请检查合法接入配置。");
            byte[] bytes = await ShigebenClient.ReadBoundedAsync(response.Content, 64 * 1024, cancellationToken);
            using var json = JsonDocument.Parse(bytes);
            var headers = ShigebenClient.RequireResult(json.RootElement, JsonValueKind.Object);
            if (!headers.TryGetProperty("headers", out headers) || headers.ValueKind != JsonValueKind.Object)
                throw new ScoreIntegrationException("签名服务响应缺少 headers。");
            var allowed = Required.Concat(new[] { "loginName", "Authorization" }).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var item in headers.EnumerateObject())
            {
                if (!allowed.Contains(item.Name) || item.Value.ValueKind != JsonValueKind.String) continue;
                string value = item.Value.GetString();
                if (value?.IndexOfAny(new[] { '\r', '\n' }) >= 0) throw new ScoreIntegrationException("签名服务返回无效请求头。");
                request.Headers.Remove(item.Name);
                if (!request.Headers.TryAddWithoutValidation(item.Name, value)) throw new ScoreIntegrationException("签名服务返回无效请求头。");
            }
            if (Required.Any(name => !request.Headers.TryGetValues(name, out var values) || values.All(string.IsNullOrWhiteSpace)))
                throw new ScoreIntegrationException("签名服务未提供完整应用/设备认证头。");
        }
        public void Dispose() => _http.Dispose();
    }

    public sealed class ShigebenClient : IDisposable
    {
        private readonly HttpClient _http;
        private readonly IScoreRequestAuthorizer _authorizer;
        private readonly Uri _api;
        private readonly Uri _downloadHost;
        private readonly bool _allowSessionDownload;
        private IReadOnlyList<Uri> _downloadHosts;
        private DateTimeOffset _downloadHostsLoaded;
        private readonly ScoreDownloadNodePolicy _nodePolicy;
        private readonly SemaphoreSlim _downloadGate = new(1, 1);
        private readonly SemaphoreSlim _lyricsHintGate = new(2, 2);
        private readonly Dictionary<int, (bool Available, DateTimeOffset Expires)> _lyricsHints = new();
        public const int MaxImageBytes = 20 * 1024 * 1024;

        public ShigebenClient(HttpClient http, IScoreRequestAuthorizer authorizer, Uri api, Uri downloadHost, bool allowSessionDownload)
            : this(http, authorizer, api, downloadHost, allowSessionDownload,
                new ScoreDownloadNodePolicy(authorizer is PublicWebScoreAuthorizer || authorizer is BrokerScoreAuthorizer
                    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CanvasCast", "OnlineScores", "download-node-health.json") : null)) { }

        public ShigebenClient(HttpClient http, IScoreRequestAuthorizer authorizer, Uri api, Uri downloadHost, bool allowSessionDownload, ScoreDownloadNodePolicy nodePolicy)
        {
            _http = http; _authorizer = authorizer; _api = api; _downloadHost = downloadHost;
            _allowSessionDownload = allowSessionDownload; _nodePolicy = nodePolicy ?? new ScoreDownloadNodePolicy(null);
        }

        public static ShigebenClient FromApprovedConfiguration()
        {
            var api = new Uri("https://api.shigeben.xyz/api/");
            string path = Path.Combine(AppContext.BaseDirectory, "shigeben-integration.json");
            if (!File.Exists(path))
                return CreatePublicClient(null, allowDownload: true);
            using var json = JsonDocument.Parse(File.ReadAllText(path));
            var root = json.RootElement;
            string hostText = GetString(root, "downloadHost");
            Uri host = null;
            if (!string.IsNullOrWhiteSpace(hostText) && (!Uri.TryCreate(hostText, UriKind.Absolute, out host) ||
                host.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(host.UserInfo) ||
                !string.IsNullOrEmpty(host.Query) || !string.IsNullOrEmpty(host.Fragment)))
                throw new ScoreIntegrationException("诗歌本下载节点配置无效，需要 HTTPS 地址。");
            if (host != null) host = new Uri(host.AbsoluteUri.TrimEnd('/') + "/");
            bool permit = !root.TryGetProperty("allowSessionImageDownload", out var value) || value.ValueKind == JsonValueKind.True;
            string signerText = GetString(root, "signerEndpoint");
            if (string.IsNullOrWhiteSpace(signerText)) return CreatePublicClient(host, permit);
            if (!Uri.TryCreate(signerText, UriKind.Absolute, out var signer)) throw new ScoreIntegrationException("诗歌本签名服务配置无效。");
            var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(20) };
            return new ShigebenClient(http, new BrokerScoreAuthorizer(signer), api, host, permit);
        }

        private static ShigebenClient CreatePublicClient(Uri downloadHost, bool allowDownload)
        {
            var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(20) };
            return new ShigebenClient(http, new PublicWebScoreAuthorizer(downloadHost), new Uri("https://api.shigeben.xyz/api/"), downloadHost, allowDownload);
        }

        public static string[] Tokenize(string text) => (text ?? "").Replace('\u3000', ' ').Split((char[])null, StringSplitOptions.RemoveEmptyEntries);

        public async Task<IReadOnlyList<OnlineScoreResult>> SearchAsync(string text, OnlineScoreSearchMode mode, CancellationToken ct)
        {
            text = (text ?? "").Trim();
            if (text.Length == 0) return Array.Empty<OnlineScoreResult>();
            if (text.Length > 200) throw new ScoreIntegrationException("搜索关键词不能超过 200 字。");
            using var request = mode switch
            {
                OnlineScoreSearchMode.Name => new HttpRequestMessage(HttpMethod.Get,
                    new Uri(_api, "image/searchByName?key=" + Uri.EscapeDataString(text) + "&bookId=0&musicType=0&take=200")),
                OnlineScoreSearchMode.Lyrics => CreatePost("image/searchEx", Tokenize(text)),
                _ => throw new ArgumentOutOfRangeException(nameof(mode))
            };
            if (mode == OnlineScoreSearchMode.Lyrics)
            {
                request.Headers.Add("searchType", "0"); request.Headers.Add("bookId", "0"); request.Headers.Add("musicType", "0");
            }
            using var json = await SendJsonAsync(request, ct);
            var array = RequireResult(json.RootElement, JsonValueKind.Array);
            var list = new List<OnlineScoreResult>();
            var seen = new HashSet<int>();
            foreach (var row in array.EnumerateArray())
            {
                int id = GetInt(row, "id"); string name = GetString(row, "name");
                if (id <= 0 || string.IsNullOrWhiteSpace(name) || !seen.Add(id)) continue;
                int bookId = GetInt(row, "bookId");
                list.Add(new OnlineScoreResult { Id = id, Name = name, BookName = GetString(row, "bookName"),
                    BookId = bookId > 0 ? bookId : null, No = GetInt(row, "no"), AccompanimentId = GetInt(row, "bId"), Matched = GetInt(row, "matched") });
                if (list.Count >= 200) break;
            }
            return list;
        }

        public async Task<IReadOnlyList<ScoreTextPart>> GetLyricsPreviewAsync(int id, string text, CancellationToken ct)
        {
            using var request = CreatePost("image/lyrics", Tokenize(text));
            request.Headers.Add("id", id.ToString(CultureInfo.InvariantCulture));
            using var json = await SendJsonAsync(request, ct);
            var result = RequireResult(json.RootElement, JsonValueKind.Object);
            return CreatePreview(GetString(result, "text") ?? "", result.TryGetProperty("highlights", out var highlights) ? highlights : default);
        }

        // 查询歌词正文是否存在，只读文字到内存；不取谱文、票据、图片，不写 YM/数据库。
        // 未知/请求失败与“无歌词”不同，失败不缓存，也不亮绿点。
        public async Task<bool> GetLyricsAvailabilityAsync(int id, CancellationToken ct)
        {
            if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));
            ct.ThrowIfCancellationRequested();
            await _lyricsHintGate.WaitAsync(ct);
            try
            {
                lock (_lyricsHints)
                    if (_lyricsHints.TryGetValue(id, out var hint) && hint.Expires > DateTimeOffset.UtcNow)
                        return hint.Available;
                // /image/lyrics 要求非空搜索词，不适合判断整首歌有没有歌词。
                return !string.IsNullOrWhiteSpace(await GetFullLyricsAsync(id, ct));
            }
            finally { _lyricsHintGate.Release(); }
        }

        private void CacheLyricsAvailability(int id, bool available)
        {
            lock (_lyricsHints)
            {
                if (_lyricsHints.Count >= 512) _lyricsHints.Clear();
                _lyricsHints[id] = (available, DateTimeOffset.UtcNow.AddMinutes(available ? 10 : 2));
            }
        }

        // 普通歌词全文接口；与 /image/lyrics 搜索命中预览分离，不裁剪正文。
        public async Task<string> GetFullLyricsAsync(int id, CancellationToken ct)
        {
            if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));
            using var request = new HttpRequestMessage(HttpMethod.Get,
                new Uri(_api, "download/text?id=" + id.ToString(CultureInfo.InvariantCulture)));
            using var json = await SendJsonAsync(request, ct);
            string lyrics = RequireResult(json.RootElement, JsonValueKind.String).GetString() ?? "";
            CacheLyricsAvailability(id, !string.IsNullOrWhiteSpace(lyrics));
            return lyrics;
        }

        public static IReadOnlyList<ScoreTextPart> CreatePreview(string text, JsonElement highlights)
        {
            var ranges = new List<(int Start, int End)>();
            if (highlights.ValueKind == JsonValueKind.Array)
                foreach (var h in highlights.EnumerateArray())
                {
                    int start = 0, length = 0;
                    if (h.ValueKind == JsonValueKind.Array && h.GetArrayLength() >= 2)
                    { if (h[0].ValueKind != JsonValueKind.Number || h[1].ValueKind != JsonValueKind.Number || !h[0].TryGetInt32(out start) || !h[1].TryGetInt32(out length)) continue; }
                    else if (h.ValueKind == JsonValueKind.Object) { start = GetInt(h, "start"); length = GetInt(h, "length"); }
                    else continue;
                    if (length <= 0 || start >= text.Length || (long)start + length <= 0) continue;
                    int s = Math.Max(0, start), e = (int)Math.Min(text.Length, (long)start + length);
                    if (s > 0 && char.IsLowSurrogate(text[s]) && char.IsHighSurrogate(text[s - 1])) s--;
                    if (e < text.Length && e > 0 && char.IsHighSurrogate(text[e - 1]) && char.IsLowSurrogate(text[e])) e++;
                    ranges.Add((s, e));
                }
            ranges.Sort((a, b) => a.Start.CompareTo(b.Start));
            var merged = new List<(int Start, int End)>();
            foreach (var range in ranges)
                if (merged.Count > 0 && range.Start <= merged[^1].End)
                    merged[^1] = (merged[^1].Start, Math.Max(merged[^1].End, range.End));
                else merged.Add(range);
            int offset = merged.Count > 0 ? Math.Max(0, merged[0].Start - 30) : 0;
            if (offset > 0 && char.IsLowSurrogate(text[offset])) offset--;
            int limit = Math.Min(text.Length, offset + 220);
            if (limit < text.Length && limit > 0 && char.IsHighSurrogate(text[limit - 1])) limit++;
            var parts = new List<ScoreTextPart>();
            if (offset > 0) parts.Add(new ScoreTextPart("…", false));
            int position = offset;
            foreach (var range in merged.Where(r => r.End > offset && r.Start < limit))
            {
                int s = Math.Max(offset, range.Start), e = Math.Min(limit, range.End);
                if (s > position) parts.Add(new ScoreTextPart(text[position..s], false));
                parts.Add(new ScoreTextPart(text[s..e], true)); position = e;
            }
            if (position < limit) parts.Add(new ScoreTextPart(text[position..limit], false));
            if (limit < text.Length) parts.Add(new ScoreTextPart("…", false));
            return parts;
        }

        // /download/pu 返回谱面数据，不是位图；保持所选 id，独立排版后交给同一图片显示链路。
        public async Task<string> GetNotationAsync(int id, int type, CancellationToken ct)
        {
            if (!_allowSessionDownload) throw new ScoreIntegrationException("当前配置未允许歌谱下载。已保留原画面。");
            if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));
            if (type is not (0 or 1)) throw new ArgumentOutOfRangeException(nameof(type));
            for (int attempt = 0; ; attempt++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(_api,
                    "download/pu?id=" + id.ToString(CultureInfo.InvariantCulture) + "&type=" + type));
                try
                {
                    using var json = await SendJsonAsync(request, ct);
                    var root = json.RootElement;
                    // 只有明确的“不存在”才能换获取方式；权限拒绝、非 JSON、协议变化不能伪装成没有谱。
                    if (GetInt(root, "code") == 0 && GetString(root, "msg") == "歌谱不存在") return null;
                    string source = RequireResult(root, JsonValueKind.String).GetString();
                    if (string.IsNullOrWhiteSpace(source) || Encoding.UTF8.GetByteCount(source) > ScoreNotationRenderer.MaxSourceBytes)
                        throw new ScoreIntegrationException("结构化歌谱内容为空或超过大小限制。");
                    return source;
                }
                // 只读请求遇到已观察到的短暂连接失败时有限重试，重新生成应用头。
                // 业务/设备/账户拒绝绝不重试，也不更换身份。
                catch (HttpRequestException) when (attempt == 0 && !ct.IsCancellationRequested) { }
                catch (OperationCanceledException) when (attempt == 0 && !ct.IsCancellationRequested) { }
                await Task.Delay(300, ct);
            }
        }

        public async Task<string> DownloadScoreAsync(int id, string directory, CancellationToken ct)
        {
            if (!_allowSessionDownload) throw new ScoreIntegrationException("当前配置未允许歌谱下载。已保留原画面。");
            string reason = null;
            for (int type = 0; type <= 1; type++)
            {
                string source;
                try { source = await GetNotationAsync(id, type, ct); }
                catch (HttpRequestException) { ct.ThrowIfCancellationRequested(); reason = "结构化歌谱接口连接失败。"; break; }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested) { reason = "结构化歌谱接口超时。"; break; }
                if (source == null) continue;
                if (!ScoreNotationRenderer.CanRender(source, out reason)) continue;
                ct.ThrowIfCancellationRequested();
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, id + "-" + Guid.NewGuid().ToString("N") + ".png");
                // CPU 排版不阻塞 WPF 输入。取消或排版失败清理只属于本次请求的新文件。
                try { await Task.Run(() => ScoreNotationRenderer.RenderToPng(source, path, ct), ct); return path; }
                catch (ScoreIntegrationException ex)
                { if (File.Exists(path)) File.Delete(path); reason = ex.Message; }
                catch { if (File.Exists(path)) File.Delete(path); throw; }
            }
            try { return await DownloadImageAsync(id, directory, ct); }
            catch (ScoreIntegrationException ex)
            {
                throw new ScoreIntegrationException((reason ?? "此版本没有可用的结构化简谱。") + " " + ex.Message);
            }
        }

        // 只取得新票据，不持久化临时 URL。文件生命周期由本地歌谱库管理。
        public async Task<string> DownloadImageAsync(int id, string directory, CancellationToken ct)
        {
            if (!_allowSessionDownload) throw new ScoreIntegrationException("未确认歌谱图片下载/会话缓存许可，当前不能下载。请先取得站方许可。");
            if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));
            ct.ThrowIfCancellationRequested();
            await _downloadGate.WaitAsync(ct);
            try
            {
                var hosts = await GetDownloadHostsAsync(ct);
                var candidates = _nodePolicy.Rank(hosts);
                if (candidates.Count == 0) throw new ScoreIntegrationException(_nodePolicy.Unavailable(hosts));
                // 只有实际文件成功才保持线路；ping只测可达性/耗时，不假装下载已成功。
                if (!_nodePolicy.HasVerifiedNode(hosts)) await ProbeDownloadNodesAsync(candidates, ct);
                foreach (var host in _nodePolicy.Rank(hosts))
                {
                    var clock = Stopwatch.StartNew();
                    try
                    {
                        string path = await DownloadImageFromHostAsync(id, directory, host, ct);
                        _nodePolicy.Success(host, clock.Elapsed.TotalMilliseconds);
                        return path;
                    }
                    catch (HttpRequestException ex) { ct.ThrowIfCancellationRequested(); _nodePolicy.Failure(host, ConnectionFailureKind(ex)); }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested) { _nodePolicy.Failure(host, "请求超时"); }
                }
                throw new ScoreIntegrationException(_nodePolicy.Unavailable(hosts));
            }
            finally { _downloadGate.Release(); }
        }

        private async Task ProbeDownloadNodesAsync(IReadOnlyList<Uri> hosts, CancellationToken ct)
        {
            using var parallel = new SemaphoreSlim(3, 3);
            await Task.WhenAll(hosts.Select(async host =>
            {
                await parallel.WaitAsync(ct);
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    timeout.CancelAfter(TimeSpan.FromSeconds(4));
                    using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(host, "ping"));
                    var watch = Stopwatch.StartNew();
                    // 不发设备认证，不申请票据；仅探测当前官方列表里的 HTTPS 地址。
                    using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                    if ((int)response.StatusCode >= 500) _nodePolicy.Failure(host, "HTTP " + (int)response.StatusCode);
                    else _nodePolicy.Reachable(host, watch.Elapsed.TotalMilliseconds);
                }
                catch (HttpRequestException ex) { ct.ThrowIfCancellationRequested(); _nodePolicy.Failure(host, ConnectionFailureKind(ex)); }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested) { _nodePolicy.Failure(host, "探测超时"); }
                finally { parallel.Release(); }
            }));
        }

        private static string ConnectionFailureKind(HttpRequestException ex)
        {
            if (ex.StatusCode.HasValue) return "HTTP " + (int)ex.StatusCode.Value;
            if (ex.HttpRequestError == HttpRequestError.SecureConnectionError) return "TLS握手失败";
            if (ex.HttpRequestError == HttpRequestError.NameResolutionError) return "DNS解析失败";
            for (Exception inner = ex; inner != null; inner = inner.InnerException)
            {
                if (inner is AuthenticationException) return "TLS握手失败";
                if (inner is SocketException socket && socket.SocketErrorCode == SocketError.HostNotFound) return "DNS解析失败";
            }
            return "连接中断";
        }

        public async Task<IReadOnlyList<Uri>> GetDownloadHostsAsync(CancellationToken ct)
        {
            // 显式配置保持原有“指定地址”语义；未指定时使用官方自动线路池。
            if (_downloadHost != null) return new[] { _downloadHost };
            if (_downloadHosts != null && DateTimeOffset.UtcNow - _downloadHostsLoaded < TimeSpan.FromHours(1)) return _downloadHosts;
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(_api, "hosts"));
            request.Headers.Add("ver", "3");
            using var json = await SendJsonAsync(request, ct);
            var result = RequireResult(json.RootElement, JsonValueKind.Object);
            if (!result.TryGetProperty("downloadHosts", out var list) || list.ValueKind != JsonValueKind.Array)
                throw new ScoreIntegrationException("下载节点响应结构发生变化。");
            var hosts = new List<Uri>();
            foreach (var item in list.EnumerateArray())
            {
                int type = GetInt(item, "type");
                if ((type == 1 || type == 3) && Uri.TryCreate(GetString(item, "url"), UriKind.Absolute, out var host) &&
                    host.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(host.UserInfo) && string.IsNullOrEmpty(host.Query) && string.IsNullOrEmpty(host.Fragment))
                {
                    host = new Uri(host.AbsoluteUri.TrimEnd('/') + "/");
                    if (hosts.Contains(host)) continue;
                    hosts.Add(host);
                    (_authorizer as PublicWebScoreAuthorizer)?.AllowAdvertisedDownloadHost(host);
                    if (hosts.Count >= 12) break;
                }
            }
            if (hosts.Count == 0) throw new ScoreIntegrationException("站点当前没有可用的 HTTPS 图片下载节点。");
            _downloadHosts = hosts;
            _downloadHostsLoaded = DateTimeOffset.UtcNow;
            return hosts;
        }

        private async Task<string> DownloadImageFromHostAsync(int id, string directory, Uri host, CancellationToken ct)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(18)); ct = timeout.Token;
            using var ticketRequest = new HttpRequestMessage(HttpMethod.Get, new Uri(host, "download/ticket?id=" + id + "&type=image"));
            using var ticketJson = await SendJsonAsync(ticketRequest, ct);
            var ticket = RequireResult(ticketJson.RootElement, JsonValueKind.Object);
            string value = GetString(ticket, "ticket");
            if (!ticket.TryGetProperty("expires", out var exp) || !exp.TryGetInt64(out long expires) ||
                expires <= DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 5 || string.IsNullOrWhiteSpace(value))
                throw new ScoreIntegrationException("图片票据无效或已到期，请重新选择。");
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(host,
                "download/image?id=" + id + "&expires=" + expires + "&ticket=" + Uri.EscapeDataString(value)));
            await _authorizer.AuthorizeAsync(request, ct);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            CheckHttp(response);
            string mime = response.Content.Headers.ContentType?.MediaType;
            if (mime != "image/png" && mime != "image/jpeg" && mime != "image/webp" && mime != "image/bmp")
                throw new ScoreIntegrationException("此歌谱未提供受支持的原始位图（PNG/JPEG/WebP/BMP）。");
            byte[] bytes = await ReadBoundedAsync(response.Content, MaxImageBytes, ct);
            bool png = bytes.Length >= 8 && bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
            bool jpg = bytes.Length >= 3 && bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255;
            bool webp = bytes.Length >= 12 && Encoding.ASCII.GetString(bytes, 0, 4) == "RIFF" && Encoding.ASCII.GetString(bytes, 8, 4) == "WEBP";
            bool bmp = bytes.Length >= 2 && bytes[0] == 66 && bytes[1] == 77;
            string extension = mime switch { "image/png" when png => ".png", "image/jpeg" when jpg => ".jpg", "image/webp" when webp => ".webp", "image/bmp" when bmp => ".bmp", _ => null };
            if (extension == null) throw new ScoreIntegrationException("下载内容与图片格式不符，已拒绝加载。");
            // 节点只有在实际位图完整可解码后才能记为成功，不把PNG文件头当成有效歌谱。
            using (var stream = new SkiaSharp.SKMemoryStream(bytes))
            using (var codec = SkiaSharp.SKCodec.Create(stream))
            {
                if (codec == null || codec.Info.Width <= 0 || codec.Info.Height <= 0 ||
                    (long)codec.Info.Width * codec.Info.Height > 40_000_000)
                    throw new ScoreIntegrationException("下载歌谱无法解码或尺寸过大，未保存到 YM。");
                using var decoded = new SkiaSharp.SKBitmap(codec.Info);
                if (codec.GetPixels(decoded.Info, decoded.GetPixels()) != SkiaSharp.SKCodecResult.Success)
                    throw new ScoreIntegrationException("下载歌谱图片不完整，未保存到 YM。");
            }
            ct.ThrowIfCancellationRequested();
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, id + "-" + Guid.NewGuid().ToString("N") + extension);
            try { await File.WriteAllBytesAsync(path, bytes, ct); return path; }
            catch { if (File.Exists(path)) File.Delete(path); throw; }
        }

        private HttpRequestMessage CreatePost(string path, string[] tokens) => new(HttpMethod.Post, new Uri(_api, path))
        { Content = new StringContent(JsonSerializer.Serialize(tokens), Encoding.UTF8, "application/json") };
        private async Task<JsonDocument> SendJsonAsync(HttpRequestMessage request, CancellationToken ct)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(20)); ct = timeout.Token;
            await _authorizer.AuthorizeAsync(request, ct);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            CheckHttp(response);
            byte[] bytes = await ReadBoundedAsync(response.Content, 2 * 1024 * 1024, ct);
            try { return JsonDocument.Parse(bytes); }
            catch (JsonException) { throw new ScoreIntegrationException("诗歌本响应不是有效 JSON。"); }
        }
        private static void CheckHttp(HttpResponseMessage response)
        {
            // 只对节点传输/5xx故障换源；账户权限、限流、内容/业务拒绝保持原异常，绝不绕过。
            if ((int)response.StatusCode is 500 or 502 or 503 or 504) throw new ScoreNodeTransportException((int)response.StatusCode);
            if (!response.IsSuccessStatusCode) throw new ScoreIntegrationException($"诗歌本请求失败（HTTP {(int)response.StatusCode}），请稍后重试或检查接入权限。");
        }
        internal static JsonElement RequireResult(JsonElement root, JsonValueKind kind)
        {
            if (root.ValueKind != JsonValueKind.Object || GetInt(root, "code") != 200 || !root.TryGetProperty("msg", out var message) || message.ValueKind != JsonValueKind.String || message.GetString() != "")
            {
                string error = GetString(root, "msg");
                // 仅显示可读业务文本，不展示头、ticket 或账户凭据。
                if (!string.IsNullOrWhiteSpace(error) && error.Length <= 150 && !error.Contains("http", StringComparison.OrdinalIgnoreCase) && !error.Contains("token", StringComparison.OrdinalIgnoreCase))
                    throw new ScoreIntegrationException("诗歌本：" + error);
                throw new ScoreIntegrationException("诗歌本业务校验失败，请检查应用签名、设备和账户权限。");
            }
            if (!root.TryGetProperty("result", out var result) || result.ValueKind != kind)
                throw new ScoreIntegrationException("诗歌本响应结构发生变化。");
            return result;
        }
        private static string GetString(JsonElement row, string key) => row.ValueKind == JsonValueKind.Object && row.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        private static int GetInt(JsonElement row, string key) => row.ValueKind == JsonValueKind.Object && row.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int n) ? n : 0;
        internal static async Task<byte[]> ReadBoundedAsync(HttpContent content, int maximum, CancellationToken ct)
        {
            if (content.Headers.ContentLength > maximum) throw new ScoreIntegrationException("响应内容超过允许大小。");
            await using var stream = await content.ReadAsStreamAsync(ct);
            using var output = new MemoryStream(); byte[] buffer = new byte[16384];
            int count;
            while ((count = await stream.ReadAsync(buffer, ct)) != 0)
            {
                if (output.Length + count > maximum) throw new ScoreIntegrationException("响应内容超过允许大小。");
                output.Write(buffer, 0, count);
            }
            if (content.Headers.ContentLength.HasValue && content.Headers.ContentLength.Value != output.Length) throw new ScoreIntegrationException("图片或响应下载不完整。");
            return output.ToArray();
        }
        public void Dispose() { _http.Dispose(); (_authorizer as IDisposable)?.Dispose(); }
    }
}
