using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using ImageColorChanger.Core;
using ImageColorChanger.Services.Ai;

namespace Canvas.TextEditor.Tests.Ai;

public sealed class AiStreamFailureTests
{
    [Theory]
    [InlineData(AiProviderProtocol.OpenAiCompletions, "data: {\"error\":{\"message\":\"fixture failure\"}}\n\n")]
    [InlineData(AiProviderProtocol.OpenAiResponses, "data: {\"type\":\"response.failed\",\"response\":{\"error\":{\"message\":\"fixture failure\"}}}\n\n")]
    [InlineData(AiProviderProtocol.OpenAiResponses, "data: {\"type\":\"error\",\"message\":\"fixture failure\"}\n\n")]
    [InlineData(AiProviderProtocol.OpenAiResponses, "data: {\"type\":\"response.incomplete\",\"response\":{\"incomplete_details\":{\"reason\":\"max_output_tokens\"}}}\n\n")]
    public async Task StreamErrors_AreNotEmptySuccess(string protocol, string body)
    {
        using var fixture = new Fixture(protocol, new MemoryStream(Encoding.UTF8.GetBytes(body)));
        await Assert.ThrowsAsync<HttpRequestException>(() => fixture.Client.StreamChatAsync(new AiChatRequest(), _ => { }, default));
    }

    [Theory]
    [InlineData(AiProviderProtocol.OpenAiCompletions, false)]
    [InlineData(AiProviderProtocol.OpenAiResponses, false)]
    [InlineData(AiProviderProtocol.OpenAiCompletions, true)]
    [InlineData(AiProviderProtocol.OpenAiResponses, true)]
    public async Task StalledBody_StopsOnCancellationOrRequestTimeout(string protocol, bool timeout)
    {
        using var stream = new StalledStream("");
        using var fixture = new Fixture(protocol, stream);
        fixture.Http.Timeout = timeout ? TimeSpan.FromMilliseconds(150) : TimeSpan.FromSeconds(30);
        using var cts = new CancellationTokenSource();
        var task = Task.Run(() => fixture.Client.StreamChatAsync(new AiChatRequest(), _ => { }, cts.Token));
        await stream.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (!timeout) cts.Cancel();
        bool stopped = await Task.WhenAny(task, Task.Delay(1500)) == task;
        stream.Release.TrySetResult();
        Exception error = await Record.ExceptionAsync(async () => await task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(stopped, "Body read ignored cancellation/deadline until remote EOF");
        Assert.IsAssignableFrom<OperationCanceledException>(error);
    }

    [Fact]
    public async Task ResponsesCompleted_DoesNotWaitForRemoteEof()
    {
        using var stream = new StalledStream("data: {\"type\":\"response.output_text.delta\",\"delta\":\"ok\"}\n\ndata: {\"type\":\"response.completed\"}\n\n");
        using var fixture = new Fixture(AiProviderProtocol.OpenAiResponses, stream);
        var task = Task.Run(() => fixture.Client.StreamChatAsync(new AiChatRequest(), _ => { }, default));
        bool stopped = await Task.WhenAny(task, Task.Delay(1500)) == task;
        stream.Release.TrySetResult();
        var result = await task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(stopped, "Completed event still waits for EOF");
        Assert.Equal("ok", result.Content);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        public HttpClient Http { get; }
        public OpenAiChatClient Client { get; }
        public Fixture(string protocol, Stream stream)
        {
            var config = new ConfigManager(path);
            config.AiSermonProviderId = "openai";
            config.AiSermonProtocol = protocol;
            config.AiSermonApiKey = "fixture-key";
            config.AiSermonBaseUrl = "https://example.test/v1";
            config.AiSermonChatCompletionsEndpoint = "https://example.test/v1/chat/completions";
            config.AiSermonResponsesEndpoint = "https://example.test/v1/responses";
            config.AiSermonModel = "fixture-model";
            Http = new HttpClient(new Handler(stream));
            Client = new OpenAiChatClient(config, Http);
        }
        public void Dispose() { Client.Dispose(); Http.Dispose(); if (File.Exists(path)) File.Delete(path); }
    }
    private sealed class Handler(Stream stream) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) });
    }
    private sealed class StalledStream(string prefix) : Stream
    {
        private readonly MemoryStream initial = new(Encoding.UTF8.GetBytes(prefix));
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override int Read(byte[] buffer, int offset, int count)
        {
            int n = initial.Read(buffer, offset, count); if (n > 0) return n;
            Started.TrySetResult(); Release.Task.GetAwaiter().GetResult(); return 0;
        }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        {
            int n = initial.Read(buffer.Span); if (n > 0) return n;
            Started.TrySetResult(); await Release.Task.WaitAsync(token); return 0;
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token) => ReadAsync(buffer.AsMemory(offset, count), token).AsTask();
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
