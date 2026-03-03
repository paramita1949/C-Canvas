using System.Collections.Generic;

namespace ImageColorChanger.Services.TextEditor.Rendering
{
    public sealed class TextEditorProjectionCacheContext
    {
        public IReadOnlyDictionary<int, string> RegionImagePaths { get; init; }
        public IReadOnlyCollection<TextEditorProjectionTextState> TextStates { get; init; }
        public string SplitMode { get; init; }
        public string SplitDisplayMode { get; init; }
        public string BackgroundColor { get; init; }
        public string BackgroundImagePath { get; init; }
        public bool BackgroundGradientEnabled { get; init; }
        public string BackgroundGradientStartColor { get; init; }
        public string BackgroundGradientEndColor { get; init; }
        public int BackgroundGradientDirection { get; init; }
        public int BackgroundOpacity { get; init; }
        public bool BiblePopupOverlayVisible { get; init; }
        public string BiblePopupOverlayReference { get; init; }
        public string BiblePopupOverlayContent { get; init; }
        public string BiblePopupOverlayPosition { get; init; }
        public string BiblePopupOverlayBackgroundColor { get; init; }
        public int BiblePopupOverlayBackgroundOpacity { get; init; }
        public double BiblePopupOverlayScrollOffset { get; init; }
    }
}
