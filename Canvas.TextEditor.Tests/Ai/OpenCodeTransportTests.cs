using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ImageColorChanger.Core;
using ImageColorChanger.Services.Ai;
using Xunit;

namespace Canvas.TextEditor.Tests.Ai;

public sealed class OpenCodeTransportTests
{
    [Theory]
    [InlineData("openai-completions")]
    [InlineData("openai-responses")]
    public async Task Go_UsesStableOpaqueConversationHeaderAndOwnIdentity(string protocol)
    {
        await WithClient("https://opencode.ai/zen/go/v1", protocol, async (client, handler) =>
        {
            var first = Request("conversation-a");
            await client.StreamChatAsync(first, _ => { }, CancellationToken.None);
            await client.StreamChatAsync(Request("conversation-a"), _ => { }, CancellationToken.None);
            await client.StreamChatAsync(Request("conversation-b"), _ => { }, CancellationToken.None);
            Assert.Matches("^canvas_[0-9a-f]{32}$", handler.Sessions[0] ?? "");
            Assert.Equal(handler.Sessions[0], handler.Sessions[1]);
            Assert.NotEqual(handler.Sessions[0], handler.Sessions[2]);
            Assert.All(handler.Agents, agent => Assert.StartsWith("CanvasCast/", agent));
            Assert.All(handler.Bodies, body => Assert.DoesNotContain("conversation-a", body));
        });
    }

    [Theory]
    [InlineData("https://api.deepseek.com")]
    [InlineData("https://opencode.ai.evil.test/zen/go/v1")]
    [InlineData("http://opencode.ai/zen/go/v1")]
    [InlineData("https://opencode.ai/zen/v1")]
    [InlineData("https://opencode.ai/zen/go/v10")]
    public async Task UnrelatedDestination_DoesNotReceiveGoHeaders(string url)
    {
        await WithClient(url, AiProviderProtocol.OpenAiCompletions, async (client, handler) =>
        {
            await client.StreamChatAsync(new AiChatRequest(), _ => { }, CancellationToken.None);
            Assert.Null(handler.Sessions.Single());
        });
    }

    [Fact]
    public async Task MissingConversation_IsIsolatedButRetryOfSameRequestIsStable()
    {
        await WithClient("https://opencode.ai/zen/go/v1", AiProviderProtocol.OpenAiCompletions, async (client, handler) =>
        {
            var request = new AiChatRequest();
            await client.StreamChatAsync(request, _ => { }, CancellationToken.None);
            await client.StreamChatAsync(request, _ => { }, CancellationToken.None);
            await client.StreamChatAsync(new AiChatRequest(), _ => { }, CancellationToken.None);
            Assert.False(string.IsNullOrEmpty(handler.Sessions[0]));
            Assert.Equal(handler.Sessions[0], handler.Sessions[1]);
            Assert.NotEqual(handler.Sessions[0], handler.Sessions[2]);
        });
    }

    [Fact]
    public void NewSession_HasUniqueStableTransportIdentity()
    {
        var field = typeof(AiSermonSessionState).GetProperty("ConversationId");
        Assert.NotNull(field);
        var session = new AiSermonSessionState();
        var id = field.GetValue(session);
        session.ProjectId = 7;
        Assert.Equal(id, field.GetValue(session));
        Assert.NotEqual(id, field.GetValue(new AiSermonSessionState()));
    }

    private static AiChatRequest Request(string id)
    {
        var request = new AiChatRequest();
        // Reflection keeps the red test compilable against the pristine contract.
        typeof(AiChatRequest).GetProperty("ConversationId")?.SetValue(request, id);
        return request;
    }

    private static async Task WithClient(string url, string protocol, Func<OpenAiChatClient, CaptureHandler, Task> test)
    {
        string path = System.IO.Path.GetTempFileName();
        try
        {
            var config = new ConfigManager(path);
            config.AiSermonProviderId = "custom";
            config.AiSermonProtocol = protocol;
            config.AiSermonBaseUrl = url;
            config.AiSermonChatCompletionsEndpoint = url.TrimEnd('/') + "/chat/completions";
            config.AiSermonResponsesEndpoint = url.TrimEnd('/') + "/responses";
            config.AiSermonApiKey = "test-key";
            config.AiSermonModel = "deepseek-v4.1-flash";
            var handler = new CaptureHandler();
            using var http = new HttpClient(handler);
            using var client = new OpenAiChatClient(config, http);
            await test(client, handler);
        }
        finally { System.IO.File.Delete(path); }
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public List<string> Sessions { get; } = new();
        public List<string> Agents { get; } = new();
        public List<string> Bodies { get; } = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Sessions.Add(request.Headers.TryGetValues("x-opencode-session", out var values) ? values.Single() : null);
            Agents.Add(request.Headers.UserAgent.ToString());
            Bodies.Add(await request.Content.ReadAsStringAsync(token));
            return new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent("data: [DONE]\n\n", Encoding.UTF8, "text/event-stream") };
        }
    }
}
