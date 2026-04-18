using System;
using System.Text.Json.Serialization;

namespace ImageColorChanger.Core
{
    public partial class ConfigManager
    {
        public string LiveCaptionAsrProvider
        {
            get
            {
                var current = string.IsNullOrWhiteSpace(_config.LiveCaptionAsrProvider) ? "baidu" : _config.LiveCaptionAsrProvider;
                return NormalizeLiveCaptionAsrProvider(current);
            }
            set
            {
                var next = NormalizeLiveCaptionAsrProvider(value);
                if (!string.Equals(_config.LiveCaptionAsrProvider, next, StringComparison.OrdinalIgnoreCase))
                {
                    _config.LiveCaptionAsrProvider = next;
                    SaveConfig();
                }
            }
        }

        public string LiveCaptionSpeechMode
        {
            get => NormalizeLiveCaptionSpeechMode(_config.LiveCaptionSpeechMode);
            set
            {
                string next = NormalizeLiveCaptionSpeechMode(value);
                if (!string.Equals(_config.LiveCaptionSpeechMode, next, StringComparison.Ordinal))
                {
                    _config.LiveCaptionSpeechMode = next;
                    SaveConfig();
                }
            }
        }

        public bool LiveCaptionRealtimeEnabled
        {
            get => _config.LiveCaptionRealtimeEnabled;
            set
            {
                if (_config.LiveCaptionRealtimeEnabled != value)
                {
                    _config.LiveCaptionRealtimeEnabled = value;
                    SaveConfig();
                }
            }
        }

        public bool LiveCaptionShortPhraseEnabled
        {
            get => _config.LiveCaptionShortPhraseEnabled;
            set
            {
                if (_config.LiveCaptionShortPhraseEnabled != value)
                {
                    _config.LiveCaptionShortPhraseEnabled = value;
                    SaveConfig();
                }
            }
        }

        public string LiveCaptionVerseSource
        {
            get => _config.LiveCaptionVerseSource ?? "shortPhrase";
            set
            {
                string v = value ?? "shortPhrase";
                if (!string.Equals(_config.LiveCaptionVerseSource, v, StringComparison.Ordinal))
                {
                    _config.LiveCaptionVerseSource = v;
                    SaveConfig();
                }
            }
        }

        public string LiveCaptionAudioInputMode
        {
            get => NormalizeLiveCaptionAudioInputMode(_config.LiveCaptionAudioInputMode);
            set
            {
                string next = NormalizeLiveCaptionAudioInputMode(value);
                if (!string.Equals(_config.LiveCaptionAudioInputMode, next, StringComparison.Ordinal))
                {
                    _config.LiveCaptionAudioInputMode = next;
                    SaveConfig();
                }
            }
        }

        public string LiveCaptionInputDeviceId
        {
            get => _config.LiveCaptionInputDeviceId ?? string.Empty;
            set
            {
                string next = value?.Trim() ?? string.Empty;
                if (!string.Equals(_config.LiveCaptionInputDeviceId, next, StringComparison.Ordinal))
                {
                    _config.LiveCaptionInputDeviceId = next;
                    SaveConfig();
                }
            }
        }

        public string LiveCaptionSystemDeviceId
        {
            get => _config.LiveCaptionSystemDeviceId ?? string.Empty;
            set
            {
                string next = value?.Trim() ?? string.Empty;
                if (!string.Equals(_config.LiveCaptionSystemDeviceId, next, StringComparison.Ordinal))
                {
                    _config.LiveCaptionSystemDeviceId = next;
                    SaveConfig();
                }
            }
        }

        private static string NormalizeLiveCaptionAudioInputMode(string value)
        {
            return (value ?? string.Empty).Trim().ToLowerInvariant() switch
            {
                "input" => "input",
                _ => "system"
            };
        }

        private static string NormalizeLiveCaptionAsrProvider(string value)
        {
            var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
            return normalized switch
            {
                "baidu" => "baidu",
                "tencent" => "tencent",
                "aliyun" => "aliyun",
                "doubao" => "doubao",
                // 迁移历史本地模式：统一切回云端豆包。
                "funasr" => "doubao",
                _ => "baidu"
            };
        }

        private static string NormalizeLiveCaptionSpeechMode(string value)
        {
            return (value ?? string.Empty).Trim().ToLowerInvariant() switch
            {
                "short" => "short",
                _ => "realtime"
            };
        }

        private static string GetDefaultRealtimeBaseUrl(string provider)
        {
            return NormalizeLiveCaptionAsrProvider(provider) switch
            {
                "aliyun" => "wss://nls-gateway-cn-shanghai.aliyuncs.com/ws/v1",
                "doubao" => "wss://openspeech.bytedance.com/api/v3/sauc/bigmodel_async",
                "tencent" => "wss://asr.cloud.tencent.com/asr/v2",
                _ => "wss://vop.baidu.com/realtime_asr"
            };
        }

