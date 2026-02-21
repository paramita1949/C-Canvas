namespace ImageColorChanger.UI
{
    /// <summary>
    /// Lyrics feature flags for fast rollback/gray release.
    /// </summary>
    public partial class MainWindow
    {
        // Hot rollback switches:
        // false => hide lyrics library tree and disable lyrics package transfer entries.
        private const bool EnableLyricsLibraryFeature = true;
        private const bool EnableLyricsTransferFeature = true;

        private bool IsLyricsLibraryFeatureEnabled => EnableLyricsLibraryFeature;
        private bool IsLyricsTransferFeatureEnabled => EnableLyricsLibraryFeature && EnableLyricsTransferFeature;
    }
}
