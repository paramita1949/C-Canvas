using ImageColorChanger.Services.LiveCaption;

namespace ImageColorChanger.CanvasTextEditor.Tests.Services
{
    public sealed class LiveCaptionInputSnapshotComposerTests
    {
        [Fact]
        public void PushInputSnapshot_Append_UsesCompleteTextAndHighlightsNewTail()
        {
            var composer = new LiveCaptionDisplayComposer(lineCharLimit: 20);

            var first = composer.PushInputSnapshot("今天我们");
            var appended = composer.PushInputSnapshot("今天我们一起学习");

            Assert.True(first.HasChanged);
            Assert.Equal(first.Display.Length, first.HighlightStart);
            Assert.True(appended.HasChanged);
            Assert.Equal("今天我们一起学习", appended.Display);
            Assert.Equal("今天我们".Length, appended.HighlightStart);
            Assert.Equal(appended.Display, composer.CurrentDisplay);
        }

        [Fact]
        public void PushInputSnapshot_DeleteTail_AcceptsShorterText()
        {
            var composer = new LiveCaptionDisplayComposer(lineCharLimit: 20);
            composer.PushInputSnapshot("今天我们一起学习系统化调试");

            var frame = composer.PushInputSnapshot("今天我们一起学习");

            Assert.True(frame.HasChanged);
            Assert.Equal("今天我们一起学习", frame.Display);
            Assert.Equal(frame.Display.Length, frame.HighlightStart);
        }

        [Theory]
        [InlineData("今天一起学习", "今天共同学习")]
        [InlineData("今天我们一起学习系统化调试", "雨")]
        public void PushInputSnapshot_ReplaceText_DiscardsPreviousSnapshot(string previous, string replacement)
        {
            var composer = new LiveCaptionDisplayComposer(lineCharLimit: 30);
            composer.PushInputSnapshot(previous);

            var frame = composer.PushInputSnapshot(replacement);

            Assert.True(frame.HasChanged);
            Assert.Equal(replacement, frame.Display);
        }

        [Fact]
        public void PushInputSnapshot_RepeatedPhrase_PreservesEveryOccurrence()
        {
            var composer = new LiveCaptionDisplayComposer(lineCharLimit: 30);
            composer.PushInputSnapshot("你好你好");

            var frame = composer.PushInputSnapshot("你好你好你好");

            Assert.True(frame.HasChanged);
            Assert.Equal("你好你好你好", frame.Display);
        }

        [Theory]
        [InlineData("")]
        [InlineData(" \r\n\t ")]
        [InlineData(null)]
        public void PushInputSnapshot_Empty_ClearsDisplayAndHighlight(string empty)
        {
            var composer = new LiveCaptionDisplayComposer();
            composer.PushInputSnapshot("需要删除的内容");

            var cleared = composer.PushInputSnapshot(empty);
            var unchanged = composer.PushInputSnapshot(empty);

            Assert.True(cleared.HasChanged);
            Assert.Equal(string.Empty, cleared.Display);
            Assert.Equal(0, cleared.HighlightStart);
            Assert.Equal(string.Empty, composer.CurrentDisplay);
            Assert.Equal(0, composer.CurrentHighlightStart);
            Assert.False(unchanged.HasChanged);
        }

        [Fact]
        public void PushInputSnapshot_AfterClear_CanReuseSamePhrase()
        {
            var composer = new LiveCaptionDisplayComposer();
            composer.PushInputSnapshot("同一句话");
            composer.PushInputSnapshot(string.Empty);

            var frame = composer.PushInputSnapshot("同一句话");

            Assert.True(frame.HasChanged);
            Assert.Equal("同一句话", frame.Display);
        }

