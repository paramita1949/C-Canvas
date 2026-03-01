namespace ImageColorChanger.Services.Lyrics.Output
{
    /// <summary>
    /// NDI 歌词输出配置读取接口，便于业务与配置系统解耦。
    /// </summary>
    public interface ILyricsNdiConfigProvider
    {
        bool LyricsNdiEnabled { get; }
        string LyricsNdiSenderName { get; }
        int LyricsNdiWidth { get; }
        int LyricsNdiHeight { get; }
        int LyricsNdiFps { get; }
        bool LyricsNdiPreferAlpha { get; }
    }
}
