using System;
using System.Text.Json;
using ImageColorChanger.Services.Ai;

namespace ImageColorChanger.Core
{
    public sealed class AiSpeakerDialectBindingEntry
    {
        public string Speaker { get; set; } = string.Empty;
        public string[] Tags { get; set; } = Array.Empty<string>();
    }

    public partial class ConfigManager
    {
        private bool MigrateLegacyAiProviderConfig(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            try
            {
                using JsonDocument document = JsonDocument.Parse(json);
                JsonElement root = document.RootElement;
                bool hasProvider = root.TryGetProperty("AiSermonProviderId", out _);
                bool hasProtocol = root.TryGetProperty("AiSermonProtocol", out _);
                bool hasApiKey = root.TryGetProperty("AiSermonApiKey", out _);
                bool hasBaseUrl = root.TryGetProperty("AiSermonBaseUrl", out _);
                bool hasChatCompletionsEndpoint = root.TryGetProperty("AiSermonChatCompletionsEndpoint", out _);
                bool hasResponsesEndpoint = root.TryGetProperty("AiSermonResponsesEndpoint", out _);
                bool hasModel = root.TryGetProperty("AiSermonModel", out _);
                bool changed = false;

                if (!hasProvider)
                {
                    _config.AiSermonProviderId = IsGeminiModel(_config.DeepSeekModel) ? "gemini" : "deepseek";
                    changed = true;
                }
                if (!hasProtocol)
                {
                    _config.AiSermonProtocol = AiProviderProtocol.OpenAiCompletions;
                    changed = true;
                }
                if (!hasApiKey)
                {
                    _config.AiSermonApiKey = string.Equals(_config.AiSermonProviderId, "gemini", StringComparison.OrdinalIgnoreCase)
                        ? (_config.GeminiApiKey ?? string.Empty)
                        : (_config.DeepSeekApiKey ?? string.Empty);
                    changed = true;
                }
                if (!hasBaseUrl)
                {
                    _config.AiSermonBaseUrl = _config.DeepSeekBaseUrl ?? string.Empty;
                    changed = true;
                }
                string providerId = AiProviderCatalog.NormalizeProviderId(_config.AiSermonProviderId);
                AiProviderPreset preset = AiProviderCatalog.Find(providerId);
                if (!hasChatCompletionsEndpoint && preset != null
                    && string.Equals((_config.AiSermonBaseUrl ?? string.Empty).Trim().TrimEnd('/'),
                        preset.BaseUrl.Trim().TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
                {
                    _config.AiSermonChatCompletionsEndpoint = preset.ChatCompletionsEndpoint ?? string.Empty;
                    changed = true;
                }
                if (!hasResponsesEndpoint && preset != null
                    && string.Equals((_config.AiSermonBaseUrl ?? string.Empty).Trim().TrimEnd('/'),
                        preset.BaseUrl.Trim().TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
                {
                    _config.AiSermonResponsesEndpoint = preset.ResponsesEndpoint ?? string.Empty;
                    changed = true;
                }
                if (!hasModel)
                {
                    _config.AiSermonModel = NormalizeDeepSeekModel(_config.DeepSeekModel ?? string.Empty);
                    changed = true;
                }

                return changed;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        public string AiSermonProviderId
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(_config.AiSermonProviderId))
                {
                    return _config.AiSermonProviderId.Trim().ToLowerInvariant();
                }

                return IsGeminiModel(_config.DeepSeekModel) ? "gemini" : "deepseek";
            }
            set
            {
                string next = string.IsNullOrWhiteSpace(value) ? "custom" : value.Trim().ToLowerInvariant();
                if (!string.Equals(_config.AiSermonProviderId, next, StringComparison.Ordinal))
                {
                    _config.AiSermonProviderId = next;
                    SaveConfig();
                }
            }
        }

        public string AiSermonProtocol
        {
            get => AiProviderCatalog.NormalizeProtocol(AiSermonProviderId, _config.AiSermonProtocol, AiSermonModel);
            set
            {
                string next = AiProviderCatalog.NormalizeProtocol(AiSermonProviderId, value, AiSermonModel);
                if (!string.Equals(_config.AiSermonProtocol, next, StringComparison.Ordinal))
                {
                    _config.AiSermonProtocol = next;
                    SaveConfig();
                }
            }
        }

        public string AiSermonApiKey
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(_config.AiSermonApiKey))
                {
                    return _config.AiSermonApiKey.Trim();
                }

                return string.Equals(AiSermonProviderId, "gemini", StringComparison.OrdinalIgnoreCase)
                    ? (_config.GeminiApiKey ?? string.Empty).Trim()
                    : (_config.DeepSeekApiKey ?? string.Empty).Trim();
            }
            set
            {
                string next = value ?? string.Empty;
                if (!string.Equals(_config.AiSermonApiKey, next, StringComparison.Ordinal))
                {
                    _config.AiSermonApiKey = next;
                    if (string.Equals(AiSermonProviderId, "gemini", StringComparison.OrdinalIgnoreCase))
                    {
                        _config.GeminiApiKey = next;
                    }
                    else
                    {
                        _config.DeepSeekApiKey = next;
                    }
                    SaveConfig();
                }
            }
        }

