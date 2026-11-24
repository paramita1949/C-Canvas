using System;
using System.Windows;
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
            AdornerLayer.LayoutUpdated -= OnAdornerLayoutUpdated;
            AdornerLayer.LayoutUpdated += OnAdornerLayoutUpdated;
            SuppressCompositionAdorners();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e) =>
            AdornerLayer.LayoutUpdated -= OnAdornerLayoutUpdated;

        private void OnAdornerLayoutUpdated(object sender, EventArgs e) => SuppressCompositionAdorners();

        private void SuppressCompositionAdorners()
        {
            // WPF 没有公开的合成下划线开关。只识别框架自身的装饰类型，
            // 不移除装饰、不修改输入法或 TextStore，也不扫描正文或窗口视觉树。
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(AdornerLayer); i++)
            {
                if (VisualTreeHelper.GetChild(AdornerLayer, i) is not Adorner adorner) continue;
                Type type = adorner.GetType();
                if (type.Assembly == typeof(Adorner).Assembly &&
                    type.FullName == "System.Windows.Documents.CompositionAdorner" && adorner.Opacity != 0)
                    adorner.SetCurrentValue(UIElement.OpacityProperty, 0d);
            }
        }
    }
}
