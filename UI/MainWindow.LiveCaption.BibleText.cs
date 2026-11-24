using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using ImageColorChanger.Services;
using ImageColorChanger.Services.Interfaces;
using ImageColorChanger.Services.LiveCaption;

namespace ImageColorChanger.UI
{
    public partial class MainWindow
    {
        private IBibleTextRecognitionService _captionBibleTextService;
        private CancellationTokenSource _captionBibleCts = new();
        private CancellationToken _captionBibleToken;
        private readonly CaptionBibleTextBuffer _captionBibleTextBuffer = new();
        private DispatcherTimer _captionBibleTextTimer;
        private int _captionBibleTextRevision;
        private Task _captionBibleWorkTail = Task.CompletedTask;

        private void EnsureCaptionBibleTextComponents()
        {
            if (_bibleService == null) InitializeBibleService();
            if (_bibleVerseContentIndex == null)
            {
                _bibleVerseContentIndex = new BibleVerseContentIndex();
                var index = _bibleVerseContentIndex;
                _ = Task.Run(() => index.LoadAsync(_bibleService, dbFilePath: GetBibleDbFilePath()));
            }
            _captionBibleTextService ??= CreateCaptionBibleTextService();
            _captionBibleToken = _captionBibleCts.Token;
        }

        private IBibleTextRecognitionService CreateCaptionBibleTextService() => new BibleTextRecognitionService(
            _bibleService, new BibleSpeechReverseLookupService(_bibleVerseContentIndex, msg => LiveCaptionDebugLogger.Log("[文字经文] " + msg)));

        private void ResetCaptionBibleTextService()
        {
            ++_captionBibleTextRevision;
            _captionBibleWorkTail = Task.CompletedTask;
            _captionBibleCts.Cancel();
            _captionBibleCts.Dispose();
            _captionBibleCts = new CancellationTokenSource();
            _captionBibleToken = _captionBibleCts.Token;
            _captionBibleTextService = _bibleService == null ? null : CreateCaptionBibleTextService();
        }

        private void ResetCaptionBibleRecognition(string baseline = "")
        {
            _captionBibleTextTimer?.Stop();
            ResetCaptionBibleTextService();
            _captionBibleTextBuffer.Reset(baseline);
        }

        private void StartInputMethodBibleRecognition()
        {
            if (!_configManager.LiveCaptionShortPhraseEnabled) return;
            EnsureCaptionBibleTextComponents();
            _captionBibleTextTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Background,
                OnCaptionBibleTextTimerTick, Dispatcher);
            _captionBibleTextTimer.Start();
        }

        private void ObserveInputMethodBibleText(string text)
        {
            if (!IsLiveCaptionInputMethodActive() || !_configManager.LiveCaptionShortPhraseEnabled) return;
            if (_captionBibleTextBuffer.Observe(text, DateTimeOffset.UtcNow))
                ResetCaptionBibleTextService(); // 用户修订使旧片段结果和上下文失效；保留局部diff基线。
        }

        private void OnCaptionBibleTextTimerTick(object sender, EventArgs e)
        {
            if (!IsLiveCaptionInputMethodActive() || !_configManager.LiveCaptionShortPhraseEnabled ||
                _liveCaptionOverlayWindow?.HasPendingCaptionSnapshot != false || _liveCaptionOverlayWindow.IsCaptionTextComposing) return;
            QueueCaptionBibleProbes(_captionBibleTextBuffer.TryFlush(DateTimeOffset.UtcNow));
        }

        private async Task<BibleTextRecognitionResult> ResolveCaptionBibleTextAsync(string text, bool isFinal, CancellationToken cancellationToken)
        {
            var service = _captionBibleTextService;
            var sessionToken = _captionBibleToken;
            if (service == null) return new BibleTextRecognitionResult { FailureReason = "not-running" };
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(sessionToken, cancellationToken);
            return await service.ResolveAsync(text, isFinal, linked.Token).ConfigureAwait(false);
        }

        private void QueueCaptionBibleProbes(IReadOnlyList<CaptionBibleTextProbe> probes)
        {
            if (probes.Count == 0 || _captionBibleTextService == null) return;
            int revision = _captionBibleTextRevision;
            int stateRevision = _liveCaptionStateRevision;
            int documentRevision = _captionDocumentRevision;
            var service = _captionBibleTextService;
            var token = _captionBibleToken;
            var previous = _captionBibleWorkTail;
            _captionBibleWorkTail = Task.Run(async () =>
            {
                try
                {
                    await previous.WaitAsync(token).ConfigureAwait(false);
                    foreach (var probe in probes)
                    {
                        token.ThrowIfCancellationRequested();
                        var result = await service.ResolveAsync(probe.Text, probe.IsFinal, token).ConfigureAwait(false);
                        if (!result.Success || result.IsDuplicate) continue;
                        await Dispatcher.InvokeAsync(() =>
                        {
                            if (_isDisposingLiveCaption || token.IsCancellationRequested || revision != _captionBibleTextRevision ||
                                stateRevision != _liveCaptionStateRevision || documentRevision != _captionDocumentRevision) return;
                            PresentCaptionBibleResult(result);
                        });
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) { LiveCaptionDebugLogger.Log($"[文字经文] error {ex.GetType().Name}: {ex.Message}"); }
            });
        }

        private void PresentCaptionBibleResult(BibleTextRecognitionResult result)
        {
            var reference = result.Reference;
            LiveCaptionDebugLogger.Log($"[文字经文] {result.MatchKind} book={reference.BookId} ch={reference.Chapter} vs={reference.StartVerse}~{reference.EndVerse}");
            ShowToast(FormatBibleReferenceToastText(reference.BookId, reference.Chapter, reference.StartVerse, reference.EndVerse));
            AddPinyinHistoryToEmptySlot(reference.BookId, reference.Chapter, reference.StartVerse, reference.EndVerse, focusHistorySlot: true);
        }
    }
}
