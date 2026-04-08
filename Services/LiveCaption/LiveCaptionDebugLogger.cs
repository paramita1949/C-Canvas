using System.Diagnostics;

namespace ImageColorChanger.Services.LiveCaption
{
    internal static class LiveCaptionDebugLogger
    {
        private static readonly string[] ProjectionDebugTags =
        {
            "[CaptionLayout:Vertical]",
            "[VerticalCapacity]",
            "[VerticalPaging]",
            "[CaptionShiftProbe:Vertical]",
            "[CaptionShiftProbe:Projection]"
        };

        public static void Log(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            bool isProjectionCaptionDebug = false;
            foreach (string tag in ProjectionDebugTags)
            {
                if (message.IndexOf(tag, System.StringComparison.Ordinal) >= 0)
                {
                    isProjectionCaptionDebug = true;
                    break;
                }
            }

            if (!isProjectionCaptionDebug)
            {
                return;
            }

            Debug.WriteLine(message);
        }
    }
}
