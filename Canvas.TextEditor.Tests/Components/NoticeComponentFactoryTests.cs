using ImageColorChanger.Services.TextEditor.Components.Notice;
using Xunit;

namespace ImageColorChanger.CanvasTextEditor.Tests.Components
{
    public sealed class NoticeComponentFactoryTests
    {
        [Fact]
        public void BuildDefault_Should_CreateTopNoticeElement()
        {
            var element = NoticeComponentFactory.BuildDefault(
                slideId: 1,
                canvasWidth: 1600,
                canvasHeight: 900,
                zIndex: 8,
                defaultTextColor: "#FFFFFF");

            Assert.Equal(0, element.X);
            Assert.Equal(0, element.Y);
            Assert.Equal(1600, element.Width);
            Assert.Equal("Notice", element.ComponentType);
            Assert.Equal("#FF8A00", element.BackgroundColor);
            Assert.Equal(NoticeComponentFactory.DefaultNoticeBackgroundOpacity, element.BackgroundOpacity);
            Assert.Equal(120, element.Height);
            Assert.Equal(8, element.ZIndex);
            Assert.False(string.IsNullOrWhiteSpace(element.ComponentConfigJson));

            var cfg = NoticeComponentConfigCodec.Deserialize(element.ComponentConfigJson);
            Assert.Equal(NoticePosition.Top, cfg.Position);
            Assert.Equal(NoticeDirection.LeftToRight, cfg.Direction);
            Assert.Equal(45, cfg.Speed);
            Assert.Equal("#FF8A00", cfg.DefaultColorHex);
            Assert.Equal(NoticeComponentFactory.DefaultNoticeBackgroundOpacity, cfg.BackgroundOpacity);
            Assert.Equal(120, cfg.BarHeight);
        }

        [Fact]
        public void BuildDefault_Should_ApplyProvidedDefaultConfig()
        {
            var preset = new NoticeComponentConfig
            {
                ScrollingEnabled = true,
                Position = NoticePosition.Bottom,
                Direction = NoticeDirection.PingPong,
                Speed = 33,
                DurationMinutes = 8,
                DefaultColorHex = "#22C55E",
                BackgroundOpacity = 66,
                BarHeight = 200,
                AutoClose = false
            };

            var element = NoticeComponentFactory.BuildDefault(
                slideId: 7,
                canvasWidth: 1600,
                canvasHeight: 900,
                zIndex: 2,
                defaultTextColor: "#FFFFFF",
                defaultConfig: preset);

            Assert.Equal(0, element.X);
            Assert.Equal(700, element.Y);
            Assert.Equal(200, element.Height);
            Assert.Equal("#22C55E", element.BackgroundColor);
            Assert.Equal(66, element.BackgroundOpacity);

            var cfg = NoticeComponentConfigCodec.Deserialize(element.ComponentConfigJson);
            Assert.Equal(NoticePosition.Bottom, cfg.Position);
            Assert.Equal(NoticeDirection.PingPong, cfg.Direction);
            Assert.Equal(33, cfg.Speed);
            Assert.Equal(8, cfg.DurationMinutes);
            Assert.Equal("#22C55E", cfg.DefaultColorHex);
            Assert.Equal(66, cfg.BackgroundOpacity);
            Assert.Equal(200, cfg.BarHeight);
            Assert.False(cfg.ScrollingEnabled);
            Assert.False(cfg.AutoClose);
        }
    }
}
