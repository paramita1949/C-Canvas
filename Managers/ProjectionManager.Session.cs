using System;
using System.Windows;

namespace ImageColorChanger.Managers
{
    /// <summary>
    /// ProjectionManager 投影会话生命周期（打开/关闭/状态）逻辑（部分类）。
    /// </summary>
    public partial class ProjectionManager
    {
        /// <summary>
        /// 打开投影窗口
        /// </summary>
        private bool OpenProjection()
        {
            try
            {
                if (!TryGetProjectionTargetScreen(out var screen))
                {
                    return false;
                }

                RunOnMainDispatcher(() => OpenProjectionOnUiThread(screen));
                StartProjectionAuthWatch();
                ProjectionStateChanged?.Invoke(this, true);
                return true;
            }
            catch (Exception ex)
            {
                _uiNotifier.ShowMessage("错误", $"投影开启失败: {ex.Message}", ProjectionUiMessageLevel.Error);
                return false;
            }
        }

        private void BindProjectionLayout(ProjectionWindowLayout layout)
        {
            _projectionWindow = layout.Window;
            _projectionScrollViewer = layout.ScrollViewer;
            _projectionContainer = layout.ProjectionContainer;
            _projectionImageControl = layout.ProjectionImageControl;
            _projectionVisualBrushRect = layout.ProjectionVisualBrushRect;
            _projectionNoticeOverlayContainer = layout.ProjectionNoticeOverlayContainer;
            _projectionNoticeOverlayImage = layout.ProjectionNoticeOverlayImage;
            _projectionCaptionOverlayContainer = layout.ProjectionCaptionOverlayContainer;
            _projectionCaptionOverlayBorder = layout.ProjectionCaptionOverlayBorder;
            _projectionCaptionOverlayText = layout.ProjectionCaptionOverlayText;
            _projectionVideoContainer = layout.ProjectionVideoContainer;
            _projectionVideoImage = layout.ProjectionVideoImage;
            _projectionVideoView = layout.ProjectionVideoView;
            _projectionMediaFileNameBorder = layout.ProjectionMediaFileNameBorder;
            _projectionMediaFileNameText = layout.ProjectionMediaFileNameText;
            _projectionBibleTitleBorder = layout.ProjectionBibleTitleBorder;
            _projectionBibleTitleText = layout.ProjectionBibleTitleText;
            _projectionBiblePopupBorder = layout.ProjectionBiblePopupBorder;
            _projectionBiblePopupReferenceText = layout.ProjectionBiblePopupReferenceText;
            _projectionBiblePopupContentScrollViewer = layout.ProjectionBiblePopupContentScrollViewer;
            _projectionBiblePopupContentText = layout.ProjectionBiblePopupContentText;
            _projectionBiblePopupCloseButton = layout.ProjectionBiblePopupCloseButton;

            _projectionWindow.KeyDown += ProjectionWindow_KeyDown;
            _projectionWindow.Closed += ProjectionWindow_Closed;
            ApplyProjectionCaptionOverlayLayoutOnUi();
            if (_projectionBiblePopupCloseButton != null)
            {
                _projectionBiblePopupCloseButton.Click += ProjectionBiblePopupCloseButton_Click;
            }
        }

        private void ProjectionWindow_Closed(object sender, EventArgs e)
        {
            if (_isClosingProjectionWindow)
            {
                return;
            }

            if (sender is Window closedWindow)
            {
                closedWindow.KeyDown -= ProjectionWindow_KeyDown;
                closedWindow.Closed -= ProjectionWindow_Closed;
            }

            _syncEnabled = false;
            StopProjectionTimer();
            ClearLockedVideo();
            _projectionWindow = null;
            ClearProjectionUiReferences();
            ProjectionStateChanged?.Invoke(this, false);
        }

        private void OpenProjectionOnUiThread(WpfScreenInfo screen)
        {
            var layout = _windowFactory.CreateProjectionWindow(videoView =>
            {
                ProjectionVideoViewLoaded?.Invoke(this, videoView);
            });

            BindProjectionLayout(layout);
            ShowProjectionWindow(screen);
            InitializeProjectionStateAfterOpen();
        }

        private bool TryGetProjectionTargetScreen(out WpfScreenInfo screen)
        {
            screen = null;
            if (_screens.Count < 2)
            {
                _uiNotifier.ShowMessage("警告", "未检测到第二个显示器！", ProjectionUiMessageLevel.Warning);
                return false;
            }

            int selectedIndex = _screenComboBox?.SelectedIndex ?? 0;
            if (selectedIndex < 0 || selectedIndex >= _screens.Count)
            {
                _uiNotifier.ShowMessage("错误", "选定的显示器无效！", ProjectionUiMessageLevel.Error);
                return false;
            }

            var selectedScreen = _screens[selectedIndex];
            if (selectedScreen.IsPrimary)
            {
                _uiNotifier.ShowMessage("警告", "不能投影到主显示器！", ProjectionUiMessageLevel.Warning);
                return false;
            }

            _currentScreenIndex = selectedIndex;
            screen = selectedScreen;
            return true;
        }

