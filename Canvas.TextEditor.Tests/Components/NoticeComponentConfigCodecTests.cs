using ImageColorChanger.Services.TextEditor.Components.Notice;
using Xunit;

namespace ImageColorChanger.CanvasTextEditor.Tests.Components
{
    public sealed class NoticeComponentConfigCodecTests
    {
        [Fact]
        public void Serialize_And_Deserialize_Should_Roundtrip()
        {
            var cfg = new NoticeComponentConfig
            {
                ScrollingEnabled = true,
                Position = NoticePosition.Center,
                Direction = NoticeDirection.LeftToRight,
                Speed = 50,
                DurationMinutes = 3,
                DefaultColorHex = "#0EA5E9",
                BackgroundOpacity = 35,
                BarHeight = 180,
                AutoClose = true,
                DebugEnabled = true
            };

            string json = NoticeComponentConfigCodec.Serialize(cfg);
            var parsed = NoticeComponentConfigCodec.Deserialize(json);

            Assert.Equal(cfg.ScrollingEnabled, parsed.ScrollingEnabled);
            Assert.Equal(cfg.Position, parsed.Position);
            Assert.Equal(cfg.Direction, parsed.Direction);
            Assert.Equal(cfg.Speed, parsed.Speed);
            Assert.Equal(cfg.DurationMinutes, parsed.DurationMinutes);
            Assert.Equal(cfg.DefaultColorHex, parsed.DefaultColorHex);
            Assert.Equal(cfg.BackgroundOpacity, parsed.BackgroundOpacity);
            Assert.Equal(cfg.BarHeight, parsed.BarHeight);
            Assert.Equal(cfg.AutoClose, parsed.AutoClose);
            Assert.Equal(cfg.DebugEnabled, parsed.DebugEnabled);
        }

        [Fact]
        public void Deserialize_InvalidJson_Should_ReturnDefaults()
        {
            var parsed = NoticeComponentConfigCodec.Deserialize("not-json");

            Assert.False(parsed.ScrollingEnabled);
            Assert.Equal(NoticePosition.Top, parsed.Position);
            Assert.Equal(NoticeDirection.LeftToRight, parsed.Direction);
            Assert.Equal(45, parsed.Speed);
            Assert.Equal(3, parsed.DurationMinutes);
            Assert.Equal(NoticeComponentConfig.DefaultNoticeColorHex, parsed.DefaultColorHex);
            Assert.Equal(0, parsed.BackgroundOpacity);
            Assert.Equal(120, parsed.BarHeight);
            Assert.True(parsed.AutoClose);
            Assert.False(parsed.DebugEnabled);
        }

        [Fact]
        public void Serialize_And_Deserialize_Should_KeepScrollingDisabled()
        {
            var cfg = new NoticeComponentConfig
            {
                ScrollingEnabled = false,
                Speed = 40,
                DurationMinutes = 5,
                DefaultColorHex = "#22C55E",
                AutoClose = true
            };

            string json = NoticeComponentConfigCodec.Serialize(cfg);
            var parsed = NoticeComponentConfigCodec.Deserialize(json);

            Assert.False(parsed.ScrollingEnabled);
            Assert.Equal("#22C55E", parsed.DefaultColorHex);
        }

        [Theory]
        [InlineData(-10, 0)]
        [InlineData(0, 0)]
        [InlineData(50, 50)]
        [InlineData(100, 100)]
        [InlineData(999, 100)]
        public void Normalize_Should_ClampSpeedToRange0To100(int input, int expected)
        {
            var cfg = new NoticeComponentConfig
            {
                Speed = input
            };

            var normalized = NoticeComponentConfigCodec.Normalize(cfg);

            Assert.Equal(expected, normalized.Speed);
        }

        [Theory]
        [InlineData(-2, 1)]
        [InlineData(0, 1)]
        [InlineData(1, 1)]
        [InlineData(10, 10)]
        [InlineData(99, 10)]
        public void Normalize_Should_ClampDurationToRange1To10(int input, int expected)
        {
            var cfg = new NoticeComponentConfig
            {
                DurationMinutes = input
            };

            var normalized = NoticeComponentConfigCodec.Normalize(cfg);

            Assert.Equal(expected, normalized.DurationMinutes);
        }

