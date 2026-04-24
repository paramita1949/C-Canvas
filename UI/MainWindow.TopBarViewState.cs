using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;

namespace ImageColorChanger.UI
{
    /// <summary>
    /// MainWindow 顶部按钮与视图状态
    /// </summary>
    public partial class MainWindow
    {
        private const double TopMenuHorizontalScrollRatio = 0.85;
        private const int TopMenuContinuousScrollIntervalMs = 24;
        private System.Windows.Threading.DispatcherTimer _topMenuContinuousScrollTimer;
        private int _topMenuContinuousScrollDirection;

        private void BtnSync_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                BtnSync.IsEnabled = false;
                BtnSync.Background = new SolidColorBrush(Colors.LightGreen);

                var (added, removed, updated) = ImportManagerService.SyncAllFolders();

                ReloadProjectsPreservingTreeState();
                LoadSearchScopes();

                ShowStatus($"同步完成: 新增 {added}, 删除 {removed}");
            }
            catch (Exception ex)
            {
                ShowStatus($"同步失败: {ex.Message}");
            }
            finally
            {
                BtnSync.IsEnabled = true;
                BtnSync.Background = Brushes.Transparent;
            }
        }

        private void BtnReset_Click(object sender, RoutedEventArgs e)
        {
            ResetView();
        }

        private void BtnOriginal_Click(object sender, RoutedEventArgs e)
        {
            ToggleOriginalMode();
        }

        private void BtnZoomReset_Click(object sender, RoutedEventArgs e)
        {
            ResetZoom();
            ShowStatus("已重置缩放比例");
        }

        /// <summary>
        /// 切换原图模式
        /// </summary>
        private void ToggleOriginalMode()
        {
            _originalMode = !_originalMode;
            _imageProcessor.OriginalMode = _originalMode;

            if (_originalMode)
            {
                BtnOriginal.Background = new SolidColorBrush(Color.FromRgb(144, 238, 144));
                ShowStatus("已启用原图模式");

                // 启用原图模式时恢复独立持久化的原图滚轮缩放值。
                _currentZoom = _originalModeZoomRatio;
                SetZoom(_currentZoom);

                if (_currentImageId > 0)
                {
                    _ = _originalManager.FindSimilarImages(_currentImageId);
                }
            }
            else
            {
                BtnOriginal.Background = Brushes.Transparent;
                ShowStatus("已关闭原图模式");
            }

            _imageProcessor.UpdateImage();
            UpdateProjection();
        }

        /// <summary>
        /// 重置视图状态以进入文本编辑器
        /// </summary>
        private void ResetViewStateForTextEditor()
        {
            if (_originalMode)
            {
                _originalMode = false;
                _imageProcessor.OriginalMode = false;
                BtnOriginal.Background = Brushes.Transparent;
            }

            if (Math.Abs(_imageProcessor.ZoomRatio - 1.0) > 0.001)
            {
                _imageProcessor.ZoomRatio = 1.0;
            }

            if (_isColorEffectEnabled)
            {
                _isColorEffectEnabled = false;
                BtnColorEffect.Background = Brushes.Transparent;
            }

            ClearImageDisplay();
            UpdateFloatingCompositePlayButton();
        }

        private void TopMenuScrollLeft_Click(object sender, RoutedEventArgs e)
        {
            ScrollTopMenuBy(-GetTopMenuScrollStep());
        }

        private void TopMenuScrollRight_Click(object sender, RoutedEventArgs e)
        {
            ScrollTopMenuBy(GetTopMenuScrollStep());
        }

        private void TopMenuScrollLeft_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            StartTopMenuContinuousScroll(-1);
        }

        private void TopMenuScrollRight_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            StartTopMenuContinuousScroll(1);
        }

        private void TopMenuScrollArrow_PreviewMouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            StopTopMenuContinuousScroll();
        }

        private void TopMenuScrollArrow_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (e.LeftButton != System.Windows.Input.MouseButtonState.Pressed)
            {
                StopTopMenuContinuousScroll();
            }
        }

        private void TopMenuScrollViewer_Loaded(object sender, RoutedEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(UpdateTopMenuScrollButtonState), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void TopMenuHostGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateTopMenuScrollButtonState();
        }

        private void TopMenuScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateTopMenuScrollButtonState();
        }

        private void TopMenuScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            UpdateTopMenuScrollButtonState();
        }

        private void TopMenuScrollViewer_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            if (TopMenuScrollViewer == null || TopMenuScrollViewer.ScrollableWidth <= 1)
            {
                return;
            }

            double direction = e.Delta > 0 ? -1 : 1;
            double step = Math.Max(48, GetTopMenuScrollStep() * 0.22);
            ScrollTopMenuBy(direction * step);
            e.Handled = true;
        }

        private void ScrollTopMenuBy(double delta)
        {
            if (TopMenuScrollViewer == null)
            {
                return;
            }

            double next = TopMenuScrollViewer.HorizontalOffset + delta;
            next = Math.Max(0, Math.Min(TopMenuScrollViewer.ScrollableWidth, next));
            if (Math.Abs(next - TopMenuScrollViewer.HorizontalOffset) > 0.1)
            {
                SuppressImportHoverAutoPopupAfterTopMenuScroll();
            }
            TopMenuScrollViewer.ScrollToHorizontalOffset(next);
            UpdateTopMenuScrollButtonState();
        }

        private void StartTopMenuContinuousScroll(int direction)
        {
            if (direction == 0 || TopMenuScrollViewer == null)
            {
                return;
            }

            if ((direction < 0 && !BtnTopMenuScrollLeft.IsEnabled) || (direction > 0 && !BtnTopMenuScrollRight.IsEnabled))
            {
                return;
            }

            _topMenuContinuousScrollDirection = direction;
            SuppressImportHoverAutoPopupAfterTopMenuScroll();
            _topMenuContinuousScrollTimer ??= new System.Windows.Threading.DispatcherTimer(
                TimeSpan.FromMilliseconds(TopMenuContinuousScrollIntervalMs),
                System.Windows.Threading.DispatcherPriority.Background,
                (_, _) => TickTopMenuContinuousScroll(),
                Dispatcher);

            if (!_topMenuContinuousScrollTimer.IsEnabled)
            {
                _topMenuContinuousScrollTimer.Start();
            }
        }

        private void TickTopMenuContinuousScroll()
        {
            if (_topMenuContinuousScrollDirection == 0)
            {
                StopTopMenuContinuousScroll();
                return;
            }

            double step = Math.Max(22, GetTopMenuScrollStep() * 0.09);
            ScrollTopMenuBy(_topMenuContinuousScrollDirection * step);
        }

        private void StopTopMenuContinuousScroll()
        {
            _topMenuContinuousScrollDirection = 0;
            _topMenuContinuousScrollTimer?.Stop();
        }

        private double GetTopMenuScrollStep()
        {
            if (TopMenuScrollViewer == null || TopMenuScrollViewer.ViewportWidth <= 0)
            {
                return 260;
            }

            return Math.Max(180, TopMenuScrollViewer.ViewportWidth * TopMenuHorizontalScrollRatio);
        }

        private void UpdateTopMenuScrollButtonState()
        {
            if (TopMenuScrollViewer == null || BtnTopMenuScrollLeft == null || BtnTopMenuScrollRight == null)
            {
                return;
            }

            bool hasOverflowContent = TopMenuOverflowPanel.Children
                .Cast<UIElement>()
                .Any(element => element.Visibility != Visibility.Collapsed);
            bool hasOverflow = hasOverflowContent && TopMenuScrollViewer.ScrollableWidth > 1;
            bool canScrollLeft = TopMenuScrollViewer.HorizontalOffset > 1;
            bool canScrollRight = TopMenuScrollViewer.HorizontalOffset < TopMenuScrollViewer.ScrollableWidth - 1;

            if (!hasOverflow)
            {
                BtnTopMenuScrollLeft.Visibility = Visibility.Collapsed;
                BtnTopMenuScrollRight.Visibility = Visibility.Collapsed;
                BtnTopMenuScrollLeft.IsEnabled = false;
                BtnTopMenuScrollRight.IsEnabled = false;
                return;
            }

            // 仅在对应方向可滚动时显示箭头；不可滚动时折叠，避免留下空白占位。
            BtnTopMenuScrollLeft.Visibility = canScrollLeft ? Visibility.Visible : Visibility.Collapsed;
            BtnTopMenuScrollRight.Visibility = canScrollRight ? Visibility.Visible : Visibility.Collapsed;

            BtnTopMenuScrollLeft.IsEnabled = canScrollLeft;
            BtnTopMenuScrollRight.IsEnabled = canScrollRight;

            BtnTopMenuScrollLeft.Opacity = canScrollLeft ? 1.0 : 0.45;
            BtnTopMenuScrollRight.Opacity = canScrollRight ? 1.0 : 0.45;
        }
    }
}


