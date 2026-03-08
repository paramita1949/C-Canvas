using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ImageColorChanger.UI.Modules;
using MaterialDesignThemes.Wpf;

namespace ImageColorChanger.UI
{
    public partial class MainWindow
    {
        private const int BibleSearchDebounceMilliseconds = 300;
        private const string BibleSearchResultDisplayModeSettingKey = "BibleSearchResultDisplayMode";

        private IBibleSearchCoordinator _bibleSearchCoordinator;
        private IBibleSearchResultPresenter _bibleSearchResultPresenter;
        private IBibleHistorySlotWriter _bibleHistorySlotWriter;
        private BibleSearchResultDisplayMode _bibleSearchDisplayMode = BibleSearchResultDisplayMode.Embedded;
        private bool _bibleSearchDisplayModeLoaded;
        private bool _bibleSearchComponentsInitialized;
        private bool _isDisposingBibleSearchComponents;

        private BibleSearchResultDisplayMode GetEffectiveBibleSearchDisplayMode()
        {
            bool isTextEditorVisible = TextEditorPanel?.Visibility == Visibility.Visible;
            return BibleUiBehaviorResolver.ResolveSearchDisplayMode(_bibleSearchDisplayMode, isTextEditorVisible);
        }

        private void EnsureBibleSearchComponentsInitialized()
        {
            if (_bibleSearchComponentsInitialized)
            {
                return;
            }

            if (_bibleService == null ||
                SearchBox == null ||
                BibleSearchPopup == null ||
                BibleSearchFilterTagsPanel == null ||
                BibleSearchResultsList == null ||
                BibleSearchPrevPageButton == null ||
                BibleSearchPageInfoText == null ||
                BibleSearchNextPageButton == null ||
                BibleSearchResultStatus == null ||
                BibleEmbeddedSearchResultsContainer == null ||
                BibleEmbeddedSearchFilterTagsPanel == null ||
                BibleEmbeddedSearchResultsList == null ||
                BibleEmbeddedSearchPrevPageButton == null ||
                BibleEmbeddedSearchPageInfoText == null ||
                BibleEmbeddedSearchNextPageButton == null ||
                BibleEmbeddedSearchResultStatus == null)
            {
                return;
            }

            EnsureBibleSearchDisplayModeLoaded();
            _bibleSearchCoordinator = new BibleSearchCoordinator(_bibleService, new BibleSearchSummaryBuilder());
            _bibleSearchResultPresenter = new BibleSearchResultPresenter(
                BibleSearchPopup,
                BibleSearchFilterTagsPanel,
                BibleSearchResultsList,
                BibleSearchPrevPageButton,
                BibleSearchPageInfoText,
                BibleSearchNextPageButton,
                BibleSearchResultStatus,
                BibleEmbeddedSearchResultsContainer,
                BibleEmbeddedSearchFilterTagsPanel,
                BibleEmbeddedSearchResultsList,
                BibleEmbeddedSearchPrevPageButton,
                BibleEmbeddedSearchPageInfoText,
                BibleEmbeddedSearchNextPageButton,
                BibleEmbeddedSearchResultStatus,
                SearchBox,
                _bibleSearchDisplayMode,
                _configManager.BibleSearchFloatingFontSize,
                _configManager.BibleSearchEmbeddedFontSize);
            _bibleSearchResultPresenter.HitSelected += OnBibleSearchHitSelected;
            _bibleSearchResultPresenter.Dismissed += OnBibleSearchPresenterDismissed;
            _bibleHistorySlotWriter = new BibleHistorySlotWriter();
            PreviewMouseDown += MainWindow_PreviewMouseDownForBibleSearchDismiss;
            _bibleSearchComponentsInitialized = true;
            UpdateBibleSearchModeToggleState();
        }

        private void ApplyBibleSearchResultFontSizes()
        {
            _bibleSearchResultPresenter?.SetResultFontSizes(
                _configManager.BibleSearchFloatingFontSize,
                _configManager.BibleSearchEmbeddedFontSize);
        }

