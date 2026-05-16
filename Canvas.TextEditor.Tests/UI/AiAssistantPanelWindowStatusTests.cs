using ImageColorChanger.UI;

namespace ImageColorChanger.CanvasTextEditor.Tests.UI
{
    public sealed class AiAssistantPanelWindowStatusTests
    {
        [Theory]
        [InlineData("DeepSeek请求已发送，处理中…")]
        [InlineData("DeepSeek已返回结果。")]
        [InlineData("AI缓存：hit=1408, miss=664, 命中率=68%")]
        public void ShouldAppendStatusToTimeline_FiltersOperationalStatus(string status)
        {
            Assert.False(AiAssistantPanelWindow.ShouldAppendStatusToTimeline(status));
        }

        [Fact]
        public void ShouldAppendStatusToTimeline_KeepsUserMeaningfulStatus()
        {
            Assert.True(AiAssistantPanelWindow.ShouldAppendStatusToTimeline("已选择传道人：B"));
        }
    }
}
