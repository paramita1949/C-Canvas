using System;
using System.Collections.Generic;
using System.Linq;
using ImageColorChanger.Services.Ai;

namespace ImageColorChanger.Core
{
    public sealed class AiConnectionProfile
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string ProviderId { get; set; } = "deepseek";
        public string Protocol { get; set; } = AiProviderProtocol.OpenAiCompletions;
        public string BaseUrl { get; set; } = string.Empty;
        public string ChatCompletionsEndpoint { get; set; } = string.Empty;
        public string ResponsesEndpoint { get; set; } = string.Empty;
        public string ApiKey { get; set; } = string.Empty;
        public string ModelId { get; set; } = string.Empty;
        public string ModelDisplayName { get; set; } = string.Empty;
        public List<AiModelOption> AvailableModels { get; set; } = new List<AiModelOption>();
        public string LastTestStatus { get; set; } = "未测试";
        public string LastTestMessage { get; set; } = string.Empty;

        public AiConnectionProfile Clone()
        {
            return new AiConnectionProfile
            {
                Id = Id,
                Name = Name,
                ProviderId = ProviderId,
                Protocol = Protocol,
                BaseUrl = BaseUrl,
                ChatCompletionsEndpoint = ChatCompletionsEndpoint,
                ResponsesEndpoint = ResponsesEndpoint,
                ApiKey = ApiKey,
                ModelId = ModelId,
                ModelDisplayName = ModelDisplayName,
                AvailableModels = (AvailableModels ?? new List<AiModelOption>())
                    .Where(model => model != null && !string.IsNullOrWhiteSpace(model.Id))
                    .GroupBy(model => model.Id, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.First())
                    .Select(model => new AiModelOption
                    {
                        Id = model.Id,
                        DisplayName = model.DisplayName,
                        ContextWindow = model.ContextWindow
                    })
                    .ToList(),
                LastTestStatus = LastTestStatus,
                LastTestMessage = LastTestMessage
            };
        }
    }

    public partial class ConfigManager
    {
        public string ActiveAiProfileId
        {
            get
            {
                EnsureAiProfiles();
                return _config.ActiveAiProfileId;
            }
            set
            {
                SetActiveAiProfile(value);
            }
        }

        public IReadOnlyList<AiConnectionProfile> GetAiProfiles()
        {
            EnsureAiProfiles();
            return _config.AiProfiles.Select(profile => profile.Clone()).ToArray();
        }

        public AiConnectionProfile GetActiveAiProfile()
        {
            EnsureAiProfiles();
            return _config.AiProfiles
                .First(profile => string.Equals(profile.Id, _config.ActiveAiProfileId, StringComparison.Ordinal))
                .Clone();
        }

        public AiConnectionProfile CreateAiProfile(string name)
        {
            return new AiConnectionProfile
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = string.IsNullOrWhiteSpace(name) ? "新建配置" : name.Trim(),
                ProviderId = "custom",
                Protocol = AiProviderProtocol.OpenAiCompletions,
                BaseUrl = string.Empty,
                ChatCompletionsEndpoint = string.Empty,
                ResponsesEndpoint = string.Empty,
                ApiKey = string.Empty,
                ModelId = string.Empty,
                LastTestStatus = "未测试"
            };
        }

        public void SaveAiProfile(AiConnectionProfile profile)
        {
            if (profile == null || string.IsNullOrWhiteSpace(profile.Id))
            {
                throw new ArgumentException("AI 配置不能为空。", nameof(profile));
            }

            EnsureAiProfiles();
            AiConnectionProfile next = profile.Clone();
            next.ProviderId = AiProviderCatalog.NormalizeProviderId(next.ProviderId);
            next.Protocol = AiProviderCatalog.NormalizeProtocol(next.ProviderId, next.Protocol, next.ModelId);
            NormalizeProfileEndpoints(next);
            int index = _config.AiProfiles.FindIndex(item => string.Equals(item.Id, next.Id, StringComparison.Ordinal));
            if (index >= 0)
            {
                _config.AiProfiles[index] = next;
            }
            else
            {
                _config.AiProfiles.Add(next);
            }

            if (string.IsNullOrWhiteSpace(_config.ActiveAiProfileId))
            {
                _config.ActiveAiProfileId = next.Id;
            }

            if (string.Equals(_config.ActiveAiProfileId, next.Id, StringComparison.Ordinal))
            {
                ApplyActiveProfileToLegacyFields(next);
            }

            SaveConfig();
        }

        public bool SetActiveAiProfile(string profileId)
        {
            EnsureAiProfiles();
            AiConnectionProfile profile = _config.AiProfiles.FirstOrDefault(item =>
                string.Equals(item.Id, profileId, StringComparison.Ordinal));
            if (profile == null)
            {
                return false;
            }

            _config.ActiveAiProfileId = profile.Id;
            ApplyActiveProfileToLegacyFields(profile);
            SaveConfig();
            return true;
        }

        public bool DeleteAiProfile(string profileId)
        {
            EnsureAiProfiles();
            if (_config.AiProfiles.Count <= 1)
            {
                return false;
            }

            int index = _config.AiProfiles.FindIndex(item =>
                string.Equals(item.Id, profileId, StringComparison.Ordinal));
            if (index < 0)
            {
                return false;
            }

            bool deletingActive = string.Equals(_config.ActiveAiProfileId, profileId, StringComparison.Ordinal);
            _config.AiProfiles.RemoveAt(index);
            if (deletingActive)
            {
                _config.ActiveAiProfileId = _config.AiProfiles[Math.Min(index, _config.AiProfiles.Count - 1)].Id;
                ApplyActiveProfileToLegacyFields(_config.AiProfiles.First(profile =>
                    string.Equals(profile.Id, _config.ActiveAiProfileId, StringComparison.Ordinal)));
            }

            SaveConfig();
            return true;
        }

        private void EnsureAiProfiles()
        {
            if (_config.AiProfiles == null)
            {
                _config.AiProfiles = new List<AiConnectionProfile>();
            }

            if (_config.AiProfiles.Count == 0)
            {
                AiConnectionProfile legacy = new AiConnectionProfile
                {
                    Id = "deepseek-main",
                    Name = "DeepSeek 主配置",
                    ProviderId = string.IsNullOrWhiteSpace(_config.AiSermonProviderId) ? "deepseek" : _config.AiSermonProviderId,
                    Protocol = AiProviderCatalog.NormalizeProtocol(
                        string.IsNullOrWhiteSpace(_config.AiSermonProviderId) ? "deepseek" : _config.AiSermonProviderId,
                        _config.AiSermonProtocol,
                        AiSermonModel),
                    BaseUrl = string.IsNullOrWhiteSpace(_config.AiSermonBaseUrl) ? _config.DeepSeekBaseUrl : _config.AiSermonBaseUrl,
                    ChatCompletionsEndpoint = _config.AiSermonChatCompletionsEndpoint,
                    ResponsesEndpoint = _config.AiSermonResponsesEndpoint,
                    ApiKey = string.IsNullOrWhiteSpace(_config.AiSermonApiKey) ? _config.DeepSeekApiKey : _config.AiSermonApiKey,
                    ModelId = AiSermonModel,
                    LastTestStatus = "未测试"
                };
                _config.AiProfiles.Add(legacy);
            }

            foreach (AiConnectionProfile profile in _config.AiProfiles)
            {
                profile.ProviderId = AiProviderCatalog.NormalizeProviderId(profile.ProviderId);
                profile.Protocol = AiProviderCatalog.NormalizeProtocol(profile.ProviderId, profile.Protocol, profile.ModelId);
                NormalizeProfileEndpoints(profile);
                profile.AvailableModels ??= new List<AiModelOption>();
            }

            if (string.IsNullOrWhiteSpace(_config.ActiveAiProfileId) ||
                !_config.AiProfiles.Any(profile => string.Equals(profile.Id, _config.ActiveAiProfileId, StringComparison.Ordinal)))
            {
                _config.ActiveAiProfileId = _config.AiProfiles[0].Id;
            }
        }

        private void ApplyActiveProfileToLegacyFields(AiConnectionProfile profile)
        {
            _config.AiSermonProviderId = AiProviderCatalog.NormalizeProviderId(profile.ProviderId);
            _config.AiSermonProtocol = AiProviderCatalog.NormalizeProtocol(
                _config.AiSermonProviderId,
                profile.Protocol,
                profile.ModelId);
            _config.AiSermonApiKey = profile.ApiKey ?? string.Empty;
            _config.AiSermonBaseUrl = profile.BaseUrl ?? string.Empty;
            _config.AiSermonChatCompletionsEndpoint = profile.ChatCompletionsEndpoint ?? string.Empty;
            _config.AiSermonResponsesEndpoint = profile.ResponsesEndpoint ?? string.Empty;
            _config.AiSermonModel = profile.ModelId ?? string.Empty;

            if (string.Equals(profile.ProviderId, "deepseek", StringComparison.OrdinalIgnoreCase))
            {
                _config.DeepSeekApiKey = _config.AiSermonApiKey;
                _config.DeepSeekBaseUrl = _config.AiSermonBaseUrl;
                _config.DeepSeekModel = _config.AiSermonModel;
            }
        }

        private static void NormalizeProfileEndpoints(AiConnectionProfile profile)
        {
            if (string.IsNullOrWhiteSpace(profile.ChatCompletionsEndpoint))
            {
                AiProviderPreset preset = AiProviderCatalog.Find(profile.ProviderId);
                bool usesPresetBaseUrl = preset != null
                    && (string.IsNullOrWhiteSpace(profile.BaseUrl)
                        || string.Equals(profile.BaseUrl.Trim().TrimEnd('/'), preset.BaseUrl.Trim().TrimEnd('/'), StringComparison.OrdinalIgnoreCase));
                profile.ChatCompletionsEndpoint = usesPresetBaseUrl && !string.IsNullOrWhiteSpace(preset.ChatCompletionsEndpoint)
                    ? preset.ChatCompletionsEndpoint
                    : string.Empty;
            }

            if (string.IsNullOrWhiteSpace(profile.ResponsesEndpoint))
            {
                AiProviderPreset preset = AiProviderCatalog.Find(profile.ProviderId);
                bool usesPresetBaseUrl = preset != null
                    && (string.IsNullOrWhiteSpace(profile.BaseUrl)
                        || string.Equals(profile.BaseUrl.Trim().TrimEnd('/'), preset.BaseUrl.Trim().TrimEnd('/'), StringComparison.OrdinalIgnoreCase));
                profile.ResponsesEndpoint = usesPresetBaseUrl && !string.IsNullOrWhiteSpace(preset.ResponsesEndpoint)
                    ? preset.ResponsesEndpoint
                    : string.Empty;
            }
        }
    }

    public partial class AppConfig
    {
        public string ActiveAiProfileId { get; set; } = string.Empty;
        public List<AiConnectionProfile> AiProfiles { get; set; } = new List<AiConnectionProfile>();
    }
}
