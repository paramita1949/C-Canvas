using ImageColorChanger.Services;
using Xunit;

namespace ImageColorChanger.CanvasTextEditor.Tests.Services
{
    public sealed class BibleSpeechReferenceParserTests
    {
        [Fact]
        public void TryParse_ChineseSingleVerse_ShouldParse()
        {
            bool ok = BibleSpeechReferenceParser.TryParse("约翰福音三章十六节", out var result);

            Assert.True(ok);
            Assert.Equal(43, result.BookId);
            Assert.Equal(3, result.Chapter);
            Assert.Equal(16, result.StartVerse);
            Assert.Equal(16, result.EndVerse);
        }

        [Fact]
        public void TryParse_ChineseVerseRange_ShouldParse()
        {
            bool ok = BibleSpeechReferenceParser.TryParse("约翰福音三章十六到十八节", out var result);

            Assert.True(ok);
            Assert.Equal(43, result.BookId);
            Assert.Equal(3, result.Chapter);
            Assert.Equal(16, result.StartVerse);
            Assert.Equal(18, result.EndVerse);
        }

        [Fact]
        public void TryParse_ChapterOnly_ShouldParse()
        {
            bool ok = BibleSpeechReferenceParser.TryParse("创世记一章", out var result);

            Assert.True(ok);
            Assert.Equal(1, result.BookId);
            Assert.Equal(1, result.Chapter);
            Assert.Equal(1, result.StartVerse);
            Assert.Equal(0, result.EndVerse);
        }

        [Fact]
        public void TryParse_InvalidText_ShouldFail()
        {
            bool ok = BibleSpeechReferenceParser.TryParse("今天主日平安", out _);

            Assert.False(ok);
        }

        [Theory]
        [InlineData("约韩福音三章十六节", 43, 3, 16, 16)] // 翰->韩
        [InlineData("路家福音二章一节", 42, 2, 1, 1)]    // 加->家
        [InlineData("马太服音五章三节", 40, 5, 3, 3)]    // 福->服
        [InlineData("创世一章", 1, 1, 1, 0)]             // 缺字
        public void TryParse_FuzzyBookName_ShouldParse(string text, int bookId, int chapter, int startVerse, int endVerse)
        {
            bool ok = BibleSpeechReferenceParser.TryParse(text, out var result);

            Assert.True(ok);
            Assert.Equal(bookId, result.BookId);
            Assert.Equal(chapter, result.Chapter);
            Assert.Equal(startVerse, result.StartVerse);
            Assert.Equal(endVerse, result.EndVerse);
        }

        [Theory]
        [InlineData("约福音三章十六节", 43, 3, 16, 16)]      // 缺字: 翰
        [InlineData("哥林多前一三章四节", 46, 13, 4, 4)]    // 缺字: 书
        [InlineData("约喊福音三章十六节", 43, 3, 16, 16)]    // 同音错字: 翰->喊
        [InlineData("路夹福音二章一节", 42, 2, 1, 1)]        // 同音错字: 加->夹
        [InlineData("非利比书第四章第四节", 50, 4, 4, 4)]
        [InlineData("飞利笔数第四章第四节", 50, 4, 4, 4)]
        [InlineData("新约的非利比书的第四章第四节", 50, 4, 4, 4)]
        [InlineData("翻到以后呢我们就一同来读这一处的经文啊在新约的菲利比书的第四章第四节来我们一起来读", 50, 4, 4, 4)]
        public void TryParse_HarshFuzzySamples_ShouldParse(string text, int bookId, int chapter, int startVerse, int endVerse)
        {
            bool ok = BibleSpeechReferenceParser.TryParse(text, out var result);

            Assert.True(ok);
            Assert.Equal(bookId, result.BookId);
            Assert.Equal(chapter, result.Chapter);
            Assert.Equal(startVerse, result.StartVerse);
            Assert.Equal(endVerse, result.EndVerse);
        }

        [Fact]
        public void TryParse_NonBibleSentenceWithNumbers_ShouldFail()
        {
            bool ok = BibleSpeechReferenceParser.TryParse("今天三点十六分开始聚会", out _);

            Assert.False(ok);
        }

