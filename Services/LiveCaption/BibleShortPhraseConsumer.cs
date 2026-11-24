using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ImageColorChanger.Services.LiveCaption
{
    internal sealed class BibleShortPhraseConsumer
    {
        internal sealed class Result
        {
            public bool Success { get; init; }
            public bool IsDuplicate { get; init; }
            public CancellationToken SessionToken { get; init; }
            public string RecognizedText { get; init; } = string.Empty;
            public BibleSpeechReference Reference { get; init; }
            public int FinalEndVerse { get; init; }
            public string FailureReason { get; init; } = string.Empty;
        }

        private readonly Func<byte[], CancellationToken, Task<string>> _transcribeAsync;
        private readonly Func<string, bool, CancellationToken, Task<BibleTextRecognitionResult>> _recognizeTextAsync;
        private readonly Action<string> _log;
        private readonly Func<CancellationToken> _getSessionToken;

        public BibleShortPhraseConsumer(
            Func<byte[], CancellationToken, Task<string>> transcribeAsync,
            Func<string, bool, CancellationToken, Task<BibleTextRecognitionResult>> recognizeTextAsync,
            Action<string> log = null,
            Func<CancellationToken> getSessionToken = null)
        {
            _transcribeAsync = transcribeAsync ?? throw new ArgumentNullException(nameof(transcribeAsync));
            _recognizeTextAsync = recognizeTextAsync ?? throw new ArgumentNullException(nameof(recognizeTextAsync));
            _log = log ?? (_ => { });
            _getSessionToken = getSessionToken ?? (() => CancellationToken.None);
        }

        public async Task<Result> ProcessPcmAsync(byte[] pcmBytes, CancellationToken cancellationToken)
        {
            var sessionToken = _getSessionToken();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, sessionToken);
            cancellationToken = linked.Token;
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

            // 转写只负责提供文字；经文能力由可替换的文字接口接入。
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var match = await _recognizeTextAsync(recognized, true, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                return new Result
                {
                    SessionToken = sessionToken,
                    Success = match.Success, RecognizedText = recognized, Reference = match.Reference,
                    FinalEndVerse = match.FinalEndVerse, FailureReason = match.FailureReason, IsDuplicate = match.IsDuplicate
                };
            }
            catch (OperationCanceledException) { return new Result { FailureReason = "canceled" }; }
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