        private static bool IsLegacyCustomProxyUrl(string value)
        {
            return string.Equals((value ?? string.Empty).Trim(), "http://localhost:8317/v1", StringComparison.OrdinalIgnoreCase);
        }

        public string LiveCaptionProxyBaseUrl
        {
            get => string.IsNullOrWhiteSpace(_config.LiveCaptionProxyBaseUrl) ? "http://localhost:8317/v1" : _config.LiveCaptionProxyBaseUrl;
            set
            {
                var next = string.IsNullOrWhiteSpace(value) ? "http://localhost:8317/v1" : value.Trim();
                if (!string.Equals(_config.LiveCaptionProxyBaseUrl, next, StringComparison.Ordinal))
                {
                    _config.LiveCaptionProxyBaseUrl = next;
                    SaveConfig();
                }
            }
        }

        public string LiveCaptionRealtimeAsrProvider
        {
            get
            {
                string current = string.IsNullOrWhiteSpace(_config.LiveCaptionRealtimeAsrProvider)
                    ? LiveCaptionAsrProvider
                    : _config.LiveCaptionRealtimeAsrProvider;
                return NormalizeLiveCaptionAsrProvider(current);
            }
            set
            {
                string next = NormalizeLiveCaptionAsrProvider(value);
                if (!string.Equals(_config.LiveCaptionRealtimeAsrProvider, next, StringComparison.OrdinalIgnoreCase))
                {
                    _config.LiveCaptionRealtimeAsrProvider = next;
                    SaveConfig();
                }
            }
        }

        public string LiveCaptionRealtimeProxyBaseUrl
        {
            get
            {
                string current = _config.LiveCaptionRealtimeProxyBaseUrl ?? string.Empty;
                if (string.IsNullOrWhiteSpace(current) || IsLegacyCustomProxyUrl(current))
                {
                    return GetDefaultRealtimeBaseUrl(LiveCaptionRealtimeAsrProvider);
                }

                return current;
            }
            set
            {
                string next = string.IsNullOrWhiteSpace(value)
                    ? GetDefaultRealtimeBaseUrl(LiveCaptionRealtimeAsrProvider)
                    : value.Trim();
                if (!string.Equals(_config.LiveCaptionRealtimeProxyBaseUrl, next, StringComparison.Ordinal))
                {
                    _config.LiveCaptionRealtimeProxyBaseUrl = next;
                    SaveConfig();
                }
            }
        }

        public string LiveCaptionRealtimeAsrModel
        {
            get => string.IsNullOrWhiteSpace(_config.LiveCaptionRealtimeAsrModel)
                ? LiveCaptionAsrModel
                : _config.LiveCaptionRealtimeAsrModel;
            set
            {
                string next = string.IsNullOrWhiteSpace(value) ? LiveCaptionAsrModel : value.Trim();
                if (!string.Equals(_config.LiveCaptionRealtimeAsrModel, next, StringComparison.Ordinal))
                {
                    _config.LiveCaptionRealtimeAsrModel = next;
                    SaveConfig();
                }
            }
        }

        public int LiveCaptionRealtimeBaiduDevPid
        {
            get => _config.LiveCaptionRealtimeBaiduDevPid <= 0 ? LiveCaptionBaiduDevPid : _config.LiveCaptionRealtimeBaiduDevPid;
            set
            {
                int next = value <= 0 ? LiveCaptionBaiduDevPid : value;
                if (_config.LiveCaptionRealtimeBaiduDevPid != next)
                {
                    _config.LiveCaptionRealtimeBaiduDevPid = next;
                    SaveConfig();
                }
            }
        }

        public string LiveCaptionShortAsrProvider
        {
            get
            {
                string current = string.IsNullOrWhiteSpace(_config.LiveCaptionShortAsrProvider)
                    ? "baidu"
                    : _config.LiveCaptionShortAsrProvider;
                return NormalizeLiveCaptionAsrProvider(current);
            }
            set
            {
                string next = NormalizeLiveCaptionAsrProvider(value);
                if (!string.Equals(_config.LiveCaptionShortAsrProvider, next, StringComparison.OrdinalIgnoreCase))
                {
                    _config.LiveCaptionShortAsrProvider = next;
                    SaveConfig();
                }
            }
        }

        public string LiveCaptionShortProxyBaseUrl
        {
            get => string.IsNullOrWhiteSpace(_config.LiveCaptionShortProxyBaseUrl)
                ? "http://vop.baidu.com/server_api"
                : _config.LiveCaptionShortProxyBaseUrl;
            set
            {
                string next = string.IsNullOrWhiteSpace(value) ? "http://vop.baidu.com/server_api" : value.Trim();
                if (!string.Equals(_config.LiveCaptionShortProxyBaseUrl, next, StringComparison.Ordinal))
                {
                    _config.LiveCaptionShortProxyBaseUrl = next;
                    SaveConfig();
                }
            }
        }

