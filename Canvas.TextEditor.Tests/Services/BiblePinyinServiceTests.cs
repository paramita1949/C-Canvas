using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ImageColorChanger.Core;
using ImageColorChanger.Database.Models.Bible;
using ImageColorChanger.Services;
using ImageColorChanger.Services.Interfaces;
using TinyPinyin;
using Xunit;

namespace ImageColorChanger.CanvasTextEditor.Tests.Services
{
    public sealed class BiblePinyinServiceTests
    {
        [Fact]
        public void FindBooksByPinyin_Lq_ReturnsLinQian()
        {
            var service = CreateService();

            var matches = service.FindBooksByPinyin("lq");

            Assert.NotEmpty(matches);
            Assert.Equal(46, matches[0].BookId);
            Assert.Equal("哥林多前书", matches[0].BookName);
        }

        [Fact]
        public void FindBooksByPinyin_Tq_ReturnsOverlappedCandidates()
        {
            var service = CreateService();

            var matches = service.FindBooksByPinyin("tq");

            Assert.Contains(matches, m => m.BookId == 52); // 帖前
            Assert.Contains(matches, m => m.BookId == 54); // 提前
        }

        [Fact]
        public async Task ParseAsync_LqChapterVerse_UsesLinQian()
        {
            var service = CreateService();

            var result = await service.ParseAsync("lq 1 2");

            Assert.True(result.Success);
            Assert.Equal(46, result.BookId);
            Assert.Equal(1, result.Chapter);
            Assert.Equal(2, result.StartVerse);
            Assert.Equal(2, result.EndVerse);
        }

        [Fact]
        public async Task ParseAsync_TqChapterVerse_IsAmbiguousAndFails()
        {
            var service = CreateService();

            var result = await service.ParseAsync("tq 1 2");

            Assert.False(result.Success);
        }

        [Fact]
        public void ReplaceWithBookName_Lq_ReplacesToLinQian()
        {
            var service = CreateService();

            var replaced = service.ReplaceWithBookName("lq 5 1");

            Assert.StartsWith("哥林多前书", replaced);
        }

        [Fact]
        public async Task ParseAsync_TuChapterVerse_UsesShiTuXingZhuan()
        {
            var service = CreateService();

            var result = await service.ParseAsync("tu 1 1");

            Assert.True(result.Success);
            Assert.Equal(44, result.BookId);
            Assert.Equal(1, result.Chapter);
            Assert.Equal(1, result.StartVerse);
            Assert.Equal(1, result.EndVerse);
        }

        [Fact]
        public async Task ParseAsync_DeChapterVerse_UsesLuDeJi()
        {
            var service = CreateService();

            var result = await service.ParseAsync("de 1 1");

            Assert.True(result.Success);
            Assert.Equal(8, result.BookId);
            Assert.Equal(1, result.Chapter);
            Assert.Equal(1, result.StartVerse);
            Assert.Equal(1, result.EndVerse);
        }

        [Fact]
        public async Task AliasCoverage_AllMultiCharShortNames_AreReachableAndConflictHandled()
        {
            var service = CreateService();
            var aliasGroups = BuildShortAliasGroups();

            Assert.NotEmpty(aliasGroups);

            foreach (var kvp in aliasGroups)
            {
                string alias = kvp.Key;
                var books = kvp.Value;

                var matches = service.FindBooksByPinyin(alias);
                foreach (var book in books)
                {
                    if (!matches.Any(m => m.BookId == book.BookId))
                    {
                        throw new Xunit.Sdk.XunitException($"alias='{alias}' 未匹配到书卷 {book.BookId}-{book.Name}");
                    }
                }

                var parseResult = await service.ParseAsync($"{alias} 1 1");
                if (books.Count == 1)
                {
                    if (!parseResult.Success)
                    {
                        throw new Xunit.Sdk.XunitException($"alias='{alias}' 预期唯一匹配 {books[0].BookId}-{books[0].Name}，但解析失败");
                    }

                    Assert.Equal(books[0].BookId, parseResult.BookId);
                }
                else
                {
                    Assert.False(parseResult.Success);
                }
            }
        }

        private static BiblePinyinService CreateService()
        {
            return new BiblePinyinService(new FakeBibleService());
        }

