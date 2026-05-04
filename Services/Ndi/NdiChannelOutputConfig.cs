namespace ImageColorChanger.Services.Ndi
{
    public sealed class NdiChannelOutputConfig
    {
        public NdiChannel Channel { get; init; }
        public bool Enabled { get; init; }
        public string SenderName { get; init; } = string.Empty;
        public int Width { get; init; }
        public int Height { get; init; }
        public int Fps { get; init; }
        public bool PreferAlpha { get; init; }
    }
}

