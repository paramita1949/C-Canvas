using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ImageColorChanger.UI.Controls;

namespace ImageColorChanger.UI
{
    public partial class MainWindow
    {
        private bool _isBibleSectionEventsWired;

        private Grid BibleDisplayContainer => BibleSectionView?.BibleDisplayContainer;
        private ScrollViewer BibleVerseScrollViewer => BibleSectionView?.BibleVerseScrollViewer;
        private Border BibleChapterTitleBorder => BibleSectionView?.BibleChapterTitleBorder;
        private TextBlock BibleChapterTitle => BibleSectionView?.BibleChapterTitle;
        private Border BibleChapterTitleScrollBorder => BibleSectionView?.BibleChapterTitleScrollBorder;
        private TextBlock BibleChapterTitleScrollText => BibleSectionView?.BibleChapterTitleScrollText;
        private ItemsControl BibleVerseList => BibleSectionView?.BibleVerseList;
        private Border BibleBottomExtension => BibleSectionView?.BibleBottomExtension;
        private BiblePinyinHintControl BiblePinyinHintControl => BibleSectionView?.BiblePinyinHintControl;
        private Border BibleVersionTriggerArea => BibleSectionView?.BibleVersionTriggerArea;
        private Border BibleVersionToolbar => BibleSectionView?.BibleVersionToolbar;
        private Border BibleEmbeddedSearchResultsContainer => BibleSectionView?.BibleEmbeddedSearchResultsContainer;
        private System.Windows.Controls.Panel BibleEmbeddedSearchFilterTagsPanel => BibleSectionView?.BibleEmbeddedSearchFilterTagsPanel;
        private System.Windows.Controls.ListBox BibleEmbeddedSearchResultsList => BibleSectionView?.BibleEmbeddedSearchResultsList;
        private System.Windows.Controls.Button BibleEmbeddedSearchPrevPageButton => BibleSectionView?.BibleEmbeddedSearchPrevPageButton;
        private TextBlock BibleEmbeddedSearchPageInfoText => BibleSectionView?.BibleEmbeddedSearchPageInfoText;
        private System.Windows.Controls.Button BibleEmbeddedSearchNextPageButton => BibleSectionView?.BibleEmbeddedSearchNextPageButton;
        private TextBlock BibleEmbeddedSearchResultStatus => BibleSectionView?.BibleEmbeddedSearchResultStatus;
        private System.Windows.Controls.RadioButton RadioBibleVersionSimplified => BibleSectionView?.RadioBibleVersionSimplified;
        private System.Windows.Controls.RadioButton RadioBibleVersionTraditional => BibleSectionView?.RadioBibleVersionTraditional;
        private MenuItem MenuBibleCopyStyleShort => BibleSectionView?.MenuBibleCopyStyleShort;
        private MenuItem MenuBibleCopyStyleFull => BibleSectionView?.MenuBibleCopyStyleFull;
        private MenuItem MenuBibleCopyStyleChapter => BibleSectionView?.MenuBibleCopyStyleChapter;
        private MenuItem MenuBibleVerseScroll => BibleSectionView?.MenuBibleVerseScroll;
        private MenuItem MenuBibleVerseScrollLegacy => BibleSectionView?.MenuBibleVerseScrollLegacy;
        private MenuItem MenuBibleVerseScrollSpeedSlow => BibleSectionView?.MenuBibleVerseScrollSpeedSlow;
        private MenuItem MenuBibleVerseScrollSpeedMedium => BibleSectionView?.MenuBibleVerseScrollSpeedMedium;
        private MenuItem MenuBibleVerseScrollSpeedFast => BibleSectionView?.MenuBibleVerseScrollSpeedFast;
        private MenuItem MenuBibleVerseScrollTest => BibleSectionView?.MenuBibleVerseScrollTest;
        private MenuItem MenuBibleVerseScrollTestSpeed => MenuBibleVerseScrollTest;
        private MenuItem MenuBibleClearScreen => BibleSectionView?.MenuBibleClearScreen;

