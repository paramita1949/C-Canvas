using System;
using System.IO;
using ImageColorChanger.Core;
using ImageColorChanger.Services.Ndi;
using Xunit;

namespace Canvas.TextEditor.Tests.Services
{
    public sealed class NdiTransportCoordinatorTests
    {
        [Fact]
        public void GetChannelConfig_DefaultSenderName_IncludesMachineSpecificBaseAndChannelLabel()
        {
            using var tempConfig = TempConfigFile.Create();
            var config = new ConfigManager(tempConfig.Path);
            var coordinator = new NdiTransportCoordinator(config, services: null, featureGate: null);

            NdiChannelOutputConfig channelConfig = coordinator.GetChannelConfig(NdiChannel.Slide);

            string expectedBase = $"YongMu-NDI-{Environment.MachineName}";
            Assert.Equal($"{expectedBase}-投影", channelConfig.SenderName);
            Assert.Contains(Environment.MachineName, channelConfig.SenderName);
            Assert.NotEqual("投影", channelConfig.SenderName);
        }

        [Fact]
        public void GetChannelConfig_CustomSenderName_PreservesBaseAndAddsChannelLabel()
        {
            using var tempConfig = TempConfigFile.Create();
            var config = new ConfigManager(tempConfig.Path)
            {
                ProjectionNdiSenderName = "礼拜堂A"
            };
            var coordinator = new NdiTransportCoordinator(config, services: null, featureGate: null);

            NdiChannelOutputConfig slideConfig = coordinator.GetChannelConfig(NdiChannel.Slide);
            NdiChannelOutputConfig captionConfig = coordinator.GetChannelConfig(NdiChannel.Caption);

            Assert.Equal("礼拜堂A-投影", slideConfig.SenderName);
            Assert.Equal("礼拜堂A-字幕", captionConfig.SenderName);
        }

        private sealed class TempConfigFile : IDisposable
        {
            private TempConfigFile(string path)
            {
                Path = path;
            }

            public string Path { get; }

            public static TempConfigFile Create()
            {
                string path = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(),
                    $"canvas_ndi_config_{Guid.NewGuid():N}.json");
                return new TempConfigFile(path);
            }

            public void Dispose()
            {
                if (File.Exists(Path))
                {
                    File.Delete(Path);
                }
            }
        }
    }
}
