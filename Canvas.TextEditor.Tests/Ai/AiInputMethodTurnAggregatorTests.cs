using System;
using System.Reflection;
using ImageColorChanger.Services.Ai;

namespace ImageColorChanger.CanvasTextEditor.Tests.Ai;

public sealed class AiInputMethodTurnAggregatorTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 9, 10, 0, 0, TimeSpan.FromHours(8));

    [Fact]
    public void StableText_FlowsAfterPause_AndIdenticalRefreshDoesNotRepeat()
    {
        var input = new Input();
        input.Observe("今天我们分享信心与盼望。", Start);
        Assert.Null(input.Flush(Start.AddMilliseconds(900)));
        Assert.Equal("今天我们分享信心与盼望。", input.Flush(Start.AddSeconds(1))?.Text);
        input.Observe("今天我们分享信心与盼望。", Start.AddSeconds(2));
        Assert.Null(input.Flush(Start.AddSeconds(10)));
    }

    [Fact]
    public void AppendedText_SendsOnlyNewPart()
    {
        var input = new Input();
        input.Observe("第一句。", Start);
        Assert.Equal("第一句。", input.Flush(Start.AddSeconds(1))?.Text);
        input.Observe("第一句。第二句。", Start.AddSeconds(2));
        Assert.Null(input.Flush(Start.AddSeconds(3)));
        var turn = input.Flush(Start.AddSeconds(5));
        Assert.Equal("第二句。", turn?.Text);
        Assert.Equal("input-method", turn.Source);
    }

    [Fact]
    public void ContinuousInput_HasBoundedLatency()
    {
        var input = new Input();
        for (int i = 0; i <= 4; i++) input.Observe(new string('字', i + 1), Start.AddSeconds(i));
        Assert.Equal("字字字字字", input.Flush(Start.AddSeconds(4))?.Text);
    }

    [Fact]
    public void RepeatedLayoutRefresh_DoesNotPostponeStableText()
    {
        var input = new Input();
        input.Observe("文本", Start);
        input.Observe("文本", Start.AddMilliseconds(900));
        Assert.Equal("文本", input.Flush(Start.AddSeconds(1))?.Text);
    }

    [Fact]
    public void EditingPendingText_SendsOnlyLatestVersion()
    {
        var input = new Input();
        input.Observe("我们分享错误文字", Start);
        input.Observe("我们分享正确文字", Start.AddMilliseconds(500));
        Assert.Null(input.Flush(Start.AddSeconds(1)));
        Assert.Equal("我们分享正确文字", input.Flush(Start.AddMilliseconds(1500))?.Text);
    }

    [Fact]
    public void EditingDeliveredText_IsExplicitCorrection()
    {
        var input = new Input();
        input.Observe("约翰福音三章十五节", Start);
        Assert.NotNull(input.Flush(Start.AddSeconds(1)));
        input.Observe("约翰福音三章十六节", Start.AddSeconds(2));
        var turn = input.Flush(Start.AddSeconds(5));
        Assert.Equal("input-method-correction", turn?.Source);
        Assert.Contains("修订", turn.Text);
        Assert.Contains("约翰福音三章十六节", turn.Text);
        Assert.DoesNotContain("十五节", turn.Text);
    }

    [Fact]
    public void Clear_DropsPendingText_AndAllowsSameWordsAgain()
    {
        var input = new Input();
        input.Observe("阿们", Start);
        input.Observe("", Start.AddMilliseconds(200));
        Assert.Null(input.Flush(Start.AddSeconds(5)));
        input.Observe("阿们", Start.AddSeconds(6));
        Assert.Equal("阿们", input.Flush(Start.AddSeconds(7))?.Text);
        input.Observe("", Start.AddSeconds(8));
        input.Observe("阿们", Start.AddSeconds(9));
        Assert.Equal("阿们", input.Flush(Start.AddSeconds(11))?.Text);
    }

    [Fact]
    public void Reset_BaselinesOldDocumentAndDiscardsPendingTurn()
    {
        var input = new Input();
        input.Observe("上一场文字", Start);
        input.Reset("上一场文字");
        Assert.Null(input.Flush(Start.AddSeconds(10)));
        input.Observe("上一场文字新一场内容", Start.AddSeconds(11));
        Assert.Equal("新一场内容", input.Flush(Start.AddSeconds(12))?.Text);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \r\n\t ")]
    public void EmptyInput_DoesNotSend(string text)
    {
        var input = new Input();
        input.Observe(text, Start);
        Assert.Null(input.Flush(Start.AddSeconds(8)));
    }

    [Fact]
    public void AsrReset_DropsPreviousPendingTextAndAllowsNewSessionSameText()
    {
        var input = new AiAsrTurnAggregator();
        input.TryAccept("这是上一场尚未发送的足够长的识别结果", false, Start, out _);
        var reset = typeof(AiAsrTurnAggregator).GetMethod("Reset");
        Assert.NotNull(reset);
        reset.Invoke(input, null);
        Assert.False(input.TryFlushPendingInterim(Start.AddSeconds(8), out _));
        Assert.True(input.TryAccept("新的一场正式开始了", true, Start.AddSeconds(9), out _));
        reset.Invoke(input, null);
        Assert.True(input.TryAccept("新的一场正式开始了", true, Start.AddSeconds(10), out _));
    }

    private sealed class Input
    {
        private readonly object _value;
        private readonly Type _type;
        public Input()
        {
            _type = typeof(AiAsrTurnAggregator).Assembly.GetType("ImageColorChanger.Services.Ai.AiInputMethodTurnAggregator");
            Assert.NotNull(_type);
            _value = Activator.CreateInstance(_type);
        }
        public void Observe(string text, DateTimeOffset now) => _type.GetMethod("Observe").Invoke(_value, new object[] { text, now });
        public void Reset(string text) => _type.GetMethod("Reset").Invoke(_value, new object[] { text });
        public AiAsrTurnEnvelope Flush(DateTimeOffset now)
        {
            object[] args = { now, null };
            bool accepted = (bool)_type.GetMethod("TryFlush").Invoke(_value, args);
            return accepted ? (AiAsrTurnEnvelope)args[1] : null;
        }
    }
}