        private async Task HandleBibleSearchInputChangedAsync(string searchTerm)
        {
            EnsureBibleSearchComponentsInitialized();
            if (!_bibleSearchComponentsInitialized)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(searchTerm))
            {
                _bibleSearchCoordinator?.CancelPendingSearch();
                _bibleSearchResultPresenter?.Hide();
                return;
            }

            try
            {
                var hits = await _bibleSearchCoordinator.SearchAsync(searchTerm, BibleSearchDebounceMilliseconds);
                if (hits == null || !_isBibleMode)
                {
                    return;
                }

                _bibleSearchResultPresenter.DisplayMode = GetEffectiveBibleSearchDisplayMode();
                _bibleSearchResultPresenter.ShowResults(hits);
            }
            catch (Exception)
            {
                _bibleSearchResultPresenter?.ShowStatus("搜索失败，请重试");
            }
        }

        private void OnBibleSearchHitSelected(object sender, BibleSearchHit hit)
        {
            if (hit == null || _historySlots == null || _bibleHistorySlotWriter == null)
            {
                return;
            }

            var writeResult = _bibleHistorySlotWriter.TryAddHit(_historySlots, hit);
            if (writeResult.Status == BibleHistorySlotWriteStatus.Added)
            {
                BibleHistoryList?.Items.Refresh();
            }

            if (!string.IsNullOrWhiteSpace(writeResult.Message))
            {
                _bibleSearchResultPresenter?.ShowStatus(writeResult.Message);
                ShowStatus(writeResult.Message);
            }
        }

        private void HideBibleSearchResults()
        {
            _bibleSearchCoordinator?.CancelPendingSearch();
            _bibleSearchResultPresenter?.Hide();
        }

        private void OnBibleSearchPresenterDismissed(object sender, EventArgs e)
        {
            if (_isDisposingBibleSearchComponents || !_isBibleMode || SearchBox == null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(SearchBox.Text))
            {
                SearchBox.Clear();
            }
        }

        private void UpdateSearchEntryModeVisual()
        {
            if (SearchBox == null)
            {
                return;
            }

            if (_isBibleMode)
            {
                HintAssist.SetHint(SearchBox, "搜索经文关键词...");
                if (SearchScope != null)
                {
                    SearchScope.Visibility = Visibility.Collapsed;
                }
                if (BibleSearchModePanel != null)
                {
                    BibleSearchModePanel.Visibility = Visibility.Visible;
                }
                if (IncludeMediaSearchModePanel != null)
                {
                    IncludeMediaSearchModePanel.Visibility = Visibility.Collapsed;
                }

                EnsureBibleSearchComponentsInitialized();
                UpdateBibleSearchModeToggleState();
            }
            else
            {
                HintAssist.SetHint(SearchBox, "搜索（双击左键清除内容）");
                if (SearchScope != null)
                {
                    SearchScope.Visibility = Visibility.Visible;
                }
                if (BibleSearchModePanel != null)
                {
                    BibleSearchModePanel.Visibility = Visibility.Collapsed;
                }
                if (IncludeMediaSearchModePanel != null)
                {
                    IncludeMediaSearchModePanel.Visibility = Visibility.Visible;
                }
                UpdateIncludeMediaSearchToggleVisual();

                HideBibleSearchResults();
            }
        }

        private void SearchBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (!_isBibleMode || _bibleSearchResultPresenter == null)
            {
                return;
            }

