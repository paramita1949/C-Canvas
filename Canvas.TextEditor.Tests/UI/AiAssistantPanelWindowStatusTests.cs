using ImageColorChanger.UI;
using ImageColorChanger.Services.Ai;
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

        [Theory]
        [InlineData("ASR已连接")]
        [InlineData("ASR未启用")]
        public void ShouldAppendStatusToTimeline_KeepsAsrStatusAsInformationFlow(string status)
        {
            Assert.True(AiAssistantPanelWindow.ShouldAppendStatusToTimeline(status));
        }

        [Theory]
        [InlineData("AI已加入历史记录：希伯来书11章23节", "希伯来书11章23节")]
        [InlineData(" AI已加入历史记录：出埃及记2章1节 ", "出埃及记2章1节")]
        [InlineData("AI缓存：hit=1408, miss=664, 命中率=68%", "")]
        public void ExtractCollapsedScriptureText_ReturnsOnlyInsertedScripture(string status, string expected)
        {
            Assert.Equal(expected, AiAssistantPanelWindow.ExtractCollapsedScriptureText(status));
        }

        [Theory]
        [InlineData(false, false, "ASR未启用")]
        [InlineData(true, false, "ASR未启用")]
        [InlineData(false, true, "ASR未启用")]
        [InlineData(true, true, "ASR已连接")]
        public void BuildAsrConnectionStatus_RequiresAiReceiveAndRealtimeEngine(bool aiReceiveAsr, bool realtimeConnected, string expected)
        {
            Assert.Equal(expected, AiAssistantPanelWindow.BuildAsrConnectionStatus(aiReceiveAsr, realtimeConnected));
        }

        [Fact]
        public void BuildSessionSettlementText_ShowsOnlyCostAndEndTime()
        {
            string text = AiAssistantPanelWindow.BuildSessionSettlementTextForTest(new AiSermonSessionHistory
            {
                LastBalance = 99.25m,
                SessionCost = 0.75m,
                EndedAt = new System.DateTime(2026, 5, 24, 20, 30, 0)
            });

            Assert.Equal("消耗 0.75 · 结束 20:30", text);
            Assert.DoesNotContain("余额", text);
        }

        [Theory]
        [InlineData(false, false, false, "字幕", "打开 AI字幕 (F4)", false)]
        [InlineData(true, true, true, "字幕", "AI字幕运行中 (F4)", true)]
        [InlineData(true, true, false, "字幕", "AI字幕运行中 (F4)", true)]
        public void BuildAiCaptionButtonState_ReflectsRealtimeAndOverlayState(bool realtimeEnabled, bool engineRunning, bool overlayVisible, string text, string tooltip, bool active)
        {
            var state = MainWindow.BuildAiCaptionButtonStateForTest(realtimeEnabled, engineRunning, overlayVisible);

            Assert.Equal(text, state.Text);
            Assert.Equal(tooltip, state.ToolTip);
            Assert.Equal(active, state.IsActive);
        }

        [Fact]
        public void BuildMessageHeader_AssistantDetailedStream_UsesVisibleTimestampAndLabel()
        {
            string header = AiAssistantPanelWindow.BuildMessageHeaderForTest("摘要");

            Assert.Matches(new Regex(@"^\d{2}:\d{2}:\d{2} \[摘要\]$"), header);
        }
    }
}
