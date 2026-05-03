using ImageColorChanger.Services.LiveCaption;
using Xunit;

namespace ImageColorChanger.CanvasTextEditor.Tests.Services
{
    public sealed class LiveCaptionPlatformLabelFormatterTests
    {
        [Theory]
        [InlineData("doubao", "豆包实时", "豆包短语")]
        [InlineData("aliyun", "飞讯语音实时", "飞讯语音短语")]
        [InlineData("tencent", "飞讯语音实时", "飞讯语音短语")]
        [InlineData("baidu", "百度实时", "百度短语")]
        [InlineData("xfyun", "飞讯语音实时", "飞讯语音短语")]
        [InlineData("funasr", "FunASR实时", "FunASR短语")]
        [InlineData("unknown", "飞讯语音实时", "飞讯语音短语")]
        public void BuildTags_ShouldIncludePlatformAndChannel(string provider, string expectedRealtime, string expectedShort)
        {
            Assert.Equal(expectedRealtime, LiveCaptionPlatformLabelFormatter.BuildRealtimeTag(provider));
            Assert.Equal(expectedShort, LiveCaptionPlatformLabelFormatter.BuildShortPhraseTag(provider));
        }
    }
}
