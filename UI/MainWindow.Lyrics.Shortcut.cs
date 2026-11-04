using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using ImageColorChanger.UI.Modules;
using Microsoft.EntityFrameworkCore;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace ImageColorChanger.UI
{
    public partial class MainWindow
    {
        private LyricsImageShortcutController _lyricsImageShortcutController;

        private bool TryHandleLyricsImageShortcut(KeyEventArgs e)
        {
            if (e.Key != Key.F6 || Keyboard.Modifiers != ModifierKeys.None
                || !IsActive || IsLyricsImeCompositionActive() || _isLoadingLyricsProject) return false;

            // 搜索/重命名等输入框不属于歌词编辑区域，不借用它们的 F6。
            if (Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase
                && LyricsEditorPanel?.IsKeyboardFocusWithin != true) return false;

            try
            {
                var mode = GetLyricsImageShortcutMode();
                if (mode == LyricsImageShortcutMode.Unavailable) return false;
                _lyricsImageShortcutController ??= new LyricsImageShortcutController(
                    EnterImageLyricsByShortcut,
                    () => TrySaveLyricsProject("F6"),
                    () => { StopAutoSaveTimer(); CompleteLyricsModeExit(); });

                return _lyricsImageShortcutController.TryHandle(e.Key, Keyboard.Modifiers, e.IsRepeat,
                    new LyricsImageShortcutState(mode, _currentImageId, IsActive,
                        IsLyricsImeCompositionActive(), _isLoadingLyricsProject));
            }
            catch (Exception ex)
            {
                // 不允许图片查询/切换异常逸出主窗口的 async void 键盘入口。
                ShowStatus($"歌词快捷切换失败：{ex.Message}");
                return true;
            }
        }

        private LyricsImageShortcutMode GetLyricsImageShortcutMode()
        {
            if (_isLyricsMode)
            {
                return _lyricsEntryKind == LyricsEntryKind.ImageBound && _lyricsEntryId > 0
                    && _lyricsEntryId == _currentImageId && LyricsEditorPanel?.Visibility == Visibility.Visible
                    ? LyricsImageShortcutMode.ImageLyrics : LyricsImageShortcutMode.Unavailable;
            }
            if (_isBibleMode || _currentImageId <= 0 || ImageScrollViewer?.Visibility != Visibility.Visible
                || VideoContainer?.Visibility == Visibility.Visible || TextEditorPanel?.Visibility == Visibility.Visible
                || _dbContext == null) return LyricsImageShortcutMode.Unavailable;

            var media = _dbContext.MediaFiles.AsNoTracking().FirstOrDefault(m => m.Id == _currentImageId);
            return media != null && string.Equals(media.FileTypeString, "image", StringComparison.OrdinalIgnoreCase)
                ? LyricsImageShortcutMode.Image : LyricsImageShortcutMode.Unavailable;
        }

        private bool EnterImageLyricsByShortcut(int imageId)
        {
            // 与现有图片右键入口相同的权限和绑定流程；不另建歌词存储路径。
            if (!TryRequirePremiumFeature(Services.Licensing.PremiumFeature.Lyrics)) return false;
            var previousKind = _lyricsEntryKind;
            int previousId = _lyricsEntryId;
            int previousProjectId = _currentLyricsProjectId;
            var previousProject = _currentLyricsProject;
            bool entered = false;
            SetLyricsEntryByImage(imageId);
            try
            {
                EnterLyricsMode();
                entered = _isLyricsMode && _currentLyricsProject?.ImageId == imageId;
                return entered;
            }
            finally
            {
                if (!entered)
                {
                    _lyricsEntryKind = previousKind;
                    _lyricsEntryId = previousId;
                    _currentLyricsProjectId = previousProjectId;
                    _currentLyricsProject = previousProject;
                    StopAutoSaveTimer();
                    CompleteLyricsModeExit();
                }
            }
        }
    }
}
