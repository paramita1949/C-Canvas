namespace ImageColorChanger.Managers
{
    /// <summary>
    /// 投影滚动同步策略，统一滚动偏移计算逻辑。
    /// </summary>
    public static class ProjectionScrollPolicy
    {
        public static double CalculateByExtentRatio(double mainScrollTop, double mainExtentHeight, double projectionExtentHeight)
        {
            if (mainExtentHeight <= 0 || projectionExtentHeight <= 0)
            {
                return 0;
            }

            double scrollRatio = mainScrollTop / mainExtentHeight;
            return scrollRatio * projectionExtentHeight;
        }

        public static double CalculateByImageHeights(double mainScrollTop, double mainImageHeight, double projectionImageHeight)
        {
            if (mainImageHeight <= 0 || projectionImageHeight <= 0)
            {
                return 0;
            }

            double relativePos = mainScrollTop / mainImageHeight;
            return relativePos * projectionImageHeight;
        }

        public static double CalculateByScrollableHeightRatio(double scrollRatio, double projectionScrollableHeight)
        {
            return scrollRatio * projectionScrollableHeight;
        }
    }
}