        public string LiveCaptionShortAsrModel
        {
            get => string.IsNullOrWhiteSpace(_config.LiveCaptionShortAsrModel)
                ? "baidu-short-standard"
                : _config.LiveCaptionShortAsrModel;
            set
            {
                string next = string.IsNullOrWhiteSpace(value) ? "baidu-short-standard" : value.Trim();
                if (!string.Equals(_config.LiveCaptionShortAsrModel, next, StringComparison.Ordinal))
                {
                    _config.LiveCaptionShortAsrModel = next;
                    SaveConfig();
                }
            }
        }

        public int LiveCaptionShortBaiduDevPid
        {
            get => _config.LiveCaptionShortBaiduDevPid <= 0 ? 1537 : _config.LiveCaptionShortBaiduDevPid;
            set
            {
                int next = value <= 0 ? 1537 : value;
                if (_config.LiveCaptionShortBaiduDevPid != next)
                {
                    _config.LiveCaptionShortBaiduDevPid = next;
                    SaveConfig();
                }
            }
        }

        public bool LiveCaptionFunAsrAllowInsecureTls
        {
            get => _config.LiveCaptionFunAsrAllowInsecureTls;
            set
            {
                if (_config.LiveCaptionFunAsrAllowInsecureTls != value)
                {
                    _config.LiveCaptionFunAsrAllowInsecureTls = value;
                    SaveConfig();
                }
            }
        }

        public bool LiveCaptionNdiEnabled
        {
            get => _config.LiveCaptionNdiEnabled;
            set
            {
                if (_config.LiveCaptionNdiEnabled != value)
                {
                    _config.LiveCaptionNdiEnabled = value;
                }
            }
        }

        public string LiveCaptionApiKey
        {
            get => _config.LiveCaptionApiKey ?? string.Empty;
            set
            {
                var next = value ?? string.Empty;
                if (!string.Equals(_config.LiveCaptionApiKey, next, StringComparison.Ordinal))
                {
                    _config.LiveCaptionApiKey = next;
                    SaveConfig();
                }
            }
        }

        public string LiveCaptionAsrModel
        {
            get => string.IsNullOrWhiteSpace(_config.LiveCaptionAsrModel) ? "gpt-4o-mini-transcribe" : _config.LiveCaptionAsrModel;
            set
            {
                var next = string.IsNullOrWhiteSpace(value) ? "gpt-4o-mini-transcribe" : value.Trim();
                if (!string.Equals(_config.LiveCaptionAsrModel, next, StringComparison.Ordinal))
                {
                    _config.LiveCaptionAsrModel = next;
                    SaveConfig();
                }
            }
        }

        public string LiveCaptionBaiduAppId
        {
            get => _config.LiveCaptionBaiduAppId ?? string.Empty;
            set
            {
                var next = value ?? string.Empty;
                if (!string.Equals(_config.LiveCaptionBaiduAppId, next, StringComparison.Ordinal))
                {
                    _config.LiveCaptionBaiduAppId = next;
                    SaveConfig();
                }
            }
        }

        public string LiveCaptionBaiduApiKey
        {
            get => _config.LiveCaptionBaiduApiKey ?? string.Empty;
            set
            {
                var next = value ?? string.Empty;
                if (!string.Equals(_config.LiveCaptionBaiduApiKey, next, StringComparison.Ordinal))
                {
                    _config.LiveCaptionBaiduApiKey = next;
                    SaveConfig();
                }
            }
        }

        public string LiveCaptionBaiduSecretKey
        {
            get => _config.LiveCaptionBaiduSecretKey ?? string.Empty;
            set
            {
                var next = value ?? string.Empty;
                if (!string.Equals(_config.LiveCaptionBaiduSecretKey, next, StringComparison.Ordinal))
                {
                    _config.LiveCaptionBaiduSecretKey = next;
                    SaveConfig();
                }
            }
        }

        public int LiveCaptionBaiduDevPid
        {
            get => _config.LiveCaptionBaiduDevPid <= 0 ? 1537 : _config.LiveCaptionBaiduDevPid;
            set
            {
                var next = value <= 0 ? 1537 : value;
                if (_config.LiveCaptionBaiduDevPid != next)
                {
                    _config.LiveCaptionBaiduDevPid = next;
                    SaveConfig();
                }
            }
        }

        public string LiveCaptionTencentAppId
        {
            get => _config.LiveCaptionTencentAppId ?? string.Empty;
            set
            {
                var next = value ?? string.Empty;
                if (!string.Equals(_config.LiveCaptionTencentAppId, next, StringComparison.Ordinal))
                {
                    _config.LiveCaptionTencentAppId = next;
                    SaveConfig();
                }
            }
        }

        public string LiveCaptionTencentSecretId
        {
            get => _config.LiveCaptionTencentSecretId ?? string.Empty;
            set
            {
                var next = value ?? string.Empty;
                if (!string.Equals(_config.LiveCaptionTencentSecretId, next, StringComparison.Ordinal))
                {
                    _config.LiveCaptionTencentSecretId = next;
                    SaveConfig();
                }
            }
        }

