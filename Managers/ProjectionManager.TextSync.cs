using System;
using System.Windows;
using System.Windows.Controls;

namespace ImageColorChanger.Managers
{
    /// <summary>
    /// ProjectionManager 文本/经文滚动同步与清屏逻辑（部分类）。
    /// </summary>
    public partial class ProjectionManager
    {
        /// <summary>
        /// 清空图片投影状态（用于切换到纯文字模式时）
        /// </summary>
        public void ClearImageState()
        {
            _currentImage = null;
            _currentImagePath = null;
            _isColorEffectEnabled = false;
            _zoomRatio = 1.0;
        }

        /// <summary>
        /// 清空投影显示内容（将投影屏幕设置为黑屏）
        /// </summary>
        public void ClearProjectionDisplay()
        {
            if (_projectionWindow == null || _projectionImageControl == null)
            {
                return;
            }

            try
            {
                RunOnMainDispatcher(() =>
                {
                    ResetVisualBrushProjection();
                    if (_projectionImageControl != null)
                    {
                        _projectionImageControl.Source = null;
                    }

                    if (_projectionBibleTitleText != null)
                    {
                        _projectionBibleTitleText.Text = string.Empty;
                    }

                    if (_projectionBibleTitleBorder != null)
                    {
                        _projectionBibleTitleBorder.Visibility = Visibility.Collapsed;
                    }

                    if (_projectionScrollViewer != null)
                    {
                        var margin = _projectionScrollViewer.Margin;
                        if (Math.Abs(margin.Top) > 0.5d)
                        {
                            _projectionScrollViewer.Margin = new Thickness(
                                margin.Left,
                                0,
                                margin.Right,
                                margin.Bottom);
                        }
                    }
                });
            }
            catch
            {
            }
        }

        /// <summary>
        /// 同步歌词滚动位置到投影
        /// </summary>
        public void SyncLyricsScroll(ScrollViewer lyricsScrollViewer)
        {
            if (!_syncEnabled || _projectionWindow == null || lyricsScrollViewer == null)
            {
                return;
            }

            try
            {
                if (ShouldThrottleSync())
                {
                    return;
                }

                RunOnMainDispatcher(() =>
                {
                    if (_projectionScrollViewer == null)
                    {
                        return;
                    }

                    double mainScrollTop = lyricsScrollViewer.VerticalOffset;
                    _projectionScrollViewer.ScrollToVerticalOffset(mainScrollTop);
                });
            }
            catch
            {
            }
        }

        /// <summary>
        /// 同步圣经滚动位置到投影（与歌词一致）
        /// </summary>
        public void SyncBibleScroll(ScrollViewer bibleScrollViewer)
        {
            if (!_syncEnabled || _projectionWindow == null || bibleScrollViewer == null)
            {
                return;
            }

            try
            {
                if (ShouldThrottleSync())
                {
                    return;
                }

                RunOnMainDispatcher(() =>
                {
                    if (_projectionScrollViewer == null)
                    {
                        return;
                    }

                    double mainScrollTop = bibleScrollViewer.VerticalOffset;
                    double mainExtentHeight = bibleScrollViewer.ExtentHeight;
                    double projExtentHeight = _projectionScrollViewer.ExtentHeight;
                    if (mainExtentHeight <= 0 || projExtentHeight <= 0)
                    {
                        return;
                    }

                    double projScrollTop = ProjectionScrollPolicy.CalculateByExtentRatio(
                        mainScrollTop,
                        mainExtentHeight,
                        projExtentHeight);
                    _projectionScrollViewer.ScrollToVerticalOffset(projScrollTop);
                });
            }
            catch (Exception)
            {
            }
        }
    }
}
