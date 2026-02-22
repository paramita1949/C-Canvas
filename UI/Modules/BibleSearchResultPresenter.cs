using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using WpfBorder = System.Windows.Controls.Border;
using WpfButton = System.Windows.Controls.Button;
using WpfListBox = System.Windows.Controls.ListBox;
using WpfPanel = System.Windows.Controls.Panel;
using WpfTextBlock = System.Windows.Controls.TextBlock;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace ImageColorChanger.UI.Modules
{
    public sealed class BibleSearchResultPresenter : IBibleSearchResultPresenter
    {
        private const int PageSize = 100;

        private readonly BiblePopupSearchResultSurface _popupSurface;
        private readonly BibleEmbeddedSearchResultSurface _embeddedSurface;
        private BibleSearchResultDisplayMode _displayMode;

        public event EventHandler<BibleSearchHit> HitSelected;
        public event EventHandler Dismissed;

        public bool IsOpen => GetActiveSurface().IsOpen;

        public BibleSearchResultDisplayMode DisplayMode
        {
            get => _displayMode;
            set
            {
                if (_displayMode == value)
                {
                    return;
                }

                _displayMode = value;
                _popupSurface.Hide();
                _embeddedSurface.Hide();
            }
        }

        public BibleSearchResultPresenter(
            Popup popup,
            WpfPanel popupFilterTagsPanel,
            WpfListBox popupListBox,
            WpfButton popupPrevPageButton,
            WpfTextBlock popupPageInfoText,
            WpfButton popupNextPageButton,
            WpfTextBlock popupStatusText,
            WpfBorder embeddedContainer,
            WpfPanel embeddedFilterTagsPanel,
            WpfListBox embeddedListBox,
            WpfButton embeddedPrevPageButton,
            WpfTextBlock embeddedPageInfoText,
            WpfButton embeddedNextPageButton,
            WpfTextBlock embeddedStatusText,
            WpfTextBox searchBox,
            BibleSearchResultDisplayMode initialMode = BibleSearchResultDisplayMode.Floating,
            double floatingFontSize = 15.0,
            double embeddedFontSize = 15.0)
        {
            _popupSurface = new BiblePopupSearchResultSurface(
                popup ?? throw new ArgumentNullException(nameof(popup)),
                popupFilterTagsPanel ?? throw new ArgumentNullException(nameof(popupFilterTagsPanel)),
                popupListBox ?? throw new ArgumentNullException(nameof(popupListBox)),
                popupPrevPageButton ?? throw new ArgumentNullException(nameof(popupPrevPageButton)),
                popupPageInfoText ?? throw new ArgumentNullException(nameof(popupPageInfoText)),
                popupNextPageButton ?? throw new ArgumentNullException(nameof(popupNextPageButton)),
                popupStatusText ?? throw new ArgumentNullException(nameof(popupStatusText)),
                searchBox ?? throw new ArgumentNullException(nameof(searchBox)));

            _embeddedSurface = new BibleEmbeddedSearchResultSurface(
                embeddedContainer ?? throw new ArgumentNullException(nameof(embeddedContainer)),
                embeddedFilterTagsPanel ?? throw new ArgumentNullException(nameof(embeddedFilterTagsPanel)),
                embeddedListBox ?? throw new ArgumentNullException(nameof(embeddedListBox)),
                embeddedPrevPageButton ?? throw new ArgumentNullException(nameof(embeddedPrevPageButton)),
                embeddedPageInfoText ?? throw new ArgumentNullException(nameof(embeddedPageInfoText)),
                embeddedNextPageButton ?? throw new ArgumentNullException(nameof(embeddedNextPageButton)),
                embeddedStatusText ?? throw new ArgumentNullException(nameof(embeddedStatusText)),
                searchBox);

            _popupSurface.HitSelected += Surface_HitSelected;
            _embeddedSurface.HitSelected += Surface_HitSelected;
            _popupSurface.Dismissed += Surface_Dismissed;
            _embeddedSurface.Dismissed += Surface_Dismissed;

            _displayMode = initialMode;
            SetResultFontSizes(floatingFontSize, embeddedFontSize);
            _popupSurface.Hide();
            _embeddedSurface.Hide();
        }

        public void SetResultFontSizes(double floatingFontSize, double embeddedFontSize)
        {
            _popupSurface.SetResultFontSize(NormalizeResultFontSize(floatingFontSize));
            _embeddedSurface.SetResultFontSize(NormalizeResultFontSize(embeddedFontSize));
        }

        public void ShowResults(IReadOnlyList<BibleSearchHit> hits)
        {
            GetInactiveSurface().Hide();
            GetActiveSurface().ShowResults(hits);
        }

        public void ShowStatus(string message)
        {
            GetInactiveSurface().Hide();
            GetActiveSurface().ShowStatus(message);
        }

        public void Hide()
        {
            _popupSurface.Hide();
            _embeddedSurface.Hide();
        }

        public bool HandleSearchBoxKeyDown(Key key)
        {
            return GetActiveSurface().HandleSearchBoxKeyDown(key);
        }

        public bool HandleWindowPreviewMouseDown(DependencyObject source)
        {
            return _displayMode == BibleSearchResultDisplayMode.Embedded
                ? _embeddedSurface.HandleWindowPreviewMouseDown(source)
                : _popupSurface.HandleWindowPreviewMouseDown(source);
        }

        public void Dispose()
        {
            _popupSurface.HitSelected -= Surface_HitSelected;
            _embeddedSurface.HitSelected -= Surface_HitSelected;
            _popupSurface.Dismissed -= Surface_Dismissed;
            _embeddedSurface.Dismissed -= Surface_Dismissed;
            _popupSurface.Dispose();
            _embeddedSurface.Dispose();
        }

        private void Surface_HitSelected(object sender, BibleSearchHit hit)
        {
            HitSelected?.Invoke(this, hit);
        }

        private void Surface_Dismissed(object sender, EventArgs e)
        {
            Dismissed?.Invoke(this, EventArgs.Empty);
        }

        private IBibleSearchResultSurface GetActiveSurface()
        {
            return _displayMode == BibleSearchResultDisplayMode.Embedded ? _embeddedSurface : _popupSurface;
        }

        private IBibleSearchResultSurface GetInactiveSurface()
        {
            return _displayMode == BibleSearchResultDisplayMode.Embedded ? _popupSurface : _embeddedSurface;
        }

        private interface IBibleSearchResultSurface : IDisposable
        {
            event EventHandler<BibleSearchHit> HitSelected;
            event EventHandler Dismissed;
            bool IsOpen { get; }
            void ShowResults(IReadOnlyList<BibleSearchHit> hits);
            void ShowStatus(string message);
            void Hide();
            bool HandleSearchBoxKeyDown(Key key);
            bool HandleWindowPreviewMouseDown(DependencyObject source);
        }

        private sealed class BookFilterOption
        {
            public const string AllKey = "__ALL__";
            public string Key { get; init; }
            public string Label { get; init; }
            public int Book { get; init; }
        }

        private static int CalculateTotalPages(int totalItems)
        {
            return totalItems <= 0 ? 0 : (totalItems + PageSize - 1) / PageSize;
        }

        private static List<BookFilterOption> BuildBookFilterOptions(IReadOnlyList<BibleSearchHit> hits)
        {
            var source = hits ?? Array.Empty<BibleSearchHit>();
            var options = source
                .GroupBy(BuildBookKey)
                .Select(g =>
                {
                    var first = g.First();
                    return new BookFilterOption
                    {
                        Key = g.Key,
                        Label = BuildBookLabel(first),
                        Book = first.Book
                    };
                })
                .OrderBy(o => o.Book)
                .ToList();

            options.Insert(0, new BookFilterOption { Key = BookFilterOption.AllKey, Label = "全部", Book = 0 });
            return options;
        }

        private static string BuildBookKey(BibleSearchHit hit)
        {
            return hit == null ? string.Empty : hit.Book.ToString();
        }

        private static string BuildBookLabel(BibleSearchHit hit)
        {
            if (hit == null)
            {
                return "未知经卷";
            }

            if (!string.IsNullOrWhiteSpace(hit.Reference))
            {
                int splitIndex = hit.Reference.LastIndexOf(' ');
                if (splitIndex > 0)
                {
                    return hit.Reference.Substring(0, splitIndex).Trim();
                }
            }

            return $"卷{hit.Book}";
        }

        private static IEnumerable<BibleSearchHit> ApplyBookFilter(IReadOnlyList<BibleSearchHit> hits, string filterKey)
        {
            var source = hits ?? Array.Empty<BibleSearchHit>();
            if (string.IsNullOrWhiteSpace(filterKey) || filterKey == BookFilterOption.AllKey)
            {
                return source;
            }

            return source.Where(h => BuildBookKey(h) == filterKey);
        }

        private static WpfButton CreateFilterTextButton(BookFilterOption option, bool selected, RoutedEventHandler onClick)
        {
            var button = new WpfButton
            {
                Tag = option.Key,
                Content = option.Label,
                FontSize = 13,
                Padding = new Thickness(0),
                Margin = new Thickness(0, 0, 4, 6),
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand,
                MinHeight = 22,
                Background = System.Windows.Media.Brushes.Transparent,
                BorderBrush = System.Windows.Media.Brushes.Transparent,
                Foreground = selected ? CreateBrush("#0A58CA") : CreateBrush("#666666"),
                FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal
            };
            button.Click += onClick;
            return button;
        }

        private static WpfTextBlock CreateFilterSeparatorText()
        {
            return new WpfTextBlock
            {
                Text = " | ",
                Foreground = CreateBrush("#9E9E9E"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 4, 6)
            };
        }

        private static SolidColorBrush CreateBrush(string hex)
        {
            return new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex));
        }

        private static void ScrollListToTop(WpfListBox listBox)
        {
            if (listBox == null)
            {
                return;
            }

            listBox.UpdateLayout();
            FindDescendantScrollViewer(listBox)?.ScrollToTop();
        }

        private static System.Windows.Controls.ScrollViewer FindDescendantScrollViewer(DependencyObject root)
        {
            if (root == null)
            {
                return null;
            }

            if (root is System.Windows.Controls.ScrollViewer viewer)
            {
                return viewer;
            }

            int childrenCount = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < childrenCount; i++)
            {
                var found = FindDescendantScrollViewer(VisualTreeHelper.GetChild(root, i));
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static double NormalizeResultFontSize(double size)
        {
            if (double.IsNaN(size) || double.IsInfinity(size) || size <= 0)
            {
                return 15.0;
            }

            return Math.Max(10.0, Math.Min(72.0, size));
        }

        private sealed class BiblePopupSearchResultSurface : IBibleSearchResultSurface
        {
            private readonly Popup _popup;
            private readonly WpfPanel _filterTagsPanel;
            private readonly WpfListBox _listBox;
            private readonly WpfButton _prevPageButton;
            private readonly WpfTextBlock _pageInfoText;
            private readonly WpfButton _nextPageButton;
            private readonly WpfTextBlock _statusText;
            private readonly WpfTextBox _searchBox;
            private bool _isProgrammaticClose;
            private IReadOnlyList<BibleSearchHit> _allHits = Array.Empty<BibleSearchHit>();
            private List<BibleSearchHit> _filteredHits = new();
            private string _selectedFilterKey = BookFilterOption.AllKey;
            private int _currentPageIndex;

            public event EventHandler<BibleSearchHit> HitSelected;
            public event EventHandler Dismissed;
            public bool IsOpen => _popup.IsOpen;

            public BiblePopupSearchResultSurface(Popup popup, WpfPanel filterTagsPanel, WpfListBox listBox, WpfButton prevPageButton, WpfTextBlock pageInfoText, WpfButton nextPageButton, WpfTextBlock statusText, WpfTextBox searchBox)
            {
                _popup = popup;
                _filterTagsPanel = filterTagsPanel;
                _listBox = listBox;
                _prevPageButton = prevPageButton;
                _pageInfoText = pageInfoText;
                _nextPageButton = nextPageButton;
                _statusText = statusText;
                _searchBox = searchBox;

                _listBox.MouseLeftButtonUp += ListBox_MouseLeftButtonUp;
                _popup.Closed += Popup_Closed;
                _prevPageButton.Click += PrevPageButton_Click;
                _nextPageButton.Click += NextPageButton_Click;
            }

            public void ShowResults(IReadOnlyList<BibleSearchHit> hits)
            {
                _allHits = hits ?? Array.Empty<BibleSearchHit>();
                _selectedFilterKey = BookFilterOption.AllKey;
                _currentPageIndex = 0;
                RenderFilterTags();
                ApplyFilter();
                _listBox.MinWidth = Math.Max(420, _searchBox.ActualWidth);
                _popup.IsOpen = true;
            }

            public void ShowStatus(string message)
            {
                _statusText.Text = message ?? string.Empty;
                _pageInfoText.Text = string.Empty;
                _prevPageButton.IsEnabled = false;
                _nextPageButton.IsEnabled = false;
                if (!_popup.IsOpen)
                {
                    _popup.IsOpen = true;
                }
            }

            public void Hide()
            {
                _isProgrammaticClose = true;
                _popup.IsOpen = false;
                ClearState();
            }

            public bool HandleSearchBoxKeyDown(Key key)
            {
                if (!_popup.IsOpen)
                {
                    return false;
                }

                return key switch
                {
                    Key.Escape => CloseByEscape(),
                    Key.Down => MoveSelectionAndHandled(1),
                    Key.Up => MoveSelectionAndHandled(-1),
                    Key.Enter => RaiseSelectedAndHandled(),
                    _ => false
                };
            }

            public bool HandleWindowPreviewMouseDown(DependencyObject source)
            {
                if (!_popup.IsOpen)
                {
                    return false;
                }

                if (_searchBox.IsMouseOver || IsPopupContentMouseOver())
                {
                    return false;
                }

                _isProgrammaticClose = false;
                _popup.IsOpen = false;
                return true;
            }

            public void Dispose()
            {
                _listBox.MouseLeftButtonUp -= ListBox_MouseLeftButtonUp;
                _popup.Closed -= Popup_Closed;
                _prevPageButton.Click -= PrevPageButton_Click;
                _nextPageButton.Click -= NextPageButton_Click;
            }

            public void SetResultFontSize(double fontSize)
            {
                _listBox.FontSize = fontSize;
            }

            private bool CloseByEscape()
            {
                _isProgrammaticClose = false;
                _popup.IsOpen = false;
                return true;
            }

            private bool MoveSelectionAndHandled(int delta)
            {
                MoveSelection(delta);
                return true;
            }

            private bool RaiseSelectedAndHandled()
            {
                RaiseSelectedHit();
                return true;
            }

            private void Popup_Closed(object sender, EventArgs e)
            {
                bool notify = !_isProgrammaticClose;
                _isProgrammaticClose = false;
                ClearState();
                if (notify)
                {
                    Dismissed?.Invoke(this, EventArgs.Empty);
                }
            }

            private void PrevPageButton_Click(object sender, RoutedEventArgs e)
            {
                if (_currentPageIndex <= 0) return;
                _currentPageIndex--;
                RenderCurrentPage();
            }

            private void NextPageButton_Click(object sender, RoutedEventArgs e)
            {
                int totalPages = CalculateTotalPages(_filteredHits.Count);
                if (_currentPageIndex >= totalPages - 1) return;
                _currentPageIndex++;
                RenderCurrentPage();
            }

            private void FilterTagButton_Click(object sender, RoutedEventArgs e)
            {
                if (sender is not WpfButton button || button.Tag is not string key) return;
                _selectedFilterKey = key;
                _currentPageIndex = 0;
                RenderFilterTags();
                ApplyFilter();
            }

            private void RenderFilterTags()
            {
                _filterTagsPanel.Children.Clear();
                var options = BuildBookFilterOptions(_allHits);
                for (int i = 0; i < options.Count; i++)
                {
                    var option = options[i];
                    _filterTagsPanel.Children.Add(CreateFilterTextButton(option, option.Key == _selectedFilterKey, FilterTagButton_Click));
                    if (i < options.Count - 1)
                    {
                        _filterTagsPanel.Children.Add(CreateFilterSeparatorText());
                    }
                }
            }

            private void ApplyFilter()
            {
                _filteredHits = ApplyBookFilter(_allHits, _selectedFilterKey).ToList();
                _currentPageIndex = 0;
                RenderCurrentPage();
                UpdateStatusText();
            }

            private void RenderCurrentPage()
            {
                int totalPages = CalculateTotalPages(_filteredHits.Count);
                if (_filteredHits.Count == 0)
                {
                    _listBox.ItemsSource = null;
                    _listBox.SelectedIndex = -1;
                    ScrollListToTop(_listBox);
                    _pageInfoText.Text = "第 0/0 页";
                    _prevPageButton.IsEnabled = false;
                    _nextPageButton.IsEnabled = false;
                    return;
                }

                if (_currentPageIndex < 0) _currentPageIndex = 0;
                if (_currentPageIndex >= totalPages) _currentPageIndex = totalPages - 1;

                var pageItems = _filteredHits.Skip(_currentPageIndex * PageSize).Take(PageSize).ToList();
                _listBox.ItemsSource = pageItems;
                _listBox.SelectedIndex = pageItems.Count > 0 ? 0 : -1;
                ScrollListToTop(_listBox);
                _pageInfoText.Text = $"第 {_currentPageIndex + 1}/{totalPages} 页";
                _prevPageButton.IsEnabled = _currentPageIndex > 0;
                _nextPageButton.IsEnabled = _currentPageIndex < totalPages - 1;
            }

            private void UpdateStatusText()
            {
                if (_allHits.Count == 0)
                {
                    _statusText.Text = "未找到相关经文";
                }
                else if (_selectedFilterKey == BookFilterOption.AllKey)
                {
                    _statusText.Text = $"找到 {_allHits.Count} 条（每页 {PageSize} 条）";
                }
                else
                {
                    _statusText.Text = $"筛选后 {_filteredHits.Count}/{_allHits.Count} 条（每页 {PageSize} 条）";
                }
            }

            private void ListBox_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => RaiseSelectedHit();

            private void MoveSelection(int delta)
            {
                if (_listBox.Items.Count == 0) return;

                int nextIndex = _listBox.SelectedIndex + delta;
                if (nextIndex < 0) nextIndex = 0;
                else if (nextIndex >= _listBox.Items.Count) nextIndex = _listBox.Items.Count - 1;

                _listBox.SelectedIndex = nextIndex;
                _listBox.ScrollIntoView(_listBox.SelectedItem);
            }

            private void RaiseSelectedHit()
            {
                if (_listBox.SelectedItem is BibleSearchHit hit)
                {
                    HitSelected?.Invoke(this, hit);
                }
            }

            private void ClearState()
            {
                _allHits = Array.Empty<BibleSearchHit>();
                _filteredHits.Clear();
                _selectedFilterKey = BookFilterOption.AllKey;
                _currentPageIndex = 0;
                _filterTagsPanel.Children.Clear();
                _listBox.ItemsSource = null;
                _listBox.SelectedIndex = -1;
                _statusText.Text = string.Empty;
                _pageInfoText.Text = string.Empty;
                _prevPageButton.IsEnabled = false;
                _nextPageButton.IsEnabled = false;
            }

            private bool IsPopupContentMouseOver()
            {
                if (_popup.Child is UIElement child)
                {
                    return child.IsMouseOver;
                }

                return false;
            }
        }

        private sealed class BibleEmbeddedSearchResultSurface : IBibleSearchResultSurface
        {
            private readonly WpfBorder _container;
            private readonly WpfPanel _filterTagsPanel;
            private readonly WpfListBox _listBox;
            private readonly WpfButton _prevPageButton;
            private readonly WpfTextBlock _pageInfoText;
            private readonly WpfButton _nextPageButton;
            private readonly WpfTextBlock _statusText;
            private readonly WpfTextBox _searchBox;
            private IReadOnlyList<BibleSearchHit> _allHits = Array.Empty<BibleSearchHit>();
            private List<BibleSearchHit> _filteredHits = new();
            private string _selectedFilterKey = BookFilterOption.AllKey;
            private int _currentPageIndex;

            public event EventHandler<BibleSearchHit> HitSelected;
            public event EventHandler Dismissed;
            public bool IsOpen => _container.Visibility == Visibility.Visible;

            public BibleEmbeddedSearchResultSurface(WpfBorder container, WpfPanel filterTagsPanel, WpfListBox listBox, WpfButton prevPageButton, WpfTextBlock pageInfoText, WpfButton nextPageButton, WpfTextBlock statusText, WpfTextBox searchBox)
            {
                _container = container;
                _filterTagsPanel = filterTagsPanel;
                _listBox = listBox;
                _prevPageButton = prevPageButton;
                _pageInfoText = pageInfoText;
                _nextPageButton = nextPageButton;
                _statusText = statusText;
                _searchBox = searchBox;

                _listBox.MouseLeftButtonUp += ListBox_MouseLeftButtonUp;
                _prevPageButton.Click += PrevPageButton_Click;
                _nextPageButton.Click += NextPageButton_Click;
            }

            public void ShowResults(IReadOnlyList<BibleSearchHit> hits)
            {
                _allHits = hits ?? Array.Empty<BibleSearchHit>();
                _selectedFilterKey = BookFilterOption.AllKey;
                _currentPageIndex = 0;
                RenderFilterTags();
                ApplyFilter();
                _container.Visibility = Visibility.Visible;
            }

            public void ShowStatus(string message)
            {
                _statusText.Text = message ?? string.Empty;
                _pageInfoText.Text = string.Empty;
                _prevPageButton.IsEnabled = false;
                _nextPageButton.IsEnabled = false;
                _container.Visibility = Visibility.Visible;
            }

            public void Hide()
            {
                _allHits = Array.Empty<BibleSearchHit>();
                _filteredHits.Clear();
                _selectedFilterKey = BookFilterOption.AllKey;
                _currentPageIndex = 0;
                _filterTagsPanel.Children.Clear();
                _container.Visibility = Visibility.Collapsed;
                _listBox.ItemsSource = null;
                _listBox.SelectedIndex = -1;
                _statusText.Text = string.Empty;
                _pageInfoText.Text = string.Empty;
                _prevPageButton.IsEnabled = false;
                _nextPageButton.IsEnabled = false;
            }

            public bool HandleSearchBoxKeyDown(Key key)
            {
                if (!IsOpen)
                {
                    return false;
                }

                return key switch
                {
                    Key.Escape => CloseByEscape(),
                    Key.Down => MoveSelectionAndHandled(1),
                    Key.Up => MoveSelectionAndHandled(-1),
                    Key.Enter => RaiseSelectedAndHandled(),
                    _ => false
                };
            }

            public bool HandleWindowPreviewMouseDown(DependencyObject source)
            {
                if (!IsOpen)
                {
                    return false;
                }

                if (_container.IsMouseOver || _searchBox.IsMouseOver)
                {
                    return false;
                }

                Hide();
                Dismissed?.Invoke(this, EventArgs.Empty);
                return true;
            }

            public void Dispose()
            {
                _listBox.MouseLeftButtonUp -= ListBox_MouseLeftButtonUp;
                _prevPageButton.Click -= PrevPageButton_Click;
                _nextPageButton.Click -= NextPageButton_Click;
            }

            public void SetResultFontSize(double fontSize)
            {
                _listBox.FontSize = fontSize;
            }

            private bool CloseByEscape()
            {
                Hide();
                Dismissed?.Invoke(this, EventArgs.Empty);
                return true;
            }

            private bool MoveSelectionAndHandled(int delta)
            {
                MoveSelection(delta);
                return true;
            }

            private bool RaiseSelectedAndHandled()
            {
                RaiseSelectedHit();
                return true;
            }

            private void PrevPageButton_Click(object sender, RoutedEventArgs e)
            {
                if (_currentPageIndex <= 0) return;
                _currentPageIndex--;
                RenderCurrentPage();
            }

            private void NextPageButton_Click(object sender, RoutedEventArgs e)
            {
                int totalPages = CalculateTotalPages(_filteredHits.Count);
                if (_currentPageIndex >= totalPages - 1) return;
                _currentPageIndex++;
                RenderCurrentPage();
            }

            private void FilterTagButton_Click(object sender, RoutedEventArgs e)
            {
                if (sender is not WpfButton button || button.Tag is not string key) return;
                _selectedFilterKey = key;
                _currentPageIndex = 0;
                RenderFilterTags();
                ApplyFilter();
            }

            private void RenderFilterTags()
            {
                _filterTagsPanel.Children.Clear();
                var options = BuildBookFilterOptions(_allHits);
                const int maxTagsPerLine = 15;
                for (int i = 0; i < options.Count; i++)
                {
                    if (i > 0 && i % maxTagsPerLine == 0)
                    {
                        _filterTagsPanel.Children.Add(new WpfBorder
                        {
                            Width = 100000,
                            Height = 0
                        });
                    }

                    var option = options[i];
                    _filterTagsPanel.Children.Add(CreateFilterTextButton(option, option.Key == _selectedFilterKey, FilterTagButton_Click));
                    if (i < options.Count - 1)
                    {
                        _filterTagsPanel.Children.Add(CreateFilterSeparatorText());
                    }
                }
            }

            private void ApplyFilter()
            {
                _filteredHits = ApplyBookFilter(_allHits, _selectedFilterKey).ToList();
                _currentPageIndex = 0;
                RenderCurrentPage();
                UpdateStatusText();
            }

            private void RenderCurrentPage()
            {
                int totalPages = CalculateTotalPages(_filteredHits.Count);
                if (_filteredHits.Count == 0)
                {
                    _listBox.ItemsSource = null;
                    _listBox.SelectedIndex = -1;
                    ScrollListToTop(_listBox);
                    _pageInfoText.Text = "第 0/0 页";
                    _prevPageButton.IsEnabled = false;
                    _nextPageButton.IsEnabled = false;
                    return;
                }

                if (_currentPageIndex < 0) _currentPageIndex = 0;
                if (_currentPageIndex >= totalPages) _currentPageIndex = totalPages - 1;

                var pageItems = _filteredHits.Skip(_currentPageIndex * PageSize).Take(PageSize).ToList();
                _listBox.ItemsSource = pageItems;
                _listBox.SelectedIndex = pageItems.Count > 0 ? 0 : -1;
                ScrollListToTop(_listBox);
                _pageInfoText.Text = $"第 {_currentPageIndex + 1}/{totalPages} 页";
                _prevPageButton.IsEnabled = _currentPageIndex > 0;
                _nextPageButton.IsEnabled = _currentPageIndex < totalPages - 1;
            }

            private void UpdateStatusText()
            {
                if (_allHits.Count == 0)
                {
                    _statusText.Text = "未找到相关经文";
                }
                else if (_selectedFilterKey == BookFilterOption.AllKey)
                {
                    _statusText.Text = $"找到 {_allHits.Count} 条（每页 {PageSize} 条）";
                }
                else
                {
                    _statusText.Text = $"筛选后 {_filteredHits.Count}/{_allHits.Count} 条（每页 {PageSize} 条）";
                }
            }

            private void ListBox_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => RaiseSelectedHit();

            private void MoveSelection(int delta)
            {
                if (_listBox.Items.Count == 0) return;

                int nextIndex = _listBox.SelectedIndex + delta;
                if (nextIndex < 0) nextIndex = 0;
                else if (nextIndex >= _listBox.Items.Count) nextIndex = _listBox.Items.Count - 1;

                _listBox.SelectedIndex = nextIndex;
                _listBox.ScrollIntoView(_listBox.SelectedItem);
            }

            private void RaiseSelectedHit()
            {
                if (_listBox.SelectedItem is BibleSearchHit hit)
                {
                    HitSelected?.Invoke(this, hit);
                }
            }

        }
    }
}
