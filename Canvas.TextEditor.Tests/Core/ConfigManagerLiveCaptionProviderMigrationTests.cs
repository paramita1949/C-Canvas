using System;
using System.IO;
using ImageColorChanger.Core;
using Xunit;

namespace ImageColorChanger.CanvasTextEditor.Tests.Core
{
    public sealed class ConfigManagerLiveCaptionProviderMigrationTests
    {
        [Fact]
        public void LegacyRealtimeAliyunConfig_ShouldFallbackToXfyunProviderAndDefaultWsUrl()
        {
            string tempFile = Path.Combine(Path.GetTempPath(), $"canvas_config_{Guid.NewGuid():N}.json");
            try
            {
                var config = new ConfigManager(tempFile);
                config.LiveCaptionRealtimeAsrProvider = "aliyun";
                config.LiveCaptionRealtimeProxyBaseUrl = "wss://nls-gateway-cn-shanghai.aliyuncs.com/ws/v1";

                Assert.Equal("xfyun", config.LiveCaptionRealtimeAsrProvider);
                Assert.Equal("wss://office-api-ast-dx.iflyaisol.com/ast/communicate/v1", config.LiveCaptionRealtimeProxyBaseUrl);
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
        public void LegacyShortTencentConfig_ShouldFallbackToXfyunProviderAndDefaultWsUrl()
        {
            string tempFile = Path.Combine(Path.GetTempPath(), $"canvas_config_{Guid.NewGuid():N}.json");
            try
            {
                var config = new ConfigManager(tempFile);
                config.LiveCaptionShortAsrProvider = "tencent";
                config.LiveCaptionShortProxyBaseUrl = "https://asr.tencentcloudapi.com";

                Assert.Equal("xfyun", config.LiveCaptionShortAsrProvider);
                Assert.Equal("wss://iat.xf-yun.com/v1", config.LiveCaptionShortProxyBaseUrl);
            }
            finally
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
            }
        }
    }
}
