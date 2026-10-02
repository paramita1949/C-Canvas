using System;
using System.Collections.Generic;
using System.Linq;

namespace ImageColorChanger.Services.Ai
{
    public static class AiProviderProtocol
    {
        public const string OpenAiCompletions = "openai-completions";
        public const string OpenAiResponses = "openai-responses";

        public static bool IsSupported(string protocol)
        {
            return string.Equals(protocol, OpenAiCompletions, StringComparison.OrdinalIgnoreCase)
                || string.Equals(protocol, OpenAiResponses, StringComparison.OrdinalIgnoreCase);
        }
    }

    public sealed class AiProviderPreset
    {
        public string Id { get; init; } = string.Empty;
        public string DisplayName { get; init; } = string.Empty;
        public string Group { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public string BaseUrl { get; init; } = string.Empty;
        public string ChatCompletionsEndpoint { get; init; } = string.Empty;
        public string ResponsesEndpoint { get; init; } = string.Empty;
        public string Protocol { get; init; } = AiProviderProtocol.OpenAiCompletions;
        public IReadOnlyList<string> SupportedProtocols { get; init; } = new[] { AiProviderProtocol.OpenAiCompletions };
        public IReadOnlyList<string> RecommendedModels { get; init; } = Array.Empty<string>();
        public IReadOnlyList<AiModelPreset> RecommendedModelPresets { get; init; } = Array.Empty<AiModelPreset>();

        public bool SupportsProtocol(string protocol)
        {
            return SupportedProtocols.Any(candidate =>
                string.Equals(candidate, protocol, StringComparison.OrdinalIgnoreCase));
        }

        public string GetEndpoint(string protocol)
        {
            return string.Equals(protocol, AiProviderProtocol.OpenAiResponses, StringComparison.OrdinalIgnoreCase)
                ? ResponsesEndpoint
                : ChatCompletionsEndpoint;
        }

        public string GetBaseUrl(string protocol)
        {
            string endpoint = (GetEndpoint(protocol) ?? string.Empty).Trim().TrimEnd('/');
            if (string.IsNullOrWhiteSpace(endpoint))
            {
                return (BaseUrl ?? string.Empty).Trim().TrimEnd('/');
            }

            string[] endpointSuffixes = { "/chat/completions", "/responses" };
            foreach (string suffix in endpointSuffixes)
            {
                if (endpoint.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    return endpoint[..^suffix.Length].TrimEnd('/');
                }
            }

            return (BaseUrl ?? string.Empty).Trim().TrimEnd('/');
        }

        public AiModelPreset FindModel(string modelId)
        {
            return RecommendedModelPresets.FirstOrDefault(model =>
                string.Equals(model.Id, modelId, StringComparison.OrdinalIgnoreCase));
        }

        public bool SupportsModelProtocol(string modelId, string protocol)
        {
            AiModelPreset model = FindModel(modelId);
            return model != null
                ? model.SupportsProtocol(protocol)
                : SupportsProtocol(protocol);
        }
    }

    public sealed class AiModelPreset
    {
        public string Id { get; init; } = string.Empty;
        public string DisplayName { get; init; } = string.Empty;
        public IReadOnlyList<string> SupportedProtocols { get; init; } = new[] { AiProviderProtocol.OpenAiCompletions };

        public bool SupportsProtocol(string protocol)
        {
            return SupportedProtocols.Any(candidate =>
                string.Equals(candidate, protocol, StringComparison.OrdinalIgnoreCase));
        }
    }

