using ImageColorChanger.Core;
using Xunit;

namespace ImageColorChanger.CanvasTextEditor.Tests.Core
{
    public sealed class SplitImageDisplayModePreferenceTests
    {
        [Theory]
        [InlineData(SplitImageDisplayMode.FitCenter)]
        [InlineData(SplitImageDisplayMode.Fill)]
        [InlineData(SplitImageDisplayMode.FitTop)]
        public void ResolveInitialPreference_KeepsValidValues(SplitImageDisplayMode mode)
        {
            var actual = SplitImageDisplayModePreference.ResolveInitialPreference(mode);
            Assert.Equal(mode, actual);
        }

        [Fact]
        public void ResolveInitialPreference_InvalidValueFallsBackToFitTop()
        {
            var actual = SplitImageDisplayModePreference.ResolveInitialPreference((SplitImageDisplayMode)99);
            Assert.Equal(SplitImageDisplayMode.FitTop, actual);
        }

        [Fact]
        public void ResolveSlideMode_InvalidSlideValueFallsBackToPreference()
        {
            var actual = SplitImageDisplayModePreference.ResolveSlideMode(
                (SplitImageDisplayMode)(-1),
                SplitImageDisplayMode.Fill);

            Assert.Equal(SplitImageDisplayMode.Fill, actual);
        }
    }
}
