using ImageColorChanger.Services.LiveCaption;

namespace ImageColorChanger.CanvasTextEditor.Tests.Services
{
    public sealed class LiveCaptionEntryPolicyTests
    {
        [Theory]
        [InlineData(true, 0)]
        [InlineData(false, 1)]
        public void ResolveTopBarEntryAction_ReturnsExpected(bool isEngineRunning, int expectedAction)
        {
            var actual = LiveCaptionEntryPolicy.ResolveTopBarEntryAction(isEngineRunning);
            Assert.Equal((LiveCaptionEntryAction)expectedAction, actual);
        }
    }
}
