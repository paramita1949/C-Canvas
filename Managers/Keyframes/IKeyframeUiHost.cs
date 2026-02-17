using System;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Threading;

namespace ImageColorChanger.Managers.Keyframes
{
    /// <summary>
    /// 关键帧模块的UI宿主能力，隔离对 MainWindow 的强类型依赖。
    /// </summary>
    public interface IKeyframeUiHost
    {
        Dispatcher Dispatcher { get; }
        ScrollViewer ImageScrollViewer { get; }
        bool IsProjectionEnabled { get; }
        int CurrentImageId { get; }
        bool IsPlaybackRecording { get; }
        bool CanToggleRecording { get; }
        bool CanTogglePlayback { get; }

        void ShowStatus(string message);
        void SetAutoProjectionSyncEnabled(bool enabled);
        void UpdateProjection();
        void UpdateKeyframeIndicators();
        void UpdatePreviewLines();
        void StartFpsMonitoring();
        void StopFpsMonitoring();
        void TriggerCompositePlayback();

        Task RecordKeyframeTimeAsync(int keyframeId);
        Task ToggleRecordingAsync();
        Task TogglePlaybackAsync();
        void PostToUi(Func<Task> action);
    }
}