        [Theory]
        [InlineData("今天三点十六分我们开始聚会")]
        [InlineData("本周计划有三章十六个小节")]
        [InlineData("请把第3章第16页发到群里")]
        [InlineData("这首歌一共3分16秒")]
        [InlineData("我们今天背了约翰福音这卷书, 一共三章内容")]
        public void TryParse_PlainSentencesWithNumbers_ShouldFail(string text)
        {
            bool ok = BibleSpeechReferenceParser.TryParse(text, out _);

            Assert.False(ok);
        }

        [Theory]
        [InlineData("创世记3:16", 1, 3, 16, 16)]
        [InlineData("约翰福音3:16-18", 43, 3, 16, 18)]
        [InlineData("约翰福音3:16到18", 43, 3, 16, 18)]
        public void TryParse_ColonFormat_ShouldParse(string text, int bookId, int chapter, int startVerse, int endVerse)
        {
            bool ok = BibleSpeechReferenceParser.TryParse(text, out var result);

            Assert.True(ok);
            Assert.Equal(bookId, result.BookId);
            Assert.Equal(chapter, result.Chapter);
            Assert.Equal(startVerse, result.StartVerse);
            Assert.Equal(endVerse, result.EndVerse);
        }

        [Theory]
        [InlineData("本周诗歌三章十六节练习")]
        [InlineData("歌曲三章十六节开始")]
        [InlineData("我们约一下三章内容")]
        [InlineData("周报约三章内容整理")]
        [InlineData("约翰福音3:16分开始祷告")]
        [InlineData("约翰福音3:16秒静默")]
        [InlineData("约翰福音3:16点开始")]
        [InlineData("约翰福音3:16页")]
        public void TryParse_AmbiguousBookLikeSegment_ShouldFail(string text)
        {
            bool ok = BibleSpeechReferenceParser.TryParse(text, out _);

            Assert.False(ok);
        }

        [Theory]
        [InlineData("犹大书二章一节")]
        [InlineData("约翰三书三章一节")]
        [InlineData("提多书四章一节")]
        public void TryParse_BookChapterOverflow_ShouldFail(string text)
        {
            bool ok = BibleSpeechReferenceParser.TryParse(text, out _);

            Assert.False(ok);
        }

        [Theory]
        [InlineData("约翰福音一章一节", 43, 1, 1, 1)]
        [InlineData("约翰一书一章九节", 62, 1, 9, 9)]
        [InlineData("约翰二书一章五节", 63, 1, 5, 5)]
        [InlineData("约翰三书一章二节", 64, 1, 2, 2)]
        public void TryParse_JohannineExplicitBooks_ShouldParse(string text, int bookId, int chapter, int startVerse, int endVerse)
        {
            bool ok = BibleSpeechReferenceParser.TryParse(text, out var result);

            Assert.True(ok);
            Assert.Equal(bookId, result.BookId);
            Assert.Equal(chapter, result.Chapter);
            Assert.Equal(startVerse, result.StartVerse);
            Assert.Equal(endVerse, result.EndVerse);
        }

        [Theory]
        [InlineData("约一章九节")]
        [InlineData("约二章一节")]
        [InlineData("约一三章十六节")]
        [InlineData("约二一章一节")]
        public void TryParse_AmbiguousJohannineOrdinal_ShouldFail(string text)
        {
            bool ok = BibleSpeechReferenceParser.TryParse(text, out _);

            Assert.False(ok);
        }

        [Theory]
        [InlineData("约四章一节", 43, 4, 1, 1)]
        [InlineData("约十章一节", 43, 10, 1, 1)]
        public void TryParse_JohannineOrdinalGuard_ShouldNotBlockNonAmbiguousChapters(string text, int bookId, int chapter, int startVerse, int endVerse)
        {
            bool ok = BibleSpeechReferenceParser.TryParse(text, out var result);

            Assert.True(ok);
            Assert.Equal(bookId, result.BookId);
            Assert.Equal(chapter, result.Chapter);
            Assert.Equal(startVerse, result.StartVerse);
            Assert.Equal(endVerse, result.EndVerse);
        }

        [Theory]
        [InlineData("约翰福音3:")]
        [InlineData("约翰福音3:16-")]
        [InlineData("约翰福音3:-18")]
        [InlineData("约翰福音三章十六到节")]
        [InlineData("约翰福音三章到十八节")]
        public void TryParse_InvalidRangeOrColonForms_ShouldFail(string text)
        {
            bool ok = BibleSpeechReferenceParser.TryParse(text, out _);

            Assert.False(ok);
        }
    }
}
