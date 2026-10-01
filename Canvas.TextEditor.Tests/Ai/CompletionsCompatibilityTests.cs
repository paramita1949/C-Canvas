using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ImageColorChanger.Core;
using ImageColorChanger.Services.Ai;
using Xunit;

namespace Canvas.TextEditor.Tests.Ai;

public sealed class CompletionsCompatibilityTests
{
    [Theory]
    [InlineData("opencode-go", "https://opencode.ai/zen/go/v1", true)]
    [InlineData("opencode-zen", "https://opencode.ai/zen/v1", false)]
    [InlineData("custom", "https://opencode.ai/zen/v1/", false)]
    public async Task OpenCode_Completions_OmitsNameFromOrdinaryMessages(string provider, string baseUrl, bool expectsSessionHeader)
    {
        string path = System.IO.Path.GetTempFileName();
        try
        {
            var config = new ConfigManager(path);
            config.AiSermonProviderId = provider;
            config.AiSermonProtocol = AiProviderProtocol.OpenAiCompletions;
            config.AiSermonBaseUrl = baseUrl;
            config.AiSermonChatCompletionsEndpoint = baseUrl.TrimEnd('/') + "/chat/completions";
            config.AiSermonApiKey = "test-key";
            config.AiSermonModel = "glm-5.3-flash";

            var handler = new RejectOrdinaryMessageNamesHandler();
            using var http = new HttpClient(handler);
            using var client = new OpenAiChatClient(config, http);

            var result = await client.StreamChatAsync(
                new AiChatRequest
                {
                    Messages = new[]
                    {
                        new AiConversationMessage { Role = "system", Name = "project_context", Content = "项目上下文" },
                        new AiConversationMessage { Role = "user", Name = "slide_context", Content = "请解读" }
                    }
                },
                _ => { },
                CancellationToken.None);

            Assert.Equal("ok", result.Content);
            Assert.Equal(expectsSessionHeader, handler.HasOpenCodeSessionHeader);
            using var json = JsonDocument.Parse(handler.Body);
            var messages = json.RootElement.GetProperty("messages").EnumerateArray().ToArray();
            Assert.Equal(2, messages.Length);
            Assert.Equal("system", messages[0].GetProperty("role").GetString());
            Assert.Equal("项目上下文", messages[0].GetProperty("content").GetString());
            Assert.False(messages[0].TryGetProperty("name", out _));
            Assert.Equal("user", messages[1].GetProperty("role").GetString());
            Assert.Equal("请解读", messages[1].GetProperty("content").GetString());
            Assert.False(messages[1].TryGetProperty("name", out _));
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }

    [Fact]
    public async Task NonOpenCode_Completions_RetainsOrdinaryMessageName()
    {
        string path = System.IO.Path.GetTempFileName();
        try
        {
            var config = new ConfigManager(path);
            config.AiSermonProviderId = "deepseek";
            config.AiSermonProtocol = AiProviderProtocol.OpenAiCompletions;
            config.AiSermonBaseUrl = "https://api.deepseek.com";
            config.AiSermonApiKey = "test-key";
            config.AiSermonModel = "deepseek-flash";

            var handler = new CaptureMessageHandler();
            using var http = new HttpClient(handler);
            using var client = new OpenAiChatClient(config, http);

            await client.StreamChatAsync(
                new AiChatRequest
                {
                    Messages = new[]
                    {
                        new AiConversationMessage { Role = "system", Name = "project_context", Content = "项目上下文" }
                    }
                },
                _ => { },
                CancellationToken.None);

            using var json = JsonDocument.Parse(handler.Body);
            var message = json.RootElement.GetProperty("messages")[0];
            Assert.Equal("project_context", message.GetProperty("name").GetString());
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }

    [Theory]
    [InlineData("custom", "glm-5.3-flash", "")]
    [InlineData("opencode-go", "glm-5.3-flash", "canvas-sermon-7")]
    [InlineData("opencode-zen", "deepseek-v4.1-flash", "canvas-sermon-8")]
    [InlineData("deepseek", "deepseek-flash", "canvas-sermon")]
    public async Task StrictCompletionsBackend_AcceptsRequestWithoutUserId(string provider, string model, string user)
    {
        string path = System.IO.Path.GetTempFileName();
        try
        {
            var config = new ConfigManager(path);
            config.AiSermonProviderId = provider;
            config.AiSermonProtocol = AiProviderProtocol.OpenAiCompletions;
            string baseUrl = provider switch
            {
                "opencode-go" => "https://opencode.ai/zen/go/v1",
                "opencode-zen" => "https://opencode.ai/zen/v1",
                "custom" => "https://opencode.ai/zen/go/v1",
                _ => "https://api.deepseek.com"
            };
            config.AiSermonBaseUrl = baseUrl;
            config.AiSermonChatCompletionsEndpoint = baseUrl + "/chat/completions";
            config.AiSermonApiKey = "test-key";
            config.AiSermonModel = model;
            using var http = new HttpClient(new StrictHandler(
                expectsSessionHeader: provider == "opencode-go" || provider == "custom"));
            using var client = new OpenAiChatClient(config, http);
            var result = await client.StreamChatAsync(new AiChatRequest { UserId = user }, _ => { }, CancellationToken.None);
            Assert.Equal("ok", result.Content);
        }
        finally { System.IO.File.Delete(path); }
    }

    private sealed class StrictHandler(bool expectsSessionHeader) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            using var json = JsonDocument.Parse(await request.Content.ReadAsStringAsync(token));
            if (json.RootElement.TryGetProperty("user_id", out _))
                return new HttpResponseMessage(HttpStatusCode.BadRequest)
                { Content = new StringContent("{\"error\":{\"param\":\"user_id\",\"message\":\"unknown field user_id\"}}") };
            Assert.Equal(expectsSessionHeader, request.Headers.Contains("x-opencode-session"));
            Assert.True(json.RootElement.GetProperty("stream").GetBoolean());
            Assert.True(json.RootElement.TryGetProperty("tools", out _));
            return new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent("data: {\"choices\":[{\"delta\":{\"content\":\"ok\"}}]}\n\ndata: [DONE]\n\n", Encoding.UTF8, "text/event-stream") };
        }
    }

    private sealed class RejectOrdinaryMessageNamesHandler : HttpMessageHandler
    {
        public string Body { get; private set; }
        public bool HasOpenCodeSessionHeader { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            HasOpenCodeSessionHeader = request.Headers.Contains("x-opencode-session");
            Body = await request.Content.ReadAsStringAsync(token);
            using var json = JsonDocument.Parse(Body);
            if (json.RootElement.GetProperty("messages").EnumerateArray()
                .Any(message => message.TryGetProperty("name", out _)))
            {
                return new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = new StringContent(
                        "{\"error\":{\"param\":\"messages\",\"message\":\"messages[1]: \"name\" is not supported by this endpoint\"}}")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "data: {\"choices\":[{\"delta\":{\"content\":\"ok\"}}]}\n\ndata: [DONE]\n\n",
                    Encoding.UTF8,
                    "text/event-stream")
            };
        }
    }

    private sealed class CaptureMessageHandler : HttpMessageHandler
    {
        public string Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Body = await request.Content.ReadAsStringAsync(token);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("data: [DONE]\n\n", Encoding.UTF8, "text/event-stream")
            };
        }
    }
}
