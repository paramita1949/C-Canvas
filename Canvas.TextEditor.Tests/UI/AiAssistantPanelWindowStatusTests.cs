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
        [InlineData(false, false, false, true, null, "字幕", "打开 AI字幕 (F4)", false, true)]
        [InlineData(true, true, true, true, null, "字幕", "AI字幕运行中 (F4)", true, true)]
        [InlineData(true, true, false, true, null, "字幕", "AI字幕运行中 (F4)", true, true)]
        [InlineData(false, false, false, false, "功能未开通", "字幕", "功能未开通", false, false)]
        public void BuildAiCaptionButtonState_ReflectsRealtimeAndAuthorizationState(
            bool realtimeEnabled,
            bool engineRunning,
            bool overlayVisible,
            bool featureAllowed,
            string deniedTooltip,
            string text,
            string tooltip,
            bool active,
            bool enabled)
        {
            var state = MainWindow.BuildAiCaptionButtonStateForTest(
                realtimeEnabled,
                engineRunning,
                overlayVisible,
                featureAllowed,
                deniedTooltip);

            Assert.Equal(text, state.Text);
            Assert.Equal(tooltip, state.ToolTip);
            Assert.Equal(active, state.IsActive);
            Assert.Equal(enabled, state.IsEnabled);
        }

        [Theory]
        [InlineData(false, false, false)]
        [InlineData(true, false, true)]
        [InlineData(false, true, true)]
        [InlineData(true, true, true)]
        public void BuildAiPlatformMenuState_IsEnabledWhenAnyAiEntryIsAvailable(
            bool aiPanelAllowed,
            bool aiCaptionAllowed,
            bool expectedEnabled)
        {
            var state = MainWindow.BuildAiPlatformMenuStateForTest(
                aiPanelAllowed,
                "AI面板不可用",
                aiCaptionAllowed,
                "AI字幕不可用");

            Assert.Equal(expectedEnabled, state.IsEnabled);
            Assert.Equal(expectedEnabled ? null : "功能未开通", state.ToolTip);
        }

        [Theory]
        [InlineData(null, false, false)]
        [InlineData(null, true, false)]
        [InlineData(false, false, false)]
        [InlineData(false, true, true)]
        [InlineData(true, false, true)]
        [InlineData(true, true, false)]
        public void ShouldReloadSlidesProjectTreeForTest_ReloadsOnlyWhenKnownAuthorizationStateChanges(
            bool? previousAllowed,
            bool currentAllowed,
            bool expected)
        {
            Assert.Equal(expected, MainWindow.ShouldReloadSlidesProjectTreeForTest(previousAllowed, currentAllowed));
        }

        [Fact]
        public void BuildMessageHeader_AssistantDetailedStream_UsesVisibleTimestampAndLabel()
        {
            string header = AiAssistantPanelWindow.BuildMessageHeaderForTest("摘要");

            Assert.Matches(new Regex(@"^\d{2}:\d{2}:\d{2} \[摘要\]$"), header);
        }

        [Fact]
        public void BuildProjectionVersePreviewManualContextForTest_UsesCompactReferenceExpression()
        {
            string text = MainWindow.BuildProjectionVersePreviewManualContextForTest(
                "马太福音",
                1,
                12,
                15);

            Assert.Equal("马太福音1 12 15", text);
        }
    }
}
