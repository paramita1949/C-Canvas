using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using ImageColorChanger.UI.Modules;
using MaterialDesignThemes.Wpf;

namespace ImageColorChanger.UI
{
    public partial class MainWindow
    {
        private const int BibleSearchDebounceMilliseconds = 300;

        private IBibleSearchCoordinator _bibleSearchCoordinator;
        private IBibleSearchResultPresenter _bibleSearchResultPresenter;
        private IBibleHistorySlotWriter _bibleHistorySlotWriter;
        private bool _bibleSearchComponentsInitialized;
        private bool _isDisposingBibleSearchComponents;

        private void EnsureBibleSearchComponentsInitialized()
        {
            if (_bibleSearchComponentsInitialized)
            {
                return;
            }

            if (_bibleService == null || SearchBox == null || BibleSearchPopup == null || BibleSearchResultsList == null || BibleSearchResultStatus == null)
            {
                return;
            }

            _bibleSearchCoordinator = new BibleSearchCoordinator(_bibleService, new BibleSearchSummaryBuilder());
            _bibleSearchResultPresenter = new BibleSearchResultPresenter(BibleSearchPopup, BibleSearchResultsList, BibleSearchResultStatus, SearchBox);
            _bibleSearchResultPresenter.HitSelected += OnBibleSearchHitSelected;
            _bibleHistorySlotWriter = new BibleHistorySlotWriter();
            BibleSearchPopup.Closed += BibleSearchPopup_Closed;
            _bibleSearchComponentsInitialized = true;
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

        private void BibleSearchPopup_Closed(object sender, EventArgs e)
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

                EnsureBibleSearchComponentsInitialized();
            }
            else
            {
                HintAssist.SetHint(SearchBox, "搜索...");
                if (SearchScope != null)
                {
                    SearchScope.Visibility = Visibility.Visible;
                }

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

        private void DisposeBibleSearchComponents()
        {
            _isDisposingBibleSearchComponents = true;

            if (_bibleSearchResultPresenter != null)
            {
                _bibleSearchResultPresenter.HitSelected -= OnBibleSearchHitSelected;
                _bibleSearchResultPresenter.Dispose();
                _bibleSearchResultPresenter = null;
            }

            if (BibleSearchPopup != null)
            {
                BibleSearchPopup.Closed -= BibleSearchPopup_Closed;
            }

            _bibleSearchCoordinator?.Dispose();
            _bibleSearchCoordinator = null;
            _bibleHistorySlotWriter = null;
            _bibleSearchComponentsInitialized = false;
            _isDisposingBibleSearchComponents = false;
        }
    }
}