        public string LiveCaptionTencentSecretKey
        {
            get => _config.LiveCaptionTencentSecretKey ?? string.Empty;
            set
            {
                var next = value ?? string.Empty;
                if (!string.Equals(_config.LiveCaptionTencentSecretKey, next, StringComparison.Ordinal))
                {
                    _config.LiveCaptionTencentSecretKey = next;
                    SaveConfig();
                }
            }
        }

        public string LiveCaptionAliAppKey
        {
            get => _config.LiveCaptionAliAppKey ?? string.Empty;
            set
            {
                var next = value ?? string.Empty;
                if (!string.Equals(_config.LiveCaptionAliAppKey, next, StringComparison.Ordinal))
                {
                    _config.LiveCaptionAliAppKey = next;
                    SaveConfig();
                }
            }
        }

        public string LiveCaptionAliAccessKeyId
        {
            get => _config.LiveCaptionAliAccessKeyId ?? string.Empty;
            set
            {
                var next = value ?? string.Empty;
                if (!string.Equals(_config.LiveCaptionAliAccessKeyId, next, StringComparison.Ordinal))
                {
                    _config.LiveCaptionAliAccessKeyId = next;
                    SaveConfig();
                }
            }
        }

        public string LiveCaptionAliAccessKeySecret
        {
            get => _config.LiveCaptionAliAccessKeySecret ?? string.Empty;
            set
            {
                var next = value ?? string.Empty;
                if (!string.Equals(_config.LiveCaptionAliAccessKeySecret, next, StringComparison.Ordinal))
                {
                    _config.LiveCaptionAliAccessKeySecret = next;
                    SaveConfig();
                }
            }
        }

        public string LiveCaptionDoubaoAppKey
        {
            get => _config.LiveCaptionDoubaoAppKey ?? string.Empty;
            set
            {
                var next = value ?? string.Empty;
                if (!string.Equals(_config.LiveCaptionDoubaoAppKey, next, StringComparison.Ordinal))
                {
                    _config.LiveCaptionDoubaoAppKey = next;
                    SaveConfig();
                }
            }
        }

        public string LiveCaptionDoubaoAccessKey
        {
            get => _config.LiveCaptionDoubaoAccessKey ?? string.Empty;
            set
            {
                var next = value ?? string.Empty;
                if (!string.Equals(_config.LiveCaptionDoubaoAccessKey, next, StringComparison.Ordinal))
                {
                    _config.LiveCaptionDoubaoAccessKey = next;
                    SaveConfig();
                }
            }
        }

        public string LiveCaptionDoubaoResourceId
        {
            get => string.IsNullOrWhiteSpace(_config.LiveCaptionDoubaoResourceId)
                ? "volc.seedasr.sauc.duration"
                : _config.LiveCaptionDoubaoResourceId;
            set
            {
                var next = string.IsNullOrWhiteSpace(value)
                    ? "volc.seedasr.sauc.duration"
                    : value.Trim();
                if (!string.Equals(_config.LiveCaptionDoubaoResourceId, next, StringComparison.Ordinal))
                {
                    _config.LiveCaptionDoubaoResourceId = next;
                    SaveConfig();
                }
            }
        }

        public bool LiveCaptionReserveWorkArea
        {
            get => _config.LiveCaptionReserveWorkArea;
            set
            {
                if (_config.LiveCaptionReserveWorkArea != value)
                {
                    _config.LiveCaptionReserveWorkArea = value;
                    SaveConfig();
                }
            }
        }

        public double LiveCaptionFloatingLeft => _config.LiveCaptionFloatingLeft;

        public double LiveCaptionFloatingTop => _config.LiveCaptionFloatingTop;

        public double LiveCaptionFloatingWidth => _config.LiveCaptionFloatingWidth;

        public double LiveCaptionFloatingHeight => _config.LiveCaptionFloatingHeight;

        public bool TryGetLiveCaptionFloatingBounds(out double left, out double top, out double width, out double height)
        {
            left = _config.LiveCaptionFloatingLeft;
            top = _config.LiveCaptionFloatingTop;
            width = _config.LiveCaptionFloatingWidth;
            height = _config.LiveCaptionFloatingHeight;

            return width > 0 && height > 0;
        }

        public void SetLiveCaptionFloatingBounds(double left, double top, double width, double height)
        {
            if (double.IsNaN(left) || double.IsInfinity(left) ||
                double.IsNaN(top) || double.IsInfinity(top) ||
                double.IsNaN(width) || double.IsInfinity(width) ||
                double.IsNaN(height) || double.IsInfinity(height))
            {
                return;
            }

            double nextWidth = Math.Max(1, width);
            double nextHeight = Math.Max(1, height);
            bool changed =
                Math.Abs(_config.LiveCaptionFloatingLeft - left) > 0.1 ||
                Math.Abs(_config.LiveCaptionFloatingTop - top) > 0.1 ||
                Math.Abs(_config.LiveCaptionFloatingWidth - nextWidth) > 0.1 ||
                Math.Abs(_config.LiveCaptionFloatingHeight - nextHeight) > 0.1;

            if (!changed)
            {
                return;
            }

            _config.LiveCaptionFloatingLeft = left;
            _config.LiveCaptionFloatingTop = top;
            _config.LiveCaptionFloatingWidth = nextWidth;
            _config.LiveCaptionFloatingHeight = nextHeight;
            SaveConfig();
        }

