namespace ImageColorChanger.Services.Projection.Output
{
    public interface IProjectionNdiConfigProvider
    {
        bool ProjectionNdiEnabled { get; }
        string ProjectionNdiSenderName { get; }
        int ProjectionNdiWidth { get; }
        int ProjectionNdiHeight { get; }
        int ProjectionNdiFps { get; }
        bool ProjectionNdiPreferAlpha { get; }
        bool ProjectionNdiLyricsTransparentEnabled { get; }
        bool ProjectionNdiBibleTransparentEnabled { get; }
        string ProjectionNdiIdleFrameWatermarkText { get; }
        string ProjectionNdiIdleFrameWatermarkPosition { get; }
        double ProjectionNdiIdleFrameWatermarkFontSize { get; }
        string ProjectionNdiIdleFrameWatermarkFontFamily { get; }
        double ProjectionNdiIdleFrameWatermarkOpacity { get; }
    }
}

