namespace ImageColorChanger.Services.LiveCaption
{
    internal enum LiveCaptionEntryAction
    {
        OpenPanel,
        StartEngine
    }

    internal static class LiveCaptionEntryPolicy
    {
        internal static LiveCaptionEntryAction ResolveTopBarEntryAction(bool isEngineRunning)
        {
            return isEngineRunning
                ? LiveCaptionEntryAction.OpenPanel
                : LiveCaptionEntryAction.StartEngine;
        }
    }
}
