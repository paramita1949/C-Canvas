namespace ImageColorChanger.Services.Lyrics.Output
{
    public sealed class LyricsNdiOutputOptions
    {
        public string SenderName { get; init; } = "CanvasCast-Lyrics";
        public int Width { get; init; } = 1920;
        public int Height { get; init; } = 1080;
        public int Fps { get; init; } = 30;
        public bool PreferAlpha { get; init; } = true;
    }
}
