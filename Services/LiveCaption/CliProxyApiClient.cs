using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Net.Security;
using System.Globalization;
using System.Security.Cryptography.X509Certificates;
using ImageColorChanger.Core;

namespace ImageColorChanger.Services.LiveCaption
{
    internal sealed class CliProxyApiClient : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly string _provider;
        private readonly string _baseUrl;
        private readonly string _apiKey;
        private readonly string _asrModel;
        private readonly string _funAsrWsUrl;
        private readonly bool _funAsrAllowInsecureTls;

        private readonly string _baiduAppId;
        private readonly string _baiduApiKey;
        private readonly string _baiduSecretKey;
        private readonly int _baiduDevPid;
        private readonly string _tencentAppId;
        private readonly string _tencentSecretId;
        private readonly string _tencentSecretKey;
        private readonly string _tencentCustomizationId;
        private readonly string _aliAppKey;
        private readonly string _aliAccessKeyId;
        private readonly string _aliAccessKeySecret;
        private readonly string _doubaoAppKey;
        private readonly string _doubaoAccessKey;
        private readonly string _doubaoResourceId;
        private readonly string _tencentRegion = "ap-shanghai";
        private readonly string _tencentVersion = "2019-06-14";
        private const string BaiduRealtimeBaseUrl = "wss://vop.baidu.com/realtime_asr";
        private const string TencentRealtimeHost = "asr.cloud.tencent.com";
        private const string TencentRealtimePathPrefix = "/asr/v2";
        private const string AliyunRealtimeWsBaseUrl = "wss://nls-gateway-cn-shanghai.aliyuncs.com/ws/v1";
        private const string DoubaoRealtimeWsBaseUrl = "wss://openspeech.bytedance.com/api/v3/sauc/bigmodel_async";
        private const string DoubaoRealtimeDefaultResourceId = "volc.seedasr.sauc.duration";
        private const byte DoubaoProtocolVersion = 1;
        private const byte DoubaoHeaderWords = 1; // 1 * 4 bytes
        private const byte DoubaoMessageTypeFullClientRequest = 0x1;
        private const byte DoubaoMessageTypeAudioOnlyRequest = 0x2;
        private const byte DoubaoMessageTypeFullServerResponse = 0x9;
        private const byte DoubaoMessageTypeServerError = 0xF;
        private const byte DoubaoFlagNone = 0x0;
        private const byte DoubaoFlagAudioLast = 0x2;
        private const byte DoubaoSerializationNone = 0x0;
        private const byte DoubaoSerializationJson = 0x1;
        private const byte DoubaoCompressionNone = 0x0;
        private const byte DoubaoCompressionGzip = 0x1;
        private static readonly string[] DefaultChurchHotwords =
        {
            "圣经", "经文", "主耶稣", "耶稣基督", "哈利路亚", "阿们", "祷告", "敬拜", "赞美",
            "马太福音", "马可福音", "路加福音", "约翰福音", "使徒行传", "罗马书", "哥林多前书",
            "哥林多后书", "加拉太书", "以弗所书", "腓立比书", "歌罗西书", "帖撒罗尼迦前书",
            "帖撒罗尼迦后书", "提摩太前书", "提摩太后书", "提多书", "腓利门书", "希伯来书",
            "雅各书", "彼得前书", "彼得后书", "约翰一书", "约翰二书", "约翰三书", "犹大书",
            "启示录", "创世记", "出埃及记", "利未记", "民数记", "申命记", "诗篇", "箴言", "以赛亚书"
        };
        private const string AliyunTokenHostPrimary = "nls-meta.cn-shanghai.aliyuncs.com";
        private const string AliyunTokenHostFallback = "nlsmeta.cn-shanghai.aliyuncs.com";
        private const string AliyunRegionId = "cn-shanghai";
        private const string AliyunApiVersion = "2019-02-28";

        private string _baiduAccessToken = string.Empty;
        private DateTime _baiduTokenExpireUtc = DateTime.MinValue;
        private readonly SemaphoreSlim _baiduTokenLock = new(1, 1);
        private string _aliyunAccessToken = string.Empty;
        private DateTime _aliyunTokenExpireUtc = DateTime.MinValue;
        private readonly SemaphoreSlim _aliyunTokenLock = new(1, 1);

        private bool _disposed;
        private ClientWebSocket _tencentRealtimeWs;
        private Task _tencentRealtimeReceiveTask;
        private readonly SemaphoreSlim _tencentRealtimeSendLock = new(1, 1);
        private readonly SemaphoreSlim _tencentRealtimeConnectLock = new(1, 1);
        private Action<LiveCaptionAsrText> _tencentRealtimeTextHandler;
        private Action<string> _tencentRealtimeStatusHandler;
        private string _tencentRealtimeVoiceId = string.Empty;
        private string _tencentRealtimeParamSummary = string.Empty;
        private ClientWebSocket _baiduRealtimeWs;
        private Task _baiduRealtimeReceiveTask;
        private readonly SemaphoreSlim _baiduRealtimeSendLock = new(1, 1);
        private readonly SemaphoreSlim _baiduRealtimeConnectLock = new(1, 1);
        private Action<LiveCaptionAsrText> _baiduRealtimeTextHandler;
        private Action<string> _baiduRealtimeStatusHandler;
        private string _baiduRealtimeSn = string.Empty;
        private ClientWebSocket _aliyunRealtimeWs;
        private Task _aliyunRealtimeReceiveTask;
        private readonly SemaphoreSlim _aliyunRealtimeSendLock = new(1, 1);
        private readonly SemaphoreSlim _aliyunRealtimeConnectLock = new(1, 1);
        private Action<LiveCaptionAsrText> _aliyunRealtimeTextHandler;
        private Action<string> _aliyunRealtimeStatusHandler;
        private string _aliyunRealtimeTaskId = string.Empty;
        private ClientWebSocket _doubaoRealtimeWs;
        private Task _doubaoRealtimeReceiveTask;
        private readonly SemaphoreSlim _doubaoRealtimeSendLock = new(1, 1);
        private readonly SemaphoreSlim _doubaoRealtimeConnectLock = new(1, 1);
        private Action<LiveCaptionAsrText> _doubaoRealtimeTextHandler;
        private Action<string> _doubaoRealtimeStatusHandler;
        private string _doubaoConnectId = string.Empty;
        private ClientWebSocket _funAsrRealtimeWs;
        private Task _funAsrRealtimeReceiveTask;
        private readonly SemaphoreSlim _funAsrRealtimeSendLock = new(1, 1);
        private readonly SemaphoreSlim _funAsrRealtimeConnectLock = new(1, 1);
        private Action<LiveCaptionAsrText> _funAsrRealtimeTextHandler;
        private Action<string> _funAsrRealtimeStatusHandler;
        public string LastError { get; private set; } = string.Empty;
        public string AsrProvider => _provider;
        public string AsrModel => _asrModel;
        public string LastTranscribeUrl { get; private set; } = string.Empty;
        public int LastTranscribeStatusCode { get; private set; }
        public long LastTranscribeElapsedMs { get; private set; }
        public bool IsBaiduRealtimeAvailable =>
            IsBaiduProvider()
            && !string.IsNullOrWhiteSpace(_baiduAppId)
            && !string.IsNullOrWhiteSpace(_baiduApiKey);
        public bool IsTencentRealtimeAvailable =>
            IsTencentProvider()
            && !string.IsNullOrWhiteSpace(_tencentAppId)
            && !string.IsNullOrWhiteSpace(_tencentSecretId)
            && !string.IsNullOrWhiteSpace(_tencentSecretKey);
        public bool IsAliyunRealtimeAvailable =>
            IsAliyunProvider()
            && !string.IsNullOrWhiteSpace(_aliAppKey)
            && !string.IsNullOrWhiteSpace(_aliAccessKeyId)
            && !string.IsNullOrWhiteSpace(_aliAccessKeySecret);
        public bool IsDoubaoRealtimeAvailable =>
            IsDoubaoProvider()
            && !string.IsNullOrWhiteSpace(_doubaoAppKey)
            && !string.IsNullOrWhiteSpace(_doubaoAccessKey);
        public bool IsFunAsrRealtimeAvailable =>
            IsFunAsrProvider()
            && !string.IsNullOrWhiteSpace(_funAsrWsUrl);
        public bool SupportsRealtimeSession => IsBaiduRealtimeAvailable || IsTencentRealtimeAvailable || IsAliyunRealtimeAvailable || IsDoubaoRealtimeAvailable || IsFunAsrRealtimeAvailable;
        public bool IsRealtimeConnected => IsBaiduRealtimeConnected || IsTencentRealtimeConnected || IsAliyunRealtimeConnected || IsDoubaoRealtimeConnected || IsFunAsrRealtimeConnected;
        public bool IsBaiduRealtimeConnected => _baiduRealtimeWs != null && _baiduRealtimeWs.State == WebSocketState.Open;
        public bool IsTencentRealtimeConnected => _tencentRealtimeWs != null && _tencentRealtimeWs.State == WebSocketState.Open;
        public bool IsAliyunRealtimeConnected => _aliyunRealtimeWs != null && _aliyunRealtimeWs.State == WebSocketState.Open;
        public bool IsDoubaoRealtimeConnected => _doubaoRealtimeWs != null && _doubaoRealtimeWs.State == WebSocketState.Open;
        public bool IsFunAsrRealtimeConnected => _funAsrRealtimeWs != null && _funAsrRealtimeWs.State == WebSocketState.Open;

