using System.Reflection;
using System.Text.Json;
using ImageColorChanger.Services;
using Xunit;

namespace ImageColorChanger.CanvasTextEditor.Tests.Services
{
    public sealed class BibleBaiduShortSpeechClientHotwordPayloadTests
    {
        [Fact]
        public void BuildDoubaoShortPayload_WithBoostingTableId_ShouldInjectId()
        {
            string json = InvokeBuildPayload("app-1", "token-1", "cluster-1", "req-1", "table-id-01", "table-name-ignored");

            using var doc = JsonDocument.Parse(json);
            JsonElement request = doc.RootElement.GetProperty("request");
            Assert.Equal("table-id-01", request.GetProperty("boosting_table_id").GetString());
            Assert.False(request.TryGetProperty("boosting_table_name", out _));
        }

        [Fact]
        public void BuildDoubaoShortPayload_WithBoostingTableName_ShouldInjectName()
        {
            string json = InvokeBuildPayload("app-1", "token-1", "cluster-1", "req-1", "", "church-short-hotwords");

            using var doc = JsonDocument.Parse(json);
            JsonElement request = doc.RootElement.GetProperty("request");
            Assert.Equal("church-short-hotwords", request.GetProperty("boosting_table_name").GetString());
            Assert.False(request.TryGetProperty("boosting_table_id", out _));
        }

        private static string InvokeBuildPayload(
            string appId,
            string token,
            string cluster,
            string requestId,
            string boostingTableId,
            string boostingTableName)
        {
            var method = typeof(BibleBaiduShortSpeechClient).GetMethod(
                "BuildDoubaoShortSpeechInitialPayloadJson",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);
            object result = method.Invoke(null, new object[]
            {
                appId,
                token,
                cluster,
                requestId,
                boostingTableId,
                boostingTableName
            });
            return Assert.IsType<string>(result);
        }
    }
}
