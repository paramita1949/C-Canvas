using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ImageColorChanger.UI.Modules
{
    public sealed class BibleSearchHit
    {
        public int Book { get; init; }
        public int Chapter { get; init; }
        public int Verse { get; init; }
        public string Reference { get; init; }
        public string Snippet { get; init; }
        public string SnippetPrefix { get; init; }
        public string SnippetMatch { get; init; }
        public string SnippetSuffix { get; init; }
    }

    public enum BibleHistorySlotWriteStatus
    {
        Added,
        Duplicate,
        Full,
        Invalid
    }

    public sealed class BibleHistorySlotWriteResult
    {
        public BibleHistorySlotWriteStatus Status { get; init; }
        public int SlotIndex { get; init; }
        public string Message { get; init; }
    }

    public interface IBibleSearchSummaryBuilder
    {
        string BuildSnippet(string scripture, IReadOnlyList<string> keywords, int maxLength = 48, int contextBefore = 12);
    }

    public interface IBibleSearchCoordinator : IDisposable
    {
        Task<IReadOnlyList<BibleSearchHit>> SearchAsync(string query, int debounceMs = 300);
        void CancelPendingSearch();
    }

    public interface IBibleSearchResultPresenter : IDisposable
    {
        event EventHandler<BibleSearchHit> HitSelected;

        bool IsOpen { get; }
        void ShowResults(IReadOnlyList<BibleSearchHit> hits);
        void ShowStatus(string message);
        void Hide();
        bool HandleSearchBoxKeyDown(System.Windows.Input.Key key);
    }

    public interface IBibleHistorySlotWriter
    {
        BibleHistorySlotWriteResult TryAddHit(
            IList<ImageColorChanger.UI.MainWindow.BibleHistoryItem> slots,
            BibleSearchHit hit);
    }
}
