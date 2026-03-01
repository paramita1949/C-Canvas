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
    }
}

