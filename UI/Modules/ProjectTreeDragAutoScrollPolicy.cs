using System;

namespace ImageColorChanger.UI.Modules
{
    /// <summary>
    /// 计算项目树拖拽时的边缘自动滚动步进。
    /// </summary>
    public static class ProjectTreeDragAutoScrollPolicy
    {
        public static double ComputeVerticalScrollDelta(
            double cursorY,
            double viewportHeight,
            double edgeThreshold = 32,
            double maxStep = 24)
        {
            if (viewportHeight <= 0 || maxStep <= 0)
            {
                return 0;
            }

            double threshold = Math.Max(4, Math.Min(edgeThreshold, viewportHeight / 2));
            if (threshold <= 0)
            {
                return 0;
            }

            if (cursorY < threshold)
            {
                double ratio = Math.Clamp((threshold - cursorY) / threshold, 0, 1);
                return -Math.Max(1, ratio * maxStep);
            }

            double bottomStart = viewportHeight - threshold;
            if (cursorY > bottomStart)
            {
                double ratio = Math.Clamp((cursorY - bottomStart) / threshold, 0, 1);
                return Math.Max(1, ratio * maxStep);
            }

            return 0;
        }
    }
}