        public string LiveCaptionProjectionOrientation
        {
            get => string.IsNullOrWhiteSpace(_config.LiveCaptionProjectionOrientation)
                ? "horizontal"
                : _config.LiveCaptionProjectionOrientation;
            set
            {
                var next = string.IsNullOrWhiteSpace(value) ? "horizontal" : value.Trim().ToLowerInvariant();
                if (!string.Equals(_config.LiveCaptionProjectionOrientation, next, StringComparison.Ordinal))
                {
                    _config.LiveCaptionProjectionOrientation = next;
                    SaveConfig();
                }
            }
        }

        public string LiveCaptionProjectionHorizontalAnchor
        {
            get => string.IsNullOrWhiteSpace(_config.LiveCaptionProjectionHorizontalAnchor)
                ? "center"
                : _config.LiveCaptionProjectionHorizontalAnchor;
            set
            {
                var next = string.IsNullOrWhiteSpace(value) ? "center" : value.Trim().ToLowerInvariant();
                if (!string.Equals(_config.LiveCaptionProjectionHorizontalAnchor, next, StringComparison.Ordinal))
                {
                    _config.LiveCaptionProjectionHorizontalAnchor = next;
                    SaveConfig();
                }
            }
        }

        public string LiveCaptionProjectionVerticalAnchor
        {
            get => string.IsNullOrWhiteSpace(_config.LiveCaptionProjectionVerticalAnchor)
                ? "top"
                : _config.LiveCaptionProjectionVerticalAnchor;
            set
            {
                var next = string.IsNullOrWhiteSpace(value) ? "top" : value.Trim().ToLowerInvariant();
                if (!string.Equals(_config.LiveCaptionProjectionVerticalAnchor, next, StringComparison.Ordinal))
                {
                    _config.LiveCaptionProjectionVerticalAnchor = next;
                    SaveConfig();
                }
            }
        }

        public string LiveCaptionLatestTextColor
        {
            get => string.IsNullOrWhiteSpace(_config.LiveCaptionLatestTextColor)
                ? "#FFFF00"
                : _config.LiveCaptionLatestTextColor;
            set
            {
                var next = string.IsNullOrWhiteSpace(value) ? "#FFFF00" : value.Trim();
                if (!string.Equals(_config.LiveCaptionLatestTextColor, next, StringComparison.OrdinalIgnoreCase))
                {
                    _config.LiveCaptionLatestTextColor = next;
                    SaveConfig();
                }
            }
        }

        public string LiveCaptionTextColor
        {
            get => string.IsNullOrWhiteSpace(_config.LiveCaptionTextColor)
                ? "#FFFFFF"
                : _config.LiveCaptionTextColor;
            set
            {
                var next = string.IsNullOrWhiteSpace(value) ? "#FFFFFF" : value.Trim();
                if (!string.Equals(_config.LiveCaptionTextColor, next, StringComparison.OrdinalIgnoreCase))
                {
                    _config.LiveCaptionTextColor = next;
                    SaveConfig();
                }
            }
        }

        public string LiveCaptionFontFamily
        {
            get => string.IsNullOrWhiteSpace(_config.LiveCaptionFontFamily)
                ? string.Empty
                : _config.LiveCaptionFontFamily;
            set
            {
                var next = value?.Trim() ?? string.Empty;
                if (!string.Equals(_config.LiveCaptionFontFamily, next, StringComparison.Ordinal))
                {
                    _config.LiveCaptionFontFamily = next;
                    SaveConfig();
                }
            }
        }

        public double LiveCaptionFontSize
        {
            get => _config.LiveCaptionFontSize <= 0 ? 0 : _config.LiveCaptionFontSize;
            set
            {
                double next = value <= 0 ? 0 : value;
                if (Math.Abs(_config.LiveCaptionFontSize - next) > 0.001)
                {
                    _config.LiveCaptionFontSize = next;
                    SaveConfig();
                }
            }
        }

        public double LiveCaptionPadding
        {
            get => _config.LiveCaptionPadding <= 0 ? 0 : _config.LiveCaptionPadding;
            set
            {
                double next = value <= 0 ? 0 : value;
                if (Math.Abs(_config.LiveCaptionPadding - next) > 0.001)
                {
                    _config.LiveCaptionPadding = next;
                    SaveConfig();
                }
            }
        }

        public double LiveCaptionLetterSpacing
        {
            get => _config.LiveCaptionLetterSpacing;
            set
            {
                double next = Math.Clamp(value, 0, 10);
                if (Math.Abs(_config.LiveCaptionLetterSpacing - next) > 0.001)
                {
                    _config.LiveCaptionLetterSpacing = next;
                    SaveConfig();
                }
            }
        }

