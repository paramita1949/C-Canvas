using System;
using ImageColorChanger.Services.LiveCaption;

namespace ImageColorChanger.CanvasTextEditor.Tests.Services
{
    public sealed class LiveCaptionIdleShutdownPolicyTests
    {
        [Theory]
        [InlineData(false, false)]
        [InlineData(true, false)]
        [InlineData(false, true)]
        [InlineData(true, true)]
        public void ShouldAutoDisable_WhenIdleNotExceeded_ReturnsFalse(bool realtimeEnabled, bool shortPhraseEnabled)
        {
            DateTime now = new(2026, 4, 24, 10, 0, 0, DateTimeKind.Utc);
            DateTime last = now.AddSeconds(-60);

            bool actual = LiveCaptionIdleShutdownPolicy.ShouldAutoDisable(
                last,
                now,
                realtimeEnabled,
                shortPhraseEnabled);

            bool expected = false;
            Assert.Equal(expected, actual);
        }

        [Theory]
        [InlineData(true, false)]
        [InlineData(false, true)]
        [InlineData(true, true)]
        public void ShouldAutoDisable_WhenIdleExceededAndRecognitionEnabled_ReturnsTrue(bool realtimeEnabled, bool shortPhraseEnabled)
        {
            DateTime now = new(2026, 4, 24, 10, 0, 0, DateTimeKind.Utc);
            DateTime last = now.AddSeconds(-61);

            bool actual = LiveCaptionIdleShutdownPolicy.ShouldAutoDisable(
                last,
                now,
                realtimeEnabled,
                shortPhraseEnabled);

            Assert.True(actual);
        }

        [Fact]
        public void ShouldAutoDisable_WhenBothRecognitionDisabled_ReturnsFalse()
        {
            DateTime now = new(2026, 4, 24, 10, 0, 0, DateTimeKind.Utc);
            DateTime last = now.AddHours(-1);

            bool actual = LiveCaptionIdleShutdownPolicy.ShouldAutoDisable(
                last,
                now,
                realtimeEnabled: false,
                shortPhraseEnabled: false);

            Assert.False(actual);
        }

        [Fact]
        public void ShouldAutoDisable_WhenLastRecognitionUnknown_ReturnsFalse()
        {
            DateTime now = new(2026, 4, 24, 10, 0, 0, DateTimeKind.Utc);

            bool actual = LiveCaptionIdleShutdownPolicy.ShouldAutoDisable(
                DateTime.MinValue,
                now,
                realtimeEnabled: true,
                shortPhraseEnabled: false);

            Assert.False(actual);
        }
    }
}
