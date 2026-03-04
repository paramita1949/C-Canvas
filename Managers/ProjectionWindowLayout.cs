using System.Windows;
using LibVLCSharp.WPF;

namespace ImageColorChanger.Managers
{
    /// <summary>
    /// 投影窗口组装结果，供 ProjectionManager 接管生命周期与业务行为。
    /// </summary>
    public sealed class ProjectionWindowLayout
    {
        public Window Window { get; init; }
        public System.Windows.Controls.ScrollViewer ScrollViewer { get; init; }
        public System.Windows.Controls.Grid ProjectionContainer { get; init; }
        public System.Windows.Controls.Image ProjectionImageControl { get; init; }
        public System.Windows.Shapes.Rectangle ProjectionVisualBrushRect { get; init; }
        public System.Windows.Controls.Grid ProjectionNoticeOverlayContainer { get; init; }
        public System.Windows.Controls.Image ProjectionNoticeOverlayImage { get; init; }
        public System.Windows.Controls.Grid ProjectionVideoContainer { get; init; }
        public System.Windows.Controls.Image ProjectionVideoImage { get; init; }
        public VideoView ProjectionVideoView { get; init; }
        public System.Windows.Controls.Grid ProjectionMediaFileNameBorder { get; init; }
        public System.Windows.Controls.TextBlock ProjectionMediaFileNameText { get; init; }
        public System.Windows.Controls.Border ProjectionBibleTitleBorder { get; init; }
        public System.Windows.Controls.TextBlock ProjectionBibleTitleText { get; init; }
        public System.Windows.Controls.Border ProjectionBiblePopupBorder { get; init; }
        public System.Windows.Controls.TextBlock ProjectionBiblePopupReferenceText { get; init; }
        public System.Windows.Controls.ScrollViewer ProjectionBiblePopupContentScrollViewer { get; init; }
        public System.Windows.Controls.TextBlock ProjectionBiblePopupContentText { get; init; }
        public System.Windows.Controls.Button ProjectionBiblePopupCloseButton { get; init; }
    }
}
