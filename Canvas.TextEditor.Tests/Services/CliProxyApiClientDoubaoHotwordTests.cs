using System.IO;
using System.Reflection;
using System.Text.Json;
using ImageColorChanger.Core;
using ImageColorChanger.Services.LiveCaption;
using Xunit;

namespace ImageColorChanger.CanvasTextEditor.Tests.Services
{
    public sealed class CliProxyApiClientDoubaoHotwordTests
    {
        [Fact]
        public void BuildDoubaoInitialPayload_WithBoostingTableId_ShouldInjectIdIntoCorpus()
        {
            string tempFile = Path.Combine(Path.GetTempPath(), $"canvas_config_{System.Guid.NewGuid():N}.json");
            try
            {
                var config = new ConfigManager(tempFile);
                config.LiveCaptionRealtimeAsrProvider = "doubao";
                config.LiveCaptionDoubaoBoostingTableId = "hotword-id-001";
                config.LiveCaptionDoubaoBoostingTableName = "table-name-should-not-use";

                using var client = new CliProxyApiClient(config, useRealtimeSettings: true);
                string json = InvokeBuildDoubaoInitialPayload(client);

                using var doc = JsonDocument.Parse(json);
                JsonElement request = doc.RootElement.GetProperty("request");
                Assert.True(request.TryGetProperty("corpus", out JsonElement corpus));
                Assert.Equal("hotword-id-001", corpus.GetProperty("boosting_table_id").GetString());
                Assert.False(corpus.TryGetProperty("boosting_table_name", out _));
                Assert.False(corpus.TryGetProperty("context", out _));
            }
            finally
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
            }
        }

        [Fact]
        public void BuildDoubaoInitialPayload_WithBoostingTableName_ShouldInjectName()
        {
            string tempFile = Path.Combine(Path.GetTempPath(), $"canvas_config_{System.Guid.NewGuid():N}.json");
            try
            {
                var config = new ConfigManager(tempFile);
                config.LiveCaptionRealtimeAsrProvider = "doubao";
                config.LiveCaptionDoubaoBoostingTableId = "";
                config.LiveCaptionDoubaoBoostingTableName = "church-hotwords";

                using var client = new CliProxyApiClient(config, useRealtimeSettings: true);
                string json = InvokeBuildDoubaoInitialPayload(client);

                using var doc = JsonDocument.Parse(json);
                JsonElement request = doc.RootElement.GetProperty("request");
                Assert.True(request.TryGetProperty("corpus", out JsonElement corpus));
                Assert.Equal("church-hotwords", corpus.GetProperty("boosting_table_name").GetString());
                Assert.False(corpus.TryGetProperty("boosting_table_id", out _));
                Assert.False(corpus.TryGetProperty("context", out _));
            }
            finally
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
            }
        }

        [Fact]
        public void BuildDoubaoInitialPayload_WithoutBoostingTable_ShouldKeepBuiltInCorpus()
        {
            string tempFile = Path.Combine(Path.GetTempPath(), $"canvas_config_{System.Guid.NewGuid():N}.json");
            try
            {
                var config = new ConfigManager(tempFile);
                config.LiveCaptionRealtimeAsrProvider = "doubao";
                config.LiveCaptionDoubaoBoostingTableId = "";
                config.LiveCaptionDoubaoBoostingTableName = "";

                using var client = new CliProxyApiClient(config, useRealtimeSettings: true);
                string json = InvokeBuildDoubaoInitialPayload(client);

                using var doc = JsonDocument.Parse(json);
                JsonElement request = doc.RootElement.GetProperty("request");
                Assert.True(request.TryGetProperty("corpus", out JsonElement corpus));
                Assert.True(corpus.TryGetProperty("context", out JsonElement context));
                Assert.False(string.IsNullOrWhiteSpace(context.GetString()));
            }
            finally
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
            }
        }

        private static string InvokeBuildDoubaoInitialPayload(CliProxyApiClient client)
        {
            var method = typeof(CliProxyApiClient).GetMethod(
                "BuildDoubaoInitialRequestPayloadJson",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);
            object result = method.Invoke(client, null);
            return Assert.IsType<string>(result);
        }
    }
}
