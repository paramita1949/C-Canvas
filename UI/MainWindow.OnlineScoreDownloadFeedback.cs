using System;
using System.Diagnostics;
using System.Windows.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ImageColorChanger.Services.OnlineScores;

namespace ImageColorChanger.UI
{
    public partial class MainWindow
    {
        private DispatcherTimer _onlineScoreFeedbackTimer;
        private OnlineScoreResult _onlineScoreFeedbackResult;
        private Stopwatch _onlineScoreFeedbackClock;
        private Func<bool> _onlineScoreFeedbackIsCurrent;
        private string _onlineScoreFeedbackMessage;

        private IProgress<ScoreDownloadProgress> BeginOnlineScoreDownloadFeedback(OnlineScoreResult result, Func<bool> isCurrent)
        {
            StopOnlineScoreDownloadFeedback();
            _onlineScoreFeedbackResult = result;
            _onlineScoreFeedbackClock = Stopwatch.StartNew();
            _onlineScoreFeedbackIsCurrent = isCurrent;
            _onlineScoreFeedbackMessage = "下载较慢，正在等待…";
            _onlineScoreFeedbackTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(100) };
            _onlineScoreFeedbackTimer.Tick += OnlineScoreFeedback_Tick;
            _onlineScoreFeedbackTimer.Start();
            void ApplyProgress(ScoreDownloadProgress update)
            {
                // 取消/旧请求排队的 progress 回调不能覆盖新点击的状态。
                if (!isCurrent() || !ReferenceEquals(result, _onlineScoreFeedbackResult)) return;
                if (update.Stage == ScoreDownloadStage.SwitchingNode)
                {
                    _onlineScoreFeedbackMessage = update.Message;
                    result.SetPreparingDownload(true);
                    OnlineScoreStatus.Text = update.Message;
                }
                else if (update.Stage != ScoreDownloadStage.Completed)
                    _onlineScoreFeedbackMessage = update.Message;
                OnlineScoreFeedback_Tick(null, EventArgs.Empty);
            }
            var dispatcher = OnlineScoreStatus.Dispatcher;
            return new Progress<ScoreDownloadProgress>(update =>
            {
                if (dispatcher.CheckAccess()) ApplyProgress(update);
                else if (!dispatcher.HasShutdownStarted)
                    dispatcher.BeginInvoke(new Action(() => ApplyProgress(update)));
            });
        }

        private void OnlineScoreFeedback_Tick(object sender, EventArgs e)
        {
            if (_onlineScoreFeedbackResult == null) return;
            if (_onlineScoreFeedbackIsCurrent?.Invoke() != true) { StopOnlineScoreDownloadFeedback(); return; }
            if (_onlineScoreFeedbackClock.ElapsedMilliseconds >= 500)
                _onlineScoreFeedbackResult.SetPreparingDownload(true);
            if (_onlineScoreFeedbackClock.ElapsedMilliseconds >= 3000)
                OnlineScoreStatus.Text = _onlineScoreFeedbackMessage;
        }

        private void SetOnlineScoreDownloadPhase(string message)
        {
            _onlineScoreFeedbackMessage = message;
            OnlineScoreFeedback_Tick(null, EventArgs.Empty);
        }

        private void StopOnlineScoreDownloadFeedback()
        {
            if (_onlineScoreFeedbackTimer != null)
            {
                _onlineScoreFeedbackTimer.Stop();
                _onlineScoreFeedbackTimer.Tick -= OnlineScoreFeedback_Tick;
                _onlineScoreFeedbackTimer = null;
            }
            _onlineScoreFeedbackResult?.SetPreparingDownload(false);
            _onlineScoreFeedbackResult = null;
            _onlineScoreFeedbackClock = null;
            _onlineScoreFeedbackIsCurrent = null;
        }

        private static void RefreshScoreDownloadSpinner(object sender, bool unloaded = false)
        {
            if (sender is not Grid grid || grid.Children.Count == 0 ||
                grid.Children[0].RenderTransform is not RotateTransform rotation) return;
            bool animate = !unloaded && grid.IsLoaded && grid.IsVisible;
            if (rotation.HasAnimatedProperties == animate) return;
            rotation.BeginAnimation(RotateTransform.AngleProperty, animate
                ? new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1)) { RepeatBehavior = RepeatBehavior.Forever } : null);
            if (!animate) rotation.Angle = 0;
        }

        private void ScoreDownloadSpinner_Loaded(object sender, RoutedEventArgs e) => RefreshScoreDownloadSpinner(sender);
        private void ScoreDownloadSpinner_Unloaded(object sender, RoutedEventArgs e) => RefreshScoreDownloadSpinner(sender, true);
        private void ScoreDownloadSpinner_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e) => RefreshScoreDownloadSpinner(sender);
    }
}
