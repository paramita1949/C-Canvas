using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
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
        private const int FixedVisiblePreviewVerseCount = 10;
        private const double PreviewListVerticalPadding = 8d;

        private FrameworkElement _adaptiveWidthHost;
        private string _lastPreviewContent = string.Empty;
        private double _lastRequestedPreviewFontSize = DefaultPreviewFontSize;
        private readonly DispatcherTimer _caretBlinkTimer;
        private string _inputDisplayText = string.Empty;
        private bool _isCaretVisible = true;
        private int _lastMatchDisplayCount;
        private List<string> _lastMatchDisplayTexts = new();
        private int? _pendingStartVerse;
        private static readonly Regex PreviewVerseLineRegex = new(@"^\s*(\d+)\s+(.+)$", RegexOptions.Compiled);

        public event Action<int, int> PreviewVerseRangeConfirmed;
        public event Action<bool> ConfirmActionRequested;

        private sealed class PreviewVerseItem
        {
            public int VerseNumber { get; init; }
            public string DisplayText { get; init; }
            public System.Windows.Media.Brush Foreground { get; init; }
            public bool IsSelectable => VerseNumber > 0;
        }

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
                PopulatePreviewVerses(previewContent, ResolvePreviewForegroundBrush(previewReference));
                ApplyAdaptivePreviewLayout();
            }
            else
            {
                PreviewPanel.Visibility = Visibility.Collapsed;
                PreviewReferenceText.Text = string.Empty;
                PreviewReferenceText.Visibility = Visibility.Collapsed;
                PreviewVerseList.ItemsSource = null;
                PreviewVerseList.SelectedItem = null;
                _pendingStartVerse = null;
                if (PreviewScrollViewer != null)
                {
                    PreviewScrollViewer.MaxHeight = MinPreviewMaxHeight;
                }
            }

            // 显示提示框
            Visibility = Visibility.Visible;
        }

        public void SetConfirmActionsVisible(bool visible, string confirmText = "确认", string cancelText = "取消")
        {
            if (ConfirmActionPanel == null || BtnPreviewConfirm == null || BtnPreviewCancel == null)
            {
                return;
            }

            ConfirmActionPanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            BtnPreviewConfirm.Content = string.IsNullOrWhiteSpace(confirmText) ? "确认" : confirmText;
            BtnPreviewCancel.Content = string.IsNullOrWhiteSpace(cancelText) ? "取消" : cancelText;
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

            double requestedFontSize = Math.Clamp(
                _lastRequestedPreviewFontSize > 0 ? _lastRequestedPreviewFontSize : DefaultPreviewFontSize,
                MinPreviewFontSize,
                MaxPreviewFontSize);
            double lineHeight = Math.Max(requestedFontSize * 1.25d, requestedFontSize + 2d);
            double maxHeight = Math.Max(MinPreviewMaxHeight, lineHeight * FixedVisiblePreviewVerseCount + PreviewListVerticalPadding);

            if (PreviewScrollViewer != null)
            {
                PreviewScrollViewer.MaxHeight = maxHeight;
            }

            if (PreviewVerseList != null)
            {
                PreviewVerseList.FontSize = requestedFontSize;
                PreviewVerseList.SetValue(TextBlock.LineHeightProperty, lineHeight);
            }
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

        private void PopulatePreviewVerses(string previewContent, System.Windows.Media.Brush foreground)
        {
            _pendingStartVerse = null;
            PreviewVerseList.SelectedItem = null;

            var lines = (previewContent ?? string.Empty)
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToList();

            var items = new List<PreviewVerseItem>(lines.Count);
            foreach (var line in lines)
            {
                var match = PreviewVerseLineRegex.Match(line);
                if (match.Success && int.TryParse(match.Groups[1].Value, out int verseNo))
                {
                    items.Add(new PreviewVerseItem
                    {
                        VerseNumber = verseNo,
                        DisplayText = line,
                        Foreground = foreground
                    });
                    continue;
                }

                items.Add(new PreviewVerseItem
                {
                    VerseNumber = 0,
                    DisplayText = line,
                    Foreground = foreground
                });
            }

            PreviewVerseList.ItemsSource = items;
        }

        private void PreviewVerseList_PreviewMouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is not System.Windows.Controls.ListBox listBox)
            {
                return;
            }

            var listBoxItem = ItemsControl.ContainerFromElement(listBox, e.OriginalSource as DependencyObject) as System.Windows.Controls.ListBoxItem;
            if (listBoxItem?.DataContext is not PreviewVerseItem verseItem || !verseItem.IsSelectable)
            {
                return;
            }

            if (!_pendingStartVerse.HasValue)
            {
                _pendingStartVerse = verseItem.VerseNumber;
                listBox.SelectedItem = verseItem;
                return;
            }

            int start = _pendingStartVerse.Value;
            int end = verseItem.VerseNumber;
            if (end < start)
            {
                (start, end) = (end, start);
            }

            _pendingStartVerse = null;
            listBox.SelectedItem = null;
            PreviewVerseRangeConfirmed?.Invoke(start, end);
            e.Handled = true;
        }

        private void PreviewVerseList_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            if (PreviewScrollViewer == null)
            {
                return;
            }

            // ListBox 作为内层控件会吞掉滚轮，这里统一转给外层 ScrollViewer。
            double offset = e.Delta > 0 ? -48d : 48d;
            PreviewScrollViewer.ScrollToVerticalOffset(PreviewScrollViewer.VerticalOffset + offset);
            e.Handled = true;
        }

        /// <summary>
        /// 隐藏提示框
        /// </summary>
        public void Hide()
        {
            StopCaretBlinking();
            Visibility = Visibility.Collapsed;
            SetConfirmActionsVisible(false);
            MatchResultsPanel.Children.Clear();
            _inputDisplayText = string.Empty;
            InputText.Text = string.Empty;
            PreviewPanel.Visibility = Visibility.Collapsed;
            PreviewReferenceText.Text = string.Empty;
            PreviewReferenceText.Visibility = Visibility.Collapsed;
            PreviewVerseList.ItemsSource = null;
            PreviewVerseList.SelectedItem = null;
            PreviewVerseList.FontSize = DefaultPreviewFontSize;
            PreviewVerseList.ClearValue(TextBlock.LineHeightProperty);
            _pendingStartVerse = null;
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

        private void BtnPreviewCancel_Click(object sender, RoutedEventArgs e)
        {
            ConfirmActionRequested?.Invoke(false);
            e.Handled = true;
        }

        private void BtnPreviewConfirm_Click(object sender, RoutedEventArgs e)
        {
            ConfirmActionRequested?.Invoke(true);
            e.Handled = true;
        }

        public void RefreshThemeResources()
        {
            try
            {
                if (PreviewVerseList == null)
                {
                    return;
                }

                if (TryFindResource("PreviewVerseListItemStyle") is Style itemStyle)
                {
                    PreviewVerseList.ItemContainerStyle = null;
                    PreviewVerseList.ItemContainerStyle = itemStyle;
                }

                PreviewVerseList.Items.Refresh();
                PreviewVerseList.UpdateLayout();
            }
            catch
            {
                // ignore refresh failures; preview will still pick up updated resources on next rebuild
            }
        }

    }
}

