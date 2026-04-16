using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ImageColorChanger.Database.Models.Bible;
using ImageColorChanger.Services;
using ImageColorChanger.Services.Interfaces;
using ImageColorChanger.Services.LiveCaption;
using Xunit;

namespace ImageColorChanger.CanvasTextEditor.Tests.Services
{
    public sealed class BibleShortPhraseConsumerTests
    {
        [Fact]
        public async Task ProcessPcmAsync_TooShort_ReturnsFailure()
        {
            var bible = new FakeBibleService();
            var consumer = new BibleShortPhraseConsumer(
                bible,
                (wav, ct) => Task.FromResult(string.Empty));

            var result = await consumer.ProcessPcmAsync(new byte[100], CancellationToken.None);

            Assert.False(result.Success);
            Assert.Equal("audio-too-short", result.FailureReason);
        }

        [Fact]
        public async Task ProcessPcmAsync_DirectParse_ReturnsReference()
        {
            var bible = new FakeBibleService();
            bible.VerseCount = 20;
            var consumer = new BibleShortPhraseConsumer(
                bible,
                (wav, ct) => Task.FromResult("约翰福音三章十六节"));

            var result = await consumer.ProcessPcmAsync(new byte[6400], CancellationToken.None);

            Assert.True(result.Success);
            Assert.Equal("约翰福音三章十六节", result.RecognizedText);
            Assert.Equal(43, result.Reference.BookId);
            Assert.Equal(3, result.Reference.Chapter);
            Assert.Equal(16, result.Reference.StartVerse);
            Assert.Equal(16, result.FinalEndVerse);
        }

        [Fact]
        public async Task ProcessPcmAsync_ChapterOnly_UsesVerseCountAsEndVerse()
        {
            var bible = new FakeBibleService();
            bible.VerseCount = 31;
            var consumer = new BibleShortPhraseConsumer(
                bible,
                (wav, ct) => Task.FromResult("诗篇一章"));

            var result = await consumer.ProcessPcmAsync(new byte[6400], CancellationToken.None);

            Assert.True(result.Success);
            Assert.Equal(19, result.Reference.BookId);
            Assert.Equal(1, result.Reference.Chapter);
            Assert.Equal(31, result.FinalEndVerse);
        }

        [Fact]
        public async Task ProcessPcmAsync_ReverseLookupFallback_ReturnsReference()
        {
            var bible = new FakeBibleService();
            bible.SearchResults = new List<BibleSearchResult>
            {
                new BibleSearchResult
                {
                    Book = 43,
                    Chapter = 3,
                    Verse = 16,
                    Scripture = "神爱世人，甚至将他的独生子赐给他们"
                }
            };

            var consumer = new BibleShortPhraseConsumer(
                bible,
                (wav, ct) => Task.FromResult("神爱世人甚至将他的独生子赐给他们"));

            var result = await consumer.ProcessPcmAsync(new byte[6400], CancellationToken.None);

            Assert.True(result.Success);
            Assert.Equal(43, result.Reference.BookId);
            Assert.Equal(3, result.Reference.Chapter);
            Assert.Equal(16, result.Reference.StartVerse);
        }

        [Fact]
        public async Task ProcessPcmAsync_CanceledDuringTranscribe_ReturnsCanceledFailure()
        {
            var bible = new FakeBibleService();
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var consumer = new BibleShortPhraseConsumer(
                bible,
                (wav, ct) => Task.FromCanceled<string>(ct));

            var result = await consumer.ProcessPcmAsync(new byte[6400], cts.Token);

            Assert.False(result.Success);
            Assert.Equal("canceled", result.FailureReason);
        }

        internal sealed class FakeBibleService : IBibleService
        {
            public int VerseCount { get; set; } = 0;
            public List<BibleSearchResult> SearchResults { get; set; } = new List<BibleSearchResult>();

            public Task<int> GetVerseCountAsync(int book, int chapter) => Task.FromResult(VerseCount);
            public Task<List<BibleSearchResult>> SearchVersesAsync(string keyword, int? bookId = null) => Task.FromResult(SearchResults);
            public Task<List<BibleSearchResult>> SearchVersesByPinyinAsync(string pinyinKeyword, int? bookId = null) => Task.FromResult(new List<BibleSearchResult>());
            public int GetChapterCount(int book) => 0;
            public Task<Dictionary<(int book, int chapter), int>> GetAllVerseCountsAsync() => Task.FromResult(new Dictionary<(int book, int chapter), int>());
            public Task<bool> IsDatabaseAvailableAsync() => Task.FromResult(true);
            public Task<Dictionary<string, string>> GetMetadataAsync() => Task.FromResult(new Dictionary<string, string>());
            public void UpdateDatabasePath() { }
            public Task<BibleVerse> GetVerseAsync(int book, int chapter, int verse) => throw new NotImplementedException();
            public Task<List<BibleVerse>> GetChapterVersesAsync(int book, int chapter) => throw new NotImplementedException();
            public Task<List<BibleVerse>> GetVerseRangeAsync(int book, int chapter, int startVerse, int endVerse) => throw new NotImplementedException();
            public Task<List<BibleTitle>> GetChapterTitlesAsync(int book, int chapter) => throw new NotImplementedException();
            public Task<List<object>> GetChapterContentAsync(int book, int chapter) => throw new NotImplementedException();
        }

        internal sealed class FakeBibleServiceAccessor
        {
            public IBibleService Create()
            {
                return new FakeBibleService
                {
                    VerseCount = 20,
                    SearchResults = new List<BibleSearchResult>
                    {
                        new BibleSearchResult
                        {
                            Book = 43,
                            Chapter = 3,
                            Verse = 16,
                            Scripture = "神爱世人，甚至将他的独生子赐给他们"
                        }
                    }
                };
            }
        }
    }
}
