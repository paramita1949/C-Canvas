using System;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Threading;
using ImageColorChanger.Managers.Keyframes;

namespace ImageColorChanger.UI
{
    /// <summary>
    /// MainWindow 对 Keyframe UI宿主接口的实现。
    /// </summary>
    public partial class MainWindow
    {
        Dispatcher IKeyframeUiHost.Dispatcher => Dispatcher;
        ScrollViewer IKeyframeUiHost.ImageScrollViewer => ImageScrollViewer;
        bool IKeyframeUiHost.IsProjectionEnabled => IsProjectionEnabled;
        int IKeyframeUiHost.CurrentImageId => GetCurrentImageId();
        bool IKeyframeUiHost.IsPlaybackRecording => _playbackViewModel?.IsRecording == true;
        bool IKeyframeUiHost.CanToggleRecording => _playbackViewModel?.ToggleRecordingCommand?.CanExecute(null) == true;
        bool IKeyframeUiHost.CanTogglePlayback => _playbackViewModel?.TogglePlaybackCommand?.CanExecute(null) == true;

        void IKeyframeUiHost.ShowStatus(string message) => ShowStatus(message);
        void IKeyframeUiHost.SetAutoProjectionSyncEnabled(bool enabled) => SetAutoProjectionSyncEnabled(enabled);
        void IKeyframeUiHost.UpdateProjection() => UpdateProjection();
        void IKeyframeUiHost.UpdateKeyframeIndicators() => UpdateKeyframeIndicators();
        void IKeyframeUiHost.UpdatePreviewLines() => UpdatePreviewLines();
        void IKeyframeUiHost.StartFpsMonitoring() => StartFpsMonitoring();
        void IKeyframeUiHost.StopFpsMonitoring() => StopFpsMonitoring();

        void IKeyframeUiHost.TriggerCompositePlayback()
        {
            BtnFloatingCompositePlay?.RaiseEvent(new System.Windows.RoutedEventArgs(
                System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        }

        Task IKeyframeUiHost.RecordKeyframeTimeAsync(int keyframeId)
        {
            return _playbackViewModel?.RecordKeyframeTimeAsync(keyframeId) ?? Task.CompletedTask;
        }

        Task IKeyframeUiHost.ToggleRecordingAsync()
        {
            if (_playbackViewModel?.ToggleRecordingCommand != null)
            {
                return _playbackViewModel.ToggleRecordingCommand.ExecuteAsync(null);
            }

            return Task.CompletedTask;
        }

        Task IKeyframeUiHost.TogglePlaybackAsync()
        {
            if (_playbackViewModel?.TogglePlaybackCommand != null)
            {
                return _playbackViewModel.TogglePlaybackCommand.ExecuteAsync(null);
            }

            return Task.CompletedTask;
        }

        void IKeyframeUiHost.PostToUi(Func<Task> action)
        {
            if (action == null)
            {
                return;
            }

            _ = Dispatcher.InvokeAsync(action, DispatcherPriority.Background);
        }
    }
}
