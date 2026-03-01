namespace ImageColorChanger.Services.Projection.Output
{
    public sealed class ProjectionNdiOutputOptions
    {
        public string SenderName { get; init; } = "CanvasCast-Projection";
        public int Width { get; init; } = 1920;
        public int Height { get; init; } = 1080;
        public int Fps { get; init; } = 30;
        public bool PreferAlpha { get; init; } = true;
    }
}

