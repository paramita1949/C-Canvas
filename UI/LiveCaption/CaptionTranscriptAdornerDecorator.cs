using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;

namespace ImageColorChanger.UI.LiveCaption
{
    /// <summary>字幕编辑区的局部装饰层：保留光标和选区，屏蔽输入法合成装饰。</summary>
    public sealed class CaptionTranscriptAdornerDecorator : AdornerDecorator
    {
        public CaptionTranscriptAdornerDecorator()
        {
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (Child is ScrollViewer contentHost) contentHost.ApplyTemplate();
            AdornerLayer.LayoutUpdated -= OnAdornerLayoutUpdated;
            AdornerLayer.LayoutUpdated += OnAdornerLayoutUpdated;
            SuppressCompositionAdorners();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e) =>
            AdornerLayer.LayoutUpdated -= OnAdornerLayoutUpdated;

        private void OnAdornerLayoutUpdated(object sender, EventArgs e) => SuppressCompositionAdorners();

        private void SuppressCompositionAdorners()
        {
            SuppressCompositionAdorners(AdornerLayer);
            // TextBoxView 的最近装饰层由 ScrollContentPresenter 提供，
            // 输入法合成线和光标都在这一层，而不是外层 AdornerDecorator。
            // 按模板部件定位，布局更新时也能兼容 ScrollViewer 模板重建。
            if (Child is ScrollViewer contentHost &&
                contentHost.Template?.FindName("PART_ScrollContentPresenter", contentHost) is ScrollContentPresenter presenter)
                SuppressCompositionAdorners(presenter.AdornerLayer);
        }

        private static void SuppressCompositionAdorners(AdornerLayer layer)
        {
            // WPF 没有公开的合成下划线开关。只识别框架自身的装饰类型，
            // 不移除装饰、不修改输入法或 TextStore，也不扫描正文或窗口视觉树。
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(layer); i++)
            {
                if (VisualTreeHelper.GetChild(layer, i) is not Adorner adorner) continue;
                Type type = adorner.GetType();
                if (type.Assembly == typeof(Adorner).Assembly &&
                    type.FullName == "System.Windows.Documents.CompositionAdorner" && adorner.Opacity != 0)
                    adorner.SetCurrentValue(UIElement.OpacityProperty, 0d);
            }
        }
    }
}