        public CliProxyApiClient(ConfigManager config, bool useRealtimeSettings = false)
        {
            string configuredBaseUrl = useRealtimeSettings
                ? config?.LiveCaptionRealtimeProxyBaseUrl
                : config?.LiveCaptionProxyBaseUrl;
            _provider = NormalizeAsrProvider(useRealtimeSettings
                ? config?.LiveCaptionRealtimeAsrProvider
                : config?.LiveCaptionAsrProvider);
            _baseUrl = (string.IsNullOrWhiteSpace(configuredBaseUrl)
                    ? "http://localhost:8317/v1"
                    : configuredBaseUrl)
                .TrimEnd('/');
            _funAsrWsUrl = ResolveFunAsrWsUrl(_baseUrl);
            _funAsrAllowInsecureTls = config?.LiveCaptionFunAsrAllowInsecureTls ?? true;
            _apiKey = config?.LiveCaptionApiKey ?? string.Empty;
            _asrModel = (useRealtimeSettings
                ? config?.LiveCaptionRealtimeAsrModel
                : config?.LiveCaptionAsrModel)
                ?? "gpt-4o-mini-transcribe";

            _baiduAppId = config?.LiveCaptionBaiduAppId ?? string.Empty;
            _baiduApiKey = config?.LiveCaptionBaiduApiKey ?? string.Empty;
            _baiduSecretKey = config?.LiveCaptionBaiduSecretKey ?? string.Empty;
            _baiduDevPid = useRealtimeSettings
                ? (config?.LiveCaptionRealtimeBaiduDevPid > 0 ? config.LiveCaptionRealtimeBaiduDevPid : 1537)
                : (config?.LiveCaptionBaiduDevPid > 0 ? config.LiveCaptionBaiduDevPid : 1537);
            _tencentAppId = config?.LiveCaptionTencentAppId ?? string.Empty;
            _tencentSecretId = config?.LiveCaptionTencentSecretId ?? string.Empty;
            _tencentSecretKey = config?.LiveCaptionTencentSecretKey ?? string.Empty;
            _tencentCustomizationId = useRealtimeSettings
                ? (config?.LiveCaptionTencentRealtimeCustomizationId ?? string.Empty)
                : (config?.LiveCaptionTencentShortCustomizationId ?? string.Empty);
            _aliAppKey = config?.LiveCaptionAliAppKey ?? string.Empty;
            _aliAccessKeyId = config?.LiveCaptionAliAccessKeyId ?? string.Empty;
            _aliAccessKeySecret = config?.LiveCaptionAliAccessKeySecret ?? string.Empty;
            _doubaoAppKey = config?.LiveCaptionDoubaoAppKey ?? string.Empty;
            _doubaoAccessKey = config?.LiveCaptionDoubaoAccessKey ?? string.Empty;
            _doubaoResourceId = string.IsNullOrWhiteSpace(config?.LiveCaptionDoubaoResourceId)
                ? DoubaoRealtimeDefaultResourceId
                : config.LiveCaptionDoubaoResourceId.Trim();

            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(25)
            };
        }

        public bool IsReady
        {
            get
            {
                if (IsBaiduProvider())
                {
                    return !string.IsNullOrWhiteSpace(_baiduAppId)
                        && !string.IsNullOrWhiteSpace(_baiduApiKey)
                        && !string.IsNullOrWhiteSpace(_baiduSecretKey);
                }
                if (IsTencentProvider())
                {
                    return !string.IsNullOrWhiteSpace(_tencentSecretId)
                        && !string.IsNullOrWhiteSpace(_tencentSecretKey);
                }
                if (IsAliyunProvider())
                {
                    return !string.IsNullOrWhiteSpace(_aliAppKey)
                        && !string.IsNullOrWhiteSpace(_aliAccessKeyId)
                        && !string.IsNullOrWhiteSpace(_aliAccessKeySecret);
                }
                if (IsDoubaoProvider())
                {
                    return !string.IsNullOrWhiteSpace(_doubaoAppKey)
                        && !string.IsNullOrWhiteSpace(_doubaoAccessKey);
                }
                if (IsFunAsrProvider())
                {
                    return !string.IsNullOrWhiteSpace(_funAsrWsUrl);
                }

                return !string.IsNullOrWhiteSpace(_apiKey);
            }
        }

        public async Task<string> TranscribeAudioAsync(byte[] wavBytes, CancellationToken cancellationToken)
        {
            if (wavBytes == null || wavBytes.Length == 0 || !IsReady)
            {
                LastError = "音频为空或ASR配置未完成";
                LastTranscribeStatusCode = 0;
                LastTranscribeElapsedMs = 0;
                return string.Empty;
            }

            if (IsBaiduProvider())
            {
                return await TranscribeWithBaiduAsync(wavBytes, cancellationToken);
            }
            if (IsTencentProvider())
            {
                return await TranscribeWithTencentAsync(wavBytes, cancellationToken);
            }
            if (IsAliyunProvider())
            {
                LastError = "阿里云当前仅接入实时识别，会话失败时不支持短语音回退";
                LastTranscribeStatusCode = 0;
                LastTranscribeElapsedMs = 0;
                LastTranscribeUrl = "https://nls-gateway-cn-shanghai.aliyuncs.com";
                return string.Empty;
            }
            if (IsDoubaoProvider())
            {
                LastError = "豆包当前仅接入实时识别，会话失败时不支持短语音回退";
                LastTranscribeStatusCode = 0;
                LastTranscribeElapsedMs = 0;
                LastTranscribeUrl = "wss://openspeech.bytedance.com/api/v3/sauc/bigmodel_async";
                return string.Empty;
            }
            if (IsFunAsrProvider())
            {
                LastError = "FunASR当前仅接入实时识别，会话失败时不支持短语音回退";
                LastTranscribeStatusCode = 0;
                LastTranscribeElapsedMs = 0;
                LastTranscribeUrl = _funAsrWsUrl;
                return string.Empty;
            }

            return await TranscribeWithOpenAiCompatAsync(wavBytes, cancellationToken);
        }

        public Task<bool> StartRealtimeSessionAsync(
            Action<LiveCaptionAsrText> onText,
            Action<string> onStatus,
            CancellationToken cancellationToken)
        {
            try
            {
                if (IsBaiduProvider())
                {
                    return StartBaiduRealtimeSessionSafeAsync(onText, onStatus, cancellationToken);
                }

                if (IsTencentProvider())
                {
                    return StartTencentRealtimeSessionSafeAsync(onText, onStatus, cancellationToken);
                }
                if (IsAliyunProvider())
                {
                    return StartAliyunRealtimeSessionSafeAsync(onText, onStatus, cancellationToken);
                }
                if (IsDoubaoProvider())
                {
                    return StartDoubaoRealtimeSessionSafeAsync(onText, onStatus, cancellationToken);
                }
                if (IsFunAsrProvider())
                {
                    return StartFunAsrRealtimeSessionSafeAsync(onText, onStatus, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                LastError = $"实时会话启动异常: {ex.Message}";
                onStatus?.Invoke(LastError);
                Debug.WriteLine($"[LiveCaption][Realtime][StartFail] {LastError}");
                return Task.FromResult(false);
            }

            LastError = "当前ASR服务未接入实时语音识别";
            onStatus?.Invoke(LastError);
            return Task.FromResult(false);
        }

        private async Task<bool> StartBaiduRealtimeSessionSafeAsync(Action<LiveCaptionAsrText> onText, Action<string> onStatus, CancellationToken cancellationToken)
        {
            try { return await StartBaiduRealtimeSessionAsync(onText, onStatus, cancellationToken); }
            catch (OperationCanceledException) { return false; }
            catch (Exception ex)
            {
                LastError = $"百度实时ASR启动异常: {ex.Message}";
                onStatus?.Invoke(LastError);
                Debug.WriteLine($"[LiveCaption][BaiduWS][StartFail] {LastError}");
                return false;
            }
        }

        private async Task<bool> StartTencentRealtimeSessionSafeAsync(Action<LiveCaptionAsrText> onText, Action<string> onStatus, CancellationToken cancellationToken)
        {
            try { return await StartTencentRealtimeSessionAsync(onText, onStatus, cancellationToken); }
            catch (OperationCanceledException) { return false; }
            catch (Exception ex)
            {
                LastError = $"腾讯实时ASR启动异常: {ex.Message}";
                onStatus?.Invoke(LastError);
                Debug.WriteLine($"[LiveCaption][TencentWS][StartFail] {LastError}");
                return false;
            }
        }

        private async Task<bool> StartAliyunRealtimeSessionSafeAsync(Action<LiveCaptionAsrText> onText, Action<string> onStatus, CancellationToken cancellationToken)
        {
            try { return await StartAliyunRealtimeSessionAsync(onText, onStatus, cancellationToken); }
            catch (OperationCanceledException) { return false; }
            catch (Exception ex)
            {
                LastError = $"阿里云实时ASR启动异常: {ex.Message}";
                onStatus?.Invoke(LastError);
                Debug.WriteLine($"[LiveCaption][AliyunWS][StartFail] {LastError}");
                return false;
            }
        }

        private async Task<bool> StartDoubaoRealtimeSessionSafeAsync(Action<LiveCaptionAsrText> onText, Action<string> onStatus, CancellationToken cancellationToken)
        {
            try { return await StartDoubaoRealtimeSessionAsync(onText, onStatus, cancellationToken); }
            catch (OperationCanceledException) { return false; }
            catch (Exception ex)
            {
                LastError = $"豆包实时ASR启动异常: {ex.Message}";
                onStatus?.Invoke(LastError);
                Debug.WriteLine($"[LiveCaption][DoubaoWS][StartFail] {LastError}");
                return false;
            }
        }

        private async Task<bool> StartFunAsrRealtimeSessionSafeAsync(Action<LiveCaptionAsrText> onText, Action<string> onStatus, CancellationToken cancellationToken)
        {
            try { return await StartFunAsrRealtimeSessionAsync(onText, onStatus, cancellationToken); }
            catch (OperationCanceledException) { return false; }
            catch (Exception ex)
            {
                LastError = $"FunASR实时启动异常: {ex.Message}";
                onStatus?.Invoke(LastError);
                Debug.WriteLine($"[LiveCaption][FunASRWS][StartFail] {LastError}");
                return false;
            }
        }

        public Task<bool> SendRealtimeAudioAsync(byte[] pcm16kMono, CancellationToken cancellationToken)
        {
            if (IsBaiduProvider())
            {
                return SendBaiduRealtimeAudioAsync(pcm16kMono, cancellationToken);
            }

            if (IsTencentProvider())
            {
                return SendTencentRealtimeAudioAsync(pcm16kMono, cancellationToken);
            }
            if (IsAliyunProvider())
            {
                return SendAliyunRealtimeAudioAsync(pcm16kMono, cancellationToken);
            }
            if (IsDoubaoProvider())
            {
                return SendDoubaoRealtimeAudioAsync(pcm16kMono, cancellationToken);
            }
            if (IsFunAsrProvider())
            {
                return SendFunAsrRealtimeAudioAsync(pcm16kMono, cancellationToken);
            }

            LastError = "当前ASR服务未接入实时语音识别";
            return Task.FromResult(false);
        }

        public Task StopRealtimeSessionAsync(CancellationToken cancellationToken)
        {
            if (IsBaiduProvider())
            {
                return StopBaiduRealtimeSessionAsync(cancellationToken);
            }

            if (IsTencentProvider())
            {
                return StopTencentRealtimeSessionAsync(cancellationToken);
            }
            if (IsAliyunProvider())
            {
                return StopAliyunRealtimeSessionAsync(cancellationToken);
            }
            if (IsDoubaoProvider())
            {
                return StopDoubaoRealtimeSessionAsync(cancellationToken);
            }
            if (IsFunAsrProvider())
            {
                return StopFunAsrRealtimeSessionAsync(cancellationToken);
            }

            return Task.CompletedTask;
        }

        public async Task<bool> StartBaiduRealtimeSessionAsync(
            Action<LiveCaptionAsrText> onText,
            Action<string> onStatus,
            CancellationToken cancellationToken)
        {
            if (!IsBaiduRealtimeAvailable)
            {
                LastError = "百度实时ASR配置不完整（需 AppID/API Key）";
                onStatus?.Invoke(LastError);
                return false;
            }

            if (!TryGetBaiduRealtimeAppId(out int appId, out string appIdError))
            {
                LastError = appIdError;
                onStatus?.Invoke(LastError);
                return false;
            }

            await _baiduRealtimeConnectLock.WaitAsync(cancellationToken);
            try
            {
                if (_baiduRealtimeWs != null && _baiduRealtimeWs.State == WebSocketState.Open)
                {
                    _baiduRealtimeTextHandler = onText;
                    _baiduRealtimeStatusHandler = onStatus;
                    return true;
                }

                _baiduRealtimeTextHandler = onText;
                _baiduRealtimeStatusHandler = onStatus;
                _baiduRealtimeSn = $"{Guid.NewGuid():D}-ws";
                string url = $"{BaiduRealtimeBaseUrl}?sn={Uri.EscapeDataString(_baiduRealtimeSn)}";
                LastTranscribeUrl = url;
                LastError = string.Empty;
                LastTranscribeStatusCode = 0;
                LastTranscribeElapsedMs = 0;

                _baiduRealtimeWs?.Dispose();
                _baiduRealtimeWs = new ClientWebSocket();
                _baiduRealtimeWs.Options.KeepAliveInterval = TimeSpan.FromSeconds(10);

                var sw = Stopwatch.StartNew();
                await _baiduRealtimeWs.ConnectAsync(new Uri(url), cancellationToken);
                sw.Stop();
                LastTranscribeElapsedMs = sw.ElapsedMilliseconds;

                string startPayload = BuildBaiduRealtimeStartFrameJson(appId);
                await _baiduRealtimeWs.SendAsync(
                    new ArraySegment<byte>(Encoding.UTF8.GetBytes(startPayload)),
                    WebSocketMessageType.Text,
                    true,
                    cancellationToken);

                _baiduRealtimeReceiveTask = Task.Run(() => ReceiveBaiduRealtimeLoopAsync(_baiduRealtimeWs, cancellationToken), cancellationToken);
                return true;
            }
            catch (Exception ex)
            {
                LastError = $"百度实时ASR连接失败: {ex.Message}";
                onStatus?.Invoke(LastError);
                return false;
            }
            finally
            {
                _baiduRealtimeConnectLock.Release();
            }
        }

        public async Task<bool> StartFunAsrRealtimeSessionAsync(
            Action<LiveCaptionAsrText> onText,
            Action<string> onStatus,
            CancellationToken cancellationToken)
        {
            if (!IsFunAsrRealtimeAvailable)
            {
                LastError = "FunASR 本地实时配置不完整";
                onStatus?.Invoke(LastError);
                return false;
            }

            await _funAsrRealtimeConnectLock.WaitAsync(cancellationToken);
            try
            {
                if (_funAsrRealtimeWs != null && _funAsrRealtimeWs.State == WebSocketState.Open)
                {
                    _funAsrRealtimeTextHandler = onText;
                    _funAsrRealtimeStatusHandler = onStatus;
                    return true;
                }

                _funAsrRealtimeTextHandler = onText;
                _funAsrRealtimeStatusHandler = onStatus;
                _funAsrRealtimeWs?.Dispose();
                _funAsrRealtimeWs = new ClientWebSocket();
                _funAsrRealtimeWs.Options.KeepAliveInterval = TimeSpan.FromSeconds(10);
                ApplyFunAsrTlsPolicy(_funAsrRealtimeWs.Options, _funAsrWsUrl);

                LastTranscribeUrl = _funAsrWsUrl;
                await _funAsrRealtimeWs.ConnectAsync(new Uri(_funAsrWsUrl), cancellationToken);

                // FunASR runtime 初始化帧。后续音频通过 binary 帧连续发送。
                // 同时携带新旧字段，兼容：
                // - 新协议：type=start/end + partial/result
                // - 旧协议：mode/is_speaking + is_final
                var initPayload = new
                {
                    type = "start",
                    language = "中文",
                    partial_interval_ms = 1200,
                    mode = "2pass",
                    wav_name = "live-caption",
                    wav_format = "pcm",
                    chunk_size = new[] { 5, 10, 5 },
                    chunk_interval = 10,
                    is_speaking = true,
                    audio_fs = 16000,
                    itn = true
                };
                byte[] payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(initPayload));
                await _funAsrRealtimeWs.SendAsync(
                    new ArraySegment<byte>(payload),
                    WebSocketMessageType.Text,
                    true,
                    cancellationToken);

                _funAsrRealtimeReceiveTask = Task.Run(
                    () => ReceiveFunAsrRealtimeLoopAsync(_funAsrRealtimeWs, cancellationToken),
                    cancellationToken);
                LastError = string.Empty;
                onStatus?.Invoke("FunASR 实时ASR连接成功");
                return true;
            }
            catch (Exception ex)
            {
                LastError = $"FunASR实时会话启动失败: {ex.Message}";
                onStatus?.Invoke(LastError);
                return false;
            }
            finally
            {
                _funAsrRealtimeConnectLock.Release();
            }
        }

        public async Task<bool> SendFunAsrRealtimeAudioAsync(byte[] pcm16kMono, CancellationToken cancellationToken)
        {
            if (pcm16kMono == null || pcm16kMono.Length == 0)
            {
                return false;
            }

            if (_funAsrRealtimeWs == null || _funAsrRealtimeWs.State != WebSocketState.Open)
            {
                LastError = "FunASR实时会话未连接";
                return false;
            }

            await _funAsrRealtimeSendLock.WaitAsync(cancellationToken);
            try
            {
                await _funAsrRealtimeWs.SendAsync(
                    new ArraySegment<byte>(pcm16kMono),
                    WebSocketMessageType.Binary,
                    true,
                    cancellationToken);
                return true;
            }
            catch (Exception ex)
            {
                LastError = $"FunASR实时音频发送失败: {ex.Message}";
                _funAsrRealtimeStatusHandler?.Invoke(LastError);
                return false;
            }
            finally
            {
                _funAsrRealtimeSendLock.Release();
            }
        }

        public async Task StopFunAsrRealtimeSessionAsync(CancellationToken cancellationToken)
        {
            await _funAsrRealtimeConnectLock.WaitAsync(cancellationToken);
            try
            {
                if (_funAsrRealtimeWs != null && _funAsrRealtimeWs.State == WebSocketState.Open)
                {
                    try
                    {
                        byte[] stopPayload = Encoding.UTF8.GetBytes("{\"type\":\"end\",\"is_speaking\":false}");
                        await _funAsrRealtimeWs.SendAsync(
                            new ArraySegment<byte>(stopPayload),
                            WebSocketMessageType.Text,
                            true,
                            cancellationToken);
                    }
                    catch
                    {
                        // ignore
                    }

                    try
                    {
                        await _funAsrRealtimeWs.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "stop", cancellationToken);
                    }
                    catch
                    {
                        // ignore
                    }
                }
            }
            finally
            {
                _funAsrRealtimeTextHandler = null;
                _funAsrRealtimeStatusHandler = null;
                if (_funAsrRealtimeWs != null)
                {
                    try
                    {
                        _funAsrRealtimeWs.Dispose();
                    }
                    catch
                    {
                    }

                    _funAsrRealtimeWs = null;
                }

                _funAsrRealtimeConnectLock.Release();
            }
        }

        private async Task ReceiveFunAsrRealtimeLoopAsync(ClientWebSocket ws, CancellationToken cancellationToken)
        {
            var buffer = new byte[32 * 1024];
            using var ms = new MemoryStream();
            try
            {
                while (ws != null && ws.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
                {
                    ms.SetLength(0);
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            return;
                        }

                        ms.Write(buffer, 0, result.Count);
                    }
                    while (!result.EndOfMessage);

                    if (result.MessageType != WebSocketMessageType.Text || ms.Length == 0)
                    {
                        continue;
                    }

                    string raw = Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
                    if (TryExtractFunAsrText(raw, out string text, out bool isFinal) && !string.IsNullOrWhiteSpace(text))
                    {
                        _funAsrRealtimeTextHandler?.Invoke(new LiveCaptionAsrText(text.Trim(), isFinal));
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // ignore
            }
            catch (Exception ex)
            {
                LastError = $"FunASR实时接收失败: {ex.Message}";
                _funAsrRealtimeStatusHandler?.Invoke(LastError);
            }
        }

        private static bool TryExtractFunAsrText(string json, out string text, out bool isFinal)
        {
            text = string.Empty;
            isFinal = false;
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            try
            {
                using JsonDocument doc = JsonDocument.Parse(json);
                JsonElement root = doc.RootElement;
                if (root.TryGetProperty("text", out JsonElement textElement))
                {
                    text = textElement.GetString() ?? string.Empty;
                }

                if (root.TryGetProperty("type", out JsonElement typeElement))
                {
                    string msgType = (typeElement.GetString() ?? string.Empty).Trim().ToLowerInvariant();
                    if (string.Equals(msgType, "result", StringComparison.Ordinal))
                    {
                        isFinal = true;
                    }
                    else if (string.Equals(msgType, "partial", StringComparison.Ordinal))
                    {
                        isFinal = false;
                    }
                }
                else if (root.TryGetProperty("is_final", out JsonElement finalElement) &&
                         finalElement.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    isFinal = finalElement.GetBoolean();
                }
                else if (root.TryGetProperty("mode", out JsonElement modeElement))
                {
                    string mode = (modeElement.GetString() ?? string.Empty).ToLowerInvariant();
                    isFinal = mode.Contains("offline", StringComparison.Ordinal);
                }
            }
            catch
            {
                return false;
            }

            return !string.IsNullOrWhiteSpace(text);
        }

        public async Task<bool> StartTencentRealtimeSessionAsync(
            Action<LiveCaptionAsrText> onText,
            Action<string> onStatus,
            CancellationToken cancellationToken)
        {
            if (!IsTencentRealtimeAvailable)
            {
                LastError = "腾讯实时ASR配置不完整（需 AppId/SecretId/SecretKey）";
                return false;
            }

            await _tencentRealtimeConnectLock.WaitAsync(cancellationToken);
            try
            {
                if (_tencentRealtimeWs != null && _tencentRealtimeWs.State == WebSocketState.Open)
                {
                    _tencentRealtimeTextHandler = onText;
                    _tencentRealtimeStatusHandler = onStatus;
                    return true;
                }

                _tencentRealtimeTextHandler = onText;
                _tencentRealtimeStatusHandler = onStatus;
                _tencentRealtimeVoiceId = Guid.NewGuid().ToString("N");
                string url = BuildTencentRealtimeWebSocketUrl(_tencentRealtimeVoiceId);
                if (!string.IsNullOrWhiteSpace(_tencentRealtimeParamSummary))
                {
                    Debug.WriteLine($"[LiveCaption][TencentWS][StartParams] {_tencentRealtimeParamSummary}");
                }
                LastTranscribeUrl = url;
                LastError = string.Empty;
                LastTranscribeStatusCode = 0;
                LastTranscribeElapsedMs = 0;

                _tencentRealtimeWs?.Dispose();
                _tencentRealtimeWs = new ClientWebSocket();
                _tencentRealtimeWs.Options.KeepAliveInterval = TimeSpan.FromSeconds(10);

                var sw = Stopwatch.StartNew();
                await _tencentRealtimeWs.ConnectAsync(new Uri(url), cancellationToken);
                sw.Stop();
                LastTranscribeElapsedMs = sw.ElapsedMilliseconds;
                _tencentRealtimeReceiveTask = Task.Run(() => ReceiveTencentRealtimeLoopAsync(_tencentRealtimeWs, cancellationToken), cancellationToken);
                return true;
            }
            catch (WebSocketException wsex)
            {
                LastError = $"腾讯实时ASR连接失败: {wsex.Message} (WebSocketError={wsex.WebSocketErrorCode})";
                Debug.WriteLine($"[LiveCaption][TencentWS][ConnectFail] {LastError}");
                onStatus?.Invoke(LastError);
                return false;
            }
            catch (Exception ex)
            {
                LastError = $"腾讯实时ASR连接失败: {ex.Message}";
                Debug.WriteLine($"[LiveCaption][TencentWS][ConnectFail] {LastError}");
                onStatus?.Invoke(LastError);
                return false;
            }
            finally
            {
                _tencentRealtimeConnectLock.Release();
            }
        }

        public async Task<bool> SendTencentRealtimeAudioAsync(byte[] pcm16kMono, CancellationToken cancellationToken)
        {
            if (pcm16kMono == null || pcm16kMono.Length == 0)
            {
                return false;
            }

            if (_tencentRealtimeWs == null || _tencentRealtimeWs.State != WebSocketState.Open)
            {
                LastError = "腾讯实时ASR连接未建立";
                return false;
            }

            await _tencentRealtimeSendLock.WaitAsync(cancellationToken);
            try
            {
                await _tencentRealtimeWs.SendAsync(
                    new ArraySegment<byte>(pcm16kMono),
                    WebSocketMessageType.Binary,
                    true,
                    cancellationToken);
                return true;
            }
            catch (WebSocketException wsex)
            {
                LastError = $"腾讯实时ASR发送失败: {wsex.Message} (WebSocketError={wsex.WebSocketErrorCode})";
                Debug.WriteLine($"[LiveCaption][TencentWS][SendFail] {LastError}");
                _tencentRealtimeStatusHandler?.Invoke(LastError);
                return false;
            }
            catch (Exception ex)
            {
                LastError = $"腾讯实时ASR发送失败: {ex.Message}";
                Debug.WriteLine($"[LiveCaption][TencentWS][SendFail] {LastError}");
                _tencentRealtimeStatusHandler?.Invoke(LastError);
                return false;
            }
            finally
            {
                _tencentRealtimeSendLock.Release();
            }
        }

        public async Task StopTencentRealtimeSessionAsync(CancellationToken cancellationToken)
        {
            try
            {
                if (_tencentRealtimeWs != null && _tencentRealtimeWs.State == WebSocketState.Open)
                {
                    byte[] endPayload = Encoding.UTF8.GetBytes("{\"type\":\"end\"}");
                    await _tencentRealtimeWs.SendAsync(
                        new ArraySegment<byte>(endPayload),
                        WebSocketMessageType.Text,
                        true,
                        cancellationToken);
                    try
                    {
                        await _tencentRealtimeWs.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "end", cancellationToken);
                    }
                    catch
                    {
                        // ignore
                    }
                }
            }
            catch
            {
                // ignore
            }
            finally
            {
                _tencentRealtimeVoiceId = string.Empty;
                _tencentRealtimeTextHandler = null;
                _tencentRealtimeStatusHandler = null;

                if (_tencentRealtimeWs != null)
                {
                    try
                    {
                        _tencentRealtimeWs.Dispose();
                    }
                    catch
                    {
                        // ignore
                    }
                    _tencentRealtimeWs = null;
                }
            }
        }

        public async Task<bool> StartAliyunRealtimeSessionAsync(
            Action<LiveCaptionAsrText> onText,
            Action<string> onStatus,
            CancellationToken cancellationToken)
        {
            if (!IsAliyunRealtimeAvailable)
            {
                LastError = "阿里云实时ASR配置不完整（需 AppKey/AccessKeyId/AccessKeySecret）";
                onStatus?.Invoke(LastError);
                return false;
            }

            await _aliyunRealtimeConnectLock.WaitAsync(cancellationToken);
            try
            {
                if (_aliyunRealtimeWs != null && _aliyunRealtimeWs.State == WebSocketState.Open)
                {
                    _aliyunRealtimeTextHandler = onText;
                    _aliyunRealtimeStatusHandler = onStatus;
                    return true;
                }

                string token = await GetAliyunAccessTokenAsync(cancellationToken);
                if (string.IsNullOrWhiteSpace(token))
                {
                    onStatus?.Invoke(string.IsNullOrWhiteSpace(LastError) ? "阿里云Token获取失败" : LastError);
                    return false;
                }

                _aliyunRealtimeTextHandler = onText;
                _aliyunRealtimeStatusHandler = onStatus;
                _aliyunRealtimeTaskId = Guid.NewGuid().ToString("N");
                string url = $"{AliyunRealtimeWsBaseUrl}?token={Uri.EscapeDataString(token)}";
                LastTranscribeUrl = url;
                LastError = string.Empty;
                LastTranscribeStatusCode = 0;
                LastTranscribeElapsedMs = 0;

                _aliyunRealtimeWs?.Dispose();
                _aliyunRealtimeWs = new ClientWebSocket();
                _aliyunRealtimeWs.Options.KeepAliveInterval = TimeSpan.FromSeconds(10);

                var sw = Stopwatch.StartNew();
                await _aliyunRealtimeWs.ConnectAsync(new Uri(url), cancellationToken);
                sw.Stop();
                LastTranscribeElapsedMs = sw.ElapsedMilliseconds;

                string startPayload = BuildAliyunRealtimeStartFrameJson(_aliyunRealtimeTaskId);
                await _aliyunRealtimeWs.SendAsync(
                    new ArraySegment<byte>(Encoding.UTF8.GetBytes(startPayload)),
                    WebSocketMessageType.Text,
                    true,
                    cancellationToken);

                _aliyunRealtimeReceiveTask = Task.Run(() => ReceiveAliyunRealtimeLoopAsync(_aliyunRealtimeWs, cancellationToken), cancellationToken);
                return true;
            }
            catch (Exception ex)
            {
                LastError = $"阿里云实时ASR连接失败: {ex.Message}";
                onStatus?.Invoke(LastError);
                return false;
            }
            finally
            {
                _aliyunRealtimeConnectLock.Release();
            }
        }

        public async Task<bool> SendAliyunRealtimeAudioAsync(byte[] pcm16kMono, CancellationToken cancellationToken)
        {
            if (pcm16kMono == null || pcm16kMono.Length == 0)
            {
                return false;
            }

            if (_aliyunRealtimeWs == null || _aliyunRealtimeWs.State != WebSocketState.Open)
            {
                LastError = "阿里云实时ASR连接未建立";
                return false;
            }

            await _aliyunRealtimeSendLock.WaitAsync(cancellationToken);
            try
            {
                await _aliyunRealtimeWs.SendAsync(
                    new ArraySegment<byte>(pcm16kMono),
                    WebSocketMessageType.Binary,
                    true,
                    cancellationToken);
                return true;
            }
            catch (Exception ex)
            {
                LastError = $"阿里云实时ASR发送失败: {ex.Message}";
                _aliyunRealtimeStatusHandler?.Invoke(LastError);
                return false;
            }
            finally
            {
                _aliyunRealtimeSendLock.Release();
            }
        }

        public async Task StopAliyunRealtimeSessionAsync(CancellationToken cancellationToken)
        {
            try
            {
                if (_aliyunRealtimeWs != null && _aliyunRealtimeWs.State == WebSocketState.Open)
                {
                    string stopPayload = BuildAliyunRealtimeStopFrameJson(_aliyunRealtimeTaskId);
                    await _aliyunRealtimeWs.SendAsync(
                        new ArraySegment<byte>(Encoding.UTF8.GetBytes(stopPayload)),
                        WebSocketMessageType.Text,
                        true,
                        cancellationToken);
                    try
                    {
                        await _aliyunRealtimeWs.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "stop", cancellationToken);
                    }
                    catch
                    {
                        // ignore
                    }
                }
            }
            catch
            {
                // ignore
            }
            finally
            {
                _aliyunRealtimeTaskId = string.Empty;
                _aliyunRealtimeTextHandler = null;
                _aliyunRealtimeStatusHandler = null;

                if (_aliyunRealtimeWs != null)
                {
                    try
                    {
                        _aliyunRealtimeWs.Dispose();
                    }
                    catch
                    {
                        // ignore
                    }

                    _aliyunRealtimeWs = null;
                }
            }
        }

        public async Task<bool> StartDoubaoRealtimeSessionAsync(
            Action<LiveCaptionAsrText> onText,
            Action<string> onStatus,
            CancellationToken cancellationToken)
        {
            if (!IsDoubaoRealtimeAvailable)
            {
                LastError = "豆包实时ASR配置不完整（需 App Key / Access Key）";
                onStatus?.Invoke(LastError);
                return false;
            }

            await _doubaoRealtimeConnectLock.WaitAsync(cancellationToken);
            try
            {
                if (_doubaoRealtimeWs != null && _doubaoRealtimeWs.State == WebSocketState.Open)
                {
                    _doubaoRealtimeTextHandler = onText;
                    _doubaoRealtimeStatusHandler = onStatus;
                    return true;
                }

                _doubaoRealtimeTextHandler = onText;
                _doubaoRealtimeStatusHandler = onStatus;
                LastTranscribeUrl = DoubaoRealtimeWsBaseUrl;
                LastError = string.Empty;
                LastTranscribeStatusCode = 0;
                LastTranscribeElapsedMs = 0;
                _doubaoConnectId = Guid.NewGuid().ToString("D");

                _doubaoRealtimeWs?.Dispose();
                _doubaoRealtimeWs = new ClientWebSocket();
                _doubaoRealtimeWs.Options.KeepAliveInterval = TimeSpan.FromSeconds(10);
                _doubaoRealtimeWs.Options.SetRequestHeader("X-Api-App-Key", _doubaoAppKey);
                _doubaoRealtimeWs.Options.SetRequestHeader("X-Api-Access-Key", _doubaoAccessKey);
                _doubaoRealtimeWs.Options.SetRequestHeader(
                    "X-Api-Resource-Id",
                    string.IsNullOrWhiteSpace(_doubaoResourceId) ? DoubaoRealtimeDefaultResourceId : _doubaoResourceId);
                _doubaoRealtimeWs.Options.SetRequestHeader("X-Api-Connect-Id", _doubaoConnectId);

                var sw = Stopwatch.StartNew();
                await _doubaoRealtimeWs.ConnectAsync(new Uri(DoubaoRealtimeWsBaseUrl), cancellationToken);
                sw.Stop();
                LastTranscribeElapsedMs = sw.ElapsedMilliseconds;

                byte[] startFrame = BuildDoubaoFullClientRequestFrame();
                await _doubaoRealtimeWs.SendAsync(
                    new ArraySegment<byte>(startFrame),
                    WebSocketMessageType.Binary,
                    true,
                    cancellationToken);

                _doubaoRealtimeReceiveTask = Task.Run(
                    () => ReceiveDoubaoRealtimeLoopAsync(_doubaoRealtimeWs, cancellationToken),
                    cancellationToken);
                onStatus?.Invoke("豆包实时ASR连接成功（教会直播预设）");
                return true;
            }
            catch (Exception ex)
            {
                LastError = $"豆包实时ASR连接失败: {ex.Message}";
                onStatus?.Invoke(LastError);
                return false;
            }
            finally
            {
                _doubaoRealtimeConnectLock.Release();
            }
        }

        public async Task<bool> SendDoubaoRealtimeAudioAsync(byte[] pcm16kMono, CancellationToken cancellationToken)
        {
            if (pcm16kMono == null || pcm16kMono.Length == 0)
            {
                return false;
            }

            if (_doubaoRealtimeWs == null || _doubaoRealtimeWs.State != WebSocketState.Open)
            {
                LastError = "豆包实时ASR连接未建立";
                return false;
            }

            await _doubaoRealtimeSendLock.WaitAsync(cancellationToken);
            try
            {
                byte[] audioFrame = BuildDoubaoAudioOnlyFrame(pcm16kMono, isLastPacket: false);
                await _doubaoRealtimeWs.SendAsync(
                    new ArraySegment<byte>(audioFrame),
                    WebSocketMessageType.Binary,
                    true,
                    cancellationToken);
                return true;
            }
            catch (Exception ex)
            {
                LastError = $"豆包实时ASR发送失败: {ex.Message}";
                _doubaoRealtimeStatusHandler?.Invoke(LastError);
                return false;
            }
            finally
            {
                _doubaoRealtimeSendLock.Release();
            }
        }

        public async Task StopDoubaoRealtimeSessionAsync(CancellationToken cancellationToken)
        {
            try
            {
                if (_doubaoRealtimeWs != null && _doubaoRealtimeWs.State == WebSocketState.Open)
                {
                    try
                    {
                        byte[] endFrame = BuildDoubaoAudioOnlyFrame(Array.Empty<byte>(), isLastPacket: true);
                        await _doubaoRealtimeWs.SendAsync(
                            new ArraySegment<byte>(endFrame),
                            WebSocketMessageType.Binary,
                            true,
                            cancellationToken);
                        await _doubaoRealtimeWs.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "stop", cancellationToken);
                    }
                    catch
                    {
                        // ignore
                    }
                }
            }
            catch
            {
                // ignore
            }
            finally
            {
                _doubaoConnectId = string.Empty;
                _doubaoRealtimeTextHandler = null;
                _doubaoRealtimeStatusHandler = null;

                if (_doubaoRealtimeWs != null)
                {
                    try
                    {
                        _doubaoRealtimeWs.Dispose();
                    }
                    catch
                    {
                        // ignore
                    }

                    _doubaoRealtimeWs = null;
                }
            }
        }

        public async Task<bool> SendBaiduRealtimeAudioAsync(byte[] pcm16kMono, CancellationToken cancellationToken)
        {
            if (pcm16kMono == null || pcm16kMono.Length == 0)
            {
                return false;
            }

            if (_baiduRealtimeWs == null || _baiduRealtimeWs.State != WebSocketState.Open)
            {
                LastError = "百度实时ASR连接未建立";
                return false;
            }

            await _baiduRealtimeSendLock.WaitAsync(cancellationToken);
            try
            {
                await _baiduRealtimeWs.SendAsync(
                    new ArraySegment<byte>(pcm16kMono),
                    WebSocketMessageType.Binary,
                    true,
                    cancellationToken);
                return true;
            }
            catch (Exception ex)
            {
                LastError = $"百度实时ASR发送失败: {ex.Message}";
                _baiduRealtimeStatusHandler?.Invoke(LastError);
                return false;
            }
            finally
            {
                _baiduRealtimeSendLock.Release();
            }
        }

        public async Task StopBaiduRealtimeSessionAsync(CancellationToken cancellationToken)
        {
            try
            {
                if (_baiduRealtimeWs != null && _baiduRealtimeWs.State == WebSocketState.Open)
                {
                    byte[] finishPayload = Encoding.UTF8.GetBytes("{\"type\":\"FINISH\"}");
                    await _baiduRealtimeWs.SendAsync(
                        new ArraySegment<byte>(finishPayload),
                        WebSocketMessageType.Text,
                        true,
                        cancellationToken);
                    try
                    {
                        await _baiduRealtimeWs.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "finish", cancellationToken);
                    }
                    catch
                    {
                        // ignore
                    }
                }
            }
            catch
            {
                // ignore
            }
            finally
            {
                _baiduRealtimeSn = string.Empty;
                _baiduRealtimeTextHandler = null;
                _baiduRealtimeStatusHandler = null;

                if (_baiduRealtimeWs != null)
                {
                    try
                    {
                        _baiduRealtimeWs.Dispose();
                    }
                    catch
                    {
                        // ignore
                    }

                    _baiduRealtimeWs = null;
                }
            }
        }

        private async Task ReceiveBaiduRealtimeLoopAsync(ClientWebSocket ws, CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[8192];
            try
            {
                while (ws.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
                {
                    string message = await ReadWebSocketTextMessageAsync(ws, buffer, cancellationToken);
                    if (message == null)
                    {
                        return;
                    }

                    HandleBaiduRealtimeMessage(message);
                }
            }
            catch (OperationCanceledException)
            {
                // ignore
            }
            catch (Exception ex)
            {
                LastError = $"百度实时ASR接收失败: {ex.Message}";
                _baiduRealtimeStatusHandler?.Invoke(LastError);
            }
        }

        private void HandleBaiduRealtimeMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            try
            {
                using JsonDocument doc = JsonDocument.Parse(message);
                JsonElement root = doc.RootElement;
                string type = root.TryGetProperty("type", out JsonElement typeEl)
                    ? (typeEl.GetString() ?? string.Empty).Trim().ToUpperInvariant()
                    : string.Empty;
                int errNo = root.TryGetProperty("err_no", out JsonElement errNoEl) && errNoEl.ValueKind == JsonValueKind.Number
                    ? errNoEl.GetInt32()
                    : 0;
                string errMsg = root.TryGetProperty("err_msg", out JsonElement errMsgEl)
                    ? errMsgEl.GetString() ?? string.Empty
                    : string.Empty;
                LastTranscribeStatusCode = errNo;

                if (string.Equals(type, "HEARTBEAT", StringComparison.Ordinal))
                {
                    return;
                }

                if (errNo != 0)
                {
                    LastError = $"百度实时ASR错误: {errNo} {errMsg}".Trim();
                    _baiduRealtimeStatusHandler?.Invoke(LastError);
                    _ = StopBaiduRealtimeSessionAsync(CancellationToken.None);
                    return;
                }

                LastError = string.Empty;
                if (string.Equals(type, "START", StringComparison.Ordinal))
                {
                    _baiduRealtimeStatusHandler?.Invoke("百度实时ASR连接成功");
                    return;
                }

                if (string.Equals(type, "FIN_TEXT", StringComparison.Ordinal)
                    || string.Equals(type, "MID_TEXT", StringComparison.Ordinal))
                {
                    string text = root.TryGetProperty("result", out JsonElement resultEl)
                        ? resultEl.GetString() ?? string.Empty
                        : string.Empty;
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        bool isFinal = string.Equals(type, "FIN_TEXT", StringComparison.Ordinal);
                        _baiduRealtimeTextHandler?.Invoke(new LiveCaptionAsrText(text.Trim(), isFinal));
                    }
                }
            }
            catch (Exception ex)
            {
                LastError = $"百度实时ASR消息解析失败: {ex.Message}";
                _baiduRealtimeStatusHandler?.Invoke(LastError);
            }
        }

        private static bool IsValidBaiduSn(string sn)
        {
            if (string.IsNullOrWhiteSpace(sn) || sn.Length > 128)
            {
                return false;
            }

            foreach (char ch in sn)
            {
                if (!(char.IsLetterOrDigit(ch) || ch == '-'))
                {
                    return false;
                }
            }

            return true;
        }

        private bool TryGetBaiduRealtimeAppId(out int appId, out string error)
        {
            appId = 0;
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(_baiduAppId))
            {
                error = "百度实时ASR配置错误：AppID 不能为空";
                return false;
            }

            if (!int.TryParse(_baiduAppId, NumberStyles.Integer, CultureInfo.InvariantCulture, out appId) || appId <= 0)
            {
                error = "百度实时ASR配置错误：AppID 必须是有效数字";
                return false;
            }

            return true;
        }

        private string BuildBaiduRealtimeStartFrameJson(int appId)
        {
            int devPid = _baiduDevPid > 0 ? _baiduDevPid : 15372;

            string cuid = string.IsNullOrWhiteSpace(_baiduAppId) ? Environment.MachineName : _baiduAppId;
            var startFrame = new
            {
                type = "START",
                data = new
                {
                    appid = appId,
                    appkey = _baiduApiKey,
                    dev_pid = devPid,
                    cuid,
                    sample = 16000,
                    format = "pcm"
                }
            };

            return JsonSerializer.Serialize(startFrame);
        }

        private async Task ReceiveTencentRealtimeLoopAsync(ClientWebSocket ws, CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[8192];
            try
            {
                while (ws.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
                {
                    string message = await ReadWebSocketTextMessageAsync(ws, buffer, cancellationToken);
                    if (message == null)
                    {
                        return;
                    }

                    HandleTencentRealtimeMessage(message);
                }
            }
            catch (OperationCanceledException)
            {
                // ignore
            }
            catch (Exception ex)
            {
                LastError = $"腾讯实时ASR接收失败: {ex.Message}";
                Debug.WriteLine($"[LiveCaption][TencentWS][ReceiveFail] {LastError}");
                _tencentRealtimeStatusHandler?.Invoke(LastError);
            }
        }

        private async Task ReceiveAliyunRealtimeLoopAsync(ClientWebSocket ws, CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[8192];
            try
            {
                while (ws.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
                {
                    string message = await ReadWebSocketTextMessageAsync(ws, buffer, cancellationToken);
                    if (message == null)
                    {
                        return;
                    }

                    HandleAliyunRealtimeMessage(message);
                }
            }
            catch (OperationCanceledException)
            {
                // ignore
            }
            catch (Exception ex)
            {
                LastError = $"阿里云实时ASR接收失败: {ex.Message}";
                _aliyunRealtimeStatusHandler?.Invoke(LastError);
            }
        }

        private async Task ReceiveDoubaoRealtimeLoopAsync(ClientWebSocket ws, CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[8192];
            try
            {
                while (ws.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
                {
                    (WebSocketMessageType type, byte[] payload) = await ReadWebSocketMessageAsync(ws, buffer, cancellationToken);
                    if (type == WebSocketMessageType.Close)
                    {
                        return;
                    }

                    if (payload == null || payload.Length == 0)
                    {
                        continue;
                    }

                    if (type == WebSocketMessageType.Text)
                    {
                        HandleDoubaoRealtimeMessage(Encoding.UTF8.GetString(payload));
                        continue;
                    }

                    HandleDoubaoRealtimeBinaryMessage(payload);
                }
            }
            catch (OperationCanceledException)
            {
                // ignore
            }
            catch (Exception ex)
            {
                LastError = $"豆包实时ASR接收失败: {ex.Message}";
                _doubaoRealtimeStatusHandler?.Invoke(LastError);
            }
        }

        private static async Task<(WebSocketMessageType type, byte[] payload)> ReadWebSocketMessageAsync(
            ClientWebSocket ws,
            byte[] buffer,
            CancellationToken cancellationToken)
        {
            using var ms = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return (WebSocketMessageType.Close, null);
                }

                if (result.Count > 0)
                {
                    ms.Write(buffer, 0, result.Count);
                }
            } while (!result.EndOfMessage);

            return (result.MessageType, ms.ToArray());
        }

        private static async Task<string> ReadWebSocketTextMessageAsync(ClientWebSocket ws, byte[] buffer, CancellationToken cancellationToken)
        {
            using var ms = new System.IO.MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return null;
                }

                if (result.Count > 0)
                {
                    ms.Write(buffer, 0, result.Count);
                }
            } while (!result.EndOfMessage);

            return Encoding.UTF8.GetString(ms.ToArray());
        }

        private byte[] BuildDoubaoFullClientRequestFrame()
        {
            string requestJson = BuildDoubaoInitialRequestPayloadJson();
            byte[] payload = GzipCompress(Encoding.UTF8.GetBytes(requestJson));
            return BuildDoubaoFrame(
                DoubaoMessageTypeFullClientRequest,
                DoubaoFlagNone,
                DoubaoSerializationJson,
                DoubaoCompressionGzip,
                payload,
                sequence: null);
        }

        private static byte[] BuildDoubaoAudioOnlyFrame(byte[] pcm16kMono, bool isLastPacket)
        {
            byte[] payload = GzipCompress(pcm16kMono ?? Array.Empty<byte>());
            return BuildDoubaoFrame(
                DoubaoMessageTypeAudioOnlyRequest,
                isLastPacket ? DoubaoFlagAudioLast : DoubaoFlagNone,
                DoubaoSerializationNone,
                DoubaoCompressionGzip,
                payload,
                sequence: null);
        }

        private string BuildDoubaoInitialRequestPayloadJson()
        {
            string uid = string.IsNullOrWhiteSpace(Environment.UserName)
                ? Guid.NewGuid().ToString("N")
                : Environment.UserName;
            string hotwordsContext = JsonSerializer.Serialize(new
            {
                hotwords = DefaultChurchHotwords
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Take(24)
                    .Select(x => new { word = x.Trim() })
                    .ToArray()
            });

            var payload = new
            {
                user = new
                {
                    uid
                },
                audio = new
                {
                    format = "pcm",
                    rate = 16000,
                    bits = 16,
                    channel = 1
                },
                request = new
                {
                    model_name = "bigmodel",
                    enable_itn = true,
                    enable_ddc = true,
                    enable_punc = true,
                    show_utterances = true,
                    result_type = "single",
                    enable_accelerate_text = true,
                    accelerate_score = 2,
                    enable_nonstream = true,
                    end_window_size = 700,
                    corpus = new
                    {
                        context = hotwordsContext
                    }
                }
            };

            return JsonSerializer.Serialize(payload);
        }

        private static byte[] BuildDoubaoFrame(
            byte messageType,
            byte messageFlags,
            byte serialization,
            byte compression,
            byte[] payload,
            int? sequence)
        {
            byte[] body = payload ?? Array.Empty<byte>();
            int totalLength = 4 + (sequence.HasValue ? 4 : 0) + 4 + body.Length;
            byte[] frame = new byte[totalLength];
            frame[0] = (byte)((DoubaoProtocolVersion << 4) | DoubaoHeaderWords);
            frame[1] = (byte)((messageType << 4) | (messageFlags & 0x0F));
            frame[2] = (byte)(((serialization & 0x0F) << 4) | (compression & 0x0F));
            frame[3] = 0;

            int offset = 4;
            if (sequence.HasValue)
            {
                WriteInt32BE(frame, offset, sequence.Value);
                offset += 4;
            }

            WriteInt32BE(frame, offset, body.Length);
            offset += 4;

            if (body.Length > 0)
            {
                Buffer.BlockCopy(body, 0, frame, offset, body.Length);
            }

            return frame;
        }

        private void HandleDoubaoRealtimeBinaryMessage(byte[] frameBytes)
        {
            if (frameBytes == null || frameBytes.Length < 8)
            {
                return;
            }

            int headerWords = frameBytes[0] & 0x0F;
            int headerBytes = Math.Max(4, headerWords * 4);
            if (frameBytes.Length < headerBytes + 4)
            {
                return;
            }

            byte messageType = (byte)(frameBytes[1] >> 4);
            byte messageFlags = (byte)(frameBytes[1] & 0x0F);
            byte serialization = (byte)((frameBytes[2] >> 4) & 0x0F);
            byte compression = (byte)(frameBytes[2] & 0x0F);

            // 服务端错误包结构: [header][code:int32][msg_size:uint32][msg_bytes]
            if (messageType == DoubaoMessageTypeServerError)
            {
                HandleDoubaoServerErrorFrame(frameBytes, headerBytes, serialization, compression, messageFlags);
                return;
            }

            int offset = headerBytes;
            int? sequence = null;
            if ((messageFlags & 0x1) != 0 && frameBytes.Length >= offset + 4)
            {
                sequence = ReadInt32BE(frameBytes, offset);
                offset += 4;
            }

            if (frameBytes.Length < offset + 4)
            {
                return;
            }

            uint payloadSizeRaw = ReadUInt32BE(frameBytes, offset);
            offset += 4;

            int available = Math.Max(0, frameBytes.Length - offset);
            int payloadSize = (int)Math.Min(payloadSizeRaw, (uint)available);
            if (payloadSize < 0)
            {
                payloadSize = 0;
            }

            byte[] payload = Array.Empty<byte>();
            if (payloadSize > 0)
            {
                payload = new byte[payloadSize];
                Buffer.BlockCopy(frameBytes, offset, payload, 0, payloadSize);
            }

            if (compression == DoubaoCompressionGzip && payload.Length > 0)
            {
                payload = GzipDecompress(payload);
            }

            if (messageType != DoubaoMessageTypeFullServerResponse)
            {
                return;
            }

            if (payload.Length == 0)
            {
                return;
            }

            if (serialization == DoubaoSerializationJson)
            {
                if (!LooksLikeJsonPayload(payload))
                {
                    _doubaoRealtimeStatusHandler?.Invoke(
                        $"豆包实时ASR协议包非JSON，已忽略（ser={serialization}, comp={compression}, len={payload.Length}, type={messageType}, flag={messageFlags}）");
                    return;
                }

                string json = Encoding.UTF8.GetString(payload);
                bool forceFinal = sequence.HasValue && sequence.Value < 0;
                HandleDoubaoRealtimeMessage(json, forceFinal);
                return;
            }

            string fallback = Encoding.UTF8.GetString(payload);
            if (!string.IsNullOrWhiteSpace(fallback) && (fallback[0] == '{' || fallback[0] == '['))
            {
                HandleDoubaoRealtimeMessage(fallback, forceFinal: sequence.HasValue && sequence.Value < 0);
            }
        }

        private void HandleDoubaoServerErrorFrame(byte[] frameBytes, int headerBytes, byte serialization, byte compression, byte messageFlags)
        {
            int offset = headerBytes;
            if (frameBytes.Length < offset + 4)
            {
                LastError = "豆包实时ASR错误: 错误包长度不足";
                _doubaoRealtimeStatusHandler?.Invoke(LastError);
                return;
            }

            // 格式A(文档描述): [code:int32][msg_size:uint32][msg_bytes]
            int codeA = 0;
            string msgA = string.Empty;
            bool formatAAccepted = false;
            if (frameBytes.Length >= offset + 8)
            {
                codeA = ReadInt32BE(frameBytes, offset);
                uint messageSizeA = ReadUInt32BE(frameBytes, offset + 4);
                int availableA = Math.Max(0, frameBytes.Length - (offset + 8));
                if (codeA != 0 && messageSizeA <= (uint)availableA && messageSizeA <= 256 * 1024)
                {
                    int copySizeA = (int)messageSizeA;
                    byte[] msgPayloadA = Array.Empty<byte>();
                    if (copySizeA > 0)
                    {
                        msgPayloadA = new byte[copySizeA];
                        Buffer.BlockCopy(frameBytes, offset + 8, msgPayloadA, 0, copySizeA);
                    }

                    if (compression == DoubaoCompressionGzip && msgPayloadA.Length > 0)
                    {
                        msgPayloadA = GzipDecompress(msgPayloadA);
                    }

                    msgA = DecodeUtf8TextPayload(msgPayloadA);
                    formatAAccepted = true;
                }
            }

            if (formatAAccepted)
            {
                HandleDoubaoServerErrorPayload(codeA, Encoding.UTF8.GetBytes(msgA ?? string.Empty), serialization);
                return;
            }

            // 格式B(兼容): [seq?][payload_size:uint32][payload]
            int offsetB = headerBytes;
            if ((messageFlags & 0x1) != 0 && frameBytes.Length >= offsetB + 4)
            {
                offsetB += 4;
            }

            if (frameBytes.Length < offsetB + 4)
            {
                LastError = "豆包实时ASR错误: 错误包结构无法解析";
                _doubaoRealtimeStatusHandler?.Invoke(LastError);
                return;
            }

            uint payloadSizeRaw = ReadUInt32BE(frameBytes, offsetB);
            offsetB += 4;
            int availableB = Math.Max(0, frameBytes.Length - offsetB);
            int payloadSizeB = (int)Math.Min(payloadSizeRaw, (uint)availableB);
            byte[] payloadB = Array.Empty<byte>();
            if (payloadSizeB > 0)
            {
                payloadB = new byte[payloadSizeB];
                Buffer.BlockCopy(frameBytes, offsetB, payloadB, 0, payloadSizeB);
            }

            if (compression == DoubaoCompressionGzip && payloadB.Length > 0)
            {
                payloadB = GzipDecompress(payloadB);
            }

            HandleDoubaoServerErrorPayload(codeA, payloadB, serialization);
        }

        private void HandleDoubaoServerErrorPayload(int code, byte[] payload, byte serialization)
        {
            string errMsg = string.Empty;

            try
            {
                if (payload == null || payload.Length == 0)
                {
                    LastError = "豆包实时ASR错误: 服务端返回空错误包";
                    _doubaoRealtimeStatusHandler?.Invoke(LastError);
                    return;
                }

                string payloadText = DecodeUtf8TextPayload(payload);
                if (!string.IsNullOrWhiteSpace(payloadText) && (serialization == DoubaoSerializationJson || LooksLikeJsonText(payloadText)))
                {
                    using JsonDocument doc = JsonDocument.Parse(payloadText);
                    JsonElement root = doc.RootElement;
                    code = TryGetInt(root, "code")
                        ?? TryGetInt(root, "status_code")
                        ?? TryGetIntFromObject(root, "error", "code")
                        ?? code;
                    errMsg = TryGetString(root, "message")
                        ?? TryGetString(root, "msg")
                        ?? TryGetStringFromObject(root, "error", "message")
                        ?? "unknown error";
                }
                else
                {
                    errMsg = payloadText;
                }
            }
            catch (Exception ex)
            {
                errMsg = $"解析失败: {ex.Message}";
            }

            LastTranscribeStatusCode = code;
            LastError = $"豆包实时ASR错误: {code} {errMsg}".Trim();
            _doubaoRealtimeStatusHandler?.Invoke(LastError);
        }

        private static bool LooksLikeJsonText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            string trimmed = text.TrimStart();
            return trimmed.StartsWith("{", StringComparison.Ordinal) || trimmed.StartsWith("[", StringComparison.Ordinal);
        }

        private static string DecodeUtf8TextPayload(byte[] payload)
        {
            if (payload == null || payload.Length == 0)
            {
                return string.Empty;
            }

            int start = 0;
            while (start < payload.Length && (payload[start] == 0 || char.IsWhiteSpace((char)payload[start])))
            {
                start++;
            }

            int end = payload.Length - 1;
            while (end >= start && (payload[end] == 0 || char.IsWhiteSpace((char)payload[end])))
            {
                end--;
            }

            if (end < start)
            {
                return string.Empty;
            }

            return Encoding.UTF8.GetString(payload, start, end - start + 1);
        }

        private static bool LooksLikeJsonPayload(byte[] payload)
        {
            if (payload == null || payload.Length == 0)
            {
                return false;
            }

            int i = 0;
            while (i < payload.Length && char.IsWhiteSpace((char)payload[i]))
            {
                i++;
            }

            if (i >= payload.Length)
            {
                return false;
            }

            byte b = payload[i];
            return b == (byte)'{' || b == (byte)'[';
        }

        private static void WriteInt32BE(byte[] target, int offset, int value)
        {
            target[offset] = (byte)((value >> 24) & 0xFF);
            target[offset + 1] = (byte)((value >> 16) & 0xFF);
            target[offset + 2] = (byte)((value >> 8) & 0xFF);
            target[offset + 3] = (byte)(value & 0xFF);
        }

        private static int ReadInt32BE(byte[] source, int offset)
        {
            return (source[offset] << 24)
                 | (source[offset + 1] << 16)
                 | (source[offset + 2] << 8)
                 | source[offset + 3];
        }

        private static uint ReadUInt32BE(byte[] source, int offset)
        {
            return ((uint)source[offset] << 24)
                 | ((uint)source[offset + 1] << 16)
                 | ((uint)source[offset + 2] << 8)
                 | source[offset + 3];
        }

        private static byte[] GzipCompress(byte[] data)
        {
            if (data == null || data.Length == 0)
            {
                return Array.Empty<byte>();
            }

            using var output = new MemoryStream();
            using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
            {
                gzip.Write(data, 0, data.Length);
            }

            return output.ToArray();
        }

        private static byte[] GzipDecompress(byte[] data)
        {
            if (data == null || data.Length == 0)
            {
                return Array.Empty<byte>();
            }

            try
            {
                using var input = new MemoryStream(data);
                using var gzip = new GZipStream(input, CompressionMode.Decompress);
                using var output = new MemoryStream();
                gzip.CopyTo(output);
                return output.ToArray();
            }
            catch
            {
                // 某些错误包会标记为gzip但内容异常，保底返回原始数据。
                return data;
            }
        }

        private void HandleAliyunRealtimeMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            try
            {
                using JsonDocument doc = JsonDocument.Parse(message);
                JsonElement root = doc.RootElement;
                JsonElement header = root.TryGetProperty("header", out JsonElement headerEl) && headerEl.ValueKind == JsonValueKind.Object
                    ? headerEl
                    : default;

                string name = header.ValueKind == JsonValueKind.Object && header.TryGetProperty("name", out JsonElement nameEl)
                    ? nameEl.GetString() ?? string.Empty
                    : string.Empty;
                int status = header.ValueKind == JsonValueKind.Object && header.TryGetProperty("status", out JsonElement statusEl) && statusEl.ValueKind == JsonValueKind.Number
                    ? statusEl.GetInt32()
                    : 0;
                string statusText = header.ValueKind == JsonValueKind.Object && header.TryGetProperty("status_text", out JsonElement statusTextEl)
                    ? statusTextEl.GetString() ?? string.Empty
                    : string.Empty;

                if (status != 0)
                {
                    LastTranscribeStatusCode = status;
                    if (status != 20000000)
                    {
                        LastError = $"阿里云实时ASR错误: {status} {statusText}".Trim();
                        _aliyunRealtimeStatusHandler?.Invoke(LastError);
                        return;
                    }
                }

                LastError = string.Empty;
                if (string.Equals(name, "TranscriptionStarted", StringComparison.OrdinalIgnoreCase))
                {
                    _aliyunRealtimeStatusHandler?.Invoke("阿里云实时ASR连接成功");
                    return;
                }

                if (!root.TryGetProperty("payload", out JsonElement payloadEl) || payloadEl.ValueKind != JsonValueKind.Object)
                {
                    return;
                }

                string text = payloadEl.TryGetProperty("result", out JsonElement resultEl)
                    ? resultEl.GetString() ?? string.Empty
                    : string.Empty;
                if (string.IsNullOrWhiteSpace(text))
                {
                    return;
                }

                if (string.Equals(name, "TranscriptionResultChanged", StringComparison.OrdinalIgnoreCase))
                {
                    _aliyunRealtimeTextHandler?.Invoke(new LiveCaptionAsrText(text.Trim(), false));
                    return;
                }

                if (string.Equals(name, "SentenceEnd", StringComparison.OrdinalIgnoreCase))
                {
                    _aliyunRealtimeTextHandler?.Invoke(new LiveCaptionAsrText(text.Trim(), true));
                    return;
                }

                if (string.Equals(name, "TranscriptionCompleted", StringComparison.OrdinalIgnoreCase))
                {
                    _aliyunRealtimeStatusHandler?.Invoke("阿里云实时ASR会话结束");
                }
            }
            catch (Exception ex)
            {
                LastError = $"阿里云实时ASR消息解析失败: {ex.Message}";
                _aliyunRealtimeStatusHandler?.Invoke(LastError);
            }
        }

        private void HandleTencentRealtimeMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            try
            {
                using JsonDocument doc = JsonDocument.Parse(message);
                JsonElement root = doc.RootElement;

                if (root.TryGetProperty("code", out JsonElement codeEl))
                {
                    int code = codeEl.GetInt32();
                    LastTranscribeStatusCode = code;
                    if (code != 0)
                    {
                        string msg = root.TryGetProperty("message", out JsonElement msgEl) ? msgEl.GetString() ?? string.Empty : string.Empty;
                        LastError = $"腾讯实时ASR错误: {code} {msg}".Trim();
                        Debug.WriteLine($"[LiveCaption][TencentWS][ServerError] code={code}, message='{msg}', params={_tencentRealtimeParamSummary}");
                        _tencentRealtimeStatusHandler?.Invoke(LastError);
                        return;
                    }
                }

                bool final = root.TryGetProperty("final", out JsonElement finalEl) && finalEl.ValueKind == JsonValueKind.Number && finalEl.GetInt32() == 1;

                if (root.TryGetProperty("result", out JsonElement resultEl) && resultEl.ValueKind == JsonValueKind.Object)
                {
                    int sliceType = resultEl.TryGetProperty("slice_type", out JsonElement sliceEl) && sliceEl.ValueKind == JsonValueKind.Number
                        ? sliceEl.GetInt32()
                        : -1;
                    if (resultEl.TryGetProperty("voice_text_str", out JsonElement textEl))
                    {
                        string text = textEl.GetString() ?? string.Empty;
                        // 极速模式：实时回传中间+最终分片，让UI按增量逻辑渲染。
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            LastError = string.Empty;
                            bool isFinalSlice = sliceType == 2;
                            _tencentRealtimeTextHandler?.Invoke(new LiveCaptionAsrText(text.Trim(), isFinalSlice));
                        }
                    }
                }

                if (final)
                {
                    _tencentRealtimeStatusHandler?.Invoke("腾讯实时ASR会话结束");
                }
            }
            catch (Exception ex)
            {
                LastError = $"腾讯实时ASR消息解析失败: {ex.Message}";
                _tencentRealtimeStatusHandler?.Invoke(LastError);
            }
        }

        private void HandleDoubaoRealtimeMessage(string message, bool forceFinal = false)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            try
            {
                using JsonDocument doc = JsonDocument.Parse(message);
                JsonElement root = doc.RootElement;

                int code = TryGetInt(root, "code")
                    ?? TryGetIntFromObject(root, "result", "code")
                    ?? 0;
                LastTranscribeStatusCode = code;
                if (code != 0)
                {
                    string errMsg = TryGetString(root, "message")
                        ?? TryGetString(root, "msg")
                        ?? TryGetStringFromObject(root, "result", "message")
                        ?? string.Empty;
                    LastError = $"豆包实时ASR错误: {code} {errMsg}".Trim();
                    _doubaoRealtimeStatusHandler?.Invoke(LastError);
                    return;
                }

                LastError = string.Empty;
                string text = ExtractDoubaoRealtimeText(root);
                if (string.IsNullOrWhiteSpace(text))
                {
                    return;
                }

                bool isFinal = forceFinal || IsDoubaoFinalResult(root);
                _doubaoRealtimeTextHandler?.Invoke(new LiveCaptionAsrText(text.Trim(), isFinal));
            }
            catch (Exception ex)
            {
                LastError = $"豆包实时ASR消息解析失败: {ex.Message}";
                _doubaoRealtimeStatusHandler?.Invoke(LastError);
            }
        }

        private static string ExtractDoubaoRealtimeText(JsonElement root)
        {
            string directText = TryGetString(root, "text");
            if (!string.IsNullOrWhiteSpace(directText))
            {
                return directText;
            }

            if (root.TryGetProperty("result", out JsonElement resultEl))
            {
                string t = TryGetString(resultEl, "text")
                    ?? TryGetString(resultEl, "partial_result")
                    ?? TryGetString(resultEl, "final_result")
                    ?? TryGetString(resultEl, "utterance");
                if (!string.IsNullOrWhiteSpace(t))
                {
                    return t;
                }
            }

            if (root.TryGetProperty("payload", out JsonElement payloadEl))
            {
                string t = TryGetString(payloadEl, "text")
                    ?? TryGetString(payloadEl, "result");
                if (!string.IsNullOrWhiteSpace(t))
                {
                    return t;
                }
            }

            return string.Empty;
        }

        private static bool IsDoubaoFinalResult(JsonElement root)
        {
            bool? final =
                TryGetBool(root, "is_final")
                ?? TryGetBool(root, "final")
                ?? TryGetBoolFromObject(root, "result", "is_final")
                ?? TryGetBoolFromObject(root, "result", "final")
                ?? TryGetBoolFromObject(root, "payload", "is_final")
                ?? TryGetBoolFromObject(root, "payload", "final");

            if (final.HasValue)
            {
                return final.Value;
            }

            string eventName = TryGetString(root, "event")
                ?? TryGetString(root, "type")
                ?? TryGetString(root, "message_type")
                ?? string.Empty;
            eventName = eventName.Trim().ToLowerInvariant();
            return eventName.Contains("final", StringComparison.Ordinal)
                || eventName.Contains("end", StringComparison.Ordinal)
                || eventName.Contains("sentence", StringComparison.Ordinal);
        }

        private static int? TryGetInt(JsonElement root, string propertyName)
        {
            if (!root.TryGetProperty(propertyName, out JsonElement valueEl))
            {
                return null;
            }

            return valueEl.ValueKind switch
            {
                JsonValueKind.Number => valueEl.GetInt32(),
                JsonValueKind.String when int.TryParse(valueEl.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) => v,
                _ => null
            };
        }

        private static int? TryGetIntFromObject(JsonElement root, string objName, string propertyName)
        {
            if (!root.TryGetProperty(objName, out JsonElement objEl) || objEl.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            return TryGetInt(objEl, propertyName);
        }

        private static bool? TryGetBool(JsonElement root, string propertyName)
        {
            if (!root.TryGetProperty(propertyName, out JsonElement valueEl))
            {
                return null;
            }

            return valueEl.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Number => valueEl.GetInt32() != 0,
                JsonValueKind.String when bool.TryParse(valueEl.GetString(), out bool b) => b,
                JsonValueKind.String when int.TryParse(valueEl.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) => n != 0,
                _ => null
            };
        }

        private static bool? TryGetBoolFromObject(JsonElement root, string objName, string propertyName)
        {
            if (!root.TryGetProperty(objName, out JsonElement objEl) || objEl.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            return TryGetBool(objEl, propertyName);
        }

        private static string TryGetString(JsonElement root, string propertyName)
        {
            if (!root.TryGetProperty(propertyName, out JsonElement valueEl))
            {
                return null;
            }

            if (valueEl.ValueKind == JsonValueKind.String)
            {
                return valueEl.GetString();
            }

            return null;
        }

        private static string TryGetStringFromObject(JsonElement root, string objName, string propertyName)
        {
            if (!root.TryGetProperty(objName, out JsonElement objEl) || objEl.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            return TryGetString(objEl, propertyName);
        }

        private string BuildTencentRealtimeWebSocketUrl(string voiceId)
        {
            string engineType = ResolveTencentEngineModelType(_asrModel);
            long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            long expired = timestamp + 3600;
            long nonce = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["convert_num_mode"] = "1",
                ["engine_model_type"] = engineType,
                ["expired"] = expired.ToString(),
                ["filter_dirty"] = "0",
                ["filter_empty_result"] = "1",
                ["filter_modal"] = "0",
                ["filter_punc"] = "0",
                ["needvad"] = "1",
                ["nonce"] = nonce.ToString(),
                ["secretid"] = _tencentSecretId,
                ["timestamp"] = timestamp.ToString(),
                ["vad_silence_time"] = "1000",
                ["voice_format"] = "1",
                ["voice_id"] = voiceId
            };
            if (!string.IsNullOrWhiteSpace(_tencentCustomizationId))
            {
                parameters["customization_id"] = _tencentCustomizationId.Trim();
            }
            _tencentRealtimeParamSummary =
                $"engine_model_type={engineType}, customization_id={(_tencentCustomizationId ?? string.Empty).Trim()}, needvad={parameters["needvad"]}, vad_silence_time={parameters["vad_silence_time"]}, voice_id={voiceId}";

            string canonicalQuery = string.Join("&", parameters.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => $"{kv.Key}={kv.Value}"));
            string signSource = $"{TencentRealtimeHost}{TencentRealtimePathPrefix}/{_tencentAppId}?{canonicalQuery}";
            string signature;
            using (var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(_tencentSecretKey)))
            {
                signature = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(signSource)));
            }

            string encodedQuery = string.Join("&", parameters.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value)}"));
            string encodedSignature = Uri.EscapeDataString(signature);
            return $"wss://{TencentRealtimeHost}{TencentRealtimePathPrefix}/{_tencentAppId}?{encodedQuery}&signature={encodedSignature}";
        }

        private static string ResolveTencentEngineModelType(string asrModel)
        {
            string raw = (asrModel ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(raw))
            {
                return "16k_zh";
            }

            string normalized = raw.ToLowerInvariant();
            return normalized switch
            {
                "tencent-realtime" => "16k_zh",
                "tencent" => "16k_zh",
                "default" => "16k_zh",
                "16k_zh" => "16k_zh",
                "16k_zh_en" => "16k_zh_en",
                "16k_en" => "16k_en",
                "16k_zh_large" => "16k_zh_large",
                "16k_en_large" => "16k_en_large",
                "8k_zh" => "8k_zh",
                "8k_zh_finance" => "8k_zh_finance",
                "8k_zh_large" => "8k_zh_large",
                _ when normalized.Contains("gpt-") => "16k_zh",
                // 兜底：未知值（例如误传 customization_id）不作为引擎类型，回退默认16k_zh
                _ => "16k_zh"
            };
        }

        private string BuildAliyunRealtimeStartFrameJson(string taskId)
        {
            var frame = new
            {
                header = new
                {
                    appkey = _aliAppKey,
                    message_id = Guid.NewGuid().ToString("N"),
                    task_id = taskId,
                    @namespace = "SpeechTranscriber",
                    name = "StartTranscription"
                },
                payload = new
                {
                    format = "pcm",
                    sample_rate = 16000,
                    enable_intermediate_result = true,
                    enable_punctuation_prediction = true,
                    enable_inverse_text_normalization = true,
                    max_sentence_silence = 1200
                }
            };
            return JsonSerializer.Serialize(frame);
        }

        private static string BuildAliyunRealtimeStopFrameJson(string taskId)
        {
            var frame = new
            {
                header = new
                {
                    message_id = Guid.NewGuid().ToString("N"),
                    task_id = taskId,
                    @namespace = "SpeechTranscriber",
                    name = "StopTranscription"
                }
            };
            return JsonSerializer.Serialize(frame);
        }

        private async Task<string> TranscribeWithOpenAiCompatAsync(byte[] wavBytes, CancellationToken cancellationToken)
        {
            foreach (var endpoint in BuildCandidateUrls("audio/transcriptions"))
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
                {
                    Content = CreateTranscribeForm(wavBytes)
                };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

                LastTranscribeUrl = endpoint;
                var sw = Stopwatch.StartNew();
                using var response = await _httpClient.SendAsync(request, cancellationToken);
                sw.Stop();
                LastTranscribeElapsedMs = sw.ElapsedMilliseconds;
                LastTranscribeStatusCode = (int)response.StatusCode;

                if (response.IsSuccessStatusCode)
                {
                    var payload = await response.Content.ReadFromJsonAsync<OpenAiTranscriptionResponse>(cancellationToken: cancellationToken);
                    if (payload == null || string.IsNullOrWhiteSpace(payload.Text))
                    {
                        LastError = "转写响应为空";
                        return string.Empty;
                    }

                    LastError = string.Empty;
                    return payload.Text.Trim();
                }

                if (response.StatusCode != System.Net.HttpStatusCode.NotFound)
                {
                    LastError = $"转写请求失败: HTTP {(int)response.StatusCode}";
                    return string.Empty;
                }
            }

            LastError = $"转写请求失败: HTTP {LastTranscribeStatusCode}";
            return string.Empty;
        }

        private async Task<string> TranscribeWithBaiduAsync(byte[] wavBytes, CancellationToken cancellationToken)
        {
            string token = await GetBaiduAccessTokenAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(token))
            {
                return string.Empty;
            }

            LastTranscribeUrl = ResolveBaiduShortSpeechEndpoint(_asrModel);
            int shortSpeechDevPid = ResolveBaiduShortSpeechDevPid(_asrModel, _baiduDevPid);
            var body = new
            {
                format = "wav",
                rate = 16000,
                channel = 1,
                token,
                cuid = string.IsNullOrWhiteSpace(_baiduAppId) ? Environment.MachineName : _baiduAppId,
                dev_pid = shortSpeechDevPid,
                speech = Convert.ToBase64String(wavBytes),
                len = wavBytes.Length
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, LastTranscribeUrl)
            {
                Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
            };

            var sw = Stopwatch.StartNew();
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            sw.Stop();
            LastTranscribeElapsedMs = sw.ElapsedMilliseconds;
            LastTranscribeStatusCode = (int)response.StatusCode;
            if (!response.IsSuccessStatusCode)
            {
                LastError = $"百度ASR请求失败: HTTP {(int)response.StatusCode}";
                return string.Empty;
            }

            string json = await response.Content.ReadAsStringAsync(cancellationToken);
            BaiduAsrResponse payload = null;
            try
            {
                payload = JsonSerializer.Deserialize<BaiduAsrResponse>(json);
            }
            catch
            {
                // ignored
            }

            if (payload == null)
            {
                LastError = "百度ASR响应解析失败";
                return string.Empty;
            }

            if (payload.ErrNo != 0)
            {
                LastError = $"百度ASR错误: {payload.ErrNo} {payload.ErrMsg}";
                return string.Empty;
            }

            if (payload.Result == null || payload.Result.Length == 0 || string.IsNullOrWhiteSpace(payload.Result[0]))
            {
                LastError = "百度ASR无文本结果";
                return string.Empty;
            }

            LastError = string.Empty;
            return payload.Result[0].Trim();
        }

        private static string ResolveBaiduShortSpeechEndpoint(string modelId)
        {
            return IsBaiduShortSpeechProModel(modelId)
                ? "https://vop.baidu.com/pro_api"
                : "http://vop.baidu.com/server_api";
        }

        private static int ResolveBaiduShortSpeechDevPid(string modelId, int configuredDevPid)
        {
            bool isProModel = IsBaiduShortSpeechProModel(modelId);
            if (configuredDevPid > 0)
            {
                if (isProModel && configuredDevPid == 1537)
                {
                    return 80001;
                }

                if (!isProModel && configuredDevPid == 80001)
                {
                    return 1537;
                }

                return configuredDevPid;
            }

            return isProModel ? 80001 : 1537;
        }

        private static bool IsBaiduShortSpeechProModel(string modelId)
        {
            if (string.IsNullOrWhiteSpace(modelId))
            {
                return false;
            }

            string normalized = modelId.Trim().ToLowerInvariant();
            return normalized.Contains("short-pro", StringComparison.Ordinal)
                || normalized.Contains("pro_api", StringComparison.Ordinal)
                || normalized.Contains("极速", StringComparison.Ordinal)
                || normalized.Contains("80001", StringComparison.Ordinal);
        }

        private async Task<string> TranscribeWithTencentAsync(byte[] wavBytes, CancellationToken cancellationToken)
        {
            LastTranscribeUrl = "https://asr.tencentcloudapi.com";
            string engineType = string.IsNullOrWhiteSpace(_asrModel) || _asrModel.Contains("gpt-", StringComparison.OrdinalIgnoreCase)
                ? "16k_zh"
                : _asrModel;
            string customizationId = (_tencentCustomizationId ?? string.Empty).Trim();
            var requestPayload = new Dictionary<string, object>
            {
                ["ProjectId"] = 0,
                ["SubServiceType"] = 2,
                ["EngSerViceType"] = engineType,
                ["SourceType"] = 1,
                ["VoiceFormat"] = "wav",
                ["Data"] = Convert.ToBase64String(wavBytes),
                ["DataLen"] = wavBytes.Length
            };
            if (!string.IsNullOrWhiteSpace(customizationId))
            {
                requestPayload["CustomizationId"] = customizationId;
            }
            string bodyJson = JsonSerializer.Serialize(requestPayload);
            Debug.WriteLine($"[LiveCaption][TencentShort] request: engineType={engineType}, customizationId={(string.IsNullOrWhiteSpace(customizationId) ? "(none)" : customizationId)}, wavBytes={wavBytes.Length}");

            long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            string date = DateTimeOffset.FromUnixTimeSeconds(timestamp).UtcDateTime.ToString("yyyy-MM-dd");
            string authorization = BuildTencentAuthorization(
                _tencentSecretId,
                _tencentSecretKey,
                "asr.tencentcloudapi.com",
                "asr",
                "SentenceRecognition",
                _tencentVersion,
                timestamp,
                date,
                bodyJson);

            using var request = new HttpRequestMessage(HttpMethod.Post, LastTranscribeUrl)
            {
                Content = new StringContent(bodyJson, Encoding.UTF8, "application/json")
            };
            request.Headers.TryAddWithoutValidation("Authorization", authorization);
            request.Headers.TryAddWithoutValidation("X-TC-Action", "SentenceRecognition");
            request.Headers.TryAddWithoutValidation("X-TC-Version", _tencentVersion);
            request.Headers.TryAddWithoutValidation("X-TC-Region", _tencentRegion);
            request.Headers.TryAddWithoutValidation("X-TC-Timestamp", timestamp.ToString());

            var sw = Stopwatch.StartNew();
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            sw.Stop();
            LastTranscribeElapsedMs = sw.ElapsedMilliseconds;
            LastTranscribeStatusCode = (int)response.StatusCode;
            string responseText = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                LastError = $"腾讯ASR请求失败: HTTP {(int)response.StatusCode}";
                return string.Empty;
            }

            TencentSentenceRecognitionEnvelope responseEnvelope = null;
            try
            {
                responseEnvelope = JsonSerializer.Deserialize<TencentSentenceRecognitionEnvelope>(responseText);
            }
            catch
            {
                // ignored
            }

            if (responseEnvelope?.Response == null)
            {
                LastError = "腾讯ASR响应解析失败";
                return string.Empty;
            }

            if (responseEnvelope.Response.Error != null && !string.IsNullOrWhiteSpace(responseEnvelope.Response.Error.Code))
            {
                LastError = $"腾讯ASR错误: {responseEnvelope.Response.Error.Code} {responseEnvelope.Response.Error.Message}";
                return string.Empty;
            }

            if (string.IsNullOrWhiteSpace(responseEnvelope.Response.Result))
            {
                LastError = "腾讯ASR无文本结果";
                return string.Empty;
            }

            LastError = string.Empty;
            return responseEnvelope.Response.Result.Trim();
        }

        private static string BuildTencentAuthorization(
            string secretId,
            string secretKey,
            string host,
            string service,
            string action,
            string version,
            long timestamp,
            string date,
            string bodyJson)
        {
            const string algorithm = "TC3-HMAC-SHA256";
            string httpRequestMethod = "POST";
            string canonicalUri = "/";
            string canonicalQueryString = string.Empty;
            string contentType = "application/json; charset=utf-8";
            string canonicalHeaders = $"content-type:{contentType}\nhost:{host}\n";
            string signedHeaders = "content-type;host";
            string hashedRequestPayload = Sha256Hex(bodyJson);
            string canonicalRequest =
                $"{httpRequestMethod}\n{canonicalUri}\n{canonicalQueryString}\n{canonicalHeaders}\n{signedHeaders}\n{hashedRequestPayload}";

            string credentialScope = $"{date}/{service}/tc3_request";
            string stringToSign =
                $"{algorithm}\n{timestamp}\n{credentialScope}\n{Sha256Hex(canonicalRequest)}";

            byte[] secretDate = HmacSha256Bytes(Encoding.UTF8.GetBytes("TC3" + secretKey), date);
            byte[] secretService = HmacSha256Bytes(secretDate, service);
            byte[] secretSigning = HmacSha256Bytes(secretService, "tc3_request");
            string signature = ToLowerHex(HmacSha256Bytes(secretSigning, stringToSign));

            return
                $"{algorithm} Credential={secretId}/{credentialScope}, SignedHeaders={signedHeaders}, Signature={signature}";
        }

        private static byte[] HmacSha256Bytes(byte[] key, string data)
        {
            using var hmac = new HMACSHA256(key);
            return hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
        }

        private static string Sha256Hex(string data)
        {
            using var sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(data));
            return ToLowerHex(hash);
        }

        private static string ToLowerHex(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes)
            {
                sb.Append(b.ToString("x2"));
            }
            return sb.ToString();
        }

        private async Task<string> GetAliyunAccessTokenAsync(CancellationToken cancellationToken)
        {
            if (!string.IsNullOrWhiteSpace(_aliyunAccessToken) && DateTime.UtcNow < _aliyunTokenExpireUtc)
            {
                return _aliyunAccessToken;
            }

            await _aliyunTokenLock.WaitAsync(cancellationToken);
            try
            {
                if (!string.IsNullOrWhiteSpace(_aliyunAccessToken) && DateTime.UtcNow < _aliyunTokenExpireUtc)
                {
                    return _aliyunAccessToken;
                }

                var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["AccessKeyId"] = _aliAccessKeyId,
                    ["Action"] = "CreateToken",
                    ["Format"] = "JSON",
                    ["RegionId"] = AliyunRegionId,
                    ["SignatureMethod"] = "HMAC-SHA1",
                    ["SignatureNonce"] = Guid.NewGuid().ToString("D"),
                    ["SignatureVersion"] = "1.0",
                    ["Timestamp"] = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
                    ["Version"] = AliyunApiVersion
                };
                string canonicalQuery = BuildAliyunCanonicalizedQuery(parameters);
                string stringToSign = $"GET&%2F&{AliyunPercentEncode(canonicalQuery)}";
                string signature;
                using (var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(_aliAccessKeySecret + "&")))
                {
                    signature = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(stringToSign)));
                }

                string signedQuery = $"Signature={AliyunPercentEncode(signature)}&{canonicalQuery}";
                string[] candidates =
                {
                    $"https://{AliyunTokenHostPrimary}/?{signedQuery}",
                    $"https://{AliyunTokenHostFallback}/?{signedQuery}"
                };

                foreach (string url in candidates)
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, url);
                    LastTranscribeUrl = $"https://{new Uri(url).Host}/";
                    using var response = await _httpClient.SendAsync(request, cancellationToken);
                    LastTranscribeStatusCode = (int)response.StatusCode;
                    if (!response.IsSuccessStatusCode)
                    {
                        string responseText = string.Empty;
                        try
                        {
                            responseText = await response.Content.ReadAsStringAsync(cancellationToken);
                        }
                        catch
                        {
                            // ignore body read failure
                        }

                        string detail = TryExtractAliyunErrorDetail(responseText);
                        LastError = string.IsNullOrWhiteSpace(detail)
                            ? $"阿里云Token请求失败: HTTP {(int)response.StatusCode}"
                            : $"阿里云Token请求失败: HTTP {(int)response.StatusCode}, {detail}";
                        continue;
                    }

                    string json = await response.Content.ReadAsStringAsync(cancellationToken);
                    AliyunTokenResponse payload = null;
                    try
                    {
                        payload = JsonSerializer.Deserialize<AliyunTokenResponse>(json);
                    }
                    catch
                    {
                        // ignore
                    }

                    if (!string.IsNullOrWhiteSpace(payload?.Token?.Id))
                    {
                        _aliyunAccessToken = payload.Token.Id;
                        DateTime expireUtc = payload.Token.ExpireTime > 0
                            ? DateTimeOffset.FromUnixTimeSeconds(payload.Token.ExpireTime).UtcDateTime
                            : DateTime.UtcNow.AddHours(1);
                        _aliyunTokenExpireUtc = expireUtc.AddMinutes(-5);
                        LastError = string.Empty;
                        return _aliyunAccessToken;
                    }

                    if (!string.IsNullOrWhiteSpace(payload?.Code))
                    {
                        LastError = $"阿里云Token错误: {payload.Code} {payload.Message}".Trim();
                    }
                    else
                    {
                        LastError = "阿里云Token响应为空";
                    }
                }

                return string.Empty;
            }
            catch (Exception ex)
            {
                LastError = $"阿里云Token异常: {ex.Message}";
                return string.Empty;
            }
            finally
            {
                _aliyunTokenLock.Release();
            }
        }

        private static string BuildAliyunCanonicalizedQuery(Dictionary<string, string> parameters)
        {
            return string.Join("&", parameters.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => $"{AliyunPercentEncode(kv.Key)}={AliyunPercentEncode(kv.Value)}"));
        }

        private static string AliyunPercentEncode(string value)
        {
            string encoded = Uri.EscapeDataString(value ?? string.Empty);
            return encoded.Replace("+", "%20").Replace("*", "%2A").Replace("%7E", "~");
        }

        private static string TryExtractAliyunErrorDetail(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return string.Empty;
            }

            try
            {
                using JsonDocument doc = JsonDocument.Parse(json);
                JsonElement root = doc.RootElement;
                string code = root.TryGetProperty("Code", out JsonElement codeEl)
                    ? codeEl.GetString() ?? string.Empty
                    : string.Empty;
                string message = root.TryGetProperty("Message", out JsonElement messageEl)
                    ? messageEl.GetString() ?? string.Empty
                    : string.Empty;
                if (!string.IsNullOrWhiteSpace(code) || !string.IsNullOrWhiteSpace(message))
                {
                    return $"{code} {message}".Trim();
                }
            }
            catch
            {
                // not a json payload
            }

            string normalized = json.Replace("\r", " ").Replace("\n", " ").Trim();
            return normalized.Length <= 180 ? normalized : normalized.Substring(0, 180) + "...";
        }

        private async Task<string> GetBaiduAccessTokenAsync(CancellationToken cancellationToken)
        {
            if (!string.IsNullOrWhiteSpace(_baiduAccessToken) && DateTime.UtcNow < _baiduTokenExpireUtc)
            {
                return _baiduAccessToken;
            }

            await _baiduTokenLock.WaitAsync(cancellationToken);
            try
            {
                if (!string.IsNullOrWhiteSpace(_baiduAccessToken) && DateTime.UtcNow < _baiduTokenExpireUtc)
                {
                    return _baiduAccessToken;
                }

                string tokenUrl =
                    "https://aip.baidubce.com/oauth/2.0/token" +
                    $"?grant_type=client_credentials&client_id={Uri.EscapeDataString(_baiduApiKey)}&client_secret={Uri.EscapeDataString(_baiduSecretKey)}";

                using var request = new HttpRequestMessage(HttpMethod.Post, tokenUrl);
                LastTranscribeUrl = "https://aip.baidubce.com/oauth/2.0/token";
                using var response = await _httpClient.SendAsync(request, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    LastError = $"百度Token请求失败: HTTP {(int)response.StatusCode}";
                    return string.Empty;
                }

                var payload = await response.Content.ReadFromJsonAsync<BaiduTokenResponse>(cancellationToken: cancellationToken);
                if (payload == null || string.IsNullOrWhiteSpace(payload.AccessToken))
                {
                    LastError = "百度Token响应为空";
                    return string.Empty;
                }

                int expiresIn = payload.ExpiresIn > 0 ? payload.ExpiresIn : 2592000;
                _baiduAccessToken = payload.AccessToken;
                _baiduTokenExpireUtc = DateTime.UtcNow.AddSeconds(Math.Max(300, expiresIn - 300));
                LastError = string.Empty;
                return _baiduAccessToken;
            }
            catch (Exception ex)
            {
                LastError = $"百度Token异常: {ex.Message}";
                return string.Empty;
            }
            finally
            {
                _baiduTokenLock.Release();
            }
        }

        private bool IsBaiduProvider() => _provider == "baidu";
        private bool IsTencentProvider() => _provider == "tencent";
        private bool IsAliyunProvider() => _provider == "aliyun";
        private bool IsDoubaoProvider() => _provider == "doubao";
        private bool IsFunAsrProvider() => _provider == "funasr";

        private static string NormalizeAsrProvider(string provider)
        {
            string normalized = (provider ?? string.Empty).Trim().ToLowerInvariant();
            return normalized switch
            {
                "baidu" => "baidu",
                "tencent" => "tencent",
                "aliyun" => "aliyun",
                "doubao" => "doubao",
                "funasr" => "doubao",
                _ => "baidu"
            };
        }

        private static string ResolveFunAsrWsUrl(string baseUrl)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                return "ws://127.0.0.1:10096";
            }

            string candidate = baseUrl.Trim();
            if (candidate.StartsWith("ws://", StringComparison.OrdinalIgnoreCase) ||
                candidate.StartsWith("wss://", StringComparison.OrdinalIgnoreCase))
            {
                return candidate.TrimEnd('/');
            }

            if (candidate.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                return "ws://" + candidate.Substring("http://".Length).TrimEnd('/');
            }

            if (candidate.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return "wss://" + candidate.Substring("https://".Length).TrimEnd('/');
            }

            return "ws://127.0.0.1:10096";
        }

        private void ApplyFunAsrTlsPolicy(ClientWebSocketOptions options, string url)
        {
            if (!_funAsrAllowInsecureTls || string.IsNullOrWhiteSpace(url))
            {
                return;
            }

            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uri))
            {
                return;
            }

            if (!string.Equals(uri.Scheme, "wss", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!IsLoopbackHost(uri.Host))
            {
                return;
            }

            options.RemoteCertificateValidationCallback = static (
                object sender,
                X509Certificate certificate,
                X509Chain chain,
                SslPolicyErrors sslPolicyErrors) => true;
        }

        private static bool IsLoopbackHost(string host)
        {
            if (string.IsNullOrWhiteSpace(host))
            {
                return false;
            }

            if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
                host.Equals("::1", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        private string[] BuildCandidateUrls(string path)
        {
            string normalizedPath = path.TrimStart('/');
            string baseUrl = _baseUrl.TrimEnd('/');
            bool hasV1 = baseUrl.EndsWith("/v1", StringComparison.OrdinalIgnoreCase);
            if (hasV1)
            {
                string baseWithoutV1 = baseUrl[..^3].TrimEnd('/');
                return new[]
                {
                    $"{baseUrl}/{normalizedPath}",
                    $"{baseWithoutV1}/{normalizedPath}"
                };
            }

            return new[]
            {
                $"{baseUrl}/{normalizedPath}",
                $"{baseUrl}/v1/{normalizedPath}"
            };
        }

        private MultipartFormDataContent CreateTranscribeForm(byte[] wavBytes)
        {
            var form = new MultipartFormDataContent();
            var audioContent = new ByteArrayContent(wavBytes);
            audioContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
            form.Add(audioContent, "file", $"chunk-{DateTime.UtcNow:yyyyMMddHHmmssfff}.wav");
            form.Add(new StringContent(_asrModel), "model");
            return form;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            try
            {
                Task.Run(() => StopBaiduRealtimeSessionAsync(CancellationToken.None)).Wait(800);
            }
            catch
            {
                // ignore
            }

            try
            {
                Task.Run(() => StopTencentRealtimeSessionAsync(CancellationToken.None)).Wait(800);
            }
            catch
            {
                // ignore
            }
            try
            {
                Task.Run(() => StopAliyunRealtimeSessionAsync(CancellationToken.None)).Wait(800);
            }
            catch
            {
                // ignore
            }
            try
            {
                Task.Run(() => StopDoubaoRealtimeSessionAsync(CancellationToken.None)).Wait(800);
            }
            catch
            {
                // ignore
            }
            try
            {
                Task.Run(() => StopFunAsrRealtimeSessionAsync(CancellationToken.None)).Wait(800);
            }
            catch
            {
                // ignore
            }

            try { _httpClient.Dispose(); } catch { }
            try { _baiduTokenLock.Dispose(); } catch { }
            try { _aliyunTokenLock.Dispose(); } catch { }
            try { _baiduRealtimeSendLock.Dispose(); } catch { }
            try { _baiduRealtimeConnectLock.Dispose(); } catch { }
            try { _tencentRealtimeSendLock.Dispose(); } catch { }
            try { _tencentRealtimeConnectLock.Dispose(); } catch { }
            try { _aliyunRealtimeSendLock.Dispose(); } catch { }
            try { _aliyunRealtimeConnectLock.Dispose(); } catch { }
            try { _doubaoRealtimeSendLock.Dispose(); } catch { }
            try { _doubaoRealtimeConnectLock.Dispose(); } catch { }
            try { _funAsrRealtimeSendLock.Dispose(); } catch { }
            try { _funAsrRealtimeConnectLock.Dispose(); } catch { }
        }

        private sealed class OpenAiTranscriptionResponse
        {
            public string Text { get; set; } = string.Empty;
        }

        private sealed class BaiduTokenResponse
        {
            [JsonPropertyName("access_token")]
            public string AccessToken { get; set; } = string.Empty;
            [JsonPropertyName("expires_in")]
            public int ExpiresIn { get; set; }
        }

        private sealed class BaiduAsrResponse
        {
            [JsonPropertyName("err_no")]
            public int ErrNo { get; set; }
            [JsonPropertyName("err_msg")]
            public string ErrMsg { get; set; } = string.Empty;
            [JsonPropertyName("result")]
            public string[] Result { get; set; } = Array.Empty<string>();
        }

        private sealed class TencentSentenceRecognitionEnvelope
        {
            public TencentSentenceRecognitionResponse Response { get; set; } = new();
        }

        private sealed class TencentSentenceRecognitionResponse
        {
            public string Result { get; set; } = string.Empty;
            public TencentError Error { get; set; }
        }

        private sealed class TencentError
        {
            public string Code { get; set; } = string.Empty;
            public string Message { get; set; } = string.Empty;
        }

        private sealed class AliyunTokenResponse
        {
            public string RequestId { get; set; } = string.Empty;
            public string Code { get; set; } = string.Empty;
            public string Message { get; set; } = string.Empty;
            public AliyunTokenData Token { get; set; } = new();
        }

        private sealed class AliyunTokenData
        {
            public string Id { get; set; } = string.Empty;
            public long ExpireTime { get; set; }
        }
    }
}
