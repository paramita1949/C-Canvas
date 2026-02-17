using System;

namespace ImageColorChanger.Managers
{
    /// <summary>
    /// 投影布局策略，集中管理图片定位与滚动容器高度规则。
    /// </summary>
    public static class ProjectionLayoutPolicy
    {
        public static System.Windows.Thickness CalculateImageMargin(
            int imageWidth,
            int imageHeight,
            double containerWidth,
            double containerHeight,
            bool isOriginalMode)
        {
            double x = Math.Max(0, (containerWidth - imageWidth) / 2.0);
            double y = Math.Max(0, (containerHeight - imageHeight) / 2.0);
            return isOriginalMode
                ? new System.Windows.Thickness(x, y, 0, 0)
                : new System.Windows.Thickness(x, 0, 0, 0);
        }

        public static double CalculateScrollContainerHeight(int imageHeight, double containerHeight, bool isOriginalMode)
        {
            if (isOriginalMode)
            {
                return imageHeight <= containerHeight ? containerHeight : imageHeight + containerHeight;
            }

            // 正常模式：即使等高也保留额外可滚动空间。
            return imageHeight >= containerHeight ? imageHeight + containerHeight : containerHeight;
        }
    }
}
