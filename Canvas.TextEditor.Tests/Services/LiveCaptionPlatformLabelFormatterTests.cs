using ImageColorChanger.Services.LiveCaption;
using Xunit;

namespace ImageColorChanger.CanvasTextEditor.Tests.Services
{
    public sealed class LiveCaptionPlatformLabelFormatterTests
    {
        [Theory]
        [InlineData("doubao", "豆包实时", "豆包短语")]
        [InlineData("aliyun", "阿里实时", "阿里短语")]
        [InlineData("tencent", "腾讯实时", "腾讯短语")]
        [InlineData("baidu", "百度实时", "百度短语")]
        [InlineData("funasr", "FunASR实时", "FunASR短语")]
        [InlineData("unknown", "百度实时", "百度短语")]
        public void BuildTags_ShouldIncludePlatformAndChannel(string provider, string expectedRealtime, string expectedShort)
        {
            Assert.Equal(expectedRealtime, LiveCaptionPlatformLabelFormatter.BuildRealtimeTag(provider));
            Assert.Equal(expectedShort, LiveCaptionPlatformLabelFormatter.BuildShortPhraseTag(provider));
        }
    }
}
