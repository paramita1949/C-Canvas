using System;
using System.Diagnostics;
using System.Net.Http;
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
        private string _token = string.Empty;
        private DateTime _tokenExpireUtc = DateTime.MinValue;
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
                _ => !string.IsNullOrWhiteSpace(_config.LiveCaptionBaiduAppId) &&
                     !string.IsNullOrWhiteSpace(_config.LiveCaptionBaiduApiKey) &&
                     !string.IsNullOrWhiteSpace(_config.LiveCaptionBaiduSecretKey)
            };

        public string MissingConfigSummary
        {
            get
            {
                var missing = new System.Collections.Generic.List<string>();
                if (string.Equals(GetShortSpeechProvider(), "tencent", StringComparison.Ordinal))
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
            if (wavBytes == null || wavBytes.Length == 0 || !IsConfigured)
            {
                Debug.WriteLine("[BibleVoice][Baidu] short-speech skipped: empty-audio-or-missing-config");
                return string.Empty;
            }

            return string.Equals(GetShortSpeechProvider(), "tencent", StringComparison.Ordinal)
                ? await TranscribeWithTencentAsync(wavBytes, cancellationToken)
                : await TranscribeWithBaiduAsync(wavBytes, cancellationToken);
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
            string requestUrl = "https://asr.tencentcloudapi.com";
            string bodyJson = JsonSerializer.Serialize(new
            {
                ProjectId = 0,
                SubServiceType = 2,
                EngSerViceType = engineType,
                SourceType = 1,
                VoiceFormat = "wav",
                Data = Convert.ToBase64String(wavBytes),
                DataLen = wavBytes.Length
            });

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

            Debug.WriteLine($"[BibleVoice][Tencent] short-speech request: model={model}, engineType={engineType}, url={requestUrl}, wavBytes={wavBytes.Length}");

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

            TencentSentenceRecognitionEnvelope payload = null;
            try
            {
                payload = JsonSerializer.Deserialize<TencentSentenceRecognitionEnvelope>(responseText);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[BibleVoice][Tencent] short-speech deserialize-failed: {ex.Message}; raw={TrimForLog(responseText)}");
                return string.Empty;
            }

            if (payload?.Response == null)
            {
                Debug.WriteLine($"[BibleVoice][Tencent] short-speech empty-response: raw={TrimForLog(responseText)}");
                return string.Empty;
            }

            if (payload.Response.Error != null && !string.IsNullOrWhiteSpace(payload.Response.Error.Code))
            {
                Debug.WriteLine($"[BibleVoice][Tencent] short-speech asr-error: code={payload.Response.Error.Code}, message={payload.Response.Error.Message}, raw={TrimForLog(responseText)}");
                return string.Empty;
            }

            string recognized = (payload.Response.Result ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(recognized))
            {
                Debug.WriteLine($"[BibleVoice][Tencent] short-speech empty-result: raw={TrimForLog(responseText)}");
                return string.Empty;
            }

            Debug.WriteLine($"[BibleVoice][Tencent] short-speech success: recognized={recognized}");
            return recognized;
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
                _ => "baidu"
            };
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
