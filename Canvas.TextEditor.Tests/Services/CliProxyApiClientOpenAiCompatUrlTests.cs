using System.IO;
using System.Reflection;
using ImageColorChanger.Core;
using ImageColorChanger.Services.LiveCaption;
using Xunit;

namespace ImageColorChanger.CanvasTextEditor.Tests.Services
{
    public sealed class CliProxyApiClientOpenAiCompatUrlTests
    {
        [Fact]
        public void BuildCandidateUrls_WhenBaseUrlIsFullTranscriptionEndpoint_ShouldNotDuplicatePath()
        {
            string tempFile = Path.Combine(Path.GetTempPath(), $"canvas_config_{System.Guid.NewGuid():N}.json");
            try
            {
                var config = new ConfigManager(tempFile);
                config.LiveCaptionRealtimeAsrProvider = "siliconflow";
                config.LiveCaptionRealtimeProxyBaseUrl = "https://api.siliconflow.cn/v1/audio/transcriptions";
                config.LiveCaptionSiliconFlowApiKey = "test-key";

                using var client = new CliProxyApiClient(config, useRealtimeSettings: true);
                string[] urls = InvokeBuildCandidateUrls(client, "audio/transcriptions");

                Assert.Contains("https://api.siliconflow.cn/v1/audio/transcriptions", urls);
                Assert.DoesNotContain(urls, u => u.Contains("/audio/transcriptions/audio/transcriptions"));
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
        public void BuildCandidateUrls_WhenBaseUrlEndsWithV1_ShouldKeepDualFallbackPattern()
        {
            string tempFile = Path.Combine(Path.GetTempPath(), $"canvas_config_{System.Guid.NewGuid():N}.json");
            try
            {
                var config = new ConfigManager(tempFile);
                config.LiveCaptionRealtimeAsrProvider = "siliconflow";
                config.LiveCaptionRealtimeProxyBaseUrl = "https://api.siliconflow.cn/v1";
                config.LiveCaptionSiliconFlowApiKey = "test-key";

                using var client = new CliProxyApiClient(config, useRealtimeSettings: true);
                string[] urls = InvokeBuildCandidateUrls(client, "audio/transcriptions");

                Assert.Equal(2, urls.Length);
                Assert.Equal("https://api.siliconflow.cn/v1/audio/transcriptions", urls[0]);
                Assert.Equal("https://api.siliconflow.cn/audio/transcriptions", urls[1]);
            }
            finally
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
            }
        }

        private static string[] InvokeBuildCandidateUrls(CliProxyApiClient client, string path)
        {
            var method = typeof(CliProxyApiClient).GetMethod(
                "BuildCandidateUrls",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);
            return (string[])method.Invoke(client, new object[] { path });
        }
    }
}
