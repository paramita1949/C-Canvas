using System;

namespace ImageColorChanger.Core
{
    /// <summary>
    /// 弹窗透明度换算工具：0=不透明，100=完全透明。
    /// </summary>
    public static class BiblePopupOpacity
    {
        public static int NormalizeTransparencyPercent(int value)
        {
            return Math.Clamp(value, 0, 100);
        }

        public static byte ToAlphaFromTransparencyPercent(int transparencyPercent)
        {
            int t = NormalizeTransparencyPercent(transparencyPercent);
            return (byte)Math.Clamp((int)Math.Round((100 - t) * 2.55), 0, 255);
        }

        public static bool ShouldHideBorder(int transparencyPercent)
        {
            return NormalizeTransparencyPercent(transparencyPercent) >= 100;
        }
    }
}
