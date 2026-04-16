using System.Collections.Generic;
using System.Threading.Tasks;
using ImageColorChanger.Database.Models.Bible;
using ImageColorChanger.Services;
using ImageColorChanger.Services.Interfaces;
using Xunit;

namespace ImageColorChanger.CanvasTextEditor.Tests.Services
{
    public sealed class BibleSpeechReverseLookupServiceTests
    {
        [Fact]
        public async Task TryResolveAsync_ShouldReturnBestMatchedVerse()
        {
            var svc = new BibleSpeechReverseLookupService();
            var bible = new FakeBibleService();

            var resolved = await svc.TryResolveAsync(
                bible,
                "神爱世人甚至将他的独生子赐给他们",
                default);

            Assert.True(resolved.HasValue);
            var reference = resolved.Value;
            Assert.Equal(43, reference.BookId);
            Assert.Equal(3, reference.Chapter);
            Assert.Equal(16, reference.StartVerse);
            Assert.Equal(16, reference.EndVerse);
        }

        [Fact]
        public async Task TryResolveAsync_InvalidText_ShouldFail()
        {
            var svc = new BibleSpeechReverseLookupService();
            var bible = new FakeBibleService();

            var resolved = await svc.TryResolveAsync(
                bible,
                "今天主日平安",
                default);

            Assert.False(resolved.HasValue);
        }

        private sealed class FakeBibleService : IBibleService
        {
            public Task<BibleVerse> GetVerseAsync(int book, int chapter, int verse) => Task.FromResult<BibleVerse>(null);
            public Task<List<BibleVerse>> GetChapterVersesAsync(int book, int chapter) => Task.FromResult(new List<BibleVerse>());
            public Task<List<BibleVerse>> GetVerseRangeAsync(int book, int chapter, int startVerse, int endVerse) => Task.FromResult(new List<BibleVerse>());
            public Task<List<BibleTitle>> GetChapterTitlesAsync(int book, int chapter) => Task.FromResult(new List<BibleTitle>());
            public Task<List<object>> GetChapterContentAsync(int book, int chapter) => Task.FromResult(new List<object>());
            public Task<List<BibleSearchResult>> SearchVersesAsync(string keyword, int? bookId = null)
            {
                var all = new List<BibleSearchResult>
                {
                    new BibleSearchResult
                    {
                        Book = 43,
                        Chapter = 3,
                        Verse = 16,
                        Scripture = "神爱世人，甚至将他的独生子赐给他们，叫一切信他的，不至灭亡，反得永生。"
                    },
                    new BibleSearchResult
                    {
                        Book = 19,
                        Chapter = 23,
                        Verse = 1,
                        Scripture = "耶和华是我的牧者，我必不致缺乏。"
                    }
                };

                keyword ??= string.Empty;
                return Task.FromResult(all.FindAll(x => x.Scripture != null && x.Scripture.Contains(keyword)));
            }

            public Task<List<BibleSearchResult>> SearchVersesByPinyinAsync(string pinyinKeyword, int? bookId = null) => Task.FromResult(new List<BibleSearchResult>());
            public int GetChapterCount(int book) => 0;
            public Task<Dictionary<(int book, int chapter), int>> GetAllVerseCountsAsync() => Task.FromResult(new Dictionary<(int book, int chapter), int>());
            public Task<int> GetVerseCountAsync(int book, int chapter) => Task.FromResult(0);
            public Task<bool> IsDatabaseAvailableAsync() => Task.FromResult(true);
            public Task<Dictionary<string, string>> GetMetadataAsync() => Task.FromResult(new Dictionary<string, string>());
            public void UpdateDatabasePath() { }
        }
    }
}
