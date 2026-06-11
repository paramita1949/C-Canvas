using System;
using System.Windows;
using System.Windows.Media;
using SkiaSharp;
using WpfFontFamily = System.Windows.Media.FontFamily;

namespace ImageColorChanger.Core
{
    /// <summary>
    /// 圣经弹窗字体解析器，统一 WPF 与 Skia 的自定义字体加载路径。
    /// </summary>
    public static class BiblePopupFontResolver
    {
        public const string DefaultPopupFontFamily = "Microsoft YaHei UI";
        private const int MicrosoftYaHeiLightWeight = 290;

        public static string ResolveFamilyName(BibleTextInsertConfig config)
        {
            return NormalizeFamilyName(ResolveRawFamilyName(config));
        }

        public static FontWeight ResolveWpfFontWeight(BibleTextInsertConfig config, bool isBold)
        {
            if (isBold)
            {
                return FontWeights.Bold;
            }

            return IsMicrosoftYaHeiLightFamilyName(ResolveRawFamilyName(config))
                ? FontWeight.FromOpenTypeWeight(MicrosoftYaHeiLightWeight)
                : FontWeights.Normal;
        }

        public static SKFontStyle ResolveSkiaFontStyle(
            BibleTextInsertConfig config,
            bool isBold = false,
            bool isItalic = false)
        {
            var slant = isItalic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright;
            int weight = isBold
                ? (int)SKFontStyleWeight.Bold
                : IsMicrosoftYaHeiLightFamilyName(ResolveRawFamilyName(config))
                    ? MicrosoftYaHeiLightWeight
                    : (int)SKFontStyleWeight.Normal;

            return new SKFontStyle(weight, (int)SKFontStyleWidth.Normal, slant);
        }

        private static string ResolveRawFamilyName(BibleTextInsertConfig config)
        {
            string popupFamily = config?.PopupFontFamily?.Trim();
            if (!string.IsNullOrWhiteSpace(popupFamily))
            {
                return popupFamily;
            }

            string insertFamily = config?.FontFamily?.Trim();
            if (!string.IsNullOrWhiteSpace(insertFamily))
            {
                return insertFamily;
            }

            return DefaultPopupFontFamily;
        }

        public static WpfFontFamily ResolveWpfFontFamily(
            BibleTextInsertConfig config,
            Func<string, WpfFontFamily> loadFontFamily = null)
        {
            string familyName = ResolveFamilyName(config);
            WpfFontFamily fontFamily = null;

            try
            {
                fontFamily = (loadFontFamily ?? ResolveWpfFontFamilyFromApp)(familyName);
            }
            catch
            {
                fontFamily = null;
            }

            return fontFamily ?? new WpfFontFamily(DefaultPopupFontFamily);
        }

        public static SKTypeface ResolveSkiaTypeface(
            BibleTextInsertConfig config,
            bool isBold = false,
            bool isItalic = false,
            Func<string, bool, bool, SKTypeface> loadTypeface = null)
        {
            string familyName = ResolveFamilyName(config);
            bool usesMicrosoftYaHeiLight = IsMicrosoftYaHeiLightFamilyName(ResolveRawFamilyName(config));
            SKFontStyle fontStyle = ResolveSkiaFontStyle(config, isBold, isItalic);
            SKTypeface typeface = null;

            try
            {
                typeface = usesMicrosoftYaHeiLight && loadTypeface == null
                    ? SKTypeface.FromFamilyName(familyName, fontStyle)
                    : (loadTypeface ?? SkiaFontService.Instance.GetTypeface)(familyName, isBold, isItalic);
            }
            catch
            {
                typeface = null;
            }

            return typeface
                ?? SKTypeface.FromFamilyName(familyName, fontStyle)
                ?? SKTypeface.FromFamilyName(DefaultPopupFontFamily, fontStyle)
                ?? SKTypeface.Default;
        }

        public static SKFont CreateSkiaFont(
            BibleTextInsertConfig config,
            float size,
            bool isBold = false,
            bool isItalic = false,
            Func<string, bool, bool, SKTypeface> loadTypeface = null)
        {
            var font = new SKFont
            {
                Typeface = ResolveSkiaTypeface(config, isBold, isItalic, loadTypeface),
                Size = size,
                Subpixel = true,
                Edging = SKFontEdging.Antialias
            };

            if (isBold)
            {
                font.Embolden = true;
            }

            return font;
        }

        private static WpfFontFamily ResolveWpfFontFamilyFromApp(string familyName)
        {
            return FontService.Instance.GetFontFamilyByFamily(familyName)
                ?? new WpfFontFamily(familyName);
        }

        private static string NormalizeFamilyName(string familyName)
        {
            return IsMicrosoftYaHeiLightFamilyName(familyName)
                ? DefaultPopupFontFamily
                : familyName;
        }

        public static bool IsMicrosoftYaHeiLightFamilyName(string familyName)
        {
            if (string.IsNullOrWhiteSpace(familyName))
            {
                return false;
            }

            string normalized = familyName.Trim();
            return normalized.Equals("Microsoft YaHei Light", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("Microsoft YaHei UI Light", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("微软雅黑 Light", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("微软雅黑Light", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("Microsoft YaHei Light & Microsoft YaHei UI Light (TrueType)", StringComparison.OrdinalIgnoreCase);
        }

    }
}