        private void ShowProjectionWindow(WpfScreenInfo screen)
        {
            if (_projectionWindow == null)
            {
                return;
            }

            _projectionWindow.Left = screen.WpfBounds.Left;
            _projectionWindow.Top = screen.WpfBounds.Top;
            _projectionWindow.Width = screen.WpfBounds.Width;
            _projectionWindow.Height = screen.WpfBounds.Height;

            _projectionWindow.Show();
            _projectionWindow.Left = screen.WpfBounds.Left;
            _projectionWindow.Top = screen.WpfBounds.Top;
            _projectionWindow.WindowState = WindowState.Maximized;
            _projectionWindow.Focusable = true;
            _projectionWindow.Focus();
            _projectionWindow.Activate();
        }

        private void InitializeProjectionStateAfterOpen()
        {
            bool isInLyricsMode = _host.IsInLyricsMode;
            if (_imageProcessor?.CurrentImage != null && !isInLyricsMode)
            {
                _currentImage = _imageProcessor.CurrentImage;
                _currentImagePath = _imageProcessor.CurrentImagePath;
                _isColorEffectEnabled = _imageProcessor.IsInverted;
                _zoomRatio = _imageProcessor.ZoomRatio;
                _isOriginalMode = _imageProcessor.OriginalMode;
                _originalDisplayMode = _imageProcessor.OriginalDisplayModeValue;
                _originalTopScalePercent = _imageProcessor.OriginalTopScalePercent;
                UpdateProjection();
            }

            _syncEnabled = true;
        }

        private void StartProjectionAuthWatch()
        {
            if (!_authPolicy.IsAuthenticated)
            {
                StartProjectionTimer();
                return;
            }

            CheckAuthenticationPeriodically();
        }

        /// <summary>
        /// 关闭投影窗口
        /// </summary>
        public bool CloseProjection()
        {
            try
            {
                _syncEnabled = false;
                StopProjectionTimer();
                bool hadProjection = RunOnMainDispatcher(CloseProjectionOnUiThread);
                if (hadProjection)
                {
                    ProjectionStateChanged?.Invoke(this, false);
                }

                return hadProjection;
            }
            catch
            {
                return false;
            }
        }

        private bool CloseProjectionOnUiThread()
        {
            bool hadProjection = false;
            if (_projectionWindow != null)
            {
                hadProjection = true;
                _projectionWindow.KeyDown -= ProjectionWindow_KeyDown;
                _projectionWindow.Closed -= ProjectionWindow_Closed;
                _isClosingProjectionWindow = true;
                try
                {
                    _projectionWindow.Close();
                }
                finally
                {
                    _isClosingProjectionWindow = false;
                }

                _projectionWindow = null;
            }

            ClearLockedVideo();
            ClearProjectionUiReferences();
            return hadProjection;
        }

        private void ClearProjectionUiReferences()
        {
            _projectionScrollViewer = null;
            _projectionContainer = null;
            _projectionImageControl = null;
            _projectionImage = null;
            _projectionVisualBrushRect = null;
            _projectionNoticeOverlayContainer = null;
            _projectionNoticeOverlayImage = null;
            _projectionCaptionOverlayContainer = null;
            _projectionCaptionOverlayText = null;
            _projectionVideoContainer = null;
            _projectionVideoImage = null;
            _projectionVideoView = null;
            _projectionMediaFileNameBorder = null;
            _projectionMediaFileNameText = null;
            _projectionBibleTitleBorder = null;
            _projectionBibleTitleText = null;
            if (_projectionBiblePopupCloseButton != null)
            {
                _projectionBiblePopupCloseButton.Click -= ProjectionBiblePopupCloseButton_Click;
            }
            _projectionBiblePopupBorder = null;
            _projectionBiblePopupReferenceText = null;
            _projectionBiblePopupContentScrollViewer = null;
            _projectionBiblePopupContentText = null;
            _projectionBiblePopupCloseButton = null;
            _projectionBiblePopupTimer?.Stop();
            _projectionBiblePopupTimer = null;
            _currentBibleScrollViewer = null;
            _projectionMediaElement = null;
            _cachedTextLayerBitmap = null;
            _cachedTextLayerTimestamp = 0;
            _lastSharedBitmap = null;
            _isPreRendering = false;
        }
    }
}
