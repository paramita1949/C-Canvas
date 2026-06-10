using System.Windows.Media;
using ImageColorChanger.Core;
using SkiaSharp;
using Xunit;

namespace ImageColorChanger.CanvasTextEditor.Tests.Core
{
    public sealed class BiblePopupFontResolverTests
    {
        [Fact]
        public void ResolveFamilyName_UsesPopupFontBeforeInsertFont()
        {
            var config = new BibleTextInsertConfig
            {
                FontFamily = "DengXian",
                PopupFontFamily = "江西拙楷"
            };

            string actual = BiblePopupFontResolver.ResolveFamilyName(config);

            Assert.Equal("江西拙楷", actual);
        }

        [Theory]
        [InlineData("", "思源宋体 CN", "思源宋体 CN")]
        [InlineData("   ", "DengXian", "DengXian")]
        [InlineData(null, "", "Microsoft YaHei UI")]
        public void ResolveFamilyName_FallsBackToInsertFontThenDefault(
            string popupFontFamily,
            string insertFontFamily,
            string expected)
        {
            var config = new BibleTextInsertConfig
            {
                PopupFontFamily = popupFontFamily,
                FontFamily = insertFontFamily
            };

            string actual = BiblePopupFontResolver.ResolveFamilyName(config);

            Assert.Equal(expected, actual);
        }

        [Fact]
        public void CreateSkiaFont_UsesPopupFontThroughTypefaceLoader()
        {
            string capturedFamily = null;
            bool capturedBold = false;
            bool capturedItalic = true;
            var config = new BibleTextInsertConfig
            {
                FontFamily = "DengXian",
                PopupFontFamily = "站酷文艺体"
            };

            using var font = BiblePopupFontResolver.CreateSkiaFont(
                config,
                42f,
                isBold: true,
                isItalic: false,
                loadTypeface: (family, bold, italic) =>
                {
                    capturedFamily = family;
                    capturedBold = bold;
                    capturedItalic = italic;
                    return SKTypeface.Default;
                });

            Assert.Equal("站酷文艺体", capturedFamily);
            Assert.True(capturedBold);
            Assert.False(capturedItalic);
            Assert.True(font.Embolden);
            Assert.Equal(42f, font.Size);
        }

        [Fact]
        public void ResolveWpfFontFamily_UsesPopupFontThroughFontLoader()
        {
            string capturedFamily = null;
            var expectedFontFamily = new FontFamily("Arial");
            var config = new BibleTextInsertConfig
            {
                FontFamily = "DengXian",
                PopupFontFamily = "思源宋体 CN"
            };

            FontFamily actual = BiblePopupFontResolver.ResolveWpfFontFamily(
                config,
                family =>
                {
                    capturedFamily = family;
                    return expectedFontFamily;
                });

            Assert.Equal("思源宋体 CN", capturedFamily);
            Assert.Same(expectedFontFamily, actual);
        }
    }
}