        public double LiveCaptionLineGap
        {
            get => _config.LiveCaptionLineGap;
            set
            {
                double next = Math.Clamp(value, 10, 60);
                if (Math.Abs(_config.LiveCaptionLineGap - next) > 0.001)
                {
                    _config.LiveCaptionLineGap = next;
                    SaveConfig();
                }
            }
        }

        public string LiveCaptionTextAlignment
        {
            get => string.IsNullOrWhiteSpace(_config.LiveCaptionTextAlignment)
                ? "left"
                : _config.LiveCaptionTextAlignment;
            set
            {
                var next = string.IsNullOrWhiteSpace(value) ? "left" : value.Trim().ToLowerInvariant();
                if (!string.Equals(_config.LiveCaptionTextAlignment, next, StringComparison.Ordinal))
                {
                    _config.LiveCaptionTextAlignment = next;
                    SaveConfig();
                }
            }
        }

        public string LiveCaptionLocalLatestTextColor
        {
            get => string.IsNullOrWhiteSpace(_config.LiveCaptionLocalLatestTextColor)
                ? LiveCaptionLatestTextColor
                : _config.LiveCaptionLocalLatestTextColor;
            set
            {
                var next = string.IsNullOrWhiteSpace(value) ? "#FFFF00" : value.Trim();
                if (!string.Equals(_config.LiveCaptionLocalLatestTextColor, next, StringComparison.OrdinalIgnoreCase))
                {
                    _config.LiveCaptionLocalLatestTextColor = next;
                    SaveConfig();
                }
            }
        }

        public string LiveCaptionLocalTextColor
        {
            get => string.IsNullOrWhiteSpace(_config.LiveCaptionLocalTextColor)
                ? LiveCaptionTextColor
                : _config.LiveCaptionLocalTextColor;
            set
            {
                var next = string.IsNullOrWhiteSpace(value) ? "#FFFFFF" : value.Trim();
                if (!string.Equals(_config.LiveCaptionLocalTextColor, next, StringComparison.OrdinalIgnoreCase))
                {
                    _config.LiveCaptionLocalTextColor = next;
                    SaveConfig();
                }
            }
        }

        public string LiveCaptionLocalFontFamily
        {
            get => string.IsNullOrWhiteSpace(_config.LiveCaptionLocalFontFamily)
                ? LiveCaptionFontFamily
                : _config.LiveCaptionLocalFontFamily;
            set
            {
                var next = value?.Trim() ?? string.Empty;
                if (!string.Equals(_config.LiveCaptionLocalFontFamily, next, StringComparison.Ordinal))
                {
                    _config.LiveCaptionLocalFontFamily = next;
                    SaveConfig();
                }
            }
        }

        public double LiveCaptionLocalFontSize
        {
            get => _config.LiveCaptionLocalFontSize <= 0 ? LiveCaptionFontSize : _config.LiveCaptionLocalFontSize;
            set
            {
                double next = value <= 0 ? 0 : value;
                if (Math.Abs(_config.LiveCaptionLocalFontSize - next) > 0.001)
                {
                    _config.LiveCaptionLocalFontSize = next;
                    SaveConfig();
                }
            }
        }

        public double LiveCaptionLocalPadding
        {
            get => _config.LiveCaptionLocalPadding <= 0 ? LiveCaptionPadding : _config.LiveCaptionLocalPadding;
            set
            {
                double next = value <= 0 ? 0 : value;
                if (Math.Abs(_config.LiveCaptionLocalPadding - next) > 0.001)
                {
                    _config.LiveCaptionLocalPadding = next;
                    SaveConfig();
                }
            }
        }

        public double LiveCaptionLocalLetterSpacing
        {
            get => _config.LiveCaptionLocalLetterSpacing <= 0 ? LiveCaptionLetterSpacing : _config.LiveCaptionLocalLetterSpacing;
            set
            {
                double next = Math.Clamp(value, 0, 10);
                if (Math.Abs(_config.LiveCaptionLocalLetterSpacing - next) > 0.001)
                {
                    _config.LiveCaptionLocalLetterSpacing = next;
                    SaveConfig();
                }
            }
        }

        public double LiveCaptionLocalLineGap
        {
            get => _config.LiveCaptionLocalLineGap <= 0 ? LiveCaptionLineGap : _config.LiveCaptionLocalLineGap;
            set
            {
                double next = Math.Clamp(value, 10, 60);
                if (Math.Abs(_config.LiveCaptionLocalLineGap - next) > 0.001)
                {
                    _config.LiveCaptionLocalLineGap = next;
                    SaveConfig();
                }
            }
        }

        public int LiveCaptionNdiLineCharLimit
        {
            get => _config.LiveCaptionNdiLineCharLimit <= 0 ? 30 : Math.Clamp(_config.LiveCaptionNdiLineCharLimit, 8, 80);
            set
            {
                int next = Math.Clamp(value, 8, 80);
                if (_config.LiveCaptionNdiLineCharLimit != next)
                {
                    _config.LiveCaptionNdiLineCharLimit = next;
                    SaveConfig();
                }
            }
        }