        public string AiSermonBaseUrl
        {
            get
            {
                string value = string.IsNullOrWhiteSpace(_config.AiSermonBaseUrl)
                    ? _config.DeepSeekBaseUrl
                    : _config.AiSermonBaseUrl;
                return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().TrimEnd('/');
            }
            set
            {
                string next = (value ?? string.Empty).Trim().TrimEnd('/');
                if (!string.Equals(_config.AiSermonBaseUrl, next, StringComparison.Ordinal))
                {
                    _config.AiSermonBaseUrl = next;
                    if (string.Equals(AiSermonProviderId, "deepseek", StringComparison.OrdinalIgnoreCase))
                    {
                        _config.DeepSeekBaseUrl = next;
                    }
                    SaveConfig();
                }
            }
        }

        public string AiSermonChatCompletionsEndpoint
        {
            get => (_config.AiSermonChatCompletionsEndpoint ?? string.Empty).Trim().TrimEnd('/');
            set => _config.AiSermonChatCompletionsEndpoint = (value ?? string.Empty).Trim().TrimEnd('/');
        }

        public string AiSermonResponsesEndpoint
        {
            get => (_config.AiSermonResponsesEndpoint ?? string.Empty).Trim().TrimEnd('/');
            set => _config.AiSermonResponsesEndpoint = (value ?? string.Empty).Trim().TrimEnd('/');
        }

        public string AiSermonModel
        {
            get
            {
                string value = string.IsNullOrWhiteSpace(_config.AiSermonModel)
                    ? _config.DeepSeekModel
                    : _config.AiSermonModel;
                string model = string.IsNullOrWhiteSpace(value) ? "deepseek-flash" : value.Trim();
                return string.Equals(AiSermonProviderId, "deepseek", StringComparison.OrdinalIgnoreCase)
                    ? NormalizeDeepSeekModel(model)
                    : model;
            }
            set
            {
                string next = string.IsNullOrWhiteSpace(value) ? "deepseek-flash" : value.Trim();
                if (string.Equals(AiSermonProviderId, "deepseek", StringComparison.OrdinalIgnoreCase))
                {
                    next = NormalizeDeepSeekModel(next);
                }
                if (!string.Equals(_config.AiSermonModel, next, StringComparison.Ordinal))
                {
                    _config.AiSermonModel = next;
                    _config.DeepSeekModel = next;
                    SaveConfig();
                }
            }
        }

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