    public static class AiProviderCatalog
    {
        private static readonly IReadOnlyList<AiProviderPreset> Presets = new[]
        {
            new AiProviderPreset
            {
                Id = "deepseek", DisplayName = "DeepSeek", Group = "官方厂商",
                Description = "DeepSeek 官方 OpenAI 兼容接口", BaseUrl = "https://api.deepseek.com",
                ChatCompletionsEndpoint = "https://api.deepseek.com/chat/completions",
                ResponsesEndpoint = "https://api.deepseek.com/responses",
                Protocol = AiProviderProtocol.OpenAiCompletions,
                SupportedProtocols = new[] { AiProviderProtocol.OpenAiCompletions, AiProviderProtocol.OpenAiResponses },
                RecommendedModels = new[] { "deepseek-flash", "deepseek-v4-pro" },
                RecommendedModelPresets = new[]
                {
                    Model("deepseek-flash", "DeepSeek V4.1 Flash", AiProviderProtocol.OpenAiCompletions, AiProviderProtocol.OpenAiResponses),
                    Model("deepseek-v4-pro", "DeepSeek V4 Pro", AiProviderProtocol.OpenAiCompletions, AiProviderProtocol.OpenAiResponses)
                }
            },
            new AiProviderPreset
            {
                Id = "qwen", DisplayName = "通义千问", Group = "官方厂商",
                Description = "阿里云百炼 DashScope OpenAI 兼容接口", BaseUrl = "https://dashscope.aliyuncs.com/compatible-mode/v1",
                ChatCompletionsEndpoint = "https://dashscope.aliyuncs.com/compatible-mode/v1/chat/completions",
                ResponsesEndpoint = "https://dashscope.aliyuncs.com/compatible-mode/v1/responses",
                Protocol = AiProviderProtocol.OpenAiCompletions,
                SupportedProtocols = new[] { AiProviderProtocol.OpenAiCompletions, AiProviderProtocol.OpenAiResponses },
                RecommendedModels = new[] { "qwen3.8-max", "qwen3.8-flash", "qwen3.7-plus" },
                RecommendedModelPresets = new[]
                {
                    Model("qwen3.8-max", "Qwen 3.8 Max", AiProviderProtocol.OpenAiCompletions, AiProviderProtocol.OpenAiResponses),
                    Model("qwen3.8-flash", "Qwen 3.8 Flash", AiProviderProtocol.OpenAiCompletions, AiProviderProtocol.OpenAiResponses),
                    Model("qwen3.7-plus", "Qwen 3.7 Plus", AiProviderProtocol.OpenAiCompletions, AiProviderProtocol.OpenAiResponses)
                }
            },
            new AiProviderPreset
            {
                Id = "zhipu", DisplayName = "智谱 GLM", Group = "官方厂商",
                Description = "智谱 BigModel OpenAI 兼容接口", BaseUrl = "https://open.bigmodel.cn/api/paas/v4",
                ChatCompletionsEndpoint = "https://open.bigmodel.cn/api/paas/v4/chat/completions",
                ResponsesEndpoint = "https://open.bigmodel.cn/api/v1/responses",
                Protocol = AiProviderProtocol.OpenAiCompletions,
                SupportedProtocols = new[] { AiProviderProtocol.OpenAiCompletions, AiProviderProtocol.OpenAiResponses },
                RecommendedModels = new[] { "glm-4.5-air", "glm-4-plus" },
                RecommendedModelPresets = new[]
                {
                    Model("glm-4.5-air", "GLM-4.5 Air", AiProviderProtocol.OpenAiCompletions, AiProviderProtocol.OpenAiResponses),
                    Model("glm-4-plus", "GLM-4 Plus", AiProviderProtocol.OpenAiCompletions, AiProviderProtocol.OpenAiResponses)
                }
            },
            new AiProviderPreset
            {
                Id = "moonshot", DisplayName = "月之暗面 Kimi", Group = "官方厂商",
                Description = "Moonshot AI OpenAI 兼容接口", BaseUrl = "https://api.moonshot.cn/v1",
                ChatCompletionsEndpoint = "https://api.moonshot.cn/v1/chat/completions",
                ResponsesEndpoint = "https://api.moonshot.cn/v1/responses",
                Protocol = AiProviderProtocol.OpenAiCompletions,
                SupportedProtocols = new[] { AiProviderProtocol.OpenAiCompletions, AiProviderProtocol.OpenAiResponses },
                RecommendedModels = new[] { "kimi-k3", "kimi-k2.7-code-highspeed", "kimi-k2.6" },
                RecommendedModelPresets = new[]
                {
                    Model("kimi-k3", "Kimi K3", AiProviderProtocol.OpenAiCompletions, AiProviderProtocol.OpenAiResponses),
                    Model("kimi-k2.7-code-highspeed", "Kimi K2.7 Code Highspeed", AiProviderProtocol.OpenAiCompletions, AiProviderProtocol.OpenAiResponses),
                    Model("kimi-k2.6", "Kimi K2.6", AiProviderProtocol.OpenAiCompletions, AiProviderProtocol.OpenAiResponses)
                }
            },
            new AiProviderPreset
            {
                Id = "minimax", DisplayName = "MiniMax", Group = "官方厂商",
                Description = "MiniMax 文本模型 OpenAI 兼容接口", BaseUrl = "https://api.minimaxi.com/v1",
                ChatCompletionsEndpoint = "https://api.minimaxi.com/v1/chat/completions",
                Protocol = AiProviderProtocol.OpenAiCompletions,
                RecommendedModels = new[] { "MiniMax-M3", "MiniMax-M2.7", "MiniMax-M2.5" }
            },
            new AiProviderPreset
            {
                Id = "baidu", DisplayName = "百度千帆", Group = "官方厂商",
                Description = "百度智能云千帆模型服务接口", BaseUrl = "https://qianfan.baidubce.com/v2",
                ChatCompletionsEndpoint = "https://qianfan.baidubce.com/v2/chat/completions",
                Protocol = AiProviderProtocol.OpenAiCompletions,
                RecommendedModels = new[] { "ernie-4.0-8k", "ernie-speed-128k" }
            },
            new AiProviderPreset
            {
                Id = "doubao", DisplayName = "火山引擎豆包", Group = "官方厂商",
                Description = "火山引擎方舟 OpenAI 兼容接口", BaseUrl = "https://ark.cn-beijing.volces.com/api/v3",
                ChatCompletionsEndpoint = "https://ark.cn-beijing.volces.com/api/v3/chat/completions",
                Protocol = AiProviderProtocol.OpenAiCompletions,
                RecommendedModels = new[] { "doubao-1-5-pro-32k-250115", "doubao-1-5-lite-32k" }
            },
            new AiProviderPreset
            {
                Id = "siliconflow", DisplayName = "硅基流动", Group = "聚合与网关",
                Description = "硅基流动国内模型聚合接口", BaseUrl = "https://api.siliconflow.cn/v1",
                ChatCompletionsEndpoint = "https://api.siliconflow.cn/v1/chat/completions",
                Protocol = AiProviderProtocol.OpenAiCompletions,
                RecommendedModels = new[] { "Qwen/Qwen3-8B", "deepseek-ai/DeepSeek-V3" }
            },
            new AiProviderPreset
            {
                Id = "opencode-zen", DisplayName = "OpenCode Zen（按量付费）", Group = "聚合与网关",
                Description = "OpenCode Zen 按量付费接口，与 Go 订阅分开计费", BaseUrl = "https://opencode.ai/zen/v1",
                ChatCompletionsEndpoint = "https://opencode.ai/zen/v1/chat/completions",
                ResponsesEndpoint = "https://opencode.ai/zen/v1/responses",
                Protocol = AiProviderProtocol.OpenAiCompletions,
                SupportedProtocols = new[] { AiProviderProtocol.OpenAiCompletions, AiProviderProtocol.OpenAiResponses },
                RecommendedModels = new[] { "gpt-5.6-luna", "grok-4.5", "deepseek-v4-pro", "deepseek-v4-flash", "minimax-m3", "minimax-m2.7", "glm-5.2", "glm-5.1" },
                RecommendedModelPresets = new[]
                {
                    Model("gpt-5.6-luna", "GPT 5.6 Luna", AiProviderProtocol.OpenAiResponses),
                    Model("grok-4.5", "Grok 4.5", AiProviderProtocol.OpenAiResponses),
                    Model("deepseek-v4-pro", "DeepSeek V4 Pro", AiProviderProtocol.OpenAiCompletions),
                    Model("deepseek-v4-flash", "DeepSeek V4 Flash", AiProviderProtocol.OpenAiCompletions),
                    Model("minimax-m3", "MiniMax M3", AiProviderProtocol.OpenAiCompletions),
                    Model("minimax-m2.7", "MiniMax M2.7", AiProviderProtocol.OpenAiCompletions),
                    Model("glm-5.2", "GLM 5.2", AiProviderProtocol.OpenAiCompletions),
                    Model("glm-5.1", "GLM 5.1", AiProviderProtocol.OpenAiCompletions)
                }
            },
            new AiProviderPreset
            {
                Id = "opencode-go", DisplayName = "OpenCode Go（编程订阅）", Group = "聚合与网关",
                Description = "OpenCode Go 编程订阅，官方定位为编程代理用途；其他用途需确认服务条款", BaseUrl = "https://opencode.ai/zen/go/v1",
                ChatCompletionsEndpoint = "https://opencode.ai/zen/go/v1/chat/completions",
                ResponsesEndpoint = "https://opencode.ai/zen/go/v1/responses",
                Protocol = AiProviderProtocol.OpenAiCompletions,
                SupportedProtocols = new[] { AiProviderProtocol.OpenAiCompletions, AiProviderProtocol.OpenAiResponses },
                RecommendedModels = new[] { "glm-5.3-flash", "deepseek-v4.1-flash", "deepseek-v4-flash", "kimi-k3", "minimax-m3", "gpt-5.6-luna" },
                RecommendedModelPresets = new[]
                {
                    Model("glm-5.3-flash", "GLM-5.3 Flash", AiProviderProtocol.OpenAiCompletions),
                    Model("deepseek-v4.1-flash", "DeepSeek V4.1 Flash", AiProviderProtocol.OpenAiCompletions),
                    Model("deepseek-v4-flash", "DeepSeek V4 Flash", AiProviderProtocol.OpenAiCompletions),
                    Model("kimi-k3", "Kimi K3", AiProviderProtocol.OpenAiCompletions),
                    Model("minimax-m3", "MiniMax M3", AiProviderProtocol.OpenAiCompletions),
                    Model("gpt-5.6-luna", "GPT 5.6 Luna", AiProviderProtocol.OpenAiResponses)
                }
            },
            new AiProviderPreset
            {
                Id = "compatible", DisplayName = "OpenAI 兼容接口", Group = "本地与自定义",
                Description = "适用于国内代理、企业网关或自建兼容服务", BaseUrl = "",
                ChatCompletionsEndpoint = "",
                ResponsesEndpoint = "",
                Protocol = AiProviderProtocol.OpenAiCompletions,
                SupportedProtocols = new[] { AiProviderProtocol.OpenAiCompletions, AiProviderProtocol.OpenAiResponses },
                RecommendedModels = Array.Empty<string>()
            },
            new AiProviderPreset
            {
                Id = "custom", DisplayName = "自定义", Group = "本地与自定义",
                Description = "手动填写地址、协议和模型", BaseUrl = "",
                ChatCompletionsEndpoint = "",
                ResponsesEndpoint = "",
                Protocol = AiProviderProtocol.OpenAiCompletions,
                SupportedProtocols = new[] { AiProviderProtocol.OpenAiCompletions, AiProviderProtocol.OpenAiResponses },
                RecommendedModels = Array.Empty<string>()
            }
        };

