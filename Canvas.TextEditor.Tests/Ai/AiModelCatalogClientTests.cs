using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ImageColorChanger.Services.Ai;
using Xunit;

namespace Canvas.TextEditor.Tests.Ai
{
    public sealed class AiModelCatalogClientTests
    {
        [Fact]
        public async Task GetModelsAsync_ParsesOpenAiModelList()
        {
            using var httpClient = new HttpClient(new StaticHandler(
                "{\"data\":[{\"id\":\"model-b\"},{\"id\":\"model-a\"}]}"));
            var client = new AiModelCatalogClient(httpClient);

            var models = await client.GetModelsAsync(
                "https://example.test/v1",
                "test-key",
                CancellationToken.None);

            Assert.Equal(new[] { "model-a", "model-b" }, models);
        }

        [Fact]
        public async Task GetModelOptionsAsync_PreservesDeepSeekDisplayNameAndApiId()
        {
            using var httpClient = new HttpClient(new StaticHandler(
                "{\"data\":[{\"id\":\"deepseek-flash\",\"name\":\"DeepSeek-V4.1-Flash\",\"context_window\":1000000}]}"));
            var client = new AiModelCatalogClient(httpClient);

            var models = await client.GetModelOptionsAsync(
                "https://example.test/v1",
                "test-key",
                CancellationToken.None);

            var model = Assert.Single(models);
            Assert.Equal("deepseek-flash", model.Id);
            Assert.Equal("DeepSeek-V4.1-Flash", model.DisplayName);
            Assert.Equal(1000000, model.ContextWindow);
        }

        private sealed class StaticHandler : HttpMessageHandler
        {
            private readonly string _body;

            public StaticHandler(string body)
            {
                _body = body;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(_body, Encoding.UTF8, "application/json")
                });
            }
        }
    }
}