        [Theory]
        [InlineData(null, "#FF8A00")]
        [InlineData("", "#FF8A00")]
        [InlineData("0EA5E9", "#0EA5E9")]
        [InlineData("#22c55e", "#22C55E")]
        [InlineData("#XYZ", "#FF8A00")]
        public void Normalize_Should_NormalizeDefaultColor(string input, string expected)
        {
            var cfg = new NoticeComponentConfig
            {
                DefaultColorHex = input
            };

            var normalized = NoticeComponentConfigCodec.Normalize(cfg);
            Assert.Equal(expected, normalized.DefaultColorHex);
        }

        [Fact]
        public void Normalize_Should_FallbackToTop_WhenPositionOutOfRange()
        {
            var cfg = new NoticeComponentConfig
            {
                Position = (NoticePosition)999
            };

            var normalized = NoticeComponentConfigCodec.Normalize(cfg);
            Assert.Equal(NoticePosition.Top, normalized.Position);
        }

        [Fact]
        public void Normalize_Should_KeepPingPongDirection_WhenDirectionIsBounceMode()
        {
            var cfg = new NoticeComponentConfig
            {
                Direction = NoticeDirection.PingPong
            };

            var normalized = NoticeComponentConfigCodec.Normalize(cfg);
            Assert.Equal(NoticeDirection.PingPong, normalized.Direction);
        }

        [Theory]
        [InlineData(-10, 0)]
        [InlineData(0, 0)]
        [InlineData(20, 20)]
        [InlineData(100, 100)]
        [InlineData(999, 100)]
        public void Normalize_Should_ClampBackgroundOpacityToRange0To100(int input, int expected)
        {
            var cfg = new NoticeComponentConfig
            {
                BackgroundOpacity = input
            };

            var normalized = NoticeComponentConfigCodec.Normalize(cfg);

            Assert.Equal(expected, normalized.BackgroundOpacity);
        }

        [Theory]
        [InlineData(10, 40)]
        [InlineData(40, 40)]
        [InlineData(120, 120)]
        [InlineData(320, 320)]
        [InlineData(999, 320)]
        public void Normalize_Should_ClampBarHeight(double input, double expected)
        {
            var cfg = new NoticeComponentConfig
            {
                BarHeight = input
            };

            var normalized = NoticeComponentConfigCodec.Normalize(cfg);
            Assert.Equal(expected, normalized.BarHeight);
        }

        [Theory]
        [InlineData(1, 120)]
        [InlineData(2, 160)]
        [InlineData(3, 200)]
        [InlineData(4, 240)]
        public void GetBarHeightByLevel_Should_MapExpectedPreset(int level, double expectedHeight)
        {
            Assert.Equal(expectedHeight, NoticeComponentConfig.GetBarHeightByLevel(level));
        }

        [Theory]
        [InlineData(40, 1)]
        [InlineData(120, 1)]
        [InlineData(159, 2)]
        [InlineData(161, 2)]
        [InlineData(210, 3)]
        [InlineData(320, 4)]
        public void GetBarHeightLevel_Should_ReturnNearestPreset(double height, int expectedLevel)
        {
            Assert.Equal(expectedLevel, NoticeComponentConfig.GetBarHeightLevel(height));
        }

        [Theory]
        [InlineData(1, 20)]
        [InlineData(2, 35)]
        [InlineData(3, 50)]
        [InlineData(4, 70)]
        [InlineData(5, 90)]
        [InlineData(999, 50)]
        public void GetSpeedByLevel_Should_MapExpectedPreset(int level, int expectedSpeed)
        {
            Assert.Equal(expectedSpeed, NoticeComponentConfig.GetSpeedByLevel(level));
        }

        [Theory]
        [InlineData(0, 1)]
        [InlineData(20, 1)]
        [InlineData(34, 2)]
        [InlineData(50, 3)]
        [InlineData(71, 4)]
        [InlineData(100, 5)]
        public void GetSpeedLevel_Should_ReturnNearestPreset(int speed, int expectedLevel)
        {
            Assert.Equal(expectedLevel, NoticeComponentConfig.GetSpeedLevel(speed));
        }
    }
}