        public static IReadOnlyList<AiProviderPreset> GetAll() => Presets;

        public static AiProviderPreset Find(string providerId)
        {
            return Presets.FirstOrDefault(item =>
                string.Equals(item.Id, providerId, StringComparison.OrdinalIgnoreCase));
        }

        public static string NormalizeProviderId(string providerId)
        {
            return Find(providerId)?.Id ?? "custom";
        }

        public static string ResolveEndpoint(
            string providerId,
            string protocol,
            string configuredChatCompletionsEndpoint,
            string configuredResponsesEndpoint)
        {
            string normalizedProtocol = NormalizeProtocol(providerId, protocol);
            bool hasConfiguredOverrides = !string.IsNullOrWhiteSpace(configuredChatCompletionsEndpoint)
                || !string.IsNullOrWhiteSpace(configuredResponsesEndpoint);
            string configured = string.Equals(normalizedProtocol, AiProviderProtocol.OpenAiResponses, StringComparison.OrdinalIgnoreCase)
                ? configuredResponsesEndpoint
                : configuredChatCompletionsEndpoint;
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return configured.Trim().TrimEnd('/');
            }

            if (hasConfiguredOverrides)
            {
                string missingName = string.Equals(normalizedProtocol, AiProviderProtocol.OpenAiResponses, StringComparison.OrdinalIgnoreCase)
                    ? "Responses"
                    : "Chat Completions";
                throw new InvalidOperationException($"未配置 {missingName} 请求地址。请为当前厂商填写对应协议地址。");
            }

