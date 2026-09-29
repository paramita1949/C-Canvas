using System.Net.Http;
using ImageColorChanger.Core;

namespace ImageColorChanger.Services.Ai
{
    // 保留旧类型名，兼容现有测试与扩展点；实际实现已迁移到 OpenAiChatClient。
    public sealed class DeepSeekChatClient : OpenAiChatClient
    {
        public DeepSeekChatClient(ConfigManager config)
            : base(config)
        {
        }

        internal DeepSeekChatClient(ConfigManager config, HttpClient httpClient, bool ownsHttpClient = false)
            : base(config, httpClient, ownsHttpClient)
        {
        }
    }
}
