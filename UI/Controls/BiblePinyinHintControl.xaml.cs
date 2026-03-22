using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ImageColorChanger.Services;

namespace ImageColorChanger.UI.Controls
{
    /// <summary>
    /// 圣经拼音快速定位提示框
    /// </summary>
    public partial class BiblePinyinHintControl : System.Windows.Controls.UserControl
    {
        private const double CompactHintWidth = 380d;
        private const double HintWidthRatio = 0.80d;
        private const double MinHintWidth = 700d;
        private const double MaxHintWidth = 1400d;
        private const double FallbackHintWidth = 760d;
        private const double DefaultPreviewFontSize = 35d;
        private const double MinPreviewFontSize = 15d;
        private const double MaxPreviewFontSize = 70d;
        private const double MinPreviewMaxHeight = 220d;
        private const double MaxPreviewMaxHeight = 680d;
        private const double PreviewHeightRatio = 0.58d;
        private const double FallbackPreviewMaxHeight = 420d;
        private const double PreviewHeightPadding = 6d;
        private const double PreviewTextSidePadding = 78d;

        private FrameworkElement _adaptiveWidthHost;
        private string _lastPreviewContent = string.Empty;
        private double _lastRequestedPreviewFontSize = DefaultPreviewFontSize;

        public BiblePinyinHintControl()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        /// <summary>
        /// 更新提示框内容
        /// </summary>
        public void UpdateHint(
            string displayText,
            List<BibleBookMatch> matches,
            string previewReference = null,
            string previewContent = null,
            double previewFontSize = 35d)
        {
            bool hasPreview = !string.IsNullOrWhiteSpace(previewContent);
            if (hasPreview)
            {
                EnsureAdaptiveWidthHost();
                ApplyAdaptiveWidth();
                _lastPreviewContent = previewContent ?? string.Empty;
                _lastRequestedPreviewFontSize = previewFontSize;
            }
            else
            {
                ApplyCompactWidth();
                _lastPreviewContent = string.Empty;
                _lastRequestedPreviewFontSize = DefaultPreviewFontSize;
            }

            // 更新输入文本
            InputText.Text = displayText;

            // 清空匹配结果
            MatchResultsPanel.Children.Clear();

            // 如果有匹配的书卷，显示横向排列
            if (matches != null && matches.Count > 0)
            {
                MatchResultsPanel.Visibility = Visibility.Visible;

                // 最多显示前10个匹配结果
                var displayMatches = matches.Take(10).ToList();

                foreach (var match in displayMatches)
                {
                    var textBlock = new TextBlock
                    {
                        Text = match.BookName,
                        Foreground = new SolidColorBrush(Colors.White),
                        FontSize = 16,
                        Margin = new Thickness(0, 0, 15, 5), // 右边距15，下边距5
                        VerticalAlignment = VerticalAlignment.Center
                    };

                    MatchResultsPanel.Children.Add(textBlock);
                }
            }
            else
            {
                MatchResultsPanel.Visibility = Visibility.Collapsed;
            }

            if (hasPreview)
            {
                PreviewPanel.Visibility = Visibility.Visible;
                PreviewReferenceText.Text = string.Empty;
                PreviewReferenceText.Visibility = Visibility.Collapsed;
                PreviewContentText.Text = previewContent;
                ApplyAdaptivePreviewLayout();
            }
            else
            {
                PreviewPanel.Visibility = Visibility.Collapsed;
                PreviewReferenceText.Text = string.Empty;
                PreviewReferenceText.Visibility = Visibility.Collapsed;
                PreviewContentText.Text = string.Empty;
                PreviewContentText.FontSize = DefaultPreviewFontSize;
                PreviewContentText.ClearValue(TextBlock.LineHeightProperty);
                if (PreviewScrollViewer != null)
                {
                    PreviewScrollViewer.MaxHeight = MinPreviewMaxHeight;
                }
            }

            // 显示提示框
            Visibility = Visibility.Visible;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            ApplyCompactWidth();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            DetachAdaptiveWidthHost();
        }

        private void EnsureAdaptiveWidthHost()
        {
            if (_adaptiveWidthHost != null)
            {
                return;
            }

            var host = Parent as FrameworkElement;
            if (host == null)
            {
                return;
            }

            _adaptiveWidthHost = host;
            _adaptiveWidthHost.SizeChanged += AdaptiveWidthHost_SizeChanged;
        }

        private void DetachAdaptiveWidthHost()
        {
            if (_adaptiveWidthHost == null)
            {
                return;
            }

            _adaptiveWidthHost.SizeChanged -= AdaptiveWidthHost_SizeChanged;
            _adaptiveWidthHost = null;
        }

