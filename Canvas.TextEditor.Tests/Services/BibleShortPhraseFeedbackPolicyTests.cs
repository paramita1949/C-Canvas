using ImageColorChanger.Services.LiveCaption;

namespace ImageColorChanger.CanvasTextEditor.Tests.Services
{
    public sealed class BibleShortPhraseFeedbackPolicyTests
    {
        [Fact]
        public void BuildStatusMessage_WhenSuccess_ReturnsSuccessMessage()
        {
            var result = new BibleShortPhraseConsumer.Result
            {
                Success = true,
                RecognizedText = "约翰福音三章十六节"
            };

            string message = BibleShortPhraseFeedbackPolicy.BuildStatusMessage(result);
            Assert.Equal("经文识别成功：约翰福音三章十六节", message);
        }

        [Theory]
        [InlineData("audio-too-short", "经文识别中：语音过短，请稍后再试")]
        [InlineData("empty-transcript", "经文识别中：暂未识别到清晰语音")]
        [InlineData("unresolved-reference", "经文识别中：已识别语音，但未匹配到经文")]
        [InlineData("busy", "经文识别中：正在处理上一段语音")]
        [InlineData("not-running", "经文识别未运行")]
        [InlineData("other", "经文识别中：本轮未得到有效结果")]
        public void BuildStatusMessage_WhenFailure_ReturnsMappedMessage(string reason, string expected)
        {
            var result = new BibleShortPhraseConsumer.Result
            {
                Success = false,
                FailureReason = reason
            };

            string message = BibleShortPhraseFeedbackPolicy.BuildStatusMessage(result);
            Assert.Equal(expected, message);
        }
    }
}
