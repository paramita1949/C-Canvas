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
        private ItemsControl BibleVerseList => BibleSectionView?.BibleVerseList;
        private Border BibleBottomExtension => BibleSectionView?.BibleBottomExtension;
        private BiblePinyinHintControl BiblePinyinHintControl => BibleSectionView?.BiblePinyinHintControl;
        private Border BibleVersionTriggerArea => BibleSectionView?.BibleVersionTriggerArea;
        private Border BibleVersionToolbar => BibleSectionView?.BibleVersionToolbar;
        private Border BibleEmbeddedSearchResultsContainer => BibleSectionView?.BibleEmbeddedSearchResultsContainer;
        private WrapPanel BibleEmbeddedSearchFilterTagsPanel => BibleSectionView?.BibleEmbeddedSearchFilterTagsPanel;
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

            _isBibleSectionEventsWired = true;
        }
    }
}