            AiProviderPreset preset = Find(providerId);
            string presetEndpoint = preset?.GetEndpoint(normalizedProtocol);
            if (!string.IsNullOrWhiteSpace(presetEndpoint))
            {
                return presetEndpoint.Trim().TrimEnd('/');
            }

            string protocolName = string.Equals(normalizedProtocol, AiProviderProtocol.OpenAiResponses, StringComparison.OrdinalIgnoreCase)
                ? "Responses"
                : "Chat Completions";
            throw new InvalidOperationException($"未配置 {protocolName} 请求地址。请为当前厂商填写对应协议地址。 ");
        }

        public static string BuildLegacyEndpoint(string baseUrl, string protocol)
        {
            string normalized = (baseUrl ?? string.Empty).Trim().TrimEnd('/');
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return string.Empty;
            }

            if (normalized.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)
                || normalized.EndsWith("/responses", StringComparison.OrdinalIgnoreCase))
            {
                return normalized;
            }

            return normalized + (string.Equals(protocol, AiProviderProtocol.OpenAiResponses, StringComparison.OrdinalIgnoreCase)
                ? "/responses"
                : "/chat/completions");
        }

        public static string ResolveEndpointFromBaseUrl(string providerId, string protocol, string baseUrl)
        {
            string normalizedProtocol = NormalizeProtocol(providerId, protocol);
            string normalizedBaseUrl = (baseUrl ?? string.Empty).Trim().TrimEnd('/');
            AiProviderPreset preset = Find(providerId);
            bool usesBuiltInPreset = preset != null
                && !string.Equals(preset.Id, "custom", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(preset.Id, "compatible", StringComparison.OrdinalIgnoreCase)
                && (string.IsNullOrWhiteSpace(normalizedBaseUrl)
                    || string.Equals(
                        normalizedBaseUrl,
                        (preset.BaseUrl ?? string.Empty).Trim().TrimEnd('/'),
                        StringComparison.OrdinalIgnoreCase));

            if (usesBuiltInPreset)
            {
                return (preset.GetEndpoint(normalizedProtocol) ?? string.Empty).Trim().TrimEnd('/');
            }

            return BuildLegacyEndpoint(normalizedBaseUrl, normalizedProtocol);
        }

        public static string ResolveBaseUrlFromProtocol(string providerId, string protocol, string baseUrl)
        {
            AiProviderPreset preset = Find(providerId);
            string normalizedBaseUrl = (baseUrl ?? string.Empty).Trim().TrimEnd('/');
            if (preset == null
                || string.Equals(preset.Id, "custom", StringComparison.OrdinalIgnoreCase)
                || string.Equals(preset.Id, "compatible", StringComparison.OrdinalIgnoreCase))
            {
                return normalizedBaseUrl;
            }

            string normalizedProtocol = NormalizeProtocol(providerId, protocol);
            string[] knownBaseUrls = preset.SupportedProtocols
                .Select(preset.GetBaseUrl)
                .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
                .Select(candidate => candidate.Trim().TrimEnd('/'))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            bool isPresetBaseUrl = string.IsNullOrWhiteSpace(normalizedBaseUrl)
                || knownBaseUrls.Any(candidate => string.Equals(candidate, normalizedBaseUrl, StringComparison.OrdinalIgnoreCase));

            return isPresetBaseUrl
                ? preset.GetBaseUrl(normalizedProtocol)
                : normalizedBaseUrl;
        }

        public static string NormalizeProtocol(string providerId, string protocol)
        {
            return NormalizeProtocol(providerId, protocol, null);
        }

        public static string NormalizeProtocol(string providerId, string protocol, string modelId)
        {
            AiProviderPreset preset = Find(providerId) ?? Find("custom");
            AiModelPreset model = preset.FindModel(modelId);
            if (model != null)
            {
                if (model.SupportsProtocol(protocol))
                {
                    return model.SupportedProtocols.First(candidate =>
                        string.Equals(candidate, protocol, StringComparison.OrdinalIgnoreCase));
                }

                return model.SupportedProtocols.First();
            }

            if (preset.Id.StartsWith("opencode-", StringComparison.OrdinalIgnoreCase))
            {
                return AiProviderProtocol.OpenAiCompletions;
            }

            if (preset.SupportsProtocol(protocol))
            {
                return preset.SupportedProtocols.First(candidate =>
                    string.Equals(candidate, protocol, StringComparison.OrdinalIgnoreCase));
            }

            return preset.SupportsProtocol(preset.Protocol)
                ? preset.Protocol
                : preset.SupportedProtocols.First();
        }

        public static bool SupportsModelProtocol(string providerId, string modelId, string protocol)
        {
            AiProviderPreset preset = Find(providerId) ?? Find("custom");
            AiModelPreset model = preset.FindModel(modelId);
            if (model != null)
            {
                return model.SupportsProtocol(protocol);
            }

            if (preset.Id.StartsWith("opencode-", StringComparison.OrdinalIgnoreCase))
            {
                return string.Equals(protocol, AiProviderProtocol.OpenAiCompletions, StringComparison.OrdinalIgnoreCase);
            }

            return preset.SupportsProtocol(protocol);
        }

        public static IReadOnlyList<string> GetSupportedProtocols(string providerId, string modelId)
        {
            AiProviderPreset preset = Find(providerId) ?? Find("custom");
            AiModelPreset model = preset.FindModel(modelId);
            if (model != null)
            {
                return model.SupportedProtocols;
            }

            if (preset.Id.StartsWith("opencode-", StringComparison.OrdinalIgnoreCase))
            {
                return new[] { AiProviderProtocol.OpenAiCompletions };
            }

            return preset.SupportedProtocols;
        }

        private static AiModelPreset Model(string id, string displayName, params string[] protocols)
        {
            return new AiModelPreset
            {
                Id = id,
                DisplayName = displayName,
                SupportedProtocols = protocols.Length == 0
                    ? new[] { AiProviderProtocol.OpenAiCompletions }
                    : protocols
            };
        }
    }
}


