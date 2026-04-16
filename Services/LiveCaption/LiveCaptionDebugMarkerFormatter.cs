namespace ImageColorChanger.Services.LiveCaption
{
    internal static class LiveCaptionDebugMarkerFormatter
    {
        internal static string Format(bool realtimeEnabled, bool shortPhraseEnabled)
        {
            if (realtimeEnabled && shortPhraseEnabled)
            {
                return "[实时语音]【实时短语】";
            }

            if (realtimeEnabled)
            {
                return "[实时语音]";
            }

            if (shortPhraseEnabled)
            {
                return "【实时短语】";
            }

            return string.Empty;
        }
    }
}
