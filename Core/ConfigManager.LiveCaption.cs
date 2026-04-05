using System;

namespace ImageColorChanger.Core
{
    public partial class ConfigManager
    {
        public string LiveCaptionAsrProvider
        {
            get => string.IsNullOrWhiteSpace(_config.LiveCaptionAsrProvider) ? "baidu" : _config.LiveCaptionAsrProvider;
            set
            {
                var next = string.IsNullOrWhiteSpace(value) ? "baidu" : value.Trim();
                if (!string.Equals(_config.LiveCaptionAsrProvider, next, StringComparison.OrdinalIgnoreCase))
                {
                    _config.LiveCaptionAsrProvider = next;
                    SaveConfig();
                }
            }
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
    }

    public partial class AppConfig
    {
        public string LiveCaptionAsrProvider { get; set; } = "baidu";
        public string LiveCaptionProxyBaseUrl { get; set; } = "http://localhost:8317/v1";
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
    }
}