        public string GeminiApiKey
        {
            get => _config.GeminiApiKey ?? string.Empty;
            set
            {
                string next = value ?? string.Empty;
                if (!string.Equals(_config.GeminiApiKey, next, StringComparison.Ordinal))
                {
                    _config.GeminiApiKey = next;
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
            get => NormalizeDeepSeekModel(_config.DeepSeekModel);
            set
            {
                string next = NormalizeDeepSeekModel(value);
                if (!string.Equals(_config.DeepSeekModel, next, StringComparison.Ordinal))
                {
                    _config.DeepSeekModel = next;
                    SaveConfig();
                }
            }
        }

        private static bool IsGeminiModel(string model)
        {
            return !string.IsNullOrWhiteSpace(model)
                && model.Trim().StartsWith("gemini-", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeDeepSeekModel(string model)
        {
            return string.Equals(model?.Trim(), "deepseek-v4-flash", StringComparison.OrdinalIgnoreCase)
                ? "deepseek-flash"
                : (string.IsNullOrWhiteSpace(model) ? "deepseek-flash" : model.Trim());
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

        public bool AiSermonDialectSchemeEnabled
        {
            get => _config.AiSermonDialectSchemeEnabled;
            set
            {
                if (_config.AiSermonDialectSchemeEnabled != value)
                {
                    _config.AiSermonDialectSchemeEnabled = value;
                    SaveConfig();
                }
            }
        }

        public string[] AiSermonDialectTags
        {
            get
            {
                var raw = _config.AiSermonDialectTags ?? Array.Empty<string>();
                return raw
                    .Select(tag => (tag ?? string.Empty).Trim())
                    .Where(tag => !string.IsNullOrWhiteSpace(tag))
                    .Distinct(StringComparer.Ordinal)
                    .Take(20)
                    .ToArray();
            }
            set
            {
                var next = (value ?? Array.Empty<string>())
                    .Select(tag => (tag ?? string.Empty).Trim())
                    .Where(tag => !string.IsNullOrWhiteSpace(tag))
                    .Distinct(StringComparer.Ordinal)
                    .Take(20)
                    .ToArray();
                _config.AiSermonDialectTags = next;
                SaveConfig();
            }
        }

        public string[] AiSermonSelectedDialectTags
        {
            get
            {
                var raw = _config.AiSermonSelectedDialectTags ?? Array.Empty<string>();
                return raw
                    .Select(tag => (tag ?? string.Empty).Trim())
                    .Where(tag => !string.IsNullOrWhiteSpace(tag))
                    .Distinct(StringComparer.Ordinal)
                    .Take(20)
                    .ToArray();
            }
            set
            {
                var next = (value ?? Array.Empty<string>())
                    .Select(tag => (tag ?? string.Empty).Trim())
                    .Where(tag => !string.IsNullOrWhiteSpace(tag))
                    .Distinct(StringComparer.Ordinal)
                    .Take(20)
                    .ToArray();
                _config.AiSermonSelectedDialectTags = next;
                SaveConfig();
            }
        }

        public AiSpeakerDialectBindingEntry[] AiSermonSpeakerDialectBindings
        {
            get
            {
                var raw = _config.AiSermonSpeakerDialectBindings ?? Array.Empty<AiSpeakerDialectBindingEntry>();
                var result = new List<AiSpeakerDialectBindingEntry>(raw.Length);
                foreach (var entry in raw)
                {
                    if (entry == null)
                    {
                        continue;
                    }

                    string speaker = (entry.Speaker ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(speaker))
                    {
                        continue;
                    }

                    string[] tags = (entry.Tags ?? Array.Empty<string>())
                        .Select(tag => (tag ?? string.Empty).Trim())
                        .Where(tag => !string.IsNullOrWhiteSpace(tag))
                        .Distinct(StringComparer.Ordinal)
                        .Take(20)
                        .ToArray();
                    result.Add(new AiSpeakerDialectBindingEntry
                    {
                        Speaker = speaker,
                        Tags = tags
                    });
                }

                return result.ToArray();
            }
            set
            {
                var incoming = value ?? Array.Empty<AiSpeakerDialectBindingEntry>();
                var result = new List<AiSpeakerDialectBindingEntry>(incoming.Length);
                foreach (var entry in incoming)
                {
                    if (entry == null)
                    {
                        continue;
                    }

                    string speaker = (entry.Speaker ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(speaker))
                    {
                        continue;
                    }

                    string[] tags = (entry.Tags ?? Array.Empty<string>())
                        .Select(tag => (tag ?? string.Empty).Trim())
                        .Where(tag => !string.IsNullOrWhiteSpace(tag))
                        .Distinct(StringComparer.Ordinal)
                        .Take(20)
                        .ToArray();
                    result.Add(new AiSpeakerDialectBindingEntry
                    {
                        Speaker = speaker,
                        Tags = tags
                    });
                }

                _config.AiSermonSpeakerDialectBindings = result.ToArray();
                SaveConfig();
            }
        }
    }

    public partial class AppConfig
    {
        public bool AiSermonEnabled { get; set; } = true;
        public string AiSermonProviderId { get; set; } = "deepseek";
        public string AiSermonProtocol { get; set; } = AiProviderProtocol.OpenAiCompletions;
        public string AiSermonApiKey { get; set; } = "";
        public string AiSermonBaseUrl { get; set; } = "https://api.deepseek.com";
        public string AiSermonChatCompletionsEndpoint { get; set; } = "https://api.deepseek.com/chat/completions";
        public string AiSermonResponsesEndpoint { get; set; } = "";
        public string AiSermonModel { get; set; } = "deepseek-flash";
        public string DeepSeekApiKey { get; set; } = "";
        public string GeminiApiKey { get; set; } = "";
        public string DeepSeekBaseUrl { get; set; } = "https://api.deepseek.com";
        public string DeepSeekModel { get; set; } = "deepseek-flash";
        public bool AiSermonAutoWriteHistory { get; set; } = true;
        public double AiSermonMinWriteConfidence { get; set; } = 0.55;
        public int AiSermonPanelOpacity { get; set; } = 80;
        public bool AiSermonDialectSchemeEnabled { get; set; } = false;
        public string[] AiSermonDialectTags { get; set; } = new[] { "国语", "吴语", "宁波话", "绍兴话" };
        public string[] AiSermonSelectedDialectTags { get; set; } = new[] { "国语" };
        public AiSpeakerDialectBindingEntry[] AiSermonSpeakerDialectBindings { get; set; } = Array.Empty<AiSpeakerDialectBindingEntry>();
    }
}
