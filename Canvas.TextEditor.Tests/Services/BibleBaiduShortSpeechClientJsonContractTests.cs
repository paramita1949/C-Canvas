using System;
using System.Reflection;
using System.Text.Json;

namespace ImageColorChanger.CanvasTextEditor.Tests.Services
{
    public sealed class BibleBaiduShortSpeechClientJsonContractTests
    {
        [Fact]
        public void BaiduTokenResponse_MapsSnakeCaseFields()
        {
            Type responseType = typeof(ImageColorChanger.Services.BibleBaiduShortSpeechClient)
                .GetNestedType("BaiduTokenResponse", BindingFlags.NonPublic);

            Assert.NotNull(responseType);

            object result = JsonSerializer.Deserialize(
                "{\"access_token\":\"token-123\",\"expires_in\":2592000}",
                responseType);

            Assert.NotNull(result);
            Assert.Equal("token-123", responseType.GetProperty("AccessToken")?.GetValue(result) as string);
            Assert.Equal(2592000, (int)(responseType.GetProperty("ExpiresIn")?.GetValue(result) ?? 0));
        }

        [Fact]
        public void BaiduAsrResponse_MapsSnakeCaseFields()
        {
            Type responseType = typeof(ImageColorChanger.Services.BibleBaiduShortSpeechClient)
                .GetNestedType("BaiduAsrResponse", BindingFlags.NonPublic);

            Assert.NotNull(responseType);

            object result = JsonSerializer.Deserialize(
                "{\"err_no\":0,\"err_msg\":\"success.\",\"result\":[\"诗篇102篇25到27节\"]}",
                responseType);

            Assert.NotNull(result);
            Assert.Equal(0, (int)(responseType.GetProperty("ErrNo")?.GetValue(result) ?? -1));
            Assert.Equal("success.", responseType.GetProperty("ErrMsg")?.GetValue(result) as string);

            var values = responseType.GetProperty("Result")?.GetValue(result) as string[];
            Assert.NotNull(values);
            Assert.Equal("诗篇102篇25到27节", values[0]);
        }
    }
}
