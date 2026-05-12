using System.Threading.Tasks;
using ImageColorChanger.Services.Ai;
using Xunit;

namespace Canvas.TextEditor.Tests.Ai
{
    public sealed class AiScriptureCandidateValidatorTests
    {
        [Fact]
        public async Task ValidateAsync_ValidBookNameAndVerse_ReturnsAcceptedCandidate()
        {
            var validator = new AiScriptureCandidateValidator();
            var candidate = new AiScriptureCandidate
            {
                BookName = "罗马书",
                Chapter = 8,
                StartVerse = 28,
                EndVerse = 28,
                Confidence = 0.91,
                Reason = "ASR 提到万事互相效力",
                EvidenceText = "万事都互相效力"
            };

            var result = await validator.ValidateAsync(candidate, GetVerseCountAsync);

            Assert.True(result.Accepted);
            Assert.Equal(45, result.Candidate.BookId);
            Assert.Equal(8, result.Candidate.Chapter);
            Assert.Equal(28, result.Candidate.StartVerse);
            Assert.Equal(28, result.Candidate.EndVerse);
        }

        [Fact]
        public async Task ValidateAsync_LowConfidence_IsRejected()
        {
            var validator = new AiScriptureCandidateValidator();
            var candidate = new AiScriptureCandidate
            {
                BookName = "罗马书",
                Chapter = 8,
                StartVerse = 28,
                EndVerse = 28,
                Confidence = 0.42
            };

            var result = await validator.ValidateAsync(candidate, GetVerseCountAsync);

            Assert.False(result.Accepted);
            Assert.Equal("confidence-too-low", result.Reason);
        }

        [Fact]
        public async Task ValidateAsync_UnknownBook_IsRejected()
        {
            var validator = new AiScriptureCandidateValidator();
            var candidate = new AiScriptureCandidate
            {
                BookName = "不存在书",
                Chapter = 1,
                StartVerse = 1,
                EndVerse = 1,
                Confidence = 0.95
            };

            var result = await validator.ValidateAsync(candidate, GetVerseCountAsync);

            Assert.False(result.Accepted);
            Assert.Equal("unknown-book", result.Reason);
        }

        [Fact]
        public async Task ValidateAsync_VerseOutOfRange_IsRejected()
        {
            var validator = new AiScriptureCandidateValidator();
            var candidate = new AiScriptureCandidate
            {
                BookName = "罗马书",
                Chapter = 8,
                StartVerse = 99,
                EndVerse = 99,
                Confidence = 0.95
            };

            var result = await validator.ValidateAsync(candidate, GetVerseCountAsync);

            Assert.False(result.Accepted);
            Assert.Equal("verse-out-of-range", result.Reason);
        }

        [Fact]
        public async Task ValidateAsync_BookOnlyCandidate_ExpandsToChapterOneWholeRange()
        {
            var validator = new AiScriptureCandidateValidator();
            var candidate = new AiScriptureCandidate
            {
                BookName = "罗马书",
                Chapter = 0,
                StartVerse = 0,
                EndVerse = 0,
                Confidence = 0.95
            };

            var result = await validator.ValidateAsync(candidate, GetVerseCountAsync);

            Assert.True(result.Accepted);
            Assert.Equal(45, result.Candidate.BookId);
            Assert.Equal(1, result.Candidate.Chapter);
            Assert.Equal(1, result.Candidate.StartVerse);
            Assert.Equal(32, result.Candidate.EndVerse);
        }

        [Fact]
        public async Task ValidateAsync_ChapterOnlyCandidate_ExpandsToWholeChapterRange()
        {
            var validator = new AiScriptureCandidateValidator();
            var candidate = new AiScriptureCandidate
            {
                BookName = "罗马书",
                Chapter = 8,
                StartVerse = 0,
                EndVerse = 0,
                Confidence = 0.95
            };

            var result = await validator.ValidateAsync(candidate, GetVerseCountAsync);

            Assert.True(result.Accepted);
            Assert.Equal(45, result.Candidate.BookId);
            Assert.Equal(8, result.Candidate.Chapter);
            Assert.Equal(1, result.Candidate.StartVerse);
            Assert.Equal(39, result.Candidate.EndVerse);
        }

        private static Task<int> GetVerseCountAsync(int book, int chapter)
        {
            if (book == 45 && chapter == 1)
            {
                return Task.FromResult(32);
            }

            if (book == 45 && chapter == 8)
            {
                return Task.FromResult(39);
            }

            return Task.FromResult(0);
        }
    }
}
