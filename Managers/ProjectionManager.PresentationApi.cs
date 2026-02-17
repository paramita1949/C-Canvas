using System;

namespace ImageColorChanger.Managers
{
    /// <summary>
    /// ProjectionManager 展示 API 与交互入口逻辑（部分类）。
    /// </summary>
    public partial class ProjectionManager
    {
        /// <summary>
        /// 设置投影窗口媒体文件名显示
        /// </summary>
        public void SetProjectionMediaFileName(string fileName, bool isAudioOnly)
        {
            if (_projectionWindow == null || _projectionMediaFileNameBorder == null || _projectionMediaFileNameText == null)
            {
                return;
            }

            RunOnMainDispatcher(() => { UpdateProjectionMediaFileNameOnUi(fileName, isAudioOnly); });
        }

        /// <summary>
        /// 设置圣经标题（固定在顶部）
        /// </summary>
        public void SetBibleTitle(string title, bool visible)
        {
            if (_projectionWindow == null || _projectionBibleTitleBorder == null || _projectionBibleTitleText == null)
            {
                return;
            }

            RunOnMainDispatcher(() => { UpdateBibleTitleOnUi(title, visible); });
        }

        /// <summary>
        /// 直接设置投影滚动位置（用于圣经同步）
        /// </summary>
        public void SetProjectionScrollPosition(double offset, bool shouldDebug = false)
        {
            _ = shouldDebug;
            if (!_syncEnabled || _projectionWindow == null || _projectionScrollViewer == null)
            {
                return;
            }

            RunOnMainDispatcher(() => { SetProjectionVerticalOffsetWithStabilization(offset); });
        }

        /// <summary>
        /// 按比例设置投影滚动位置（用于圣经同步，确保像素级对齐）
        /// </summary>
        public void SetProjectionScrollPositionByRatio(double scrollRatio, bool shouldDebug = false)
        {
            _ = shouldDebug;
            if (!_syncEnabled || _projectionWindow == null || _projectionScrollViewer == null)
            {
                return;
            }

            RunOnMainDispatcher(() =>
            {
                double projScrollableHeight = _projectionScrollViewer.ScrollableHeight;
                double projScrollOffset = ProjectionScrollPolicy.CalculateByScrollableHeightRatio(scrollRatio, projScrollableHeight);
                SetProjectionVerticalOffsetWithStabilization(projScrollOffset);
            });
        }

        /// <summary>
        /// 显示视频投影（隐藏图片，显示视频）
        /// </summary>
        public void ShowVideoProjection()
        {
            if (_projectionWindow == null)
            {
                return;
            }

            RunOnMainDispatcher(() => { SetProjectionMode(showVideo: true); });
        }

        /// <summary>
        /// 显示图片投影（隐藏视频，显示图片）
        /// </summary>
        public void ShowImageProjection()
        {
            if (_projectionWindow == null)
            {
                return;
            }

            RunOnMainDispatcher(() => { SetProjectionMode(showVideo: false); });
        }

        /// <summary>
        /// 投影窗口键盘事件处理
        /// </summary>
        private void ProjectionWindow_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            _ = sender;
            try
            {
                _host.ForwardProjectionKeyDown(e);
                e.Handled = true;
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// 导航到上一张图片
        /// </summary>
        private void NavigateToPreviousImage()
        {
            RunOnMainDispatcher(() => { _host.SwitchToPreviousSimilarImage(); });
        }

        /// <summary>
        /// 导航到下一张图片
        /// </summary>
        private void NavigateToNextImage()
        {
            RunOnMainDispatcher(() => { _host.SwitchToNextSimilarImage(); });
        }
    }
}
