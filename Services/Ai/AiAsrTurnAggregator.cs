using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using ImageColorChanger.Core;

namespace ImageColorChanger.Services.Ai
{
    public sealed class AiAsrTurnAggregator
    {
        private static readonly Regex ChapterLikeRegex = new(@"[0-9一二三四五六七八九十百]+[章:：]", RegexOptions.Compiled);
        private readonly int _minTextLength;
        private readonly int _minInterimTextLength;
        private readonly TimeSpan _interimSilenceTimeout;
        private readonly TimeSpan _minTurnInterval;
        private string _lastHash = string.Empty;
        private DateTimeOffset _lastTurnAcceptedAt = DateTimeOffset.MinValue;
        private string _pendingInterimText = string.Empty;
        private DateTimeOffset _pendingInterimUpdatedAt = DateTimeOffset.MinValue;

        public AiAsrTurnAggregator(
            int minTextLength = 8,
            int minInterimTextLength = 14,
            int interimSilenceTimeoutSeconds = 2,
            int minTurnIntervalSeconds = 8)
        {
            _minTextLength = Math.Max(1, minTextLength);
            _minInterimTextLength = Math.Max(_minTextLength, minInterimTextLength);
            _interimSilenceTimeout = TimeSpan.FromSeconds(Math.Max(1, interimSilenceTimeoutSeconds));
            _minTurnInterval = TimeSpan.FromSeconds(Math.Max(1, minTurnIntervalSeconds));
        }

        public bool TryAccept(string text, bool isFinal, DateTimeOffset capturedAt, out AiAsrTurnEnvelope turn)
        {
            turn = null;
            string normalized = NormalizeText(text);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return false;
            }

            bool containsReferenceHint = ContainsExplicitReferenceHint(normalized);
            if (normalized.Length < _minTextLength && !containsReferenceHint)
            {
                return false;
            }

            if (isFinal)
            {
                _pendingInterimText = string.Empty;
                _pendingInterimUpdatedAt = DateTimeOffset.MinValue;
                return TryBuildTurn(normalized, isFinal: true, capturedAt, "realtime-asr-final", out turn);
            }

            bool longEnoughForInterim = normalized.Length >= _minInterimTextLength;
            if (containsReferenceHint || longEnoughForInterim)
            {
                _pendingInterimText = normalized;
                _pendingInterimUpdatedAt = capturedAt;
            }

            return TryFlushPendingInterim(capturedAt, out turn);
        }

        public bool TryFlushPendingInterim(DateTimeOffset now, out AiAsrTurnEnvelope turn)
        {
            turn = null;
            if (string.IsNullOrWhiteSpace(_pendingInterimText))
            {
                return false;
            }

            if (now - _pendingInterimUpdatedAt < _interimSilenceTimeout)
            {
                return false;
            }

            if (now - _lastTurnAcceptedAt < _minTurnInterval)
            {
                return false;
            }

            string text = _pendingInterimText;
            _pendingInterimText = string.Empty;
            _pendingInterimUpdatedAt = DateTimeOffset.MinValue;
            return TryBuildTurn(text, isFinal: false, now, "realtime-asr-interim-silence", out turn);
        }

        private bool TryBuildTurn(string normalized, bool isFinal, DateTimeOffset capturedAt, string source, out AiAsrTurnEnvelope turn)
        {
            turn = null;
            string hash = ComputeStableHash(normalized);
            if (string.Equals(_lastHash, hash, StringComparison.Ordinal))
            {
                return false;
            }

            _lastHash = hash;
            _lastTurnAcceptedAt = capturedAt;
            turn = new AiAsrTurnEnvelope
            {
                TurnId = $"asr-{capturedAt:yyyyMMddHHmmssfff}",
                Text = normalized,
                IsFinal = isFinal,
                CapturedAt = capturedAt,
                Source = source
            };
            return true;
        }

        private static string NormalizeText(string text)
        {
            return (text ?? string.Empty)
                .Replace('\r', ' ')
                .Replace('\n', ' ')
                .Trim();
        }

        private static bool ContainsExplicitReferenceHint(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            if (ChapterLikeRegex.IsMatch(text))
            {
                return true;
            }

            foreach (var book in BibleBookConfig.Books)
            {
                if (!string.IsNullOrWhiteSpace(book.Name) && text.Contains(book.Name, StringComparison.Ordinal))
                {
                    return true;
                }

                if (!string.IsNullOrWhiteSpace(book.ShortName) && text.Contains(book.ShortName, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static string ComputeStableHash(string text)
        {
            byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text ?? string.Empty));
            return Convert.ToHexString(bytes);
        }
    }
}
