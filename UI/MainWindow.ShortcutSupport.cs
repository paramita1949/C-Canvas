using System.Threading.Tasks;
using System.Windows;

namespace ImageColorChanger.UI
{
    /// <summary>
    /// MainWindow快捷键支持方法
    /// 为ShortcutActionHandler提供必要的访问接口
    /// </summary>
    public partial class MainWindow
    {
        #region 快捷键管理器

        private Utils.KeyboardShortcutManager _keyboardShortcutManager;

        /// <summary>
        /// 初始化快捷键管理器
        /// </summary>
        private void InitializeShortcutManagers()
        {
            _keyboardShortcutManager = new Utils.KeyboardShortcutManager(this);
        }

        #endregion

        #region 状态查询方法（供ActionHandler调用）

        /// <summary>
        /// 获取投影管理器
        /// </summary>
        public Managers.ProjectionManager GetProjectionManager() => _projectionManager;

        /// <summary>
        /// 获取视频播放管理器
        /// </summary>
        public Managers.VideoPlayerManager GetVideoPlayerManager() => _videoPlayerManager;

        /// <summary>
        /// 获取图片处理器
        /// </summary>
        public Core.ImageProcessor GetImageProcessor() => _imageProcessor;

        /// <summary>
        /// 获取合成播放服务
        /// </summary>
        public Services.Implementations.CompositePlaybackService GetCompositePlaybackService()
        {
            return _playbackServiceFactory?
                .GetPlaybackService(Database.Models.Enums.PlaybackMode.Composite)
                as Services.Implementations.CompositePlaybackService;
        }

        /// <summary>
        /// 是否处于文本编辑器模式
        /// </summary>
        public bool IsTextEditorActive()
        {
            return TextEditorPanel.Visibility == Visibility.Visible;
        }

        /// <summary>
        /// 文本编辑器中是否有选中的文本框
        /// </summary>
        public bool HasSelectedTextBoxInEditor()
        {
            return IsTextEditorActive() && _selectedTextBox != null;
        }

        /// <summary>
        /// 复制当前选中文本框到内部剪贴板
        /// </summary>
        public async Task<bool> TryCopySelectedTextBoxAsync()
        {
            if (SlideListBox?.IsKeyboardFocusWithin == true)
            {
                return false;
            }

            if (!HasSelectedTextBoxInEditor() || IsTextBoxInEditMode())
            {
                return false;
            }

            await CopyTextBoxToClipboardAsync(_selectedTextBox);
            return true;
        }

        /// <summary>
        /// 从内部剪贴板粘贴文本框
        /// </summary>
        public async Task<bool> TryPasteTextBoxAsync()
        {
            if (SlideListBox?.IsKeyboardFocusWithin == true)
            {
                return false;
            }

            if (!IsTextEditorActive() || IsTextBoxInEditMode())
            {
                return false;
            }

            await PasteTextBoxFromClipboardAsync(_selectedTextBox);
            return true;
        }

        /// <summary>
        /// 当前是否有文本框处于编辑模式
        /// </summary>
        public bool IsTextBoxInEditMode()
        {
            return _selectedTextBox != null && _selectedTextBox.IsInEditMode;
        }

        /// <summary>
        /// 是否处于原图模式
        /// </summary>
        public bool IsOriginalMode()
        {
            return _originalMode;
        }

        /// <summary>
        /// 是否正在脚本播放
        /// </summary>
        public bool IsScriptPlaying()
        {
            return _playbackViewModel != null && _playbackViewModel.IsPlaying;
        }

        /// <summary>
        /// 是否处于圣经模式
        /// </summary>
        public bool IsBibleMode()
        {
            return _isBibleMode;
        }

        /// <summary>
        /// 是否处于歌词模式
        /// </summary>
        public bool IsLyricsMode()
        {
            return _isLyricsMode;
        }

