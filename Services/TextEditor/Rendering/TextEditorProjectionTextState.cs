namespace ImageColorChanger.Services.TextEditor.Rendering
{
    public sealed class TextEditorProjectionTextState
    {
        public string Content { get; init; }
        public double X { get; init; }
        public double Y { get; init; }
        public double Width { get; init; }
        public double Height { get; init; }
        public double FontSize { get; init; }
        public string FontFamily { get; init; }
        public string FontColor { get; init; }
        public bool IsBold { get; init; }
        public bool IsItalic { get; init; }
        public bool IsUnderline { get; init; }
        public string TextAlign { get; init; }
        public string TextVerticalAlign { get; init; }
        public int ZIndex { get; init; }
        public string BorderColor { get; init; }
        public double BorderWidth { get; init; }
        public double BorderRadius { get; init; }
        public double BorderOpacity { get; init; }
        public string BackgroundColor { get; init; }
        public double BackgroundRadius { get; init; }
        public double BackgroundOpacity { get; init; }
        public double LineSpacing { get; init; }
        public double LetterSpacing { get; init; }
        public string ComponentType { get; init; }
        public string ComponentConfigJson { get; init; }
    }
}
