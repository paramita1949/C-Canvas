using System.Text.Json;
using ImageColorChanger.Core;
using Xunit;

namespace ImageColorChanger.CanvasTextEditor.Tests.Core
{
    public sealed class LegacySplitImageDisplayModeJsonConverterTests
    {
        [Fact]
        public void AppConfig_LegacyBoolFalse_MapsToFitCenter()
        {
            var config = JsonSerializer.Deserialize<AppConfig>("{\"SplitStretchMode\":false}");
            Assert.Equal(SplitImageDisplayMode.FitCenter, config.SplitImageDisplayMode);
        }

        [Fact]
        public void AppConfig_LegacyBoolTrue_MapsToFill()
        {
            var config = JsonSerializer.Deserialize<AppConfig>("{\"SplitStretchMode\":true}");
            Assert.Equal(SplitImageDisplayMode.Fill, config.SplitImageDisplayMode);
        }

        [Fact]
        public void AppConfig_NumericTwo_MapsToFitTop()
        {
            var config = JsonSerializer.Deserialize<AppConfig>("{\"SplitStretchMode\":2}");
            Assert.Equal(SplitImageDisplayMode.FitTop, config.SplitImageDisplayMode);
        }
    }
}
