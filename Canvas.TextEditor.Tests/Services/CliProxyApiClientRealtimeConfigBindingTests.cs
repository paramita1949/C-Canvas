using System.IO;
using System.Reflection;
using ImageColorChanger.Core;
using ImageColorChanger.Services.LiveCaption;
using Xunit;

namespace ImageColorChanger.CanvasTextEditor.Tests.Services
{
    public sealed class CliProxyApiClientRealtimeConfigBindingTests
    {
        [Fact]
        public void Constructor_WhenUseRealtimeSettingsTrue_ShouldReadRealtimeProviderAndModel()
        {
            string tempFile = Path.Combine(Path.GetTempPath(), $"canvas_config_{System.Guid.NewGuid():N}.json");
            try
            {
                var config = new ConfigManager(tempFile);
                config.LiveCaptionAsrProvider = "doubao";
                config.LiveCaptionProxyBaseUrl = "http://legacy-proxy/v1";
                config.LiveCaptionAsrModel = "legacy-model";
                config.LiveCaptionBaiduDevPid = 80001;

                config.LiveCaptionRealtimeAsrProvider = "aliyun";
                config.LiveCaptionRealtimeProxyBaseUrl = "wss://realtime-aliyun/ws/v1";
                config.LiveCaptionRealtimeAsrModel = "qwen3-asr-flash";
                config.LiveCaptionRealtimeBaiduDevPid = 1936;
                config.LiveCaptionTencentCustomizationId = "legacy-custom";
                config.LiveCaptionTencentRealtimeCustomizationId = "realtime-custom";
                config.LiveCaptionTencentShortCustomizationId = "short-custom";

                using var client = new CliProxyApiClient(config, useRealtimeSettings: true);

                Assert.Equal("aliyun", client.AsrProvider);
                Assert.Equal("qwen3-asr-flash", client.AsrModel);
                Assert.Equal("wss://realtime-aliyun/ws/v1", ReadPrivateField<string>(client, "_baseUrl"));
                Assert.Equal(1936, ReadPrivateField<int>(client, "_baiduDevPid"));
                Assert.Equal("realtime-custom", ReadPrivateField<string>(client, "_tencentCustomizationId"));
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
        public void Constructor_WhenRealtimeProviderIsXfyun_ShouldReadXfyunRealtimeBinding()
        {
            string tempFile = Path.Combine(Path.GetTempPath(), $"canvas_config_{System.Guid.NewGuid():N}.json");
            try
            {
                var config = new ConfigManager(tempFile);
                config.LiveCaptionAsrProvider = "baidu";
                config.LiveCaptionProxyBaseUrl = "http://legacy-proxy/v1";
                config.LiveCaptionAsrModel = "legacy-model";
                config.LiveCaptionBaiduDevPid = 1537;

                config.LiveCaptionRealtimeAsrProvider = "xfyun";
                config.LiveCaptionRealtimeProxyBaseUrl = "wss://office-api-ast-dx.iflyaisol.com/ast/communicate/v1";
                config.LiveCaptionRealtimeAsrModel = "xfyun-rtasr-llm";

                using var client = new CliProxyApiClient(config, useRealtimeSettings: true);

                Assert.Equal("xfyun", client.AsrProvider);
                Assert.Equal("xfyun-rtasr-llm", client.AsrModel);
                Assert.Equal("wss://office-api-ast-dx.iflyaisol.com/ast/communicate/v1", ReadPrivateField<string>(client, "_baseUrl"));
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
        public void Constructor_WhenUseRealtimeSettingsFalse_ShouldKeepLegacyGlobalBinding()
        {
            string tempFile = Path.Combine(Path.GetTempPath(), $"canvas_config_{System.Guid.NewGuid():N}.json");
            try
            {
                var config = new ConfigManager(tempFile);
                config.LiveCaptionAsrProvider = "doubao";
                config.LiveCaptionProxyBaseUrl = "http://legacy-proxy/v1";
                config.LiveCaptionAsrModel = "legacy-model";
                config.LiveCaptionBaiduDevPid = 80001;

                config.LiveCaptionRealtimeAsrProvider = "aliyun";
                config.LiveCaptionRealtimeProxyBaseUrl = "wss://realtime-aliyun/ws/v1";
                config.LiveCaptionRealtimeAsrModel = "qwen3-asr-flash";
                config.LiveCaptionRealtimeBaiduDevPid = 1936;
                config.LiveCaptionTencentCustomizationId = "legacy-custom";
                config.LiveCaptionTencentRealtimeCustomizationId = "realtime-custom";
                config.LiveCaptionTencentShortCustomizationId = "short-custom";

                using var client = new CliProxyApiClient(config);

                Assert.Equal("doubao", client.AsrProvider);
                Assert.Equal("legacy-model", client.AsrModel);
                Assert.Equal("http://legacy-proxy/v1", ReadPrivateField<string>(client, "_baseUrl"));
                Assert.Equal(80001, ReadPrivateField<int>(client, "_baiduDevPid"));
                Assert.Equal("short-custom", ReadPrivateField<string>(client, "_tencentCustomizationId"));
            }
            finally
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
            }
        }

        private static T ReadPrivateField<T>(object instance, string fieldName)
        {
            var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            return (T)field.GetValue(instance);
        }
    }
}
