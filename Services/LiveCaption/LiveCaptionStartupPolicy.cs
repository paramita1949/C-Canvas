namespace ImageColorChanger.Services.LiveCaption
{
    internal static class LiveCaptionStartupPolicy
    {
        internal static bool ShouldAutoStartRecognition(bool realtimeEnabled, bool shortPhraseEnabled)
        {
            return realtimeEnabled || shortPhraseEnabled;
        }
    }
}