        private static Dictionary<string, List<BibleBook>> BuildShortAliasGroups()
        {
            var groups = new Dictionary<string, List<BibleBook>>();
            var unresolved = new List<string>();
            foreach (var book in BibleBookConfig.Books)
            {
                string shortName = book.ShortName?.Trim();
                if (string.IsNullOrWhiteSpace(shortName))
                {
                    continue;
                }

                var aliases = BuildAliasesByTinyPinyin(shortName);
                if (aliases.Count == 0)
                {
                    unresolved.Add($"{book.Name}({shortName})");
                    continue;
                }

                foreach (var alias in aliases)
                {
                    if (!groups.TryGetValue(alias, out var list))
                    {
                        list = new List<BibleBook>();
                        groups[alias] = list;
                    }

                    if (!list.Any(b => b.BookId == book.BookId))
                    {
                        list.Add(book);
                    }
                }
            }

            Assert.Empty(unresolved);
            return groups
                .OrderBy(kvp => kvp.Key)
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value.OrderBy(b => b.BookId).ToList());
        }

        private static HashSet<string> BuildAliasesByTinyPinyin(string shortName)
        {
            var aliases = new HashSet<string>();
            if (string.IsNullOrWhiteSpace(shortName))
            {
                return aliases;
            }

            var initials = new List<char>(shortName.Length);
            var fullSyllables = new List<string>(shortName.Length);
            foreach (var c in shortName.Trim())
            {
                if (char.IsWhiteSpace(c))
                {
                    continue;
                }

                if (c <= 127)
                {
                    if (char.IsLetterOrDigit(c))
                    {
                        char lower = char.ToLowerInvariant(c);
                        initials.Add(lower);
                        fullSyllables.Add(lower.ToString());
                    }
                    else
                    {
                        return new HashSet<string>();
                    }

                    continue;
                }

                if (!PinyinHelper.IsChinese(c))
                {
                    return new HashSet<string>();
                }

                var pinyin = NormalizePinyinSyllable(PinyinHelper.GetPinyin(c.ToString()));
                if (string.IsNullOrWhiteSpace(pinyin))
                {
                    return new HashSet<string>();
                }

                initials.Add(char.ToLowerInvariant(pinyin[0]));
                fullSyllables.Add(pinyin);
            }

            if (initials.Count == 0)
            {
                return aliases;
            }

            aliases.Add(new string(initials.ToArray()));
            aliases.Add(string.Concat(fullSyllables));
            return aliases;
        }

        private static string NormalizePinyinSyllable(string pinyin)
        {
            if (string.IsNullOrWhiteSpace(pinyin))
            {
                return string.Empty;
            }

            string normalized = pinyin
                .Trim()
                .ToLowerInvariant()
                .Replace("u:", "v")
                .Replace('ü', 'v');

            string decomposed = normalized.Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder(decomposed.Length);
            foreach (char ch in decomposed)
            {
                var category = CharUnicodeInfo.GetUnicodeCategory(ch);
                if (category == UnicodeCategory.NonSpacingMark)
                {
                    continue;
                }

                char lower = char.ToLowerInvariant(ch);
                if (lower is >= 'a' and <= 'z')
                {
                    builder.Append(lower);
                }
                else if (lower == 'v')
                {
                    builder.Append(lower);
                }
            }

            return builder.ToString();
        }

        private sealed class FakeBibleService : IBibleService
        {
            public Task<BibleVerse> GetVerseAsync(int book, int chapter, int verse) => Task.FromResult<BibleVerse>(null);

            public Task<List<BibleVerse>> GetChapterVersesAsync(int book, int chapter) => Task.FromResult(new List<BibleVerse>());

            public Task<List<BibleVerse>> GetVerseRangeAsync(int book, int chapter, int startVerse, int endVerse) => Task.FromResult(new List<BibleVerse>());

            public Task<List<BibleTitle>> GetChapterTitlesAsync(int book, int chapter) => Task.FromResult(new List<BibleTitle>());

            public Task<List<object>> GetChapterContentAsync(int book, int chapter) => Task.FromResult(new List<object>());

            public Task<List<BibleSearchResult>> SearchVersesAsync(string keyword, int? bookId = null) => Task.FromResult(new List<BibleSearchResult>());

            public Task<List<BibleSearchResult>> SearchVersesByPinyinAsync(string pinyinKeyword, int? bookId = null) => Task.FromResult(new List<BibleSearchResult>());

            public int GetChapterCount(int book) => 150;

            public Task<Dictionary<(int book, int chapter), int>> GetAllVerseCountsAsync() => Task.FromResult(new Dictionary<(int book, int chapter), int>());

            public Task<int> GetVerseCountAsync(int book, int chapter) => Task.FromResult(80);

            public Task<bool> IsDatabaseAvailableAsync() => Task.FromResult(true);

            public Task<Dictionary<string, string>> GetMetadataAsync() => Task.FromResult(new Dictionary<string, string>());

            public void UpdateDatabasePath()
            {
            }
        }
    }
}
