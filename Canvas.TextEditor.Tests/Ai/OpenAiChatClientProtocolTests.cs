using System;
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
    public sealed class OpenAiChatClientProtocolTests
    {
        [Fact]
        public async Task StreamChatAsync_ResponsesProtocol_UsesResponsesEndpointAndParsesText()
        {
            var config = new ConfigManager(System.IO.Path.GetTempFileName());
            config.AiSermonProviderId = "openai";
            config.AiSermonProtocol = AiProviderProtocol.OpenAiResponses;
            config.AiSermonApiKey = "test-key";
            config.AiSermonBaseUrl = "https://example.test/v1";
            config.AiSermonModel = "gpt-test";

            var handler = new CaptureResponseHandler(
                "event: response.output_text.delta\n" +
                "data: {\"type\":\"response.output_text.delta\",\"delta\":\"你好\"}\n\n" +
                "event: response.completed\n" +
                "data: {\"type\":\"response.completed\"}\n\n" +
                "data: [DONE]\n\n");
            using var httpClient = new HttpClient(handler);
            using var client = new OpenAiChatClient(config, httpClient);
            var diagnostics = new List<string>();
            client.DiagnosticEmitted += diagnostics.Add;

            AiChatStreamResult result = await client.StreamChatAsync(
                new AiChatRequest
                {
                    Messages = new[] { new AiConversationMessage { Role = "user", Content = "请问候" } }
                },
                _ => { },
                CancellationToken.None);

            Assert.Equal("https://example.test/v1/responses", handler.Request.RequestUri.ToString());
            Assert.Equal("Bearer", handler.Request.Headers.Authorization.Scheme);
            Assert.Contains("\"input\"", handler.Body);
            Assert.Contains("\"stream\":true", handler.Body);
            Assert.Equal("你好", result.Content);
            Assert.Contains(diagnostics, message => message.Contains("POST /responses", StringComparison.Ordinal));
            Assert.Contains(diagnostics, message => message.Contains("已收到响应头", StringComparison.Ordinal));
        }

        [Fact]
        public async Task StreamChatAsync_UsesTheSavedActiveProfileAfterSelection()
        {
            string configPath = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"canvas-ai-active-profile-{Guid.NewGuid():N}.json");
            try
            {
                var config = new ConfigManager(configPath);
                AiConnectionProfile profile = config.CreateAiProfile("我的自定义 Qwen");
                profile.ProviderId = "qwen";
                profile.Protocol = AiProviderProtocol.OpenAiResponses;
                profile.BaseUrl = "https://profile.example/v1";
                profile.ApiKey = "profile-key";
                profile.ModelId = "qwen-plus-custom";
                config.SaveAiProfile(profile);
                Assert.True(config.SetActiveAiProfile(profile.Id));

                var handler = new CaptureResponseHandler("data: [DONE]\n\n");
                using var httpClient = new HttpClient(handler);
                using var client = new OpenAiChatClient(config, httpClient);

                await client.StreamChatAsync(new AiChatRequest(), _ => { }, CancellationToken.None);

                Assert.Equal("https://profile.example/v1/responses", handler.Request.RequestUri.ToString());
                Assert.Equal("profile-key", handler.Request.Headers.Authorization.Parameter);
                Assert.Contains("\"model\":\"qwen-plus-custom\"", handler.Body);
            }
            finally
            {
                if (System.IO.File.Exists(configPath)) System.IO.File.Delete(configPath);
            }
        }

        [Fact]
        public async Task StreamChatAsync_DeepSeekCompletions_UsesThinkingDefaults()
        {
            var config = new ConfigManager(System.IO.Path.GetTempFileName());
            config.AiSermonProviderId = "deepseek";
            config.AiSermonProtocol = AiProviderProtocol.OpenAiCompletions;
            config.AiSermonApiKey = "test-key";
            config.AiSermonBaseUrl = "https://example.test";
            config.AiSermonModel = "deepseek-flash";

            var handler = new CaptureResponseHandler("data: [DONE]\n\n");
            using var httpClient = new HttpClient(handler);
            using var client = new OpenAiChatClient(config, httpClient);
            var diagnostics = new List<string>();
            client.DiagnosticEmitted += diagnostics.Add;

            await client.StreamChatAsync(new AiChatRequest(), _ => { }, CancellationToken.None);

            Assert.Equal("https://example.test/chat/completions", handler.Request.RequestUri.ToString());
            Assert.Contains("\"model\":\"deepseek-flash\"", handler.Body);
            Assert.Contains("\"thinking\":{\"type\":\"enabled\"}", handler.Body);
            Assert.Contains("\"reasoning_effort\":\"high\"", handler.Body);
            Assert.DoesNotContain("\"temperature\"", handler.Body);
            Assert.Contains(diagnostics, message => message.Contains("POST /chat/completions", StringComparison.Ordinal));
            Assert.Contains(diagnostics, message => message.Contains("已收到响应头", StringComparison.Ordinal));
        }

        [Fact]
        public async Task StreamChatAsync_ResponsesProtocol_ParsesCompletedUsageWithoutDoneSentinel()
        {
            var config = new ConfigManager(System.IO.Path.GetTempFileName());
            config.AiSermonProviderId = "deepseek";
            config.AiSermonProtocol = AiProviderProtocol.OpenAiResponses;
            config.AiSermonApiKey = "test-key";
            config.AiSermonBaseUrl = "https://example.test";
            config.AiSermonModel = "deepseek-flash";

            var handler = new CaptureResponseHandler(
                "event: response.output_text.delta\n" +
                "data: {\"type\":\"response.output_text.delta\",\"delta\":\"完成\"}\n\n" +
                "event: response.completed\n" +
                "data: {\"type\":\"response.completed\",\"response\":{\"usage\":{\"input_tokens\":10,\"input_tokens_details\":{\"cached_tokens\":4}}}}\n\n");
            using var httpClient = new HttpClient(handler);
            using var client = new OpenAiChatClient(config, httpClient);

            AiChatStreamResult result = await client.StreamChatAsync(new AiChatRequest(), _ => { }, CancellationToken.None);

            Assert.Equal("完成", result.Content);
            Assert.Equal(4, result.PromptCacheHitTokens);
            Assert.Equal(6, result.PromptCacheMissTokens);
        }

        private sealed class CaptureResponseHandler : HttpMessageHandler
        {
            private readonly string _body;
            public HttpRequestMessage Request { get; private set; }
            public string Body { get; private set; }

            public CaptureResponseHandler(string body)
            {
                _body = body;
            }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Request = request;
                Body = await request.Content.ReadAsStringAsync(cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(_body, Encoding.UTF8, "text/event-stream")
                };
            }
        }
    }
}
