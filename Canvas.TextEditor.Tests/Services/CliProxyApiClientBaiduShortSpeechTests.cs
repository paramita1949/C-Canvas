using System.Reflection;
using ImageColorChanger.Services.LiveCaption;
using Xunit;

namespace ImageColorChanger.CanvasTextEditor.Tests.Services
{
    public sealed class CliProxyApiClientBaiduShortSpeechTests
    {
        [Theory]
        [InlineData("baidu-short-standard", "http://vop.baidu.com/server_api")]
        [InlineData("baidu-short-pro", "https://vop.baidu.com/pro_api")]
        [InlineData("baidu-short-pro-80001", "https://vop.baidu.com/pro_api")]
        [InlineData("baidu-realtime", "http://vop.baidu.com/server_api")]
        [InlineData("", "http://vop.baidu.com/server_api")]
        public void ResolveBaiduShortSpeechEndpoint_ShouldMatchModel(string modelId, string expectedUrl)
        {
            var method = typeof(CliProxyApiClient).GetMethod(
                "ResolveBaiduShortSpeechEndpoint",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.NotNull(method);
            object result = method.Invoke(null, new object[] { modelId });
            Assert.Equal(expectedUrl, result as string);
        }

        [Theory]
        [InlineData("baidu-short-standard", 0, 1537)]
        [InlineData("baidu-short-pro", 0, 80001)]
        [InlineData("baidu-short-pro", 1537, 80001)]
        [InlineData("baidu-short-standard", 80001, 1537)]
        [InlineData("baidu-short-pro", 15372, 15372)]
        [InlineData("baidu-realtime", 0, 1537)]
        [InlineData("", 1936, 1936)]
        public void ResolveBaiduShortSpeechDevPid_ShouldRespectConfiguredValue(string modelId, int configuredDevPid, int expectedDevPid)
        {
            var method = typeof(CliProxyApiClient).GetMethod(
                "ResolveBaiduShortSpeechDevPid",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.NotNull(method);
            object result = method.Invoke(null, new object[] { modelId, configuredDevPid });
            Assert.Equal(expectedDevPid, (int)result);
        }
    }
}