        private void AdaptiveWidthHost_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (PreviewPanel?.Visibility == Visibility.Visible)
            {
                ApplyAdaptiveWidth();
                ApplyAdaptivePreviewLayout();
            }
        }

        private void ApplyAdaptiveWidth()
        {
            double hostWidth = _adaptiveWidthHost?.ActualWidth ?? 0d;
            double targetWidth = hostWidth > 0
                ? hostWidth * HintWidthRatio
                : FallbackHintWidth;

            Width = System.Math.Clamp(targetWidth, MinHintWidth, MaxHintWidth);
        }

        private void ApplyCompactWidth()
        {
            Width = CompactHintWidth;
        }

        private void ApplyAdaptivePreviewLayout()
        {
            if (PreviewPanel?.Visibility != Visibility.Visible || string.IsNullOrWhiteSpace(_lastPreviewContent))
            {
                return;
            }

            double maxHeight = GetAdaptivePreviewMaxHeight();
            if (PreviewScrollViewer != null)
            {
                PreviewScrollViewer.MaxHeight = maxHeight;
            }

            double requestedFontSize = Math.Clamp(
                _lastRequestedPreviewFontSize > 0 ? _lastRequestedPreviewFontSize : DefaultPreviewFontSize,
                MinPreviewFontSize,
                MaxPreviewFontSize);
            double textWidth = EstimatePreviewTextWidth();

            double fittedFontSize = requestedFontSize;
            double measuredHeight = MeasurePreviewHeight(_lastPreviewContent, fittedFontSize, textWidth);
            while (measuredHeight > maxHeight - PreviewHeightPadding && fittedFontSize > MinPreviewFontSize)
            {
                fittedFontSize = Math.Max(MinPreviewFontSize, fittedFontSize - 1);
                measuredHeight = MeasurePreviewHeight(_lastPreviewContent, fittedFontSize, textWidth);
            }

            PreviewContentText.FontSize = fittedFontSize;
            PreviewContentText.LineHeight = Math.Max(fittedFontSize * 1.25d, fittedFontSize + 2d);
        }

        private double GetAdaptivePreviewMaxHeight()
        {
            double hostHeight = _adaptiveWidthHost?.ActualHeight ?? 0d;
            double rawHeight = hostHeight > 0
                ? hostHeight * PreviewHeightRatio
                : FallbackPreviewMaxHeight;
            return Math.Clamp(rawHeight, MinPreviewMaxHeight, MaxPreviewMaxHeight);
        }

        private double EstimatePreviewTextWidth()
        {
            double hintWidth = ActualWidth > 0 ? ActualWidth : Width;
            if (double.IsNaN(hintWidth) || hintWidth <= 0)
            {
                hintWidth = FallbackHintWidth;
            }

            return Math.Max(240d, hintWidth - PreviewTextSidePadding);
        }

        private double MeasurePreviewHeight(string text, double fontSize, double width)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return 0d;
            }

            var typeface = new Typeface(
                PreviewContentText.FontFamily,
                PreviewContentText.FontStyle,
                PreviewContentText.FontWeight,
                PreviewContentText.FontStretch);

            double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            var formattedText = new FormattedText(
                text,
                CultureInfo.CurrentUICulture,
                System.Windows.FlowDirection.LeftToRight,
                typeface,
                fontSize,
                System.Windows.Media.Brushes.White,
                pixelsPerDip)
            {
                MaxTextWidth = Math.Max(1d, width),
                Trimming = TextTrimming.None,
                TextAlignment = TextAlignment.Left
            };

            return formattedText.Height;
        }

        /// <summary>
        /// 隐藏提示框
        /// </summary>
        public void Hide()
        {
            Visibility = Visibility.Collapsed;
            MatchResultsPanel.Children.Clear();
            PreviewPanel.Visibility = Visibility.Collapsed;
            PreviewReferenceText.Text = string.Empty;
            PreviewReferenceText.Visibility = Visibility.Collapsed;
            PreviewContentText.Text = string.Empty;
            PreviewContentText.FontSize = DefaultPreviewFontSize;
            PreviewContentText.ClearValue(TextBlock.LineHeightProperty);
            _lastPreviewContent = string.Empty;
            _lastRequestedPreviewFontSize = DefaultPreviewFontSize;
            if (PreviewScrollViewer != null)
            {
                PreviewScrollViewer.MaxHeight = MinPreviewMaxHeight;
            }
            ApplyCompactWidth();
        }
    }
}

