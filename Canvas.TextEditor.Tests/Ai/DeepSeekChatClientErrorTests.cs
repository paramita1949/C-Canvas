using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ImageColorChanger.Core;
using ImageColorChanger.Services.Ai;
using Xunit;

namespace Canvas.TextEditor.Tests.Ai
{
    public sealed class DeepSeekChatClientErrorTests
    {
        [Fact]
        public async Task StreamChatAsync_Unauthorized_ReturnsFriendlyConfigurationError()
        {
            var config = new TestConfigManager();
            config.DeepSeekApiKey = "invalid";
            using var httpClient = new HttpClient(new StaticResponseHandler(
                HttpStatusCode.Unauthorized,
                "{\"error\":{\"message\":\"Authentication Fails, Your api key is invalid\"}}"));
            using var client = new DeepSeekChatClient(config, httpClient);

            var ex = await Assert.ThrowsAsync<HttpRequestException>(() =>
                client.StreamChatAsync(new AiChatRequest(), _ => { }, CancellationToken.None));

            Assert.Contains("DeepSeek 认证失败", ex.Message);
            Assert.Contains("AI DeepSeek 配置中心", ex.Message);
            Assert.DoesNotContain("{\"error\"", ex.Message);
        }

        [Fact]
        public async Task StreamChatAsync_UsageNull_DoesNotThrowJsonElementTypeError()
        {
            var config = new TestConfigManager();
            config.DeepSeekApiKey = "valid";
            using var httpClient = new HttpClient(new StaticResponseHandler(
                HttpStatusCode.OK,
                "data: {\"choices\":[{\"delta\":{\"content\":\"主题已读取\"}}],\"usage\":null}\n\n" +
                "data: {\"choices\":[{\"delta\":{}}],\"usage\":{\"prompt_cache_hit_tokens\":3,\"prompt_cache_miss_tokens\":5}}\n\n" +
                "data: [DONE]\n\n"));
            using var client = new DeepSeekChatClient(config, httpClient);

            var result = await client.StreamChatAsync(new AiChatRequest(), _ => { }, CancellationToken.None);

            Assert.Equal("主题已读取", result.Content);
            Assert.Equal(3, result.PromptCacheHitTokens);
            Assert.Equal(5, result.PromptCacheMissTokens);
        }

        [Fact]
        public async Task GetBalanceAsync_ParsesCnyTotalBalance()
        {
            var config = new TestConfigManager();
            config.DeepSeekApiKey = "valid";
            using var httpClient = new HttpClient(new StaticResponseHandler(
                HttpStatusCode.OK,
                "{\"is_available\":true,\"balance_infos\":[{\"currency\":\"CNY\",\"total_balance\":\"110.25\",\"granted_balance\":\"10.00\",\"topped_up_balance\":\"100.25\"}]}",
                "application/json"));
            using var client = new DeepSeekChatClient(config, httpClient);

            var balance = await client.GetBalanceAsync(CancellationToken.None);

            Assert.True(balance.IsAvailable);
            Assert.Equal("CNY", balance.Currency);
            Assert.Equal(110.25m, balance.TotalBalance);
        }

        private sealed class StaticResponseHandler : HttpMessageHandler
        {
            private readonly HttpStatusCode _statusCode;
            private readonly string _content;
            private readonly string _mediaType;

            public StaticResponseHandler(HttpStatusCode statusCode, string content, string mediaType = "text/event-stream")
            {
                _statusCode = statusCode;
                _content = content;
                _mediaType = mediaType;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                return Task.FromResult(new HttpResponseMessage(_statusCode)
                {
                    Content = new StringContent(_content, Encoding.UTF8, _mediaType)
                });
            }
        }

        private sealed class TestConfigManager : ConfigManager
        {
            public TestConfigManager()
                : base(System.IO.Path.GetTempFileName())
            {
            }
        }
    }
}
