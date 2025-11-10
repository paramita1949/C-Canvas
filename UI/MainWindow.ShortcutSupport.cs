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
        /// 是否处于文本编辑器模式
        /// </summary>
        public bool IsTextEditorActive()
        {
            return TextEditorPanel.Visibility == Visibility.Visible;
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

        #endregion
    }
}

