namespace ImageColorChanger.Services.TextEditor.Components.Notice
{
    public sealed class NoticeRuntimeState
    {
        public long StartTimestampMs { get; set; }
        public bool IsManuallyClosed { get; set; }
        public bool IsAutoPausedByTimeout { get; set; }
        public long PausedElapsedMs { get; set; }
        public bool HasLastOffset { get; set; }
        public double LastOffsetX { get; set; }
    }
}