        public string LiveCaptionNdiTextColor
        {
            get => string.IsNullOrWhiteSpace(_config.LiveCaptionNdiTextColor)
                ? LiveCaptionTextColor
                : _config.LiveCaptionNdiTextColor;
            set
            {
                var next = string.IsNullOrWhiteSpace(value) ? "#FFFFFF" : value.Trim();
                if (!string.Equals(_config.LiveCaptionNdiTextColor, next, StringComparison.OrdinalIgnoreCase))
                {
                    _config.LiveCaptionNdiTextColor = next;
                    SaveConfig();
                }
            }
        }

        public string LiveCaptionNdiLatestTextColor
        {
            get => string.IsNullOrWhiteSpace(_config.LiveCaptionNdiLatestTextColor)
                ? LiveCaptionLatestTextColor
                : _config.LiveCaptionNdiLatestTextColor;
            set
            {
                var next = string.IsNullOrWhiteSpace(value) ? "#FFFF00" : value.Trim();
                if (!string.Equals(_config.LiveCaptionNdiLatestTextColor, next, StringComparison.OrdinalIgnoreCase))
                {
                    _config.LiveCaptionNdiLatestTextColor = next;
                    SaveConfig();
                }
            }
        }

        public string LiveCaptionNdiFontFamily
        {
            get => string.IsNullOrWhiteSpace(_config.LiveCaptionNdiFontFamily)
                ? LiveCaptionFontFamily
                : _config.LiveCaptionNdiFontFamily;
            set
            {
                var next = value?.Trim() ?? string.Empty;
                if (!string.Equals(_config.LiveCaptionNdiFontFamily, next, StringComparison.Ordinal))
                {
                    _config.LiveCaptionNdiFontFamily = next;
                    SaveConfig();
                }
            }
        }

        public double LiveCaptionNdiFontSize
        {
            get => _config.LiveCaptionNdiFontSize <= 0 ? LiveCaptionFontSize : _config.LiveCaptionNdiFontSize;
            set
            {
                double next = value <= 0 ? 0 : value;
                if (Math.Abs(_config.LiveCaptionNdiFontSize - next) > 0.001)
                {
                    _config.LiveCaptionNdiFontSize = next;
                    SaveConfig();
                }
            }
        }

        public double LiveCaptionNdiPadding
        {
            get => _config.LiveCaptionNdiPadding <= 0 ? LiveCaptionPadding : _config.LiveCaptionNdiPadding;
            set
            {
                double next = value <= 0 ? 0 : value;
                if (Math.Abs(_config.LiveCaptionNdiPadding - next) > 0.001)
                {
                    _config.LiveCaptionNdiPadding = next;
                    SaveConfig();
                }
            }
        }

        public double LiveCaptionNdiLetterSpacing
        {
            get => _config.LiveCaptionNdiLetterSpacing <= 0 ? LiveCaptionLetterSpacing : _config.LiveCaptionNdiLetterSpacing;
            set
            {
                double next = Math.Clamp(value, 0, 10);
                if (Math.Abs(_config.LiveCaptionNdiLetterSpacing - next) > 0.001)
                {
                    _config.LiveCaptionNdiLetterSpacing = next;
                    SaveConfig();
                }
            }
        }

        public double LiveCaptionNdiLineGap
        {
            get => _config.LiveCaptionNdiLineGap <= 0 ? LiveCaptionLineGap : _config.LiveCaptionNdiLineGap;
            set
            {
                double next = Math.Clamp(value, 10, 60);
                if (Math.Abs(_config.LiveCaptionNdiLineGap - next) > 0.001)
                {
                    _config.LiveCaptionNdiLineGap = next;
                    SaveConfig();
                }
            }
        }

        public string LiveCaptionNdiTextAlignment
        {
            get => NormalizeNdiTextAlignment(_config.LiveCaptionNdiTextAlignment);
            set
            {
                string next = NormalizeNdiTextAlignment(value);
                if (!string.Equals(_config.LiveCaptionNdiTextAlignment, next, StringComparison.Ordinal))
                {
                    _config.LiveCaptionNdiTextAlignment = next;
                    SaveConfig();
                }
            }
        }

        private static string NormalizeNdiTextAlignment(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "center";
            }