        private void InitializeBibleSectionBindings()
        {
            if (_isBibleSectionEventsWired || BibleSectionView == null)
            {
                return;
            }

            if (BibleVerseScrollViewer != null)
            {
                BibleVerseScrollViewer.PreviewKeyDown += BibleVerseScrollViewer_PreviewKeyDown;
                BibleVerseScrollViewer.PreviewMouseDown += BibleVerseScrollViewer_PreviewMouseDown;
                BibleVerseScrollViewer.PreviewMouseWheel += BibleVerseScrollViewer_PreviewMouseWheel;
                BibleVerseScrollViewer.ScrollChanged += BibleVerseContentScroller_ScrollChanged;
                BibleVerseScrollViewer.Unloaded += (s, e) => StopBibleVerseScrollAnimation();
            }

            if (BibleVerseList != null)
            {
                BibleVerseList.AddHandler(Border.MouseLeftButtonDownEvent, new MouseButtonEventHandler(BibleVerse_Click), true);
            }

            if (BibleVersionTriggerArea != null)
            {
                BibleVersionTriggerArea.MouseEnter += BibleVersionTrigger_MouseEnter;
                BibleVersionTriggerArea.MouseLeave += BibleVersionTrigger_MouseLeave;
            }

            if (BibleVersionToolbar != null)
            {
                BibleVersionToolbar.MouseEnter += BibleVersionTrigger_MouseEnter;
                BibleVersionToolbar.MouseLeave += BibleVersionTrigger_MouseLeave;
            }

            if (RadioBibleVersionSimplified != null)
            {
                RadioBibleVersionSimplified.Click += BibleVersionRadio_Click;
            }

            if (RadioBibleVersionTraditional != null)
            {
                RadioBibleVersionTraditional.Click += BibleVersionRadio_Click;
            }

            if (BibleSectionView.MenuBibleCopyVerses != null)
            {
                BibleSectionView.MenuBibleCopyVerses.Click += CopyBibleVerses_Click;
            }

            if (MenuBibleClearScreen != null)
            {
                MenuBibleClearScreen.Click += ClearBibleVerses_Click;
            }

            if (MenuBibleCopyStyleShort != null)
            {
                MenuBibleCopyStyleShort.Click += SetBibleCopyStyle_Click;
            }

            if (MenuBibleCopyStyleFull != null)
            {
                MenuBibleCopyStyleFull.Click += SetBibleCopyStyle_Click;
            }

            if (MenuBibleCopyStyleChapter != null)
            {
                MenuBibleCopyStyleChapter.Click += SetBibleCopyStyle_Click;
            }

            if (MenuBibleVerseScrollSpeedSlow != null)
            {
                MenuBibleVerseScrollSpeedSlow.Click += BibleVerseScrollSpeed_Click;
            }

            if (MenuBibleVerseScrollSpeedMedium != null)
            {
                MenuBibleVerseScrollSpeedMedium.Click += BibleVerseScrollSpeed_Click;
            }

            if (MenuBibleVerseScrollSpeedFast != null)
            {
                MenuBibleVerseScrollSpeedFast.Click += BibleVerseScrollSpeed_Click;
            }

            if (MenuBibleVerseScrollTestSpeed != null)
            {
                foreach (object child in MenuBibleVerseScrollTestSpeed.Items)
                    if (child is MenuItem item) item.Click += BibleVerseScrollTestSpeed_Click;
            }

            RefreshBibleVerseScrollMenuState();
            _isBibleSectionEventsWired = true;
        }

        private void RefreshBibleVerseScrollMenuState()
        {
            UpdateBibleVerseScrollSpeedMenu();
        }

        private void UpdateBibleVerseScrollSpeedMenu()
        {
            ImageColorChanger.UI.Modules.BibleVerseScrollMenuPolicy.Refresh(
                _configManager, MenuBibleVerseScrollSpeedSlow,
                MenuBibleVerseScrollSpeedMedium, MenuBibleVerseScrollSpeedFast, MenuBibleVerseScrollTest);
            ImageColorChanger.UI.Modules.BibleVerseScrollMenuPolicy.RefreshTestSpeed(
                _configManager, MenuBibleVerseScrollTestSpeed);
            ImageColorChanger.UI.Modules.BibleVerseScrollMenuPolicy.RefreshLegacy(
                _configManager, MenuBibleVerseScrollLegacy);
        }

        private void BibleVerseScrollSpeed_Click(object sender, RoutedEventArgs e)
        {
            if (_configManager != null && sender is MenuItem menuItem && int.TryParse(menuItem.Tag as string, out int speed))
            {
                StopBibleVerseScrollAnimation();
                ImageColorChanger.UI.Modules.BibleVerseScrollMenuPolicy.SelectLegacy(_configManager, speed);
                RefreshBibleVerseScrollMenuState();
            }
        }
        private void BibleVerseScrollTestSpeed_Click(object sender, RoutedEventArgs e)
        {
            if (_configManager != null && sender is MenuItem item && int.TryParse(item.Tag as string, out int level))
            {
                StopLegacyBibleVerseScrollAnimation();
                ImageColorChanger.UI.Modules.BibleVerseScrollMenuPolicy.SelectTestSpeed(_configManager, level);
                _bibleSpringScroller?.SetSpeedLevel(_configManager.BibleVerseSpringSpeedLevel);
                RefreshBibleVerseScrollMenuState();
            }
        }
    }
}
