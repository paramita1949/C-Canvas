namespace ImageColorChanger.Services.TextEditor.Components.Notice
{
    public sealed class NoticeRuntimeStateSnapshot
    {
        public long StartTimestampMs { get; init; }
        public bool IsManuallyClosed { get; init; }
        public bool IsAutoPausedByTimeout { get; init; }
        public long PausedElapsedMs { get; init; }
        public bool HasLastOffset { get; init; }
        public double LastOffsetX { get; init; }
        public long ElapsedMs { get; init; }
    }
}