            return value.Trim().ToLowerInvariant() switch
            {
                "left" => "left",
                "right" => "right",
                _ => "center"
            };
        }
    }

    public partial class AppConfig
    {
        public string LiveCaptionSpeechMode { get; set; } = "realtime";
        public bool LiveCaptionRealtimeEnabled { get; set; } = false;
        /// <summary>
        /// 经文识别开关：不持久化，每次启动默认 false，用户手动勾选后本次会话有效。
        /// </summary>
        [JsonIgnore]
        public bool LiveCaptionShortPhraseEnabled { get; set; } = false;
        /// <summary>
        /// 经文识别文本来源：shortPhrase=短语ASR, realtime=实时语音ASR, both=双路
        /// </summary>
        public string LiveCaptionVerseSource { get; set; } = "shortPhrase";
        public string LiveCaptionAsrProvider { get; set; } = "baidu";
        public string LiveCaptionRealtimeAsrProvider { get; set; } = "baidu";
        public string LiveCaptionRealtimeProxyBaseUrl { get; set; } = "http://localhost:8317/v1";
        public string LiveCaptionRealtimeAsrModel { get; set; } = "baidu-realtime";
        public int LiveCaptionRealtimeBaiduDevPid { get; set; } = 1537;
        public string LiveCaptionShortAsrProvider { get; set; } = "baidu";
        public string LiveCaptionShortProxyBaseUrl { get; set; } = "http://vop.baidu.com/server_api";
        public string LiveCaptionShortAsrModel { get; set; } = "baidu-short-standard";
        public int LiveCaptionShortBaiduDevPid { get; set; } = 1537;
        public string LiveCaptionAudioInputMode { get; set; } = "system";
        public string LiveCaptionInputDeviceId { get; set; } = "";
        public string LiveCaptionSystemDeviceId { get; set; } = "";
        public string LiveCaptionProxyBaseUrl { get; set; } = "http://localhost:8317/v1";
        public bool LiveCaptionFunAsrAllowInsecureTls { get; set; } = true;
        public bool LiveCaptionNdiEnabled { get; set; } = false;
        public string LiveCaptionApiKey { get; set; } = "";
        public string LiveCaptionAsrModel { get; set; } = "gpt-4o-mini-transcribe";
        public string LiveCaptionBaiduAppId { get; set; } = "";
        public string LiveCaptionBaiduApiKey { get; set; } = "";
        public string LiveCaptionBaiduSecretKey { get; set; } = "";
        public int LiveCaptionBaiduDevPid { get; set; } = 1537;
        public string LiveCaptionTencentAppId { get; set; } = "";
        public string LiveCaptionTencentSecretId { get; set; } = "";
        public string LiveCaptionTencentSecretKey { get; set; } = "";
        public string LiveCaptionAliAppKey { get; set; } = "";
        public string LiveCaptionAliAccessKeyId { get; set; } = "";
        public string LiveCaptionAliAccessKeySecret { get; set; } = "";
        public string LiveCaptionDoubaoAppKey { get; set; } = "";
        public string LiveCaptionDoubaoAccessKey { get; set; } = "";
        public string LiveCaptionDoubaoResourceId { get; set; } = "volc.seedasr.sauc.duration";
        public bool LiveCaptionReserveWorkArea { get; set; } = false;
        public double LiveCaptionFloatingLeft { get; set; } = 0;
        public double LiveCaptionFloatingTop { get; set; } = 0;
        public double LiveCaptionFloatingWidth { get; set; } = 0;
        public double LiveCaptionFloatingHeight { get; set; } = 0;
        public string LiveCaptionProjectionOrientation { get; set; } = "horizontal";
        public string LiveCaptionProjectionHorizontalAnchor { get; set; } = "center";
        public string LiveCaptionProjectionVerticalAnchor { get; set; } = "top";
        public string LiveCaptionLatestTextColor { get; set; } = "#FFFF00";
        public string LiveCaptionTextColor { get; set; } = "#FFFFFF";
        public string LiveCaptionFontFamily { get; set; } = "";
        public double LiveCaptionFontSize { get; set; } = 0;
        public double LiveCaptionPadding { get; set; } = 0;
        public double LiveCaptionLetterSpacing { get; set; } = 0;
        public double LiveCaptionLineGap { get; set; } = 30;
        public string LiveCaptionTextAlignment { get; set; } = "left";
        public string LiveCaptionLocalLatestTextColor { get; set; } = "";
        public string LiveCaptionLocalTextColor { get; set; } = "";
        public string LiveCaptionLocalFontFamily { get; set; } = "";
        public double LiveCaptionLocalFontSize { get; set; } = 0;
        public double LiveCaptionLocalPadding { get; set; } = 0;
        public double LiveCaptionLocalLetterSpacing { get; set; } = 0;
        public double LiveCaptionLocalLineGap { get; set; } = 0;
        public int LiveCaptionNdiLineCharLimit { get; set; } = 30;
        public string LiveCaptionNdiTextColor { get; set; } = "";
        public string LiveCaptionNdiLatestTextColor { get; set; } = "";
        public string LiveCaptionNdiFontFamily { get; set; } = "";
        public double LiveCaptionNdiFontSize { get; set; } = 0;
        public double LiveCaptionNdiPadding { get; set; } = 0;
        public double LiveCaptionNdiLetterSpacing { get; set; } = 0;
        public double LiveCaptionNdiLineGap { get; set; } = 0;
        public string LiveCaptionNdiTextAlignment { get; set; } = "center";
    }
}
