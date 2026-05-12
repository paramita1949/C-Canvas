using System;

namespace ImageColorChanger.Core
{
    public partial class ConfigManager
    {
        public bool AiSermonEnabled
        {
            get => _config.AiSermonEnabled;
            set
            {
                if (_config.AiSermonEnabled != value)
                {
                    _config.AiSermonEnabled = value;
                    SaveConfig();
                }
            }
        }

        public string DeepSeekApiKey
        {
            get => _config.DeepSeekApiKey ?? string.Empty;
            set
            {
                string next = value ?? string.Empty;
                if (!string.Equals(_config.DeepSeekApiKey, next, StringComparison.Ordinal))
                {
                    _config.DeepSeekApiKey = next;
                    SaveConfig();
                }
            }
        }

        public string DeepSeekBaseUrl
        {
            get => string.IsNullOrWhiteSpace(_config.DeepSeekBaseUrl)
                ? "https://api.deepseek.com"
                : _config.DeepSeekBaseUrl.Trim().TrimEnd('/');
            set
            {
                string next = string.IsNullOrWhiteSpace(value)
                    ? "https://api.deepseek.com"
                    : value.Trim().TrimEnd('/');
                if (!string.Equals(_config.DeepSeekBaseUrl, next, StringComparison.Ordinal))
                {
                    _config.DeepSeekBaseUrl = next;
                    SaveConfig();
                }
            }
        }

        public string DeepSeekModel
        {
            get => string.IsNullOrWhiteSpace(_config.DeepSeekModel) ? "deepseek-v4-flash" : _config.DeepSeekModel.Trim();
            set
            {
                string next = string.IsNullOrWhiteSpace(value) ? "deepseek-v4-flash" : value.Trim();
                if (!string.Equals(_config.DeepSeekModel, next, StringComparison.Ordinal))
                {
                    _config.DeepSeekModel = next;
                    SaveConfig();
                }
            }
        }

        public bool AiSermonAutoWriteHistory
        {
            get => _config.AiSermonAutoWriteHistory;
            set
            {
                if (_config.AiSermonAutoWriteHistory != value)
                {
                    _config.AiSermonAutoWriteHistory = value;
                    SaveConfig();
                }
            }
        }

        public double AiSermonMinWriteConfidence
        {
            get => _config.AiSermonMinWriteConfidence <= 0 ? 0.55 : Math.Clamp(_config.AiSermonMinWriteConfidence, 0.0, 1.0);
            set
            {
                double next = Math.Clamp(value, 0.0, 1.0);
                if (Math.Abs(_config.AiSermonMinWriteConfidence - next) > 0.0001)
                {
                    _config.AiSermonMinWriteConfidence = next;
                    SaveConfig();
                }
            }
        }

        public int AiSermonPanelOpacity
        {
            get => Math.Clamp(_config.AiSermonPanelOpacity, 35, 100);
            set
            {
                int next = Math.Clamp(value, 35, 100);
                if (_config.AiSermonPanelOpacity != next)
                {
                    _config.AiSermonPanelOpacity = next;
                    SaveConfig();
                }
            }
        }
    }

    public partial class AppConfig
    {
        public bool AiSermonEnabled { get; set; } = true;
        public string DeepSeekApiKey { get; set; } = "";
        public string DeepSeekBaseUrl { get; set; } = "https://api.deepseek.com";
        public string DeepSeekModel { get; set; } = "deepseek-v4-flash";
        public bool AiSermonAutoWriteHistory { get; set; } = true;
        public double AiSermonMinWriteConfidence { get; set; } = 0.55;
        public int AiSermonPanelOpacity { get; set; } = 80;
    }
}
