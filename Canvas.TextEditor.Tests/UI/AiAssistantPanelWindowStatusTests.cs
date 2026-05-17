using ImageColorChanger.UI;
using System.Text.RegularExpressions;

namespace ImageColorChanger.CanvasTextEditor.Tests.UI
{
    public sealed class AiAssistantPanelWindowStatusTests
    {
        [Theory]
        [InlineData("DeepSeek请求已发送，处理中…")]
        [InlineData("DeepSeek已返回结果。")]
        [InlineData("正在整理提示词…")]
        [InlineData("正在发送提示词…")]
        [InlineData("已收到反馈，正在生成摘要…")]
        [InlineData("已收到反馈，本次无摘要。")]
        [InlineData("反馈接收完成。")]
        [InlineData("已选择传道人：B")]
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
        public void BuildMessageHeader_AssistantDetailedStream_UsesVisibleTimestampAndLabel()
        {
            string header = AiAssistantPanelWindow.BuildMessageHeaderForTest("摘要");

            Assert.Matches(new Regex(@"^\d{2}:\d{2}:\d{2} \[摘要\]$"), header);
        }
    }
}
