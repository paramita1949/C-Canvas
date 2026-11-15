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
                WpfScreenInfo screen = null;
                if (!RunOnMainDispatcher(() => TryGetProjectionTargetScreen(out screen)))
                {
                    return false;
                }

                RunOnMainDispatcher(() => OpenProjectionOnUiThread(screen));
                CompleteProjectionOpen();
                return true;
            }
            catch (Exception ex)
            {
                AbortProjectionOpen();
                _uiNotifier.ShowMessage("错误", $"投影开启失败: {ex.Message}", ProjectionUiMessageLevel.Error);
                return false;
            }
        }

        private void CompleteProjectionOpen()
        {
            try
            {
                StartProjectionHealthWatch();
                ProjectionStateChanged?.Invoke(this, true);
            }
            catch
            {
                AbortProjectionOpen();
                throw;
            }
        }

        private void AbortProjectionOpen()
        {
            bool wasActive = _syncEnabled;
            _syncEnabled = false;
            try { StopProjectionTimer(); }
            catch { }
            try { RunOnMainDispatcher(() => CloseProjectionOnUiThread()); }
            catch { }
            // 一个出错的订阅者不能阻止其余UI接收回滚状态，也不能覆盖开启时的原始异常。
            var handlers = ProjectionStateChanged;
            if (wasActive && handlers != null)
            {
                foreach (EventHandler<bool> handler in handlers.GetInvocationList())
                {
                    try { handler(this, false); }
                    catch { }
                }
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
            ApplyProjectionCaptionTypographyFromCacheOnUi();
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
#if DEBUG
            // System.Diagnostics.Debug.WriteLine("[ProjectionSession] ProjectionStateChanged => false (source=ProjectionWindow_Closed)");
#endif
            ProjectionStateChanged?.Invoke(this, false);
        }

        private void OpenProjectionOnUiThread(WpfScreenInfo screen)
        {
            var layout = _windowFactory.CreateProjectionWindow(videoView =>
            {
                ProjectionVideoViewLoaded?.Invoke(this, videoView);
            });

            try
            {
                BindProjectionLayout(layout);
                ShowProjectionWindow(screen);
                InitializeProjectionStateAfterOpen();
            }
            catch
            {
                AbortProjectionOpen();
                throw;
            }
        }

        private bool TryGetProjectionTargetScreen(out WpfScreenInfo screen)
        {
            screen = null;
            RefreshProjectionTargetScreens();
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

            ProjectionNativePlacement.Place(_projectionWindow, screen);
            _projectionWindow.Show();
            ProjectionNativePlacement.Place(_projectionWindow, screen);
            _projectionWindow.Focusable = true;
            _projectionWindow.Focus();
            _projectionWindow.Activate();
            if (!ProjectionNativePlacement.Verify(_projectionWindow, screen, out _))
            {
                ProjectionNativePlacement.Place(_projectionWindow, screen);
                if (!ProjectionNativePlacement.Verify(_projectionWindow, screen, out _))
                    throw new InvalidOperationException("投影窗口未落在选定显示器。请重新选择显示器。");
            }
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

        private void StartProjectionHealthWatch()
        {
            // 基础投影不能依赖账号、网络或试用计时器。付费能力由 Services.Licensing.IFeatureGate 控制。
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
#if DEBUG
                    // System.Diagnostics.Debug.WriteLine("[ProjectionSession] ProjectionStateChanged => false (source=CloseProjection)");
#endif
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
            var window = _projectionWindow;
            bool hadProjection = window != null;
            System.ComponentModel.CancelEventHandler forceClose = (_, e) => e.Cancel = false;
            try
            {
                // 视频清理需保留window作为dispatcher owner；清理异常不得跳过窗口关闭。
                try { ClearLockedVideo(); }
                catch { }
                if (window != null)
                {
                    window.KeyDown -= ProjectionWindow_KeyDown;
                    window.Closed -= ProjectionWindow_Closed;
                    _isClosingProjectionWindow = true;
                    // 这是管理器决定的终止，而非可取消的用户编辑窗口关闭。
                    // 最后执行，防止Closing取消后遗留无owner的隐藏HWND。
                    window.Closing += forceClose;
                    // Closing可能被订阅者取消/抛错：先隐藏，避免失败返回后仍占据显示器。
                    window.Hide();
                    window.Close();
                }
            }
            finally
            {
                if (window != null) window.Closing -= forceClose;
                _isClosingProjectionWindow = false;
                _projectionWindow = null;
                ClearProjectionUiReferences();
            }
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
            _projectionCaptionOverlayBorder = null;
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
