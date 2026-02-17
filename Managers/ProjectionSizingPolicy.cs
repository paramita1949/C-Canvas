using System;
using ImageColorChanger.Core;

namespace ImageColorChanger.Managers
{
    /// <summary>
    /// 投影尺寸计算策略，隔离纯计算逻辑，避免 UI 管理器膨胀。
    /// </summary>
    public static class ProjectionSizingPolicy
    {
        public static (int width, int height) Calculate(
            int imageWidth,
            int imageHeight,
            double canvasWidth,
            double canvasHeight,
            bool isOriginalMode,
            OriginalDisplayMode originalDisplayMode,
            double zoomRatio)
        {
            if (isOriginalMode)
            {
                return CalculateOriginalModeSize(imageWidth, imageHeight, canvasWidth, canvasHeight, originalDisplayMode);
            }

            return CalculateNormalModeSize(imageWidth, imageHeight, canvasWidth, zoomRatio);
        }

        private static (int width, int height) CalculateOriginalModeSize(
            int imageWidth,
            int imageHeight,
            double canvasWidth,
            double canvasHeight,
            OriginalDisplayMode originalDisplayMode)
        {
            double widthRatio = canvasWidth / imageWidth;
            double heightRatio = canvasHeight / imageHeight;
            double scaleRatio = originalDisplayMode == OriginalDisplayMode.Stretch
                ? heightRatio
                : Math.Min(widthRatio, heightRatio);

            if (scaleRatio >= 1)
            {
                double screenArea = canvasWidth * canvasHeight;
                double imageArea = imageWidth * imageHeight;
                double areaRatio = screenArea / imageArea;

                double maxScale;
                if (areaRatio > 16) maxScale = 6.0;
                else if (areaRatio > 9) maxScale = 4.0;
                else if (areaRatio > 4) maxScale = 3.0;
                else maxScale = 2.0;

                scaleRatio = Math.Min(scaleRatio, maxScale);
            }

            if (originalDisplayMode == OriginalDisplayMode.Stretch)
            {
                int stretchWidth = (int)canvasWidth;
                int stretchHeight = (int)(imageHeight * scaleRatio);
                return (stretchWidth, stretchHeight);
            }

            int fitWidth = (int)(imageWidth * scaleRatio);
            int fitHeight = (int)(imageHeight * scaleRatio);
            return (fitWidth, fitHeight);
        }

        private static (int width, int height) CalculateNormalModeSize(
            int imageWidth,
            int imageHeight,
            double canvasWidth,
            double zoomRatio)
        {
            double baseRatio = canvasWidth / imageWidth;
            double finalRatio = baseRatio * zoomRatio;
            int width = (int)(imageWidth * finalRatio);
            int height = (int)(imageHeight * finalRatio);
            return (width, height);
        }
    }
}
