using System;
using System.Windows.Input;

namespace ImageColorChanger.UI.Modules
{
    public enum LyricsImageShortcutMode { Unavailable, Image, ImageLyrics }

    public readonly record struct LyricsImageShortcutState(
        LyricsImageShortcutMode Mode, int ImageId, bool IsForeground, bool IsComposing, bool IsLoading);

    /// <summary>只编排 F6 切换；图片身份、原生输入状态和保存结果由宿主提供。</summary>
    public sealed class LyricsImageShortcutController
    {
        private readonly Func<int, bool> _enter;
        private readonly Func<bool> _save;
        private readonly Action _exit;
        private bool _isSwitching;

        public LyricsImageShortcutController(Func<int, bool> enter, Func<bool> save, Action exit)
        {
            _enter = enter ?? throw new ArgumentNullException(nameof(enter));
            _save = save ?? throw new ArgumentNullException(nameof(save));
            _exit = exit ?? throw new ArgumentNullException(nameof(exit));
        }

        public bool TryHandle(Key key, ModifierKeys modifiers, bool isRepeat, LyricsImageShortcutState state)
        {
            if (key != Key.F6 || modifiers != ModifierKeys.None || !state.IsForeground
                || state.IsComposing || state.IsLoading || state.ImageId <= 0
                || state.Mode == LyricsImageShortcutMode.Unavailable) return false;

            // 按住 F6 的自动重复与同步回调重入都不能切回去。
            if (isRepeat || _isSwitching) return true;
            _isSwitching = true;
            try
            {
                if (state.Mode == LyricsImageShortcutMode.Image) _enter(state.ImageId);
                else if (state.Mode == LyricsImageShortcutMode.ImageLyrics && _save()) _exit();
                return true;
            }
            finally { _isSwitching = false; }
        }
    }
}
