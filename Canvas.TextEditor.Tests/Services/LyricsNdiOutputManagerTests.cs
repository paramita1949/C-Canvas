using ImageColorChanger.Core;
using ImageColorChanger.Services.Lyrics.Output;
using SkiaSharp;
using Xunit;

namespace ImageColorChanger.CanvasTextEditor.Tests.Services
{
    public sealed class LyricsNdiOutputManagerTests
    {
        [Fact]
        public void PublishFrame_WhenDisabled_DoesNotStartOrSend()
        {
            var config = new ConfigManagerForTest();
            config.LyricsNdiEnabled = false;
            var sender = new FakeLyricsNdiSender();
            var manager = new LyricsNdiOutputManager(config, sender);
            using var frame = new SKBitmap(8, 8);

            var result = manager.PublishFrame(frame);

            Assert.False(result);
            Assert.False(sender.StartCalled);
            Assert.False(sender.SendCalled);
        }

        [Fact]
        public void PublishFrame_WhenEnabled_StartsAndSends()
        {
            var config = new ConfigManagerForTest();
            config.LyricsNdiEnabled = true;
            config.LyricsNdiSenderName = "UnitTestSender";
            var sender = new FakeLyricsNdiSender();
            var manager = new LyricsNdiOutputManager(config, sender);
            using var frame = new SKBitmap(8, 8);

            var result = manager.PublishFrame(frame);

            Assert.True(result);
            Assert.True(sender.StartCalled);
            Assert.True(sender.SendCalled);
            Assert.Equal("UnitTestSender", sender.LastStartOptions?.SenderName);
        }

        private sealed class ConfigManagerForTest : ILyricsNdiConfigProvider
        {
            public bool LyricsNdiEnabled { get; set; }
            public string LyricsNdiSenderName { get; set; } = "CanvasCast-Lyrics";
            public int LyricsNdiWidth { get; set; } = 1920;
            public int LyricsNdiHeight { get; set; } = 1080;
            public int LyricsNdiFps { get; set; } = 30;
            public bool LyricsNdiPreferAlpha { get; set; } = true;
        }

        private sealed class FakeLyricsNdiSender : ILyricsNdiSender
        {
            public bool IsRunning { get; private set; }
            public bool StartCalled { get; private set; }
            public bool SendCalled { get; private set; }
            public LyricsNdiOutputOptions LastStartOptions { get; private set; }

            public bool Start(LyricsNdiOutputOptions options)
            {
                StartCalled = true;
                LastStartOptions = options;
                IsRunning = true;
                return true;
            }

            public bool SendFrame(SKBitmap frame)
            {
                SendCalled = true;
                return IsRunning && frame != null;
            }

            public void Stop()
            {
                IsRunning = false;
            }
        }
    }
}
