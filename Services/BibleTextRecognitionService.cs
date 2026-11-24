using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ImageColorChanger.Core;
using ImageColorChanger.Services.Interfaces;

namespace ImageColorChanger.Services
{
    public sealed class BibleTextRecognitionResult
    {
        public bool Success { get; init; }
        public bool IsDuplicate { get; init; }
        public string RecognizedText { get; init; } = string.Empty;
        public BibleSpeechReference Reference { get; init; }
        public int FinalEndVerse { get; init; }
        public string MatchKind { get; init; } = string.Empty;
        public string FailureReason { get; init; } = string.Empty;
    }

    /// <summary>每个会话一个实例。只处理文字，不依赖麦克风、语音平台、AI或UI。</summary>
    public sealed class BibleTextRecognitionService : IBibleTextRecognitionService
    {
        private readonly IBibleService _bible;
        private readonly BibleSpeechReverseLookupService _reverse;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private int _book;
        private int _chapter;
        private DateTime _contextAt;
        private BibleSpeechReference _lastAccepted;
        private DateTime _acceptedAt;

        public BibleTextRecognitionService(IBibleService bible, BibleSpeechReverseLookupService reverse = null)
        {
            _bible = bible ?? throw new ArgumentNullException(nameof(bible));
            _reverse = reverse ?? new BibleSpeechReverseLookupService();
        }

        public async Task<BibleTextRecognitionResult> ResolveAsync(string text, bool isFinal, CancellationToken cancellationToken)
        {
            text = (text ?? string.Empty).Trim();
            if (text.Length == 0) return new BibleTextRecognitionResult { FailureReason = "empty-transcript" };
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var candidates = BibleSpeechTextNormalizer.BuildReferenceCandidates(text, aggressive: isFinal, allowInferVerseUnit: isFinal);
                string kind;
                BibleSpeechReference reference;
                if (TryParse(candidates, out reference)) kind = "direct";
                else if (!isFinal) return new BibleTextRecognitionResult { RecognizedText = text, FailureReason = "unresolved-reference" };
                else if (TryResolveContext(candidates, out reference)) kind = "context";
                else
                {
                    var reversed = await _reverse.TryResolveAsync(_bible, text, cancellationToken).ConfigureAwait(false);
                    if (!reversed.HasValue) return new BibleTextRecognitionResult { RecognizedText = text, FailureReason = "unresolved-reference" };
                    reference = reversed.Value;
                    kind = "content";
                }

                cancellationToken.ThrowIfCancellationRequested();
                int end = reference.EndVerse;
                if (end <= 0)
                {
                    int count = await _bible.GetVerseCountAsync(reference.BookId, reference.Chapter).WaitAsync(cancellationToken).ConfigureAwait(false);
                    end = count > 0 ? count : Math.Max(1, reference.StartVerse);
                }
                cancellationToken.ThrowIfCancellationRequested();
                var resolved = new BibleSpeechReference(reference.BookId, reference.Chapter, Math.Max(1, reference.StartVerse), Math.Max(1, end));
                DateTime now = DateTime.UtcNow;
                bool duplicate = BibleRecognitionDuplicatePolicy.IsDuplicateReference(_lastAccepted, resolved) && now - _acceptedAt < TimeSpan.FromSeconds(15);
                _book = resolved.BookId;
                _chapter = resolved.Chapter;
                _contextAt = now;
                if (!duplicate)
                {
                    _lastAccepted = resolved;
                    _acceptedAt = now;
                    if (kind != "content") _reverse.NotifyDirectParse(_book, _chapter, resolved.StartVerse);
                }
                return new BibleTextRecognitionResult
                {
                    Success = true, IsDuplicate = duplicate, RecognizedText = text,
                    Reference = resolved, FinalEndVerse = resolved.EndVerse, MatchKind = kind
                };
            }
            finally { _gate.Release(); }
        }

        private static bool TryParse(IReadOnlyList<string> candidates, out BibleSpeechReference reference)
        {
            reference = default;
            int best = int.MinValue;
            bool matched = false;
            foreach (string candidate in candidates)
            {
                if (!BibleSpeechReferenceParser.TryParse(candidate, out var parsed)) continue;
                int score = (parsed.EndVerse > 0 ? 6 : 0) + (parsed.StartVerse > 1 ? 3 : 0)
                    + (candidate.Contains("节", StringComparison.Ordinal) || candidate.Contains(":", StringComparison.Ordinal) ? 2 : 0)
                    + (candidate.Contains("章", StringComparison.Ordinal) ? 1 : 0);
                if (!matched || score > best) { matched = true; best = score; reference = parsed; }
            }
            return matched;
        }

        private bool TryResolveContext(IReadOnlyList<string> candidates, out BibleSpeechReference reference)
        {
            reference = default;
            if (_book <= 0 || _chapter <= 0 || DateTime.UtcNow - _contextAt > TimeSpan.FromSeconds(12)) return false;
            string bookName = BibleBookConfig.GetBook(_book)?.Name;
            if (string.IsNullOrWhiteSpace(bookName)) return false;
            int best = int.MinValue;
            bool matched = false;
            foreach (string candidate in candidates)
            {
                string synthetic = $"{bookName}{_chapter}章{candidate}";
                if (!BibleSpeechReferenceParser.TryParse(synthetic, out var parsed) || parsed.BookId != _book || parsed.Chapter != _chapter || parsed.EndVerse <= 0)
                    continue;
                int score = (candidate.Contains("节", StringComparison.Ordinal) || candidate.Contains(":", StringComparison.Ordinal) ? 3 : 0)
                    + (parsed.StartVerse > 1 ? 2 : 0) + 1;
                if (!matched || score > best) { matched = true; best = score; reference = parsed; }
            }
            return matched;
        }
    }
}
