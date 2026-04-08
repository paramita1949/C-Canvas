using System.Reflection;
using ImageColorChanger.Services.LiveCaption;
using Xunit;

namespace ImageColorChanger.CanvasTextEditor.Tests.Services
{
    public sealed class CliProxyApiClientFunAsrTests
    {
        [Fact]
        public void TryExtractFunAsrText_ResultType_ShouldBeFinal()
        {
            bool ok = InvokeTryExtract("{\"type\":\"result\",\"text\":\"哈利路亚\"}", out string text, out bool isFinal);

            Assert.True(ok);
            Assert.Equal("哈利路亚", text);
            Assert.True(isFinal);
        }

        [Fact]
        public void TryExtractFunAsrText_PartialType_ShouldNotBeFinal()
        {
            bool ok = InvokeTryExtract("{\"type\":\"partial\",\"text\":\"哈利\"}", out string text, out bool isFinal);

            Assert.True(ok);
            Assert.Equal("哈利", text);
            Assert.False(isFinal);
        }

        [Theory]
        [InlineData("localhost", true)]
        [InlineData("127.0.0.1", true)]
        [InlineData("::1", true)]
        [InlineData("192.168.1.2", false)]
        [InlineData("example.com", false)]
        public void IsLoopbackHost_ShouldMatchExpected(string host, bool expected)
        {
            var method = typeof(CliProxyApiClient).GetMethod(
                "IsLoopbackHost",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.NotNull(method);
            object result = method.Invoke(null, new object[] { host });
            Assert.Equal(expected, result is bool b && b);
        }

        private static bool InvokeTryExtract(string json, out string text, out bool isFinal)
        {
            var method = typeof(CliProxyApiClient).GetMethod(
                "TryExtractFunAsrText",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.NotNull(method);
            object[] args = { json, string.Empty, false };
            object result = method.Invoke(null, args);

            text = (string)args[1];
            isFinal = (bool)args[2];
            return result is bool b && b;
        }
    }
}
