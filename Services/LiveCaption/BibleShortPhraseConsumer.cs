using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ImageColorChanger.Services.Interfaces;

namespace ImageColorChanger.Services.LiveCaption
{
    internal sealed class BibleShortPhraseConsumer
    {
        internal sealed class Result
        {
            public bool Success { get; init; }
            public string RecognizedText { get; init; } = string.Empty;
            public BibleSpeechReference Reference { get; init; }
            public int FinalEndVerse { get; init; }
            public string FailureReason { get; init; } = string.Empty;
        }

        private readonly IBibleService _bibleService;
        private readonly Func<byte[], CancellationToken, Task<string>> _transcribeAsync;
        private readonly BibleSpeechReverseLookupService _reverseLookupService;
        private readonly Action<string> _log;

        public BibleShortPhraseConsumer(
            IBibleService bibleService,
            Func<byte[], CancellationToken, Task<string>> transcribeAsync,
            BibleSpeechReverseLookupService reverseLookupService = null,
            Action<string> log = null)
        {
            _bibleService = bibleService ?? throw new ArgumentNullException(nameof(bibleService));
            _transcribeAsync = transcribeAsync ?? throw new ArgumentNullException(nameof(transcribeAsync));
            _reverseLookupService = reverseLookupService ?? new BibleSpeechReverseLookupService();
            _log = log ?? (_ => { });
        }

        public async Task<Result> ProcessPcmAsync(byte[] pcmBytes, CancellationToken cancellationToken)
        {
            byte[] pcm = pcmBytes ?? Array.Empty<byte>();
            if (pcm.Length < 3200)
            {
                _log("Ignored: pcm too short.");
                return new Result { FailureReason = "audio-too-short" };
            }

            byte[] wavBytes = BuildWavFromPcm16kMono(pcm);
            _log($"WAV built. pcm={pcm.Length}, wav={wavBytes.Length}");

            string recognized;
            try
            {
                recognized = await _transcribeAsync(wavBytes, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                _log("Transcribe canceled.");
                return new Result { FailureReason = "canceled" };
            }

            recognized = (recognized ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(recognized))
            {
                _log("Transcribe returned empty text.");
                return new Result { FailureReason = "empty-transcript" };
            }

            _log($"Transcribe result: {recognized}");

            BibleSpeechReference reference;
            if (!BibleSpeechReferenceParser.TryParse(recognized, out reference))
            {
                _log("Direct parse failed. Trying reverse lookup.");
                BibleSpeechReference? reversed = await _reverseLookupService.TryResolveAsync(_bibleService, recognized, cancellationToken);
                if (!reversed.HasValue)
                {
                    _log("Reverse lookup failed.");
                    return new Result
                    {
                        RecognizedText = recognized,
                        FailureReason = "unresolved-reference"
                    };
                }

                reference = reversed.Value;
            }

            int finalEndVerse = reference.EndVerse;
            if (finalEndVerse <= 0)
            {
                int verseCount = await _bibleService.GetVerseCountAsync(reference.BookId, reference.Chapter);
                finalEndVerse = verseCount > 0 ? verseCount : Math.Max(1, reference.StartVerse);
            }

            _log($"Resolved reference: book={reference.BookId}, chapter={reference.Chapter}, start={reference.StartVerse}, end={finalEndVerse}");
            return new Result
            {
                Success = true,
                RecognizedText = recognized,
                Reference = reference,
                FinalEndVerse = finalEndVerse
            };
        }

        internal static byte[] BuildWavFromPcm16kMono(byte[] pcmBytes)
        {
            byte[] payload = pcmBytes ?? Array.Empty<byte>();
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + payload.Length);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
            writer.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)1);
            writer.Write(16000);
            writer.Write(16000 * 2);
            writer.Write((short)2);
            writer.Write((short)16);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
            writer.Write(payload.Length);
            writer.Write(payload);
            writer.Flush();
            return ms.ToArray();
        }
    }
}
