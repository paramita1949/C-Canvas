using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using ImageColorChanger.Services;
using NAudio.Wave;

namespace ImageColorChanger.UI
{
    public partial class MainWindow
    {
        private WaveInEvent _bibleAiVoiceCapture;
        private MemoryStream _bibleAiVoicePcm;
        private CancellationTokenSource _bibleAiVoiceCts;
        private bool _isBibleAiVoiceListening;
        private BibleBaiduShortSpeechClient _bibleBaiduShortSpeechClient;
        private BibleSpeechReverseLookupService _bibleSpeechReverseLookupService;

        private static void LogBibleAiVoice(string message)
        {
            Debug.WriteLine($"[BibleVoice] {message}");
        }

        private async void BtnBibleAiVoice_Click(object sender, RoutedEventArgs e)
        {
            LogBibleAiVoice("UI trigger invoked.");
            if (_configManager?.BibleAiVoiceRecognitionEnabled != true)
            {
                LogBibleAiVoice("Blocked: BibleAiVoiceRecognitionEnabled=false");
                ShowStatus("圣经 AI短语未启用，请先在圣经设置中勾选");
                return;
            }

            if (_isBibleAiVoiceListening)
            {
                await StopBibleAiVoiceAndProcessAsync(manualStop: true);
                return;
            }

            await StartBibleAiVoiceCaptureAsync();
        }

