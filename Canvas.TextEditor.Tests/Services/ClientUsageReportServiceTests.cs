using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ImageColorChanger.Services;

namespace Canvas.TextEditor.Tests.Services
{
    public sealed class ClientUsageReportServiceTests
    {
        [Fact]
        public async Task ReportStartupAsync_PostsHardwareIdAndVersion()
        {
            var handler = new CaptureHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
            var reporter = new ClientUsageReportService(
                new HttpClient(handler),
                () => "hwid-sha256",
                () => "6.0.8.2",
                () => "Windows 11",
                () => "Church-PC",
                new[] { "https://example.test/api/client/version/report" });

            await reporter.ReportStartupAsync();

            Assert.Single(handler.Requests);
            Assert.Equal("https://example.test/api/client/version/report", handler.Requests[0].RequestUri.ToString());

            using var document = JsonDocument.Parse(handler.Requests[0].Body);
            JsonElement root = document.RootElement;
            Assert.Equal("hwid-sha256", root.GetProperty("hardware_id").GetString());
            Assert.Equal("6.0.8.2", root.GetProperty("app_version").GetString());
            Assert.Equal("stable", root.GetProperty("channel").GetString());
            Assert.Equal("Windows 11", root.GetProperty("os_version").GetString());
            Assert.Equal("Church-PC", root.GetProperty("device_name").GetString());
        }

        [Fact]
        public async Task ReportStartupAsync_SwallowsNetworkFailures()
        {
            var handler = new CaptureHandler(_ => throw new HttpRequestException("offline"));
            var reporter = new ClientUsageReportService(
                new HttpClient(handler),
                () => "hwid-sha256",
                () => "6.0.8.2",
                () => "Windows 11",
                () => "Church-PC",
                new[] { "https://example.test/api/client/version/report" });

            await reporter.ReportStartupAsync();

            Assert.Single(handler.Requests);
        }

        private sealed class CaptureHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

            public CaptureHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
            {
                _responder = responder;
            }

            public List<CapturedRequest> Requests { get; } = new();

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                string body = request.Content == null
                    ? string.Empty
                    : await request.Content.ReadAsStringAsync(cancellationToken);

                Requests.Add(new CapturedRequest(request.RequestUri, body));
                return _responder(request);
            }
        }

        private sealed record CapturedRequest(Uri RequestUri, string Body);
    }
}