        [Fact]
        public void PushInputSnapshot_Overflow_MovesWindowByWholeLines()
        {
            var composer = new LiveCaptionDisplayComposer(lineCharLimit: 4, displayLineLimit: 2);

            var full = composer.PushInputSnapshot("AAAABBBBCCCC");
            var nextLine = composer.PushInputSnapshot("AAAABBBBCCCCD");
            var appended = composer.PushInputSnapshot("AAAABBBBCCCCDE");

            Assert.Equal($"BBBB{Environment.NewLine}CCCC", full.Display);
            Assert.Equal($"CCCC{Environment.NewLine}D", nextLine.Display);
            Assert.Equal($"CCCC{Environment.NewLine}DE", appended.Display);
        }

        [Fact]
        public void PushInputSnapshot_ExceedsSourceLimit_PreservesWholeLineAlignment()
        {
            var composer = new LiveCaptionDisplayComposer(lineCharLimit: 8, displayLineLimit: 2, maxSourceChars: 64);
            string snapshot = new string(Enumerable.Range(0, 80).Select(index => (char)('\u4e00' + index)).ToArray());

            var full = composer.PushInputSnapshot(snapshot);
            var appended = composer.PushInputSnapshot(snapshot + "甲");

            Assert.Equal(snapshot.Substring(64, 8) + Environment.NewLine + snapshot.Substring(72), full.Display);
            Assert.Equal(snapshot.Substring(72) + Environment.NewLine + "甲", appended.Display);
        }

        [Fact]
        public void PushInputSnapshot_DeleteOverflow_RestoresEarlierSnapshotText()
        {
            var composer = new LiveCaptionDisplayComposer(lineCharLimit: 4, displayLineLimit: 2);
            composer.PushInputSnapshot("AAAABBBBCCCC");

            var frame = composer.PushInputSnapshot("AAAABB");

            Assert.True(frame.HasChanged);
            Assert.Equal($"AAAA{Environment.NewLine}BB", frame.Display);
        }

        [Fact]
        public void PushInputSnapshot_LayoutWidthChanges_RendersUnchangedSnapshotAgain()
        {
            var composer = new LiveCaptionDisplayComposer(lineCharLimit: 4, displayLineLimit: 2);
            const string snapshot = "abcdefghijkl";
            var before = composer.PushInputSnapshot(snapshot);

            composer.SetLineCharLimit(6);
            var after = composer.PushInputSnapshot(snapshot);

            Assert.Equal($"efgh{Environment.NewLine}ijkl", before.Display);
            Assert.True(after.HasChanged);
            Assert.Equal($"abcdef{Environment.NewLine}ghijkl", after.Display);
        }

        [Fact]
        public void PushInputSnapshot_UnchangedDisplay_DoesNotNotifyOrChangeHighlight()
        {
            var composer = new LiveCaptionDisplayComposer(lineCharLimit: 20);
            composer.PushInputSnapshot("今天");
            var appended = composer.PushInputSnapshot("今天学习");

            var repeated = composer.PushInputSnapshot("今天学习");

            Assert.False(repeated.HasChanged);
            Assert.Equal(appended.Display, repeated.Display);
            Assert.Equal(appended.HighlightStart, repeated.HighlightStart);
        }

        [Fact]
        public void PushInputSnapshot_NormalizesDisplayWhitespace()
        {
            var composer = new LiveCaptionDisplayComposer(lineCharLimit: 30);

            var frame = composer.PushInputSnapshot("  第一行\r\n\t 第二行  ");

            Assert.Equal("第一行 第二行", frame.Display);
        }

        [Fact]
        public void Reset_BetweenSources_DiscardsPreviousSourceText()
        {
            var composer = new LiveCaptionDisplayComposer(lineCharLimit: 30);
            composer.Push(new LiveCaptionAsrText("语音内容", isFinal: true));
            composer.Reset();

            var input = composer.PushInputSnapshot("输入内容");
            Assert.Equal("输入内容", input.Display);

            composer.Reset();
            var asr = composer.Push(new LiveCaptionAsrText("新的语音", isFinal: false));
            Assert.Equal("新的语音", asr.Display);
        }
    }
}
