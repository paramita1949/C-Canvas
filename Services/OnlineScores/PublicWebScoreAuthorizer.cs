using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ImageColorChanger.Services.OnlineScores
{
    /// <summary>
    /// 公开 Web 客户端协议适配。来源 index-DDy4baaI.js（build42），不是账户登录凭据。
    /// 应用头逐请求生成；下载时通过正常 statistics/v2 登记本程序自己的随机设备。
    /// 不读取浏览器存储，不复用用户设备/账户，不对禁用设备重登记规避限制。
    /// </summary>
    public sealed class PublicWebScoreAuthorizer : IScoreRequestAuthorizer, IDisposable
    {
        private static readonly byte[] Mask = { 90, 195, 39, 158, 97, 180 };
        private static readonly byte[] AppBytes = { 50, 186, 74, 240, 22, 209, 56 };
        private static readonly byte[] MaterialBytes = { 48, 130, 125, 248, 84, 216, 49, 142, 109, 170, 0, 255, 29, 245 };
        private static readonly SemaphoreSlim DeviceGate = new(1, 1);
        private static readonly byte[] StoreEntropy = Encoding.UTF8.GetBytes("CanvasCast.Shigeben.Device.v1");
        private readonly HttpClient _registrationHttp;
        private readonly string _deviceFile;
        private readonly Uri _downloadOrigin;
        private readonly ConcurrentDictionary<string, byte> _permittedOrigins = new(StringComparer.OrdinalIgnoreCase);
        private DeviceState _state;

        public sealed class DeviceState
        {
            public string InstallationId { get; set; }
            public string DeviceId { get; set; }
            public string Secret { get; set; }
        }

        public PublicWebScoreAuthorizer(Uri downloadOrigin = null, HttpClient registrationHttp = null, string deviceFile = null)
        {
            _downloadOrigin = downloadOrigin ?? new Uri("https://api.shigeben.xyz/api/");
            _registrationHttp = registrationHttp ?? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(15) };
            _deviceFile = deviceFile ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CanvasCast", "OnlineScores", "shigeben-device.dpapi");
            _permittedOrigins.TryAdd(_downloadOrigin.GetLeftPart(UriPartial.Authority), 0);
        }

        internal void AllowAdvertisedDownloadHost(Uri host) => _permittedOrigins.TryAdd(host.GetLeftPart(UriPartial.Authority), 0);

        private static string Decode(byte[] bytes)
        {
            var output = new char[bytes.Length];
            for (int i = 0; i < bytes.Length; i++) output[i] = (char)(bytes[i] ^ Mask[i % Mask.Length]);
            return new string(output);
        }

        public static void ApplyApplicationHeaders(HttpRequestMessage request, long timestampMilliseconds)
        {
            string app = Decode(AppBytes), time = timestampMilliseconds.ToString(CultureInfo.InvariantCulture);
            string token = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(app + Decode(MaterialBytes) + time))).ToLowerInvariant();
            SetHeader(request, "appid", app); SetHeader(request, "timestamp", time); SetHeader(request, "token", token);
        }

        private static void SetHeader(HttpRequestMessage request, string name, string value)
        {
            request.Headers.Remove(name); request.Headers.Add(name, value);
        }

        public async Task AuthorizeAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var uri = request.RequestUri;
            if (uri == null || uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo) ||
                !(uri.Host == "api.shigeben.xyz" && uri.Port == 443 || _permittedOrigins.ContainsKey(uri.GetLeftPart(UriPartial.Authority))))
                throw new ScoreIntegrationException("拒绝向未经配置的地址发送诗歌本设备认证头。");
            ct.ThrowIfCancellationRequested();
            if (uri.AbsolutePath.EndsWith("/download/ticket", StringComparison.OrdinalIgnoreCase))
                await EnsureDeviceAsync(ct);
            // 时间必须在等待设备登记之后生成，避免使用过期的应用头。
            ApplyApplicationHeaders(request, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            if (_state?.DeviceId != null && _state?.Secret != null)
            {
                string time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
                string nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
                string sign = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(_state.Secret),
                    Encoding.UTF8.GetBytes(_state.DeviceId + time + nonce))).ToLowerInvariant();
                SetHeader(request, "X-Device-Id", _state.DeviceId); SetHeader(request, "X-Device-Timestamp", time);
                SetHeader(request, "X-Device-Nonce", nonce); SetHeader(request, "X-Device-Sign", sign);
            }
        }

        private async Task EnsureDeviceAsync(CancellationToken ct)
        {
            if (_state?.DeviceId != null && _state?.Secret != null) return;
            await DeviceGate.WaitAsync(ct);
            try
            {
                if (_state?.DeviceId != null && _state?.Secret != null) return;
                if (File.Exists(_deviceFile))
                {
                    if (new FileInfo(_deviceFile).Length > 64 * 1024) throw new ScoreIntegrationException("诗歌本设备缓存无效，请检查本机资料。");
                    try
                    {
                        byte[] clear = ProtectedData.Unprotect(await File.ReadAllBytesAsync(_deviceFile, ct), StoreEntropy, DataProtectionScope.CurrentUser);
                        try { _state = JsonSerializer.Deserialize<DeviceState>(clear); }
                        finally { CryptographicOperations.ZeroMemory(clear); }
                    }
                    catch (CryptographicException) { throw new ScoreIntegrationException("诗歌本设备缓存不能由当前 Windows 用户读取；未自动重登记设备。"); }
                    catch (JsonException) { throw new ScoreIntegrationException("诗歌本设备缓存损坏；未自动重登记设备。"); }
                    if (_state == null || string.IsNullOrEmpty(_state.InstallationId)) throw new ScoreIntegrationException("诗歌本设备缓存缺少标识。");
                }
                else
                {
                    _state = new DeviceState { InstallationId = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant() };
                    await SaveStateAsync(ct); // 失败后继续使用同一设备标识，不创建一串新设备。
                }
                if (!string.IsNullOrEmpty(_state.DeviceId) && !string.IsNullOrEmpty(_state.Secret)) return;
                using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.shigeben.xyz/api/statistics/v2");
                ApplyApplicationHeaders(request, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                request.Content = new StringContent(JsonSerializer.Serialize(new
                {
                    DeviceId = _state.InstallationId, AppName = "CanvasCast",
                    Ver = typeof(PublicWebScoreAuthorizer).Assembly.GetName().Version?.ToString() ?? "", OS = "Windows"
                }), Encoding.UTF8, "application/json");
                using var response = await _registrationHttp.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                if (!response.IsSuccessStatusCode) throw new ScoreIntegrationException($"诗歌本设备登记失败（HTTP {(int)response.StatusCode}）。");
                byte[] bytes = await ShigebenClient.ReadBoundedAsync(response.Content, 64 * 1024, ct);
                using var json = JsonDocument.Parse(bytes);
                var result = ShigebenClient.RequireResult(json.RootElement, JsonValueKind.Object);
                string status = ReadString(result, "deviceStatus");
                if (status != "ok") throw new ScoreIntegrationException("诗歌本设备登记被拒绝（" + (status ?? "未知状态") + "）；未自动重登记。");
                string device = ReadString(result, "deviceId"), encrypted = ReadString(result, "deviceSecret");
                if (string.IsNullOrEmpty(device) || string.IsNullOrEmpty(encrypted)) throw new ScoreIntegrationException("诗歌本设备登记响应缺少凭据。");
                string secret = DecodeIssuedSecret(device, encrypted);
                if (string.IsNullOrEmpty(secret)) throw new ScoreIntegrationException("诗歌本设备凭据解包失败。");
                _state.DeviceId = device; _state.Secret = secret;
                await SaveStateAsync(ct);
            }
            finally { DeviceGate.Release(); }
        }

        public static string DecodeIssuedSecret(string deviceId, string encrypted)
        {
            // 仅解包站点正常登记接口颁发给本客户端的密钥，不读取其他设备的密钥。
            var reordered = new StringBuilder();
            for (int i = 1; i < deviceId.Length; i += 2) reordered.Append(deviceId[i]);
            for (int i = 0; i < deviceId.Length; i += 2) reordered.Append(deviceId[i]);
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes("hymn2026ka9f" + reordered + "zx7Qv2pwm3"));
            byte[] envelope;
            try { envelope = Convert.FromBase64String(encrypted); }
            catch (FormatException) { throw new ScoreIntegrationException("设备凭据编码无效。"); }
            if (envelope.Length <= 16 || (envelope.Length - 16) % 16 != 0) throw new ScoreIntegrationException("设备凭据长度无效。");
            using var aes = Aes.Create(); aes.Key = hash.AsSpan(0, 16).ToArray(); aes.Mode = CipherMode.CBC; aes.Padding = PaddingMode.PKCS7;
            try
            {
                byte[] plain = aes.DecryptCbc(envelope.AsSpan(16), envelope.AsSpan(0, 16), PaddingMode.PKCS7);
                try { return new UTF8Encoding(false, true).GetString(plain); }
                finally { CryptographicOperations.ZeroMemory(plain); }
            }
            catch (CryptographicException) { throw new ScoreIntegrationException("设备凭据解密失败。"); }
            finally { CryptographicOperations.ZeroMemory(hash); }
        }

        private static string ReadString(JsonElement value, string name) => value.TryGetProperty(name, out var item) && item.ValueKind == JsonValueKind.String ? item.GetString() : null;
        private async Task SaveStateAsync(CancellationToken ct)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_deviceFile));
            byte[] clear = JsonSerializer.SerializeToUtf8Bytes(_state);
            byte[] secured;
            try { secured = ProtectedData.Protect(clear, StoreEntropy, DataProtectionScope.CurrentUser); }
            finally { CryptographicOperations.ZeroMemory(clear); }
            string temporary = _deviceFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { await File.WriteAllBytesAsync(temporary, secured, ct); File.Move(temporary, _deviceFile, overwrite: true); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        public void Dispose() { _registrationHttp.Dispose(); _state = null; }
    }
}
