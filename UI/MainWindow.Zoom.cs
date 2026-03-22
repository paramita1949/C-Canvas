using System;
using System.Windows;
using System.Windows.Input;
using ImageColorChanger.Core;
using ImageColorChanger.Database.Models.Enums;

namespace ImageColorChanger.UI
{
    public partial class MainWindow
    {
        #region 图片缩放功能

        private void ImageScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (ImageDisplay.Source == null) return;

            // Ctrl+滚轮 = 缩放
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                e.Handled = true;

                double delta = e.Delta / 120.0 * 0.05;
                double newZoom = _currentZoom + delta;
                newZoom = Math.Max(MinZoom, Math.Min(MaxZoom, newZoom));
                
                // 关键：只使用ImageProcessor的渲染缩放，不使用UI层ScaleTransform
                // 避免双重缩放导致的拉伸变形问题
                if (_imageProcessor != null && !_originalMode)
                {
                    _currentZoom = newZoom; // 更新当前缩放值
                    _imageProcessor.ZoomRatio = newZoom; // ImageProcessor会重新渲染图片
                    
                    // 更新投影屏幕
                    if (_projectionManager?.IsProjecting == true)
                    {
                        _projectionManager.UpdateProjectionImage(
                            _imageProcessor.CurrentImage,
                            _isColorEffectEnabled,
                            newZoom,
                            _originalMode,
                            _originalDisplayMode
                        );
                    }
                }
                else
                {
                    // 原图模式：只使用UI层ScaleTransform（因为ImageProcessor在原图模式下不支持缩放）
                    SetZoom(newZoom);
                }
            }
        }

        private void ResetZoom()
        {
            if (ImageDisplay.Source == null) return;
            
            _currentZoom = 1.0;
            
            if (!_originalMode)
            {
                // 正常模式：使用ImageProcessor的渲染缩放
                _imageProcessor?.ResetZoom();
                _imageProcessor?.UpdateImage();
                
                // 更新投影屏幕
                if (_projectionManager?.IsProjecting == true)
                {
                    _projectionManager.UpdateProjectionImage(
                        _imageProcessor.CurrentImage,
                        _isColorEffectEnabled,
                        1.0,
                        _originalMode,
                        _originalDisplayMode
                    );
                }
            }
            else
            {
                // 原图模式：重置UI层ScaleTransform
                SetZoom(1.0);
            }
            
            // 滚动到顶部
            ImageScrollViewer.ScrollToHorizontalOffset(0);
            ImageScrollViewer.ScrollToVerticalOffset(0);
        }

        private void FitImageToView()
        {
            if (ImageDisplay.Source == null) return;
            
            // 使用ImageProcessor的FitToView方法
            _imageProcessor?.FitToView();
        }

        private void ImageScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_originalMode && Math.Abs(_currentZoom - 1.0) > 0.001)
            {
                ApplyOriginalZoomTopCenterLayout();
            }

            if (ImageDisplay.Source != null && _currentZoom <= 1.0)
            {
                FitImageToView();
            }
            
            // 更新关键帧预览线和指示块
            _keyframeManager?.UpdatePreviewLines();
        }

        private void SetZoom(double zoom)
        {
            _currentZoom = Math.Max(MinZoom, Math.Min(MaxZoom, zoom));
            
            ImageScaleTransform.ScaleX = _currentZoom;
            ImageScaleTransform.ScaleY = _currentZoom;

            if (_originalMode)
            {
                ApplyOriginalZoomTopCenterLayout();
            }
        }

        /// <summary>
        /// 原图模式缩放时采用“居中置顶”布局，避免底部内容被挤到屏幕下方。
        /// </summary>
        private void ApplyOriginalZoomTopCenterLayout()
        {
            if (ImageDisplay.Source == null)
                return;

            // 原图模式下，只要处于缩放态（!=1x），统一采用“居中置顶”。
            if (Math.Abs(_currentZoom - 1.0) > 0.001)
            {
                ImageDisplay.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
                ImageDisplay.VerticalAlignment = System.Windows.VerticalAlignment.Top;

                if (_isDragging)
                {
                    _isDragging = false;
                    ImageDisplay.Cursor = System.Windows.Input.Cursors.Hand;
                    ImageDisplay.ReleaseMouseCapture();
                }

                ImageScrollViewer.UpdateLayout();

                // 横向始终保持居中，纵向始终置顶。
                double centeredHorizontalOffset = Math.Max(0, (ImageScrollViewer.ExtentWidth - ImageScrollViewer.ViewportWidth) / 2.0);
                ImageScrollViewer.ScrollToHorizontalOffset(centeredHorizontalOffset);
                ImageScrollViewer.ScrollToVerticalOffset(0);
            }
            else
            {
                ImageDisplay.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
                ImageDisplay.VerticalAlignment = System.Windows.VerticalAlignment.Center;
            }
        }

        #endregion

        #region 图片拖动功能

        private void ImageDisplay_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2 && e.ChangedButton == MouseButton.Left)
            {
                ResetZoom();
            }
        }

        private void ImageDisplay_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            // 鼠标中键点击切换原图显示模式(仅在原图模式下有效)
            if (e.ChangedButton == MouseButton.Middle && _originalMode)
            {
                ToggleOriginalDisplayMode();
                e.Handled = true;
            }
        }

        /// <summary>
        /// 切换原图显示模式(拉伸/适中)
        /// </summary>
        private void ToggleOriginalDisplayMode()
        {
            if (_originalDisplayMode == OriginalDisplayMode.Stretch)
            {
                _originalDisplayMode = OriginalDisplayMode.Fit;
                ShowStatus("原图模式: 适中显示");
            }
            else
            {
                _originalDisplayMode = OriginalDisplayMode.Stretch;
                ShowStatus("原图模式: 拉伸显示");
            }
            
            // 更新ImageProcessor的显示模式
            _imageProcessor.OriginalDisplayModeValue = _originalDisplayMode;
            
            // 重新显示图片
            _imageProcessor.UpdateImage();
            
            // 更新投影窗口
            UpdateProjection();
            
            // 保存设置到数据库
            SaveSettings();
        }

        private void ImageDisplay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_originalMode && Math.Abs(_currentZoom - 1.0) > 0.001)
            {
                // 原图缩放分支固定“居中置顶”，不允许手动拖拽改变位置。
                return;
            }

            if (_currentZoom > 1.0)
            {
                _isDragging = true;
                _dragStartPoint = e.GetPosition(ImageScrollViewer);
                ImageDisplay.Cursor = System.Windows.Input.Cursors.SizeAll;
                ImageDisplay.CaptureMouse();
            }
        }

        private void ImageDisplay_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDragging)
            {
                _isDragging = false;
                ImageDisplay.Cursor = System.Windows.Input.Cursors.Hand;
                ImageDisplay.ReleaseMouseCapture();
            }
        }

        private void ImageDisplay_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (_originalMode && Math.Abs(_currentZoom - 1.0) > 0.001)
            {
                return;
            }

            if (_isDragging && e.LeftButton == MouseButtonState.Pressed)
            {
                var currentPoint = e.GetPosition(ImageScrollViewer);
                var offset = currentPoint - _dragStartPoint;

                ImageScrollViewer.ScrollToHorizontalOffset(ImageScrollViewer.HorizontalOffset - offset.X);
                ImageScrollViewer.ScrollToVerticalOffset(ImageScrollViewer.VerticalOffset - offset.Y);

                _dragStartPoint = currentPoint;
            }
        }

        #endregion
    }
}