        // 注意：IsPinyinInputActive 已在 MainWindow.Bible.cs 中定义
        // 注意：NavigateHighlightedVerse() 已在 MainWindow.Bible.cs 中定义为internal方法

        #endregion

        #region 操作方法（供ActionHandler调用）

        // 注意：ClearImageDisplay() 已在 MainWindow.xaml.cs 中定义为public方法
        // 注意：以下方法在其他部分类中定义为internal，可以被ShortcutActionHandler访问：
        //   - SwitchToImageMode() - MainWindow.Media.cs
        //   - NavigateToPreviousSlide() / NavigateToNextSlide() - MainWindow.TextEditor.cs
        //   - IsMediaPlaybackMode() - MainWindow.Keyframe.cs
        //   - SwitchToPreviousMediaFile() / SwitchToNextMediaFile() - MainWindow.Media.cs

        /// <summary>
        /// 切换到上一张相似图片（原图模式）
        /// </summary>
        public void SwitchSimilarImagePrevious()
        {
            if (_originalMode && _currentImageId > 0)
            {
                SwitchSimilarImage(false);
            }
        }

        /// <summary>
        /// 切换到下一张相似图片（原图模式）
        /// </summary>
        public void SwitchSimilarImageNext()
        {
            if (_originalMode && _currentImageId > 0)
            {
                SwitchSimilarImage(true);
            }
        }

        /// <summary>
        /// 触发上一个关键帧按钮点击
        /// </summary>
        public void InvokePrevKeyframe()
        {
            Dispatcher.InvokeAsync(() =>
            {
                BtnPrevKeyframe_Click(null, null);
            });
        }

        /// <summary>
        /// 触发下一个关键帧按钮点击
        /// </summary>
        public void InvokeNextKeyframe()
        {
            Dispatcher.InvokeAsync(() =>
            {
                BtnNextKeyframe_Click(null, null);
            });
        }

        /// <summary>
        /// 触发播放按钮点击
        /// </summary>
        public void InvokePlayClick()
        {
            Dispatcher.InvokeAsync(() =>
            {
                BtnPlay_Click(null, null);
            });
        }

        /// <summary>
        /// 触发合成播放按钮点击
        /// </summary>
        public void InvokeCompositePlayClick()
        {
            Dispatcher.InvokeAsync(() =>
            {
                BtnFloatingCompositePlay.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            });
        }

        /// <summary>
        /// F2：合成播放暂停/继续
        /// </summary>
        public async Task<bool> ToggleCompositePauseResumeByHotkeyAsync()
        {
            var compositeService = GetCompositePlaybackService();
            if (compositeService == null || !compositeService.IsPlaying)
            {
                return false;
            }

            if (compositeService.IsPaused)
            {
                await compositeService.ResumePlaybackAsync();
                _countdownService?.Resume();
                ShowStatus("已继续合成播放");
            }
            else
            {
                await compositeService.PausePlaybackAsync();
                StopCompositeScrollAnimation();
                _countdownService?.Pause();
                ShowStatus("已暂停合成播放");
            }

            SetCompositePauseButtonContent(compositeService.IsPaused);

            return true;
        }

        /// <summary>
        /// 保存歌词项目（供快捷键调用）
        /// 注意：实际保存逻辑在 MainWindow.Lyrics.cs 的 SaveLyricsProject() 方法中
        /// </summary>
        public void InvokeSaveLyrics()
        {
            SaveLyricsProject();
            ShowToast("歌词已保存");
#if DEBUG
            System.Diagnostics.Debug.WriteLine($"[Ctrl+S] 歌词已保存");
#endif
        }

        /// <summary>
        /// 保存幻灯片项目（供快捷键调用）
        /// </summary>
        public void InvokeSaveTextProject()
        {
            Dispatcher.InvokeAsync(() =>
            {
                BtnSaveTextProject_Click(null, null);
                ShowToast("幻灯片已保存");
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"[Ctrl+S] 幻灯片已保存");
#endif
            });
        }

        #endregion
    }
}



