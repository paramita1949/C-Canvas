using System;

namespace ImageColorChanger.UI.Modules
{
    /// <summary>
    /// 判断项目树排序拖拽是否可以启动，避免左键选中时轻微移动就误排序。
    /// </summary>
    public static class ProjectTreeDragStartPolicy
    {
        public const long DefaultDelayMilliseconds = 300;

        public static bool ShouldStartDrag(
            long elapsedMilliseconds,
            double horizontalDistance,
            double verticalDistance,
            double minimumHorizontalDistance,
            double minimumVerticalDistance,
            long delayMilliseconds = DefaultDelayMilliseconds)
        {
            if (elapsedMilliseconds < delayMilliseconds)
            {
                return false;
            }

            return Math.Abs(horizontalDistance) > minimumHorizontalDistance ||
                   Math.Abs(verticalDistance) > minimumVerticalDistance;
        }
    }
}
