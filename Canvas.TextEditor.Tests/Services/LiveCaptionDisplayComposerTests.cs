using ImageColorChanger.Services.LiveCaption;

namespace ImageColorChanger.CanvasTextEditor.Tests.Services
{
    public sealed class LiveCaptionDisplayComposerTests
    {
        [Fact]
        public void Push_InterimShrink_DoesNotRewriteToShorterText()
        {
            var composer = new LiveCaptionDisplayComposer(lineCharLimit: 20, displayLineLimit: 2);

            var first = composer.Push(new LiveCaptionAsrText("今天我们一起学习系统化调试", isFinal: false));
            var shrink = composer.Push(new LiveCaptionAsrText("今天我们一起学习", isFinal: false));

            Assert.True(first.HasChanged);
            Assert.False(shrink.HasChanged);
            Assert.Equal(first.Display, composer.CurrentDisplay);
        }

        [Fact]
        public void Push_FinalThenInterimAppend_HighlightStartsFromNewTail()
        {
            var composer = new LiveCaptionDisplayComposer(lineCharLimit: 30, displayLineLimit: 2);

            composer.Push(new LiveCaptionAsrText("第一句已经确认。", isFinal: true));
            var frame = composer.Push(new LiveCaptionAsrText("第二句正在输出", isFinal: false));

            Assert.True(frame.HasChanged);
            Assert.Contains("第一句已经确认。第二句正在输出", frame.Display.Replace("\r\n", string.Empty));
            Assert.True(frame.HighlightStart > 0);
            Assert.True(frame.HighlightStart <= frame.Display.Length);
        }

        [Fact]
        public void Push_WhenOverflow_KeepsOnlyLastTwoLinesWindow()
        {
            var composer = new LiveCaptionDisplayComposer(lineCharLimit: 7, displayLineLimit: 2, maxSourceChars: 120);

            composer.Push(new LiveCaptionAsrText("AAAAAA。", isFinal: true));
            composer.Push(new LiveCaptionAsrText("BBBBBB。", isFinal: true));
            composer.Push(new LiveCaptionAsrText("CCCCCC。", isFinal: true));

            string display = composer.CurrentDisplay.Replace("\r\n", "\n");
            string[] lines = display.Split('\n');

            Assert.Equal(2, lines.Length);
            Assert.DoesNotContain("AAAAAA", display);
            Assert.Contains("CCCC", display);
        }

        [Fact]
        public void Push_DuplicateFinal_IsIgnored()
        {
            var composer = new LiveCaptionDisplayComposer();

            var first = composer.Push(new LiveCaptionAsrText("重复句子。", isFinal: true));
            var second = composer.Push(new LiveCaptionAsrText("重复句子。", isFinal: true));

            Assert.True(first.HasChanged);
            Assert.False(second.HasChanged);
            Assert.Equal(first.Display, composer.CurrentDisplay);
        }
    }
}
