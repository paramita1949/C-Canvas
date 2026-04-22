using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using ImageColorChanger.Core;

namespace ImageColorChanger.Services
{
    internal sealed class BibleBaiduShortSpeechClient : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly ConfigManager _config;
        private readonly SemaphoreSlim _tokenLock = new(1, 1);
        private readonly string _tencentRegion = "ap-shanghai";
        private readonly string _tencentVersion = "2019-06-14";
        private const string DoubaoShortSpeechWsUrl = "wss://openspeech.bytedance.com/api/v2/asr";
        private const string DoubaoShortDefaultCluster = "volcengine_input_common";
        private const string DoubaoRealtimeDefaultResourceId = "volc.seedasr.sauc.duration";
        private const string XfyunShortSpeechHost = "iat.xf-yun.com";
        private const string XfyunShortSpeechPath = "/v1";
        private const string XfyunShortSpeechWsUrl = "wss://iat.xf-yun.com/v1";
        private const byte DoubaoProtocolVersion = 1;
        private const byte DoubaoHeaderWords = 1;
        private const byte DoubaoMessageTypeFullClientRequest = 0x1;
        private const byte DoubaoMessageTypeAudioOnlyRequest = 0x2;
        private const byte DoubaoMessageTypeFullServerResponse = 0x9;
        private const byte DoubaoMessageTypeServerError = 0xF;
        private const byte DoubaoFlagAudioLast = 0x2;
        private const byte DoubaoSerializationNone = 0x0;
        private const byte DoubaoSerializationJson = 0x1;
        private const byte DoubaoCompressionNone = 0x0;
        private const byte DoubaoCompressionGzip = 0x1;
        private string _token = string.Empty;
        private DateTime _tokenExpireUtc = DateTime.MinValue;
        private string _aliyunToken = string.Empty;
        private DateTime _aliyunTokenExpireUtc = DateTime.MinValue;
        private readonly SemaphoreSlim _aliyunTokenLock = new(1, 1);
        private const string AliyunAsrEndpoint = "https://nls-gateway-cn-shanghai.aliyuncs.com/stream/v1/asr";
        private const string AliyunTokenHostPrimary = "nls-meta.cn-shanghai.aliyuncs.com";
        private const string AliyunTokenHostFallback = "nlsmeta.cn-shanghai.aliyuncs.com";
        private const string AliyunRegionId = "cn-shanghai";
        private const string AliyunApiVersion = "2019-02-28";
        private bool _disposed;

        public BibleBaiduShortSpeechClient(ConfigManager config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        }

        public bool IsConfigured =>
            GetShortSpeechProvider() switch
            {
                "tencent" => !string.IsNullOrWhiteSpace(_config.LiveCaptionTencentSecretId) &&
                             !string.IsNullOrWhiteSpace(_config.LiveCaptionTencentSecretKey),
                "doubao" => !string.IsNullOrWhiteSpace(_config.LiveCaptionDoubaoAppKey) &&
                             !string.IsNullOrWhiteSpace(_config.LiveCaptionDoubaoAccessKey),
                "aliyun" => !string.IsNullOrWhiteSpace(_config.LiveCaptionAliAppKey) &&
                            !string.IsNullOrWhiteSpace(_config.LiveCaptionAliAccessKeyId) &&
                            !string.IsNullOrWhiteSpace(_config.LiveCaptionAliAccessKeySecret),
                "xfyun" => !string.IsNullOrWhiteSpace(_config.LiveCaptionXfyunAppId) &&
                           !string.IsNullOrWhiteSpace(_config.LiveCaptionXfyunApiKey) &&
                           !string.IsNullOrWhiteSpace(_config.LiveCaptionXfyunApiSecret),
                _ => !string.IsNullOrWhiteSpace(_config.LiveCaptionBaiduAppId) &&
                     !string.IsNullOrWhiteSpace(_config.LiveCaptionBaiduApiKey) &&
                     !string.IsNullOrWhiteSpace(_config.LiveCaptionBaiduSecretKey)
            };