        private async Task StartBibleAiVoiceCaptureAsync()
        {
            if (_bibleService == null)
            {
                InitializeBibleService();
                LogBibleAiVoice("Bible service initialized on demand.");
            }

            _bibleBaiduShortSpeechClient ??= new BibleBaiduShortSpeechClient(_configManager);
            if (!_bibleBaiduShortSpeechClient.IsConfigured)
            {
                LogBibleAiVoice("Blocked: Baidu short speech client not configured.");
                ShowStatus("百度语音凭据未配置，请先在 AI 配置中填写 AppID/API Key/Secret Key");
                return;
            }

            try
            {
                _bibleAiVoiceCts?.Cancel();
                _bibleAiVoiceCts?.Dispose();
                _bibleAiVoiceCts = new CancellationTokenSource();

                _bibleAiVoicePcm?.Dispose();
                _bibleAiVoicePcm = new MemoryStream();

                _bibleAiVoiceCapture?.Dispose();
                _bibleAiVoiceCapture = new WaveInEvent
                {
                    WaveFormat = new WaveFormat(16000, 16, 1),
                    BufferMilliseconds = 100
                };
                _bibleAiVoiceCapture.DataAvailable += BibleAiVoiceCapture_DataAvailable;
                _bibleAiVoiceCapture.StartRecording();

                _isBibleAiVoiceListening = true;
                SetBibleAiVoiceButtonState(listening: true);
                LogBibleAiVoice("Recording started. format=pcm16k mono, auto-stop=4.5s");
                ShowStatus("圣经语音识别已开始，请说经文（自动结束）");

                var localCts = _bibleAiVoiceCts;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(4.5), localCts.Token);
                        await Dispatcher.InvokeAsync(async () =>
                        {
                            if (_isBibleAiVoiceListening)
                            {
                                await StopBibleAiVoiceAndProcessAsync(manualStop: false);
                            }
                        });
                    }
                    catch (OperationCanceledException)
                    {
                    }
                });
            }
            catch (Exception ex)
            {
                _isBibleAiVoiceListening = false;
                SetBibleAiVoiceButtonState(listening: false);
                LogBibleAiVoice($"Start failed: {ex.GetType().Name}: {ex.Message}");
                ShowStatus($"启动圣经语音识别失败: {ex.Message}");
            }
        }

        private async Task StopBibleAiVoiceAndProcessAsync(bool manualStop)
        {
            if (!_isBibleAiVoiceListening)
            {
                return;
            }

            _isBibleAiVoiceListening = false;
            SetBibleAiVoiceButtonState(listening: false);

            try
            {
                _bibleAiVoiceCts?.Cancel();
                if (_bibleAiVoiceCapture != null)
                {
                    _bibleAiVoiceCapture.DataAvailable -= BibleAiVoiceCapture_DataAvailable;
                    _bibleAiVoiceCapture.StopRecording();
                    _bibleAiVoiceCapture.Dispose();
                    _bibleAiVoiceCapture = null;
                }
            }
            catch
            {
            }

            byte[] pcmBytes = _bibleAiVoicePcm?.ToArray() ?? Array.Empty<byte>();
            _bibleAiVoicePcm?.Dispose();
            _bibleAiVoicePcm = null;
            LogBibleAiVoice($"Recording stopped. manualStop={manualStop}, pcmBytes={pcmBytes.Length}");

            if (pcmBytes.Length < 3200)
            {
                if (manualStop)
                {
                    ShowStatus("语音过短，未识别到经文");
                }
                return;
            }

            byte[] wavBytes = BuildWavFromPcm16kMono(pcmBytes);
            LogBibleAiVoice($"PCM converted to WAV. wavBytes={wavBytes.Length}");
            string recognized = await _bibleBaiduShortSpeechClient.TranscribeWavAsync(
                wavBytes,
                CancellationToken.None);
            LogBibleAiVoice($"Transcribe completed. recognized='{recognized}'");

            if (string.IsNullOrWhiteSpace(recognized))
            {
                LogBibleAiVoice("Transcribe returned empty text.");
                ShowStatus("语音识别失败，请重试");
                return;
            }

            BibleSpeechReference reference;
            if (!BibleSpeechReferenceParser.TryParse(recognized, out reference))
            {
                LogBibleAiVoice("Direct parser failed. Trying reverse lookup.");
                _bibleSpeechReverseLookupService ??= new BibleSpeechReverseLookupService();
                var reversed = await _bibleSpeechReverseLookupService.TryResolveAsync(
                    _bibleService,
                    recognized,
                    CancellationToken.None);

                if (!reversed.HasValue)
                {
                    LogBibleAiVoice("Reverse lookup failed.");
                    ShowStatus($"未识别到经文引用：{recognized}");
                    return;
                }

                reference = reversed.Value;
                LogBibleAiVoice($"Reverse lookup resolved: book={reference.BookId}, chapter={reference.Chapter}, start={reference.StartVerse}, end={reference.EndVerse}");
            }
            else
            {
                LogBibleAiVoice($"Direct parse resolved: book={reference.BookId}, chapter={reference.Chapter}, start={reference.StartVerse}, end={reference.EndVerse}");
            }

            int endVerse = reference.EndVerse;
            if (endVerse <= 0)
            {
                int verseCount = await _bibleService.GetVerseCountAsync(reference.BookId, reference.Chapter);
                endVerse = verseCount > 0 ? verseCount : reference.StartVerse;
                LogBibleAiVoice($"Chapter-only resolved verse count. verseCount={verseCount}, finalEndVerse={endVerse}");
            }

            var finalReference = new BibleSpeechReference(
                reference.BookId,
                reference.Chapter,
                Math.Max(1, reference.StartVerse),
                Math.Max(1, endVerse));

            ShowToast(FormatBibleReferenceToastText(
                finalReference.BookId,
                finalReference.Chapter,
                finalReference.StartVerse,
                finalReference.EndVerse));
            AddPinyinHistoryToEmptySlot(
                finalReference.BookId,
                finalReference.Chapter,
                finalReference.StartVerse,
                finalReference.EndVerse);
            LogBibleAiVoice($"History slot updated. book={reference.BookId}, chapter={reference.Chapter}, start={reference.StartVerse}, end={endVerse}");

            ShowStatus($"已识别并加入历史槽：{recognized}");
        }

        private void BibleAiVoiceCapture_DataAvailable(object sender, WaveInEventArgs e)
        {
            if (!_isBibleAiVoiceListening || e == null || e.BytesRecorded <= 0 || _bibleAiVoicePcm == null)
            {
                return;
            }

            _bibleAiVoicePcm.Write(e.Buffer, 0, e.BytesRecorded);
        }

        private void SetBibleAiVoiceButtonState(bool listening)
        {
            _ = listening;
        }

        private static byte[] BuildWavFromPcm16kMono(byte[] pcmBytes)
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
