using System.IO;
using ImageColorChanger.Core;
using ImageColorChanger.Services.Ndi;
using Xunit;

namespace Canvas.TextEditor.Tests.Services
{
    public sealed class NdiTransportCoordinatorTests
    {
        [Fact]
        public void GetChannelConfig_ConfiguredDeviceCode_UsesShortChannelNameAndStableSuffix()
        {
            using var tempConfig = TempConfigFile.Create();
            File.WriteAllText(tempConfig.Path, """
            {
              "ProjectionNdiDeviceCode": "A7F3",
              "ProjectionNdiSenderName": "YongMu-NDI-同名电脑"
            }
            """);

            var config = new ConfigManager(tempConfig.Path);
            var coordinator = new NdiTransportCoordinator(config, services: null, featureGate: null);

            NdiChannelOutputConfig slideConfig = coordinator.GetChannelConfig(NdiChannel.Slide);
            NdiChannelOutputConfig captionConfig = coordinator.GetChannelConfig(NdiChannel.Caption);

            Assert.Equal("投影-A7F3", slideConfig.SenderName);
            Assert.Equal("字幕-A7F3", captionConfig.SenderName);
            Assert.DoesNotContain("YongMu-NDI", slideConfig.SenderName);
            Assert.DoesNotContain("同名电脑", slideConfig.SenderName);
        }

        [Fact]
        public void GetChannelConfig_MissingDeviceCode_CreatesAndPersistsStableSuffix()
        {
            using var tempConfig = TempConfigFile.Create();
            var config = new ConfigManager(tempConfig.Path);
            var coordinator = new NdiTransportCoordinator(config, services: null, featureGate: null);

            NdiChannelOutputConfig slideConfig = coordinator.GetChannelConfig(NdiChannel.Slide);
            string deviceCode = ExtractDeviceCode(slideConfig.SenderName);

            Assert.Matches(@"^投影-[A-Z0-9]{4}$", slideConfig.SenderName);
            Assert.NotEmpty(deviceCode);

            var reloadedConfig = new ConfigManager(tempConfig.Path);
            var reloadedCoordinator = new NdiTransportCoordinator(reloadedConfig, services: null, featureGate: null);
            NdiChannelOutputConfig captionConfig = reloadedCoordinator.GetChannelConfig(NdiChannel.Caption);

            Assert.Equal($"字幕-{deviceCode}", captionConfig.SenderName);
        }

        private static string ExtractDeviceCode(string senderName)
        {
            int dashIndex = senderName.LastIndexOf('-');
            return dashIndex >= 0 && dashIndex < senderName.Length - 1
                ? senderName[(dashIndex + 1)..]
                : string.Empty;
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