            if (_bibleSearchResultPresenter.HandleSearchBoxKeyDown(e.Key))
            {
                e.Handled = true;
            }
        }

        private void MainWindow_PreviewMouseDownForBibleSearchDismiss(object sender, MouseButtonEventArgs e)
        {
            if (!_isBibleMode || _bibleSearchResultPresenter == null || e.OriginalSource is not DependencyObject source)
            {
                return;
            }

            _bibleSearchResultPresenter.HandleWindowPreviewMouseDown(source);
        }

        private void BibleSearchModeFloatingText_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            ApplyBibleSearchResultDisplayMode(BibleSearchResultDisplayMode.Floating, persist: true);
        }

        private void BibleSearchModeEmbeddedText_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            ApplyBibleSearchResultDisplayMode(BibleSearchResultDisplayMode.Embedded, persist: true);
        }

        private void EnsureBibleSearchDisplayModeLoaded()
        {
            if (_bibleSearchDisplayModeLoaded)
            {
                return;
            }

            _bibleSearchDisplayModeLoaded = true;
            try
            {
                var savedValue = DatabaseManagerService.GetUISetting(
                    BibleSearchResultDisplayModeSettingKey,
                    BibleSearchResultDisplayMode.Embedded.ToString());

                if (!Enum.TryParse(savedValue, ignoreCase: true, out BibleSearchResultDisplayMode parsedMode))
                {
                    parsedMode = BibleSearchResultDisplayMode.Embedded;
                }

                _bibleSearchDisplayMode = parsedMode;
            }
            catch
            {
                _bibleSearchDisplayMode = BibleSearchResultDisplayMode.Embedded;
            }
        }

        private void ApplyBibleSearchResultDisplayMode(BibleSearchResultDisplayMode mode, bool persist)
        {
            EnsureBibleSearchDisplayModeLoaded();
            if (_bibleSearchDisplayMode == mode)
            {
                UpdateBibleSearchModeToggleState();
                return;
            }

            _bibleSearchDisplayMode = mode;
            if (persist)
            {
                try
                {
                    DatabaseManagerService.SaveUISetting(BibleSearchResultDisplayModeSettingKey, mode.ToString());
                }
                catch
                {
                }
            }

            if (_bibleSearchResultPresenter != null)
            {
                _bibleSearchResultPresenter.DisplayMode = GetEffectiveBibleSearchDisplayMode();
                _bibleSearchResultPresenter.Hide();
            }

            UpdateBibleSearchModeToggleState();

            if (_isBibleMode && !string.IsNullOrWhiteSpace(SearchBox?.Text))
            {
                _ = HandleBibleSearchInputChangedAsync(SearchBox.Text.Trim());
            }
        }

        private void UpdateBibleSearchModeToggleState()
        {
            if (BibleSearchModeFloatingText == null || BibleSearchModeEmbeddedText == null)
            {
                return;
            }

            bool floatingActive = _bibleSearchDisplayMode == BibleSearchResultDisplayMode.Floating;
            ApplySearchModeTextStyle(BibleSearchModeFloatingText, floatingActive);
            ApplySearchModeTextStyle(BibleSearchModeEmbeddedText, !floatingActive);
        }

        private static void ApplySearchModeTextStyle(System.Windows.Controls.TextBlock textBlock, bool isActive)
        {
            if (textBlock == null)
            {
                return;
            }

            textBlock.Foreground = isActive
                ? new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#5E35B1"))
                : new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#757575"));
            textBlock.FontWeight = isActive ? FontWeights.Bold : FontWeights.Normal;
            textBlock.TextDecorations = isActive ? TextDecorations.Underline : null;
        }

        private void DisposeBibleSearchComponents()
        {
            _isDisposingBibleSearchComponents = true;
            PreviewMouseDown -= MainWindow_PreviewMouseDownForBibleSearchDismiss;

            if (_bibleSearchResultPresenter != null)
            {
                _bibleSearchResultPresenter.HitSelected -= OnBibleSearchHitSelected;
                _bibleSearchResultPresenter.Dismissed -= OnBibleSearchPresenterDismissed;
                _bibleSearchResultPresenter.Dispose();
                _bibleSearchResultPresenter = null;
            }

            _bibleSearchCoordinator?.Dispose();
            _bibleSearchCoordinator = null;
            _bibleHistorySlotWriter = null;
            _bibleSearchComponentsInitialized = false;
            _isDisposingBibleSearchComponents = false;
        }
    }
}
