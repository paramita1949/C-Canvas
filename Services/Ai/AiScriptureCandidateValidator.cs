using System;
using System.Linq;
using System.Threading.Tasks;
using ImageColorChanger.Core;

namespace ImageColorChanger.Services.Ai
{
    public sealed class AiScriptureCandidateValidator
    {
        private readonly double _minWriteConfidence;

        public AiScriptureCandidateValidator(double minWriteConfidence = 0.55)
        {
            _minWriteConfidence = Math.Clamp(minWriteConfidence, 0.0, 1.0);
        }

        public async Task<AiScriptureCandidateValidationResult> ValidateAsync(
            AiScriptureCandidate candidate,
            Func<int, int, Task<int>> getVerseCountAsync)
        {
            if (candidate == null)
            {
                return AiScriptureCandidateValidationResult.Reject("candidate-null");
            }

            if (candidate.Confidence < _minWriteConfidence)
            {
                return AiScriptureCandidateValidationResult.Reject("confidence-too-low");
            }

            var book = candidate.BookId > 0
                ? BibleBookConfig.GetBook(candidate.BookId)
                : ResolveBook(candidate.BookName);
            if (book == null)
            {
                return AiScriptureCandidateValidationResult.Reject("unknown-book");
            }

            int chapter = candidate.Chapter <= 0 ? 1 : candidate.Chapter;
            if (chapter > book.ChapterCount)
            {
                return AiScriptureCandidateValidationResult.Reject("chapter-out-of-range");
            }

            int verseCount = getVerseCountAsync == null
                ? 0
                : await getVerseCountAsync(book.BookId, chapter).ConfigureAwait(false);
            if (verseCount <= 0)
            {
                return AiScriptureCandidateValidationResult.Reject("verse-out-of-range");
            }

            bool isBookOnly = candidate.Chapter <= 0;
            bool isChapterOnly = !isBookOnly && candidate.StartVerse <= 0;
            int startVerse;
            int endVerse;
            if (isBookOnly || isChapterOnly)
            {
                startVerse = 1;
                endVerse = verseCount;
            }
            else
            {
                startVerse = Math.Max(1, candidate.StartVerse);
                endVerse = candidate.EndVerse <= 0 ? startVerse : Math.Max(startVerse, candidate.EndVerse);
            }

            if (startVerse > verseCount || endVerse > verseCount)
            {
                return AiScriptureCandidateValidationResult.Reject("verse-out-of-range");
            }

            return AiScriptureCandidateValidationResult.Accept(new AiScriptureCandidate
            {
                BookName = book.Name,
                BookId = book.BookId,
                Chapter = chapter,
                StartVerse = startVerse,
                EndVerse = endVerse,
                Confidence = candidate.Confidence,
                Reason = candidate.Reason ?? string.Empty,
                EvidenceText = candidate.EvidenceText ?? string.Empty,
                SourceTurnId = candidate.SourceTurnId ?? string.Empty
            });
        }

        private static Database.Models.Bible.BibleBook ResolveBook(string bookName)
        {
            string normalized = NormalizeBookName(bookName);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return null;
            }

            return BibleBookConfig.Books.FirstOrDefault(book =>
                string.Equals(NormalizeBookName(book.Name), normalized, StringComparison.Ordinal) ||
                string.Equals(NormalizeBookName(book.ShortName), normalized, StringComparison.Ordinal));
        }

        private static string NormalizeBookName(string value)
        {
            return (value ?? string.Empty)
                .Replace(" ", string.Empty)
                .Replace("　", string.Empty)
                .Trim();
        }
    }
}
