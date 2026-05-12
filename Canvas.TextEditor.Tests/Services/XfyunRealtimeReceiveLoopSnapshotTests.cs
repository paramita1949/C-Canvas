using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using ImageColorChanger.Services.LiveCaption;
using Xunit;

namespace Canvas.TextEditor.Tests.Services
{
    public sealed class XfyunRealtimeReceiveLoopSnapshotTests
    {
        [Fact]
        public async Task StartWebSocketReceiveLoopAsync_UsesSocketSnapshot()
        {
            using var originalSocket = new ClientWebSocket();
            ClientWebSocket observedSocket = null;

            Task task = CliProxyApiClient.StartWebSocketReceiveLoopAsync(
                originalSocket,
                CancellationToken.None,
                (ws, _) =>
                {
                    observedSocket = ws;
                    return Task.CompletedTask;
                });

            await task;

            Assert.Same(originalSocket, observedSocket);
        }

        [Fact]
        public async Task StartWebSocketReceiveLoopAsync_NullSocketCompletesWithoutCallingLoop()
        {
            bool called = false;

            Task task = CliProxyApiClient.StartWebSocketReceiveLoopAsync(
                null,
                CancellationToken.None,
                (_, _) =>
                {
                    called = true;
                    return Task.CompletedTask;
                });

            await task;

            Assert.False(called);
        }
    }
}
