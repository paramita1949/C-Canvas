namespace ImageColorChanger.UI.Modules
{
    /// <summary>
    /// 圣经经文导航输入方向规范化。
    /// </summary>
    public static class BibleVerseNavigationInputPolicy
    {
        public static int NormalizeDirection(int direction)
        {
            if (direction > 0)
            {
                return 1;
            }

            if (direction < 0)
            {
                return -1;
            }

            return 0;
        }

        public static double AccumulateVerseHeight(double currentOffset, double measuredHeight, double estimatedHeight)
        {
            double fallback = estimatedHeight > 1d ? estimatedHeight : 110d;
            return currentOffset + (measuredHeight > 1d ? measuredHeight : fallback);
        }
    }
}
