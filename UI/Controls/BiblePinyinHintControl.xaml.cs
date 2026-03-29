using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ImageColorChanger.Core;
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
        private const int MatchColumnsPerRow = 6;
        private const double MatchPanelHorizontalPadding = 20d;
        private const double MinMatchHintWidth = 360d;
        private const double MaxMatchHintWidth = 1280d;
        private const double MatchHostWidthRatioLimit = 0.88d;
        private const double MatchItemHorizontalSpacing = 6d;
        private const double MatchRowBottomSpacing = 2d;
        private const double MatchItemFontSize = 15d;
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
        private readonly DispatcherTimer _caretBlinkTimer;
        private string _inputDisplayText = string.Empty;
        private bool _isCaretVisible = true;
        private int _lastMatchDisplayCount;
        private List<string> _lastMatchDisplayTexts = new();

        public BiblePinyinHintControl()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;

            _caretBlinkTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _caretBlinkTimer.Tick += CaretBlinkTimer_Tick;
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
            bool hasMatches = matches != null && matches.Count > 0;
            var displayMatches = hasMatches
                ? matches.Take(9).ToList()
                : new List<BibleBookMatch>();
            _lastMatchDisplayTexts = displayMatches
                .Select((m, idx) => BuildMatchDisplayText(m, idx + 1))
                .ToList();
            _lastMatchDisplayCount = _lastMatchDisplayTexts.Count;

            if (hasPreview)
            {
                EnsureAdaptiveWidthHost();
                ApplyAdaptiveWidth();
                _lastPreviewContent = previewContent ?? string.Empty;
                _lastRequestedPreviewFontSize = previewFontSize;
            }
            else if (_lastMatchDisplayCount > 0)
            {
                EnsureAdaptiveWidthHost();
                ApplyAdaptiveWidthForMatchList(_lastMatchDisplayTexts);
                _lastPreviewContent = string.Empty;
                _lastRequestedPreviewFontSize = DefaultPreviewFontSize;
            }
            else
            {
                ApplyCompactWidth();
                _lastPreviewContent = string.Empty;
                _lastRequestedPreviewFontSize = DefaultPreviewFontSize;
            }

            _inputDisplayText = displayText ?? string.Empty;
            EnsureCaretBlinking();
            RefreshInputTextWithCaret();

            // 清空匹配结果
            MatchResultsPanel.Children.Clear();

            // 如果有匹配的书卷，显示横向排列
            if (hasMatches)
            {
                MatchResultsPanel.Visibility = Visibility.Visible;
                ConfigureMatchResultsLayout(_lastMatchDisplayTexts);

                for (int rowStart = 0; rowStart < _lastMatchDisplayTexts.Count; rowStart += MatchColumnsPerRow)
                {
                    int rowEndExclusive = Math.Min(rowStart + MatchColumnsPerRow, _lastMatchDisplayTexts.Count);
                    bool hasNextRow = rowEndExclusive < _lastMatchDisplayTexts.Count;

                    var rowPanel = new StackPanel
                    {
                        Orientation = System.Windows.Controls.Orientation.Horizontal,
                        HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                        Margin = new Thickness(0, 0, 0, hasNextRow ? MatchRowBottomSpacing : 0)
                    };

                    for (int i = rowStart; i < rowEndExclusive; i++)
                    {
                        bool isLastInRow = i == rowEndExclusive - 1;
                        var textBlock = new TextBlock
                        {
                            Text = _lastMatchDisplayTexts[i],
                            Foreground = new SolidColorBrush(Colors.White),
                            FontSize = MatchItemFontSize,
                            Margin = new Thickness(0, 0, isLastInRow ? 0 : MatchItemHorizontalSpacing, 0),
                            TextTrimming = TextTrimming.None,
                            VerticalAlignment = VerticalAlignment.Center
                        };

                        rowPanel.Children.Add(textBlock);
                    }

                    MatchResultsPanel.Children.Add(rowPanel);
                }
            }
            else
            {
                MatchResultsPanel.Visibility = Visibility.Collapsed;
                MatchResultsPanel.Width = double.NaN;
                _lastMatchDisplayCount = 0;
                _lastMatchDisplayTexts = new List<string>();
            }

            if (hasPreview)
            {
                PreviewPanel.Visibility = Visibility.Visible;
                PreviewReferenceText.Text = string.Empty;
                PreviewReferenceText.Visibility = Visibility.Collapsed;
                PreviewContentText.Text = previewContent;
                PreviewContentText.Foreground = ResolvePreviewForegroundBrush(previewReference);
                ApplyAdaptivePreviewLayout();
            }
            else
            {
                PreviewPanel.Visibility = Visibility.Collapsed;
                PreviewReferenceText.Text = string.Empty;
                PreviewReferenceText.Visibility = Visibility.Collapsed;
                PreviewContentText.Text = string.Empty;
                PreviewContentText.Foreground = new SolidColorBrush(Colors.White);
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
            StopCaretBlinking();
            DetachAdaptiveWidthHost();
        }

        private void CaretBlinkTimer_Tick(object sender, EventArgs e)
        {
            _isCaretVisible = !_isCaretVisible;
            RefreshInputTextWithCaret();
        }

        private void EnsureCaretBlinking()
        {
            if (_caretBlinkTimer.IsEnabled)
            {
                return;
            }

            _isCaretVisible = true;
            _caretBlinkTimer.Start();
        }

        private void StopCaretBlinking()
        {
            if (_caretBlinkTimer.IsEnabled)
            {
                _caretBlinkTimer.Stop();
            }

            _isCaretVisible = true;
        }

        private void RefreshInputTextWithCaret()
        {
            if (InputText == null)
            {
                return;
            }

            string caret = _isCaretVisible ? "|" : " ";
            if (string.IsNullOrEmpty(_inputDisplayText))
            {
                InputText.Text = caret;
                return;
            }

            InputText.Text = $"{_inputDisplayText} {caret}";
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
            bool isPreviewVisible = PreviewPanel?.Visibility == Visibility.Visible;
            bool isMatchVisible = MatchResultsPanel?.Visibility == Visibility.Visible;

            if (isPreviewVisible)
            {
                ApplyAdaptiveWidth();
                ApplyAdaptivePreviewLayout();
            }
            else if (isMatchVisible)
            {
                ApplyAdaptiveWidthForMatchList(_lastMatchDisplayTexts);
                ConfigureMatchResultsLayout(_lastMatchDisplayTexts);
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

        private void ApplyAdaptiveWidthForMatchList(List<string> displayTexts)
        {
            int columns = GetMatchColumns(displayTexts?.Count ?? 0);
            double maxRowWidth = MeasureMaxRowWidth(displayTexts, columns);
            double desiredWidth = maxRowWidth + MatchPanelHorizontalPadding;
            double hostWidth = _adaptiveWidthHost?.ActualWidth ?? 0d;

            double maxByHost = hostWidth > 0
                ? hostWidth * MatchHostWidthRatioLimit
                : MaxMatchHintWidth;
            double widthUpperBound = Math.Max(MinMatchHintWidth, Math.Min(MaxMatchHintWidth, maxByHost));

            Width = Math.Clamp(desiredWidth, MinMatchHintWidth, widthUpperBound);
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

        private void ConfigureMatchResultsLayout(List<string> displayTexts)
        {
            if (MatchResultsPanel == null || MatchResultsPanel.Visibility != Visibility.Visible)
            {
                return;
            }

            int columns = GetMatchColumns(displayTexts?.Count ?? 0);
            double maxRowWidth = MeasureMaxRowWidth(displayTexts, columns);
            MatchResultsPanel.Width = Math.Max(240d, maxRowWidth);
        }

        private static string BuildMatchDisplayText(BibleBookMatch match, int index)
        {
            var book = BibleBookConfig.GetBook(match.BookId);
            string shortName = book?.ShortName?.Trim();
            string displayName = match.BookName;
            if (!string.IsNullOrWhiteSpace(shortName) &&
                !string.Equals(shortName, match.BookName, StringComparison.Ordinal))
            {
                displayName = $"{match.BookName}（{shortName}）";
            }

            return $"{index} {displayName}";
        }

        private static bool IsSingleVerseReference(string previewReference)
        {
            if (string.IsNullOrWhiteSpace(previewReference))
            {
                return false;
            }

            string text = previewReference.Trim();
            return text.Contains(':') && !text.Contains('-') && !text.Contains('—');
        }

        private static System.Windows.Media.Brush ResolvePreviewForegroundBrush(string previewReference)
        {
            if (!IsSingleVerseReference(previewReference))
            {
                return new SolidColorBrush(Colors.White);
            }

            try
            {
                string hex = ConfigManager.Instance?.BibleHighlightColor;
                if (!string.IsNullOrWhiteSpace(hex))
                {
                    var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
                    return new SolidColorBrush(color);
                }
            }
            catch
            {
            }

            return new SolidColorBrush(Colors.Yellow);
        }

        private double MeasureTextWidth(string text, double fontSize)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return 0d;
            }

            var typeface = new Typeface(
                FontFamily,
                FontStyles.Normal,
                FontWeights.Normal,
                FontStretches.Normal);

            double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            var formattedText = new FormattedText(
                text,
                CultureInfo.CurrentUICulture,
                System.Windows.FlowDirection.LeftToRight,
                typeface,
                fontSize,
                System.Windows.Media.Brushes.White,
                pixelsPerDip);

            return formattedText.WidthIncludingTrailingWhitespace;
        }

        private double MeasureMaxRowWidth(List<string> displayTexts, int columns)
        {
            if (displayTexts == null || displayTexts.Count == 0 || columns <= 0)
            {
                return 240d;
            }

            double maxWidth = 0d;
            for (int rowStart = 0; rowStart < displayTexts.Count; rowStart += columns)
            {
                int rowEndExclusive = Math.Min(rowStart + columns, displayTexts.Count);
                double rowWidth = 0d;
                for (int i = rowStart; i < rowEndExclusive; i++)
                {
                    rowWidth += MeasureTextWidth(displayTexts[i], MatchItemFontSize);
                    if (i < rowEndExclusive - 1)
                    {
                        rowWidth += MatchItemHorizontalSpacing;
                    }
                }

                maxWidth = Math.Max(maxWidth, rowWidth);
            }

            return Math.Max(240d, maxWidth);
        }

        private static int GetMatchColumns(int matchCount)
        {
            if (matchCount <= 0)
            {
                return 1;
            }

            return Math.Min(MatchColumnsPerRow, matchCount);
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
            StopCaretBlinking();
            Visibility = Visibility.Collapsed;
            MatchResultsPanel.Children.Clear();
            _inputDisplayText = string.Empty;
            InputText.Text = string.Empty;
            PreviewPanel.Visibility = Visibility.Collapsed;
            PreviewReferenceText.Text = string.Empty;
            PreviewReferenceText.Visibility = Visibility.Collapsed;
            PreviewContentText.Text = string.Empty;
            PreviewContentText.Foreground = new SolidColorBrush(Colors.White);
            PreviewContentText.FontSize = DefaultPreviewFontSize;
            PreviewContentText.ClearValue(TextBlock.LineHeightProperty);
            _lastPreviewContent = string.Empty;
            _lastRequestedPreviewFontSize = DefaultPreviewFontSize;
            MatchResultsPanel.Width = double.NaN;
            _lastMatchDisplayCount = 0;
            _lastMatchDisplayTexts = new List<string>();
            if (PreviewScrollViewer != null)
            {
                PreviewScrollViewer.MaxHeight = MinPreviewMaxHeight;
            }
            ApplyCompactWidth();
        }
    }
}

