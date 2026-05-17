using ImageColorChanger.UI;
using System.Text.RegularExpressions;

namespace ImageColorChanger.CanvasTextEditor.Tests.UI
{
    public sealed class AiAssistantPanelWindowStatusTests
    {
        [Theory]
        [InlineData("DeepSeek请求已发送，处理中…")]
        [InlineData("DeepSeek已返回结果。")]
        public void ShouldAppendStatusToTimeline_FiltersOperationalStatus(string status)
        {
            Assert.False(AiAssistantPanelWindow.ShouldAppendStatusToTimeline(status));
        }

        [Fact]
        public void ShouldAppendStatusToTimeline_KeepsCacheStatusForOperatorVisibility()
        {
            Assert.True(AiAssistantPanelWindow.ShouldAppendStatusToTimeline("AI缓存：hit=1408, miss=664, 命中率=68%"));
        }

        [Fact]
        public void ShouldAppendStatusToTimeline_KeepsUserMeaningfulStatus()
        {
            Assert.True(AiAssistantPanelWindow.ShouldAppendStatusToTimeline("已选择传道人：B"));
        }

        [Fact]
        public void BuildMessageHeader_AssistantDetailedStream_UsesVisibleTimestampAndLabel()
        {
            string header = AiAssistantPanelWindow.BuildMessageHeaderForTest("摘要");

            Assert.Matches(new Regex(@"^\d{2}:\d{2}:\d{2} \[摘要\]$"), header);
        }
    }
}
