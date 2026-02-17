using System;

namespace ImageColorChanger.Managers
{
    /// <summary>
    /// 基于委托的投影宿主适配器，避免 Projection 侧耦合具体 UI 类型。
    /// </summary>
    public sealed class DelegateProjectionHost : IProjectionHost
    {
        private readonly Func<bool> _isInLyricsMode;
        private readonly Action _switchToPreviousSimilarImage;
        private readonly Action _switchToNextSimilarImage;
        private readonly Action _recordProjectionSync;
        private readonly Action<System.Windows.Input.KeyEventArgs> _forwardProjectionKeyDown;

        public DelegateProjectionHost(
            Func<bool> isInLyricsMode,
            Action switchToPreviousSimilarImage,
            Action switchToNextSimilarImage,
            Action recordProjectionSync,
            Action<System.Windows.Input.KeyEventArgs> forwardProjectionKeyDown)
        {
            _isInLyricsMode = isInLyricsMode ?? throw new ArgumentNullException(nameof(isInLyricsMode));
            _switchToPreviousSimilarImage = switchToPreviousSimilarImage ?? throw new ArgumentNullException(nameof(switchToPreviousSimilarImage));
            _switchToNextSimilarImage = switchToNextSimilarImage ?? throw new ArgumentNullException(nameof(switchToNextSimilarImage));
            _recordProjectionSync = recordProjectionSync ?? throw new ArgumentNullException(nameof(recordProjectionSync));
            _forwardProjectionKeyDown = forwardProjectionKeyDown ?? throw new ArgumentNullException(nameof(forwardProjectionKeyDown));
        }

        public bool IsInLyricsMode => _isInLyricsMode();

        public void SwitchToPreviousSimilarImage() => _switchToPreviousSimilarImage();

        public void SwitchToNextSimilarImage() => _switchToNextSimilarImage();

        public void RecordProjectionSync() => _recordProjectionSync();

        public void ForwardProjectionKeyDown(System.Windows.Input.KeyEventArgs e) => _forwardProjectionKeyDown(e);
    }
}