        public string MissingConfigSummary
        {
            get
            {
                var missing = new System.Collections.Generic.List<string>();
                string provider = GetShortSpeechProvider();
                if (string.Equals(provider, "tencent", StringComparison.Ordinal))
                {
                    if (string.IsNullOrWhiteSpace(_config.LiveCaptionTencentSecretId))
                    {
                        missing.Add("SecretId");
                    }
                    if (string.IsNullOrWhiteSpace(_config.LiveCaptionTencentSecretKey))
                    {
                        missing.Add("SecretKey");
                    }
                }
                else if (string.Equals(provider, "doubao", StringComparison.Ordinal))
                {
                    if (string.IsNullOrWhiteSpace(_config.LiveCaptionDoubaoAppKey))
                    {
                        missing.Add("AppID");
                    }
                    if (string.IsNullOrWhiteSpace(_config.LiveCaptionDoubaoAccessKey))
                    {
                        missing.Add("Token");
                    }
                }
                else if (string.Equals(provider, "aliyun", StringComparison.Ordinal))
                {
                    if (string.IsNullOrWhiteSpace(_config.LiveCaptionAliAppKey))
                        missing.Add("AppKey");
                    if (string.IsNullOrWhiteSpace(_config.LiveCaptionAliAccessKeyId))
                        missing.Add("AccessKeyId");
                    if (string.IsNullOrWhiteSpace(_config.LiveCaptionAliAccessKeySecret))
                        missing.Add("AccessKeySecret");
                }
                else if (string.Equals(provider, "xfyun", StringComparison.Ordinal))
                {
                    if (string.IsNullOrWhiteSpace(_config.LiveCaptionXfyunAppId))
                        missing.Add("AppID");
                    if (string.IsNullOrWhiteSpace(_config.LiveCaptionXfyunApiKey))
                        missing.Add("APIKey");
                    if (string.IsNullOrWhiteSpace(_config.LiveCaptionXfyunApiSecret))
                        missing.Add("APISecret");
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(_config.LiveCaptionBaiduAppId))
                    {
                        missing.Add("AppID");
                    }
                    if (string.IsNullOrWhiteSpace(_config.LiveCaptionBaiduApiKey))
                    {
                        missing.Add("API Key");
                    }
                    if (string.IsNullOrWhiteSpace(_config.LiveCaptionBaiduSecretKey))
                    {
                        missing.Add("Secret Key");
                    }
                }

                return missing.Count == 0
                    ? string.Empty
                    : string.Join(" / ", missing);
            }
        }

        public async Task<string> TranscribeWavAsync(byte[] wavBytes, CancellationToken cancellationToken)
        {
            string provider = GetShortSpeechProvider();
            string shortTag = LiveCaption.LiveCaptionPlatformLabelFormatter.BuildShortPhraseTag(provider);
            if (wavBytes == null || wavBytes.Length == 0 || !IsConfigured)
            {
                Debug.WriteLine($"[BibleVoice][{shortTag}] skipped: empty-audio-or-missing-config");
                return string.Empty;
            }

            Debug.WriteLine($"[BibleVoice][{shortTag}] request: wavBytes={wavBytes.Length}");
            return provider switch
            {
                "tencent" => await TranscribeWithTencentAsync(wavBytes, cancellationToken),
                "doubao" => await TranscribeWithDoubaoAsync(wavBytes, cancellationToken),
                "aliyun" => await TranscribeWithAliyunAsync(wavBytes, cancellationToken),
                "xfyun" => await TranscribeWithXfyunAsync(wavBytes, cancellationToken),
                _ => await TranscribeWithBaiduAsync(wavBytes, cancellationToken)
            };
        }

        private async Task<string> TranscribeWithBaiduAsync(byte[] wavBytes, CancellationToken cancellationToken)
        {
            if (wavBytes == null || wavBytes.Length == 0)
            {
                return string.Empty;
            }

            string token = await GetAccessTokenAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(token))
            {
                Debug.WriteLine("[BibleVoice][Baidu] short-speech skipped: access-token-empty");
                return string.Empty;
            }

            string model = _config.LiveCaptionShortAsrModel;
            string url = ResolveEndpoint(model);
            int devPid = ResolveDevPid(model, _config.LiveCaptionShortBaiduDevPid);
            Debug.WriteLine($"[BibleVoice][Baidu] short-speech request: model={model}, configuredDevPid={_config.LiveCaptionShortBaiduDevPid}, resolvedDevPid={devPid}, url={url}, wavBytes={wavBytes.Length}");
            var payload = new
            {
                format = "wav",
                rate = 16000,
                channel = 1,
                token,
                cuid = string.IsNullOrWhiteSpace(_config.LiveCaptionBaiduAppId) ? Environment.MachineName : _config.LiveCaptionBaiduAppId,
                dev_pid = devPid,
                speech = Convert.ToBase64String(wavBytes),
                len = wavBytes.Length
            };

            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };

            using var resp = await _httpClient.SendAsync(req, cancellationToken);
            if (!resp.IsSuccessStatusCode)
            {
                Debug.WriteLine($"[BibleVoice][Baidu] short-speech http-failed: status={(int)resp.StatusCode} {resp.ReasonPhrase}");
                return string.Empty;
            }

            string json = await resp.Content.ReadAsStringAsync(cancellationToken);
            BaiduAsrResponse result = null;
            try
            {
                result = JsonSerializer.Deserialize<BaiduAsrResponse>(json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[BibleVoice][Baidu] short-speech deserialize-failed: {ex.Message}; raw={TrimForLog(json)}");
                return string.Empty;
            }

            if (result == null)
            {
                Debug.WriteLine($"[BibleVoice][Baidu] short-speech null-response: raw={TrimForLog(json)}");
                return string.Empty;
            }

            if (result.ErrNo != 0)
            {
                Debug.WriteLine($"[BibleVoice][Baidu] short-speech asr-error: err_no={result.ErrNo}, err_msg={result.ErrMsg}, raw={TrimForLog(json)}");
                return string.Empty;
            }

            if (result.Result == null || result.Result.Length == 0)
            {
                Debug.WriteLine($"[BibleVoice][Baidu] short-speech empty-result: err_no={result.ErrNo}, err_msg={result.ErrMsg}, raw={TrimForLog(json)}");
                return string.Empty;
            }

            string recognized = (result.Result[0] ?? string.Empty).Trim();
            Debug.WriteLine($"[BibleVoice][Baidu] short-speech success: recognized={recognized}");
            return recognized;
        }

        private async Task<string> TranscribeWithTencentAsync(byte[] wavBytes, CancellationToken cancellationToken)
        {
            string model = _config.LiveCaptionShortAsrModel;
            string engineType = string.IsNullOrWhiteSpace(model) || model.Contains("gpt-", StringComparison.OrdinalIgnoreCase)
                ? "16k_zh"
                : model.Trim();
            string customizationId = (_config.LiveCaptionTencentShortCustomizationId ?? string.Empty).Trim();
            string requestUrl = "https://asr.tencentcloudapi.com";
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

            long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            string date = DateTimeOffset.FromUnixTimeSeconds(timestamp).UtcDateTime.ToString("yyyy-MM-dd");
            string authorization = BuildTencentAuthorization(
                _config.LiveCaptionTencentSecretId,
                _config.LiveCaptionTencentSecretKey,
                "asr.tencentcloudapi.com",
                "asr",
                timestamp,
                date,
                bodyJson);

            Debug.WriteLine($"[BibleVoice][Tencent] short-speech request: model={model}, engineType={engineType}, customizationId={(string.IsNullOrWhiteSpace(customizationId) ? "(none)" : customizationId)}, url={requestUrl}, wavBytes={wavBytes.Length}");

            using var request = new HttpRequestMessage(HttpMethod.Post, requestUrl)
            {
                Content = new StringContent(bodyJson, Encoding.UTF8, "application/json")
            };
            request.Headers.TryAddWithoutValidation("Authorization", authorization);
            request.Headers.TryAddWithoutValidation("X-TC-Action", "SentenceRecognition");
            request.Headers.TryAddWithoutValidation("X-TC-Version", _tencentVersion);
            request.Headers.TryAddWithoutValidation("X-TC-Region", _tencentRegion);
            request.Headers.TryAddWithoutValidation("X-TC-Timestamp", timestamp.ToString());

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            string responseText = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                Debug.WriteLine($"[BibleVoice][Tencent] short-speech http-failed: status={(int)response.StatusCode} {response.ReasonPhrase}, raw={TrimForLog(responseText)}");
                return string.Empty;
            }

            TencentSentenceRecognitionEnvelope responseEnvelope = null;
            try
            {
                responseEnvelope = JsonSerializer.Deserialize<TencentSentenceRecognitionEnvelope>(responseText);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[BibleVoice][Tencent] short-speech deserialize-failed: {ex.Message}; raw={TrimForLog(responseText)}");
                return string.Empty;
            }

            if (responseEnvelope?.Response == null)
            {
                Debug.WriteLine($"[BibleVoice][Tencent] short-speech empty-response: raw={TrimForLog(responseText)}");
                return string.Empty;
            }

            if (responseEnvelope.Response.Error != null && !string.IsNullOrWhiteSpace(responseEnvelope.Response.Error.Code))
            {
                Debug.WriteLine($"[BibleVoice][Tencent] short-speech asr-error: code={responseEnvelope.Response.Error.Code}, message={responseEnvelope.Response.Error.Message}, raw={TrimForLog(responseText)}");
                return string.Empty;
            }

            string recognized = (responseEnvelope.Response.Result ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(recognized))
            {
                Debug.WriteLine($"[BibleVoice][Tencent] short-speech empty-result: raw={TrimForLog(responseText)}");
                return string.Empty;
            }

            Debug.WriteLine($"[BibleVoice][Tencent] short-speech success: recognized={recognized}");
            return recognized;
        }

        private async Task<string> TranscribeWithDoubaoAsync(byte[] wavBytes, CancellationToken cancellationToken)
        {
            if (wavBytes == null || wavBytes.Length == 0)
            {
                return string.Empty;
            }

            string appId = (_config.LiveCaptionDoubaoAppKey ?? string.Empty).Trim();
            string rawToken = (_config.LiveCaptionDoubaoAccessKey ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(appId) || string.IsNullOrWhiteSpace(rawToken))
            {
                Debug.WriteLine("[BibleVoice][Doubao] short-speech skipped: missing-appid-or-token");
                return string.Empty;
            }

            string wsUrl = ResolveDoubaoShortSpeechWsUrl(_config.LiveCaptionShortProxyBaseUrl);
            string cluster = ResolveDoubaoShortSpeechCluster(_config.LiveCaptionDoubaoResourceId, _config.LiveCaptionShortAsrModel);
            string authorization = BuildDoubaoAuthorizationHeader(rawToken);
            string payloadToken = BuildDoubaoPayloadToken(rawToken);
            string requestId = Guid.NewGuid().ToString("N");
            string boostingTableId = (_config.LiveCaptionDoubaoBoostingTableId ?? string.Empty).Trim();
            string boostingTableName = (_config.LiveCaptionDoubaoBoostingTableName ?? string.Empty).Trim();

            using var ws = new ClientWebSocket();
            ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(10);
            ws.Options.SetRequestHeader("Authorization", authorization);

            string hotwordMode = !string.IsNullOrWhiteSpace(boostingTableId)
                ? $"热词表ID:{boostingTableId}"
                : (!string.IsNullOrWhiteSpace(boostingTableName)
                    ? $"热词表名:{boostingTableName}"
                    : "未配置热词表");
            Debug.WriteLine($"[BibleVoice][Doubao] short-speech request: cluster={cluster}, url={wsUrl}, wavBytes={wavBytes.Length}, hotword={hotwordMode}");
            await ws.ConnectAsync(new Uri(wsUrl), cancellationToken);

            string startJson = BuildDoubaoShortSpeechInitialPayloadJson(
                appId,
                payloadToken,
                cluster,
                requestId,
                boostingTableId,
                boostingTableName);
            byte[] startFrame = BuildDoubaoFrame(
                DoubaoMessageTypeFullClientRequest,
                0,
                DoubaoSerializationJson,
                DoubaoCompressionGzip,
                GzipCompress(Encoding.UTF8.GetBytes(startJson)),
                sequence: null);

            await ws.SendAsync(
                new ArraySegment<byte>(startFrame),
                WebSocketMessageType.Binary,
                true,
                cancellationToken);

            byte[] audioFrame = BuildDoubaoFrame(
                DoubaoMessageTypeAudioOnlyRequest,
                DoubaoFlagAudioLast,
                DoubaoSerializationNone,
                DoubaoCompressionGzip,
                GzipCompress(wavBytes),
                sequence: null);

            await ws.SendAsync(
                new ArraySegment<byte>(audioFrame),
                WebSocketMessageType.Binary,
                true,
                cancellationToken);

            string recognized = string.Empty;
            while (!cancellationToken.IsCancellationRequested)
            {
                byte[] packet = await ReceiveDoubaoPacketAsync(ws, cancellationToken);
                if (packet == null || packet.Length == 0)
                {
                    break;
                }

                if (LooksLikeJsonText(packet))
                {
                    string jsonText = Encoding.UTF8.GetString(packet);
                    if (TryHandleDoubaoJsonPayload(jsonText, out string recognizedText, out bool isFinal))
                    {
                        if (!string.IsNullOrWhiteSpace(recognizedText))
                        {
                            recognized = recognizedText;
                        }
                        if (isFinal)
                        {
                            break;
                        }
                    }
                    continue;
                }

                if (!TryReadDoubaoFrame(packet, out byte messageType, out byte serialization, out byte compression, out int? sequence, out byte[] payload))
                {
                    continue;
                }

                if (messageType == DoubaoMessageTypeServerError)
                {
                    string serverError = ParseDoubaoServerError(packet);
                    if (!string.IsNullOrWhiteSpace(serverError))
                    {
                        Debug.WriteLine($"[BibleVoice][Doubao] short-speech server-error: {serverError}");
                    }
                    return string.Empty;
                }

                if (messageType != DoubaoMessageTypeFullServerResponse || payload.Length == 0)
                {
                    continue;
                }

                if (compression == DoubaoCompressionGzip)
                {
                    payload = GzipDecompress(payload);
                }

                if (serialization != DoubaoSerializationJson || !LooksLikeJsonText(payload))
                {
                    continue;
                }

                string json = Encoding.UTF8.GetString(payload);
                if (TryHandleDoubaoJsonPayload(json, out string parsedText, out bool isFinalByPayload))
                {
                    if (!string.IsNullOrWhiteSpace(parsedText))
                    {
                        recognized = parsedText;
                    }
                    if (isFinalByPayload || (sequence.HasValue && sequence.Value < 0))
                    {
                        break;
                    }
                }
            }

            if (ws.State == WebSocketState.Open || ws.State == WebSocketState.CloseReceived)
            {
                try
                {
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", cancellationToken);
                }
                catch
                {
                    // ignore close failure
                }
            }

            if (string.IsNullOrWhiteSpace(recognized))
            {
                Debug.WriteLine("[BibleVoice][Doubao] short-speech empty-result.");
                return string.Empty;
            }

            string finalText = recognized.Trim();
            Debug.WriteLine($"[BibleVoice][Doubao] short-speech success: recognized={finalText}");
            return finalText;
        }

        private async Task<string> TranscribeWithAliyunAsync(byte[] wavBytes, CancellationToken cancellationToken)
        {
            if (wavBytes == null || wavBytes.Length == 0)
                return string.Empty;

            string appKey = (_config.LiveCaptionAliAppKey ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(appKey))
            {
                Debug.WriteLine("[BibleVoice][Aliyun] short-speech skipped: missing-appkey");
                return string.Empty;
            }

            string token = await GetAliyunAccessTokenAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(token))
            {
                Debug.WriteLine("[BibleVoice][Aliyun] short-speech skipped: access-token-empty");
                return string.Empty;
            }

            // WAV 文件中提取 PCM 数据（跳过 44 字节 WAV 头）
            byte[] pcmData = wavBytes.Length > 44 ? wavBytes.AsSpan(44).ToArray() : wavBytes;

            string url = $"{AliyunAsrEndpoint}?appkey={Uri.EscapeDataString(appKey)}&format=pcm&sample_rate=16000&enable_punctuation_prediction=true";
            Debug.WriteLine($"[BibleVoice][Aliyun] short-speech request: wavBytes={wavBytes.Length}, pcmBytes={pcmData.Length}");

            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new ByteArrayContent(pcmData)
            };
            req.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
            req.Headers.TryAddWithoutValidation("X-NLS-Token", token);

            using var resp = await _httpClient.SendAsync(req, cancellationToken);
            string json = await resp.Content.ReadAsStringAsync(cancellationToken);

            if (!resp.IsSuccessStatusCode)
            {
                Debug.WriteLine($"[BibleVoice][Aliyun] short-speech http-failed: status={(int)resp.StatusCode}, raw={TrimForLog(json)}");
                return string.Empty;
            }

            AliyunAsrResponse result = null;
            try
            {
                result = JsonSerializer.Deserialize<AliyunAsrResponse>(json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[BibleVoice][Aliyun] short-speech deserialize-failed: {ex.Message}; raw={TrimForLog(json)}");
                return string.Empty;
            }

            if (result == null || result.Status != 20000000)
            {
                Debug.WriteLine($"[BibleVoice][Aliyun] short-speech asr-error: status={result?.Status}, message={result?.Message}, raw={TrimForLog(json)}");
                return string.Empty;
            }

            string recognized = (result.Result ?? string.Empty).Trim();
            Debug.WriteLine($"[BibleVoice][Aliyun] short-speech success: recognized={recognized}");
            return recognized;
        }

        private async Task<string> TranscribeWithXfyunAsync(byte[] wavBytes, CancellationToken cancellationToken)
        {
            if (wavBytes == null || wavBytes.Length == 0)
            {
                return string.Empty;
            }

            string appId = (_config.LiveCaptionXfyunAppId ?? string.Empty).Trim();
            string apiKey = (_config.LiveCaptionXfyunApiKey ?? string.Empty).Trim();
            string apiSecret = (_config.LiveCaptionXfyunApiSecret ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(appId) || string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(apiSecret))
            {
                return string.Empty;
            }

            byte[] pcmData = wavBytes.Length > 44 ? wavBytes.AsSpan(44).ToArray() : wavBytes;
            string wsBaseUrl = ResolveXfyunShortSpeechWsUrl(_config.LiveCaptionShortProxyBaseUrl);
            string wsUrl = BuildXfyunShortSpeechWebSocketUrl(wsBaseUrl, apiKey, apiSecret);

            using var ws = new ClientWebSocket();
            ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(10);
            await ws.ConnectAsync(new Uri(wsUrl), cancellationToken);

            string recognized = string.Empty;
            int frameSize = 1280;
            int offset = 0;
            bool firstFrameSent = false;
            var recvBuffer = new byte[8192];

            // 按官方流式协议发送：首帧(status=0) -> 中间帧(status=1) -> 末帧(status=2)
            while (offset < pcmData.Length && !cancellationToken.IsCancellationRequested)
            {
                int bytes = Math.Min(frameSize, pcmData.Length - offset);
                byte[] frame = new byte[bytes];
                Buffer.BlockCopy(pcmData, offset, frame, 0, bytes);
                offset += bytes;

                int status = firstFrameSent ? 1 : 0;
                firstFrameSent = true;

                string frameJson = BuildXfyunShortAudioFrameJson(appId, Convert.ToBase64String(frame), status);
                byte[] payload = Encoding.UTF8.GetBytes(frameJson);
                await ws.SendAsync(new ArraySegment<byte>(payload), WebSocketMessageType.Text, true, cancellationToken);

                await Task.Delay(40, cancellationToken);
            }

            string lastFrameJson = BuildXfyunShortAudioFrameJson(appId, string.Empty, 2);
            byte[] lastPayload = Encoding.UTF8.GetBytes(lastFrameJson);
            await ws.SendAsync(new ArraySegment<byte>(lastPayload), WebSocketMessageType.Text, true, cancellationToken);

            bool finalArrived = await ReadXfyunShortMessagesAsync(ws, recvBuffer, cancellationToken, text =>
            {
                if (!string.IsNullOrWhiteSpace(text))
                {
                    recognized = text.Trim();
                }
            }, stopOnFinal: true);

            if (!finalArrived)
            {
            }

            if (ws.State == WebSocketState.Open || ws.State == WebSocketState.CloseReceived)
            {
                try
                {
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", cancellationToken);
                }
                catch
                {
                    // ignore
                }
            }

            if (string.IsNullOrWhiteSpace(recognized))
            {
                return string.Empty;
            }

            Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [BibleVoice][Xfyun] recognized={recognized}");
            return recognized;
        }

        private static async Task<bool> ReadXfyunShortMessagesAsync(
            ClientWebSocket ws,
            byte[] buffer,
            CancellationToken cancellationToken,
            Action<string> onText,
            bool stopOnFinal)
        {
            bool gotFinal = false;
            var wpgsSegments = new SortedDictionary<int, string>();
            int lastSn = -1;
            while (!cancellationToken.IsCancellationRequested && ws.State == WebSocketState.Open)
            {
                string message = await ReceiveXfyunShortTextMessageAsync(ws, buffer, cancellationToken);
                if (message == null)
                {
                    break;
                }

                if (!TryParseXfyunShortMessage(
                        message,
                        out int code,
                        out int status,
                        out string text,
                        out bool sentenceFinal,
                        out int ret,
                        out string pgs,
                        out int sn,
                        out int rgStart,
                        out int rgEnd,
                        out string err))
                {
                    continue;
                }

                if (code != 0)
                {
                    break;
                }
                if (ret > 0)
                {
                    break;
                }

                if (!string.IsNullOrWhiteSpace(text))
                {
                    string merged = text;
                    if (sn >= 0)
                    {
                        if (lastSn >= 0 && sn < lastSn)
                        {
                            wpgsSegments.Clear();
                        }
                        lastSn = Math.Max(lastSn, sn);

                        if (string.Equals(pgs, "rpl", StringComparison.OrdinalIgnoreCase) && rgStart > 0 && rgEnd >= rgStart)
                        {
                            for (int i = rgStart; i <= rgEnd; i++)
                            {
                                wpgsSegments.Remove(i);
                            }
                        }

                        wpgsSegments[sn] = text;
                        var sb = new StringBuilder();
                        foreach (var kv in wpgsSegments.OrderBy(k => k.Key))
                        {
                            sb.Append(kv.Value);
                        }
                        merged = sb.ToString();
                    }

                    onText?.Invoke(merged);
                }

                if (sentenceFinal || status == 2)
                {
                    gotFinal = true;
                    if (stopOnFinal)
                    {
                        break;
                    }
                }

                if (!stopOnFinal)
                {
                    break;
                }
            }

            return gotFinal;
        }

        private async Task<string> GetAliyunAccessTokenAsync(CancellationToken cancellationToken)
        {
            if (!string.IsNullOrWhiteSpace(_aliyunToken) && DateTime.UtcNow < _aliyunTokenExpireUtc)
                return _aliyunToken;

            await _aliyunTokenLock.WaitAsync(cancellationToken);
            try
            {
                if (!string.IsNullOrWhiteSpace(_aliyunToken) && DateTime.UtcNow < _aliyunTokenExpireUtc)
                    return _aliyunToken;

                string accessKeyId = (_config.LiveCaptionAliAccessKeyId ?? string.Empty).Trim();
                string accessKeySecret = (_config.LiveCaptionAliAccessKeySecret ?? string.Empty).Trim();

                var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["AccessKeyId"] = accessKeyId,
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
                using (var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(accessKeySecret + "&")))
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
                    using var response = await _httpClient.SendAsync(request, cancellationToken);
                    if (!response.IsSuccessStatusCode)
                    {
                        Debug.WriteLine($"[BibleVoice][Aliyun] token http-failed: host={new Uri(url).Host}, status={(int)response.StatusCode}");
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
                        _aliyunToken = payload.Token.Id;
                        DateTime expireUtc = payload.Token.ExpireTime > 0
                            ? DateTimeOffset.FromUnixTimeSeconds(payload.Token.ExpireTime).UtcDateTime
                            : DateTime.UtcNow.AddHours(1);
                        _aliyunTokenExpireUtc = expireUtc.AddMinutes(-5);
                        Debug.WriteLine($"[BibleVoice][Aliyun] token success");
                        return _aliyunToken;
                    }

                    Debug.WriteLine($"[BibleVoice][Aliyun] token error: {payload?.Code} {payload?.Message}");
                }

                return string.Empty;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[BibleVoice][Aliyun] token failed: {ex.Message}");
                return string.Empty;
            }
            finally
            {
                _aliyunTokenLock.Release();
            }
        }

        private static string BuildAliyunCanonicalizedQuery(Dictionary<string, string> parameters)
        {
            var sorted = new List<string>(parameters.Count);
            foreach (var kv in parameters.OrderBy(kv => kv.Key, StringComparer.Ordinal))
                sorted.Add($"{AliyunPercentEncode(kv.Key)}={AliyunPercentEncode(kv.Value)}");
            return string.Join("&", sorted);
        }

        private static string AliyunPercentEncode(string value)
        {
            string encoded = Uri.EscapeDataString(value ?? string.Empty);
            return encoded.Replace("+", "%20").Replace("*", "%2A").Replace("%7E", "~");
        }

        private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
        {
            if (!string.IsNullOrWhiteSpace(_token) && DateTime.UtcNow < _tokenExpireUtc)
            {
                return _token;
            }

            await _tokenLock.WaitAsync(cancellationToken);
            try
            {
                if (!string.IsNullOrWhiteSpace(_token) && DateTime.UtcNow < _tokenExpireUtc)
                {
                    return _token;
                }

                string url =
                    "https://aip.baidubce.com/oauth/2.0/token" +
                    $"?grant_type=client_credentials&client_id={Uri.EscapeDataString(_config.LiveCaptionBaiduApiKey)}&client_secret={Uri.EscapeDataString(_config.LiveCaptionBaiduSecretKey)}";

                using var resp = await _httpClient.GetAsync(url, cancellationToken);
                if (!resp.IsSuccessStatusCode)
                {
                    Debug.WriteLine($"[BibleVoice][Baidu] token http-failed: status={(int)resp.StatusCode} {resp.ReasonPhrase}");
                    return string.Empty;
                }

                string json = await resp.Content.ReadAsStringAsync(cancellationToken);
                BaiduTokenResponse tokenResp = null;
                try
                {
                    tokenResp = JsonSerializer.Deserialize<BaiduTokenResponse>(json);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[BibleVoice][Baidu] token deserialize-failed: {ex.Message}; raw={TrimForLog(json)}");
                    return string.Empty;
                }

                if (tokenResp == null || string.IsNullOrWhiteSpace(tokenResp.AccessToken))
                {
                    Debug.WriteLine($"[BibleVoice][Baidu] token empty-response: raw={TrimForLog(json)}");
                    return string.Empty;
                }

                int expiresIn = Math.Max(300, tokenResp.ExpiresIn);
                _token = tokenResp.AccessToken;
                _tokenExpireUtc = DateTime.UtcNow.AddSeconds(expiresIn - 300);
                Debug.WriteLine($"[BibleVoice][Baidu] token success: expiresIn={tokenResp.ExpiresIn}");
                return _token;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[BibleVoice][Baidu] token failed: {ex.Message}");
                return string.Empty;
            }
            finally
            {
                _tokenLock.Release();
            }
        }

        private static string ResolveEndpoint(string modelId)
        {
            return IsProModel(modelId)
                ? "https://vop.baidu.com/pro_api"
                : "http://vop.baidu.com/server_api";
        }

        private static int ResolveDevPid(string modelId, int configuredDevPid)
        {
            bool isProModel = IsProModel(modelId);
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

        private static bool IsProModel(string modelId)
        {
            if (string.IsNullOrWhiteSpace(modelId))
            {
                return false;
            }

            string normalized = modelId.Trim().ToLowerInvariant();
            return normalized.Contains("short-pro", StringComparison.Ordinal) ||
                   normalized.Contains("pro_api", StringComparison.Ordinal) ||
                   normalized.Contains("80001", StringComparison.Ordinal);
        }

        private string GetShortSpeechProvider()
        {
            return (_config.LiveCaptionShortAsrProvider ?? string.Empty).Trim().ToLowerInvariant() switch
            {
                "tencent" => "tencent",
                "doubao" => "doubao",
                "funasr" => "doubao",
                "aliyun" => "aliyun",
                "xfyun" => "xfyun",
                _ => "baidu"
            };
        }

        private static string ResolveXfyunShortSpeechWsUrl(string configuredUrl)
        {
            string raw = (configuredUrl ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(raw))
            {
                return XfyunShortSpeechWsUrl;
            }

            if (raw.StartsWith("ws://", StringComparison.OrdinalIgnoreCase) ||
                raw.StartsWith("wss://", StringComparison.OrdinalIgnoreCase))
            {
                return raw;
            }

            if (raw.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                return "ws://" + raw.Substring("http://".Length);
            }

            if (raw.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return "wss://" + raw.Substring("https://".Length);
            }

            return XfyunShortSpeechWsUrl;
        }

        private static string BuildXfyunShortSpeechWebSocketUrl(string wsBaseUrl, string apiKey, string apiSecret)
        {
            var baseUri = new Uri(string.IsNullOrWhiteSpace(wsBaseUrl) ? XfyunShortSpeechWsUrl : wsBaseUrl);
            string host = string.IsNullOrWhiteSpace(baseUri.Host) ? XfyunShortSpeechHost : baseUri.Host;
            string path = string.IsNullOrWhiteSpace(baseUri.AbsolutePath) ? XfyunShortSpeechPath : baseUri.AbsolutePath;
            string scheme = string.Equals(baseUri.Scheme, "ws", StringComparison.OrdinalIgnoreCase) ? "ws" : "wss";

            string date = DateTime.UtcNow.ToString("r", CultureInfo.InvariantCulture);
            string requestLine = $"GET {path} HTTP/1.1";
            string signatureOrigin = $"host: {host}\ndate: {date}\n{requestLine}";
            string signature;
            using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(apiSecret)))
            {
                signature = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(signatureOrigin)));
            }

            string authorizationOrigin =
                $"api_key=\"{apiKey}\",algorithm=\"hmac-sha256\",headers=\"host date request-line\",signature=\"{signature}\"";
            string authorization = Convert.ToBase64String(Encoding.UTF8.GetBytes(authorizationOrigin));
            string query =
                $"authorization={Uri.EscapeDataString(authorization)}&date={Uri.EscapeDataString(date)}&host={Uri.EscapeDataString(host)}";

            return $"{scheme}://{host}{path}?{query}";
        }

        private static string BuildXfyunShortAudioFrameJson(string appId, string audioBase64, int frameStatus)
        {
            var frame = new
            {
                header = new
                {
                    status = frameStatus,
                    app_id = appId
                },
                parameter = new
                {
                    iat = new
                    {
                        domain = "slm",
                        language = "zh_cn",
                        accent = "mandarin",
                        eos = 1800,
                        ltc = 2,
                        dwa = "wpgs",
                        result = new
                        {
                            encoding = "utf8",
                            compress = "raw",
                            format = "json"
                        }
                    }
                },
                payload = new
                {
                    audio = new
                    {
                        audio = audioBase64 ?? string.Empty,
                        sample_rate = 16000,
                        encoding = "raw"
                    }
                }
            };

            return JsonSerializer.Serialize(frame);
        }

        private static async Task<string> ReceiveXfyunShortTextMessageAsync(ClientWebSocket ws, byte[] buffer, CancellationToken cancellationToken)
        {
            using var ms = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return null;
                }

                if (result.MessageType != WebSocketMessageType.Text)
                {
                    continue;
                }

                if (result.Count > 0)
                {
                    ms.Write(buffer, 0, result.Count);
                }
            } while (!result.EndOfMessage);

            return Encoding.UTF8.GetString(ms.ToArray());
        }

        private static bool TryParseXfyunShortMessage(
            string message,
            out int code,
            out int status,
            out string text,
            out bool sentenceFinal,
            out int ret,
            out string pgs,
            out int sn,
            out int rgStart,
            out int rgEnd,
            out string errorMessage)
        {
            code = 0;
            status = 0;
            text = string.Empty;
            sentenceFinal = false;
            ret = -1;
            pgs = string.Empty;
            sn = -1;
            rgStart = -1;
            rgEnd = -1;
            errorMessage = string.Empty;

            try
            {
                using JsonDocument doc = JsonDocument.Parse(message);
                JsonElement root = doc.RootElement;
                JsonElement header = root.TryGetProperty("header", out JsonElement headerEl) &&
                                     headerEl.ValueKind == JsonValueKind.Object
                    ? headerEl
                    : default;

                if (header.ValueKind == JsonValueKind.Object &&
                    header.TryGetProperty("code", out JsonElement codeEl) &&
                    codeEl.ValueKind == JsonValueKind.Number)
                {
                    code = codeEl.GetInt32();
                }
                if (header.ValueKind == JsonValueKind.Object &&
                    header.TryGetProperty("status", out JsonElement statusEl) &&
                    statusEl.ValueKind == JsonValueKind.Number)
                {
                    status = statusEl.GetInt32();
                }
                if (header.ValueKind == JsonValueKind.Object &&
                    header.TryGetProperty("message", out JsonElement msgEl) &&
                    msgEl.ValueKind == JsonValueKind.String)
                {
                    errorMessage = msgEl.GetString() ?? string.Empty;
                }

                if (code != 0)
                {
                    return true;
                }

                if (!root.TryGetProperty("payload", out JsonElement payloadEl) || payloadEl.ValueKind != JsonValueKind.Object
                    || !payloadEl.TryGetProperty("result", out JsonElement resultEl) || resultEl.ValueKind != JsonValueKind.Object
                    || !resultEl.TryGetProperty("text", out JsonElement textEl) || textEl.ValueKind != JsonValueKind.String)
                {
                    return true;
                }

                string encoded = textEl.GetString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(encoded))
                {
                    return true;
                }

                byte[] decodedBytes = Convert.FromBase64String(encoded);
                string decodedJson = Encoding.UTF8.GetString(decodedBytes);
                using JsonDocument resultDoc = JsonDocument.Parse(decodedJson);
                if (resultDoc.RootElement.TryGetProperty("ret", out JsonElement retEl) && retEl.ValueKind == JsonValueKind.Number)
                {
                    ret = retEl.GetInt32();
                }
                sentenceFinal = resultDoc.RootElement.TryGetProperty("ls", out JsonElement lsEl) &&
                                lsEl.ValueKind is JsonValueKind.True or JsonValueKind.False &&
                                lsEl.GetBoolean();
                if (resultDoc.RootElement.TryGetProperty("pgs", out JsonElement pgsEl) && pgsEl.ValueKind == JsonValueKind.String)
                {
                    pgs = pgsEl.GetString() ?? string.Empty;
                }
                if (resultDoc.RootElement.TryGetProperty("sn", out JsonElement snEl) && snEl.ValueKind == JsonValueKind.Number)
                {
                    sn = snEl.GetInt32();
                }
                if (resultDoc.RootElement.TryGetProperty("rg", out JsonElement rgEl) && rgEl.ValueKind == JsonValueKind.Array)
                {
                    int[] nums = rgEl.EnumerateArray()
                        .Where(e => e.ValueKind == JsonValueKind.Number)
                        .Select(e => e.GetInt32())
                        .Take(2)
                        .ToArray();
                    if (nums.Length >= 2)
                    {
                        rgStart = nums[0];
                        rgEnd = nums[1];
                        if (rgEnd < rgStart)
                        {
                            (rgStart, rgEnd) = (rgEnd, rgStart);
                        }
                    }
                }

                if (!resultDoc.RootElement.TryGetProperty("ws", out JsonElement wsEl) || wsEl.ValueKind != JsonValueKind.Array)
                {
                    return true;
                }

                var sb = new StringBuilder();
                foreach (JsonElement wsItem in wsEl.EnumerateArray())
                {
                    if (!wsItem.TryGetProperty("cw", out JsonElement cwEl) || cwEl.ValueKind != JsonValueKind.Array)
                    {
                        continue;
                    }

                    foreach (JsonElement cwItem in cwEl.EnumerateArray())
                    {
                        if (cwItem.TryGetProperty("w", out JsonElement wEl) && wEl.ValueKind == JsonValueKind.String)
                        {
                            sb.Append(wEl.GetString() ?? string.Empty);
                            break;
                        }
                    }
                }

                text = sb.ToString().Trim();
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }

        private static string ResolveDoubaoShortSpeechWsUrl(string configuredUrl)
        {
            string raw = (configuredUrl ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(raw))
            {
                return DoubaoShortSpeechWsUrl;
            }

            if (raw.StartsWith("ws://", StringComparison.OrdinalIgnoreCase) ||
                raw.StartsWith("wss://", StringComparison.OrdinalIgnoreCase))
            {
                return raw;
            }

            if (raw.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                return "ws://" + raw.Substring("http://".Length);
            }

            if (raw.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return "wss://" + raw.Substring("https://".Length);
            }

            return DoubaoShortSpeechWsUrl;
        }

        private static string ResolveDoubaoShortSpeechCluster(string configuredCluster, string modelId)
        {
            string normalizedModel = (modelId ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(normalizedModel) &&
                !normalizedModel.Contains("doubao", StringComparison.OrdinalIgnoreCase))
            {
                return normalizedModel;
            }

            string normalizedConfig = (configuredCluster ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(normalizedConfig) &&
                !string.Equals(normalizedConfig, DoubaoRealtimeDefaultResourceId, StringComparison.OrdinalIgnoreCase))
            {
                return normalizedConfig;
            }

            return DoubaoShortDefaultCluster;
        }

        private static string BuildDoubaoAuthorizationHeader(string token)
        {
            string raw = (token ?? string.Empty).Trim();
            if (raw.StartsWith("Bearer;", StringComparison.OrdinalIgnoreCase))
            {
                return raw;
            }

            if (raw.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                raw = raw.Substring("Bearer ".Length).Trim();
            }

            return $"Bearer; {raw}";
        }

        private static string BuildDoubaoPayloadToken(string token)
        {
            string raw = (token ?? string.Empty).Trim();
            if (raw.StartsWith("Bearer;", StringComparison.OrdinalIgnoreCase))
            {
                return raw.Substring("Bearer;".Length).Trim();
            }

            if (raw.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                return raw.Substring("Bearer ".Length).Trim();
            }

            return raw;
        }

        private static string BuildDoubaoShortSpeechInitialPayloadJson(
            string appId,
            string token,
            string cluster,
            string requestId,
            string boostingTableId = "",
            string boostingTableName = "")
        {
            string effectiveBoostingTableId = (boostingTableId ?? string.Empty).Trim();
            string effectiveBoostingTableName = string.IsNullOrWhiteSpace(effectiveBoostingTableId)
                ? (boostingTableName ?? string.Empty).Trim()
                : string.Empty;

            var request = new Dictionary<string, object>
            {
                ["reqid"] = requestId,
                ["sequence"] = 1
            };
            if (!string.IsNullOrWhiteSpace(effectiveBoostingTableId))
            {
                request["boosting_table_id"] = effectiveBoostingTableId;
            }
            else if (!string.IsNullOrWhiteSpace(effectiveBoostingTableName))
            {
                request["boosting_table_name"] = effectiveBoostingTableName;
            }

            var payload = new
            {
                app = new
                {
                    appid = appId,
                    token,
                    cluster
                },
                user = new
                {
                    uid = string.IsNullOrWhiteSpace(Environment.UserName)
                        ? "canvas-user"
                        : Environment.UserName
                },
                request,
                audio = new
                {
                    format = "wav",
                    rate = 16000,
                    bits = 16,
                    channel = 1
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

        private static bool TryReadDoubaoFrame(
            byte[] frameBytes,
            out byte messageType,
            out byte serialization,
            out byte compression,
            out int? sequence,
            out byte[] payload)
        {
            messageType = 0;
            serialization = 0;
            compression = 0;
            sequence = null;
            payload = Array.Empty<byte>();

            if (frameBytes == null || frameBytes.Length < 8)
            {
                return false;
            }

            int headerWords = frameBytes[0] & 0x0F;
            int headerBytes = Math.Max(4, headerWords * 4);
            if (frameBytes.Length < headerBytes + 4)
            {
                return false;
            }

            messageType = (byte)(frameBytes[1] >> 4);
            byte messageFlags = (byte)(frameBytes[1] & 0x0F);
            serialization = (byte)((frameBytes[2] >> 4) & 0x0F);
            compression = (byte)(frameBytes[2] & 0x0F);

            int offset = headerBytes;
            if ((messageFlags & 0x1) != 0 && frameBytes.Length >= offset + 4)
            {
                sequence = ReadInt32BE(frameBytes, offset);
                offset += 4;
            }

            if (frameBytes.Length < offset + 4)
            {
                return false;
            }

            uint payloadSizeRaw = ReadUInt32BE(frameBytes, offset);
            offset += 4;
            int available = Math.Max(0, frameBytes.Length - offset);
            int payloadSize = (int)Math.Min(payloadSizeRaw, (uint)available);
            if (payloadSize <= 0)
            {
                return true;
            }

            payload = new byte[payloadSize];
            Buffer.BlockCopy(frameBytes, offset, payload, 0, payloadSize);
            return true;
        }

        private static string ParseDoubaoServerError(byte[] frameBytes)
        {
            if (frameBytes == null || frameBytes.Length < 12)
            {
                return string.Empty;
            }

            int headerWords = frameBytes[0] & 0x0F;
            int headerBytes = Math.Max(4, headerWords * 4);
            if (frameBytes.Length < headerBytes + 8)
            {
                return string.Empty;
            }

            int code = ReadInt32BE(frameBytes, headerBytes);
            uint messageSize = ReadUInt32BE(frameBytes, headerBytes + 4);
            int available = Math.Max(0, frameBytes.Length - (headerBytes + 8));
            int size = (int)Math.Min(messageSize, (uint)available);
            if (size <= 0)
            {
                return code == 0 ? string.Empty : code.ToString();
            }

            byte[] payload = new byte[size];
            Buffer.BlockCopy(frameBytes, headerBytes + 8, payload, 0, size);
            if (payload.Length > 0 && payload[0] == 0x1F && payload.Length > 1 && payload[1] == 0x8B)
            {
                payload = GzipDecompress(payload);
            }

            string msg = Encoding.UTF8.GetString(payload).Trim();
            return string.IsNullOrWhiteSpace(msg) ? code.ToString() : $"{code} {msg}".Trim();
        }

        private static bool TryHandleDoubaoJsonPayload(string json, out string recognized, out bool isFinal)
        {
            recognized = string.Empty;
            isFinal = false;

            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            DoubaoAsrResponse payload = null;
            try
            {
                payload = JsonSerializer.Deserialize<DoubaoAsrResponse>(json);
            }
            catch
            {
                return false;
            }

            if (payload == null)
            {
                return false;
            }

            if (payload.Code != 0 && payload.Code != 1000)
            {
                return false;
            }

            string text = string.Empty;
            if (payload.Result != null && payload.Result.Length > 0)
            {
                text = payload.Result[0]?.Text ?? string.Empty;
            }

            recognized = text.Trim();
            isFinal = payload.Sequence < 0;
            return true;
        }

        private static async Task<byte[]> ReceiveDoubaoPacketAsync(ClientWebSocket ws, CancellationToken cancellationToken)
        {
            var buffer = new byte[8192];
            using var ms = new MemoryStream();
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

            return ms.ToArray();
        }

        private static bool LooksLikeJsonText(byte[] payload)
        {
            if (payload == null || payload.Length == 0)
            {
                return false;
            }

            byte first = payload[0];
            if (first == '{' || first == '[')
            {
                return true;
            }

            return false;
        }

        private static byte[] GzipCompress(byte[] data)
        {
            byte[] source = data ?? Array.Empty<byte>();
            using var ms = new MemoryStream();
            using (var gzip = new GZipStream(ms, CompressionLevel.Fastest, leaveOpen: true))
            {
                gzip.Write(source, 0, source.Length);
            }

            return ms.ToArray();
        }

        private static byte[] GzipDecompress(byte[] data)
        {
            byte[] source = data ?? Array.Empty<byte>();
            if (source.Length == 0)
            {
                return Array.Empty<byte>();
            }

            using var input = new MemoryStream(source);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            gzip.CopyTo(output);
            return output.ToArray();
        }

        private static void WriteInt32BE(byte[] buffer, int offset, int value)
        {
            buffer[offset] = (byte)((value >> 24) & 0xFF);
            buffer[offset + 1] = (byte)((value >> 16) & 0xFF);
            buffer[offset + 2] = (byte)((value >> 8) & 0xFF);
            buffer[offset + 3] = (byte)(value & 0xFF);
        }

        private static int ReadInt32BE(byte[] buffer, int offset)
        {
            return (buffer[offset] << 24)
                 | (buffer[offset + 1] << 16)
                 | (buffer[offset + 2] << 8)
                 | buffer[offset + 3];
        }

        private static uint ReadUInt32BE(byte[] buffer, int offset)
        {
            return (uint)((buffer[offset] << 24)
                | (buffer[offset + 1] << 16)
                | (buffer[offset + 2] << 8)
                | buffer[offset + 3]);
        }

        private static string BuildTencentAuthorization(
            string secretId,
            string secretKey,
            string host,
            string service,
            long timestamp,
            string date,
            string bodyJson)
        {
            const string algorithm = "TC3-HMAC-SHA256";
            string canonicalHeaders = "content-type:application/json; charset=utf-8\nhost:" + host + "\n";
            string signedHeaders = "content-type;host";
            string canonicalRequest =
                "POST\n/\n\n" +
                canonicalHeaders + "\n" +
                signedHeaders + "\n" +
                Sha256Hex(bodyJson);

            string credentialScope = $"{date}/{service}/tc3_request";
            string stringToSign =
                $"{algorithm}\n{timestamp}\n{credentialScope}\n{Sha256Hex(canonicalRequest)}";

            byte[] secretDate = HmacSha256Bytes(Encoding.UTF8.GetBytes("TC3" + secretKey), date);
            byte[] secretService = HmacSha256Bytes(secretDate, service);
            byte[] secretSigning = HmacSha256Bytes(secretService, "tc3_request");
            string signature = ToLowerHex(HmacSha256Bytes(secretSigning, stringToSign));

            return $"{algorithm} Credential={secretId}/{credentialScope}, SignedHeaders={signedHeaders}, Signature={signature}";
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

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            try { _tokenLock.Dispose(); } catch { }
            try { _aliyunTokenLock.Dispose(); } catch { }
            try { _httpClient.Dispose(); } catch { }
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

        private sealed class DoubaoAsrResponse
        {
            [JsonPropertyName("code")]
            public int Code { get; set; }

            [JsonPropertyName("message")]
            public string Message { get; set; } = string.Empty;

            [JsonPropertyName("sequence")]
            public int Sequence { get; set; }

            [JsonPropertyName("result")]
            public DoubaoAsrResult[] Result { get; set; } = Array.Empty<DoubaoAsrResult>();
        }

        private sealed class DoubaoAsrResult
        {
            [JsonPropertyName("text")]
            public string Text { get; set; } = string.Empty;
        }

        private sealed class TencentSentenceRecognitionEnvelope
        {
            [JsonPropertyName("Response")]
            public TencentSentenceRecognitionResponse Response { get; set; } = new();
        }

        private sealed class TencentSentenceRecognitionResponse
        {
            [JsonPropertyName("Result")]
            public string Result { get; set; } = string.Empty;

            [JsonPropertyName("Error")]
            public TencentError Error { get; set; }
        }

        private sealed class TencentError
        {
            [JsonPropertyName("Code")]
            public string Code { get; set; } = string.Empty;

            [JsonPropertyName("Message")]
            public string Message { get; set; } = string.Empty;
        }

        private sealed class AliyunAsrResponse
        {
            [JsonPropertyName("task_id")]
            public string TaskId { get; set; } = string.Empty;

            [JsonPropertyName("result")]
            public string Result { get; set; } = string.Empty;

            [JsonPropertyName("status")]
            public int Status { get; set; }

            [JsonPropertyName("message")]
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

        private static string TrimForLog(string value)
        {
            string text = value ?? string.Empty;
            if (text.Length <= 400)
            {
                return text;
            }

            return text.Substring(0, 400) + "...";
        }
    }
}
