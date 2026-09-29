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
        public string Protocol { get; init; } = AiProviderProtocol.OpenAiCompletions;
        public IReadOnlyList<string> RecommendedModels { get; init; } = Array.Empty<string>();
    }

    public static class AiProviderCatalog
    {
        private static readonly IReadOnlyList<AiProviderPreset> Presets = new[]
        {
            new AiProviderPreset
            {
                Id = "deepseek", DisplayName = "DeepSeek", Group = "官方厂商",
                Description = "DeepSeek 官方 OpenAI 兼容接口", BaseUrl = "https://api.deepseek.com",
                Protocol = AiProviderProtocol.OpenAiCompletions,
                RecommendedModels = new[] { "deepseek-flash", "deepseek-v4-pro" }
            },
            new AiProviderPreset
            {
                Id = "qwen", DisplayName = "通义千问", Group = "官方厂商",
                Description = "阿里云百炼 DashScope OpenAI 兼容接口", BaseUrl = "https://dashscope.aliyuncs.com/compatible-mode/v1",
                Protocol = AiProviderProtocol.OpenAiCompletions,
                RecommendedModels = new[] { "qwen-plus", "qwen-max", "qwen-turbo" }
            },
            new AiProviderPreset
            {
                Id = "zhipu", DisplayName = "智谱 GLM", Group = "官方厂商",
                Description = "智谱 BigModel OpenAI 兼容接口", BaseUrl = "https://open.bigmodel.cn/api/paas/v4",
                Protocol = AiProviderProtocol.OpenAiCompletions,
                RecommendedModels = new[] { "glm-4.5-air", "glm-4-plus" }
            },
            new AiProviderPreset
            {
                Id = "moonshot", DisplayName = "月之暗面 Kimi", Group = "官方厂商",
                Description = "Moonshot AI OpenAI 兼容接口", BaseUrl = "https://api.moonshot.cn/v1",
                Protocol = AiProviderProtocol.OpenAiCompletions,
                RecommendedModels = new[] { "moonshot-v1-8k", "moonshot-v1-32k" }
            },
            new AiProviderPreset
            {
                Id = "minimax", DisplayName = "MiniMax", Group = "官方厂商",
                Description = "MiniMax 文本模型 OpenAI 兼容接口", BaseUrl = "https://api.minimaxi.com/v1",
                Protocol = AiProviderProtocol.OpenAiCompletions,
                RecommendedModels = new[] { "MiniMax-Text-01", "abab6.5s-chat" }
            },
            new AiProviderPreset
            {
                Id = "baidu", DisplayName = "百度千帆", Group = "官方厂商",
                Description = "百度智能云千帆模型服务接口", BaseUrl = "https://qianfan.baidubce.com/v2",
                Protocol = AiProviderProtocol.OpenAiCompletions,
                RecommendedModels = new[] { "ernie-4.0-8k", "ernie-speed-128k" }
            },
            new AiProviderPreset
            {
                Id = "doubao", DisplayName = "火山引擎豆包", Group = "官方厂商",
                Description = "火山引擎方舟 OpenAI 兼容接口", BaseUrl = "https://ark.cn-beijing.volces.com/api/v3",
                Protocol = AiProviderProtocol.OpenAiCompletions,
                RecommendedModels = new[] { "doubao-1-5-pro-32k-250115", "doubao-1-5-lite-32k" }
            },
            new AiProviderPreset
            {
                Id = "siliconflow", DisplayName = "硅基流动", Group = "聚合与网关",
                Description = "硅基流动国内模型聚合接口", BaseUrl = "https://api.siliconflow.cn/v1",
                Protocol = AiProviderProtocol.OpenAiCompletions,
                RecommendedModels = new[] { "Qwen/Qwen3-8B", "deepseek-ai/DeepSeek-V3" }
            },
            new AiProviderPreset
            {
                Id = "opencode-zen", DisplayName = "OpenCode Zen（按量付费）", Group = "聚合与网关",
                Description = "OpenCode Zen 按量付费接口，与 Go 订阅分开计费", BaseUrl = "https://opencode.ai/zen/v1",
                Protocol = AiProviderProtocol.OpenAiCompletions,
                RecommendedModels = new[] { "deepseek-v4.1-flash" }
            },
            new AiProviderPreset
            {
                Id = "opencode-go", DisplayName = "OpenCode Go（编程订阅）", Group = "聚合与网关",
                Description = "OpenCode Go 编程订阅，官方定位为编程代理用途；其他用途需确认服务条款", BaseUrl = "https://opencode.ai/zen/go/v1",
                Protocol = AiProviderProtocol.OpenAiCompletions,
                RecommendedModels = new[] { "deepseek-v4.1-flash" }
            },
            new AiProviderPreset
            {
                Id = "compatible", DisplayName = "OpenAI 兼容接口", Group = "本地与自定义",
                Description = "适用于国内代理、企业网关或自建兼容服务", BaseUrl = "",
                Protocol = AiProviderProtocol.OpenAiCompletions,
                RecommendedModels = Array.Empty<string>()
            },
            new AiProviderPreset
            {
                Id = "custom", DisplayName = "自定义", Group = "本地与自定义",
                Description = "手动填写地址、协议和模型", BaseUrl = "",
                Protocol = AiProviderProtocol.OpenAiCompletions,
                RecommendedModels = Array.Empty<string>()
            }
        };

        public static IReadOnlyList<AiProviderPreset> GetAll() => Presets;

        public static AiProviderPreset Find(string providerId)
        {
            return Presets.FirstOrDefault(item =>
                string.Equals(item.Id, providerId, StringComparison.OrdinalIgnoreCase));
        }
    }
}


