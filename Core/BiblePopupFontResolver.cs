using System;
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

        public static string ResolveFamilyName(BibleTextInsertConfig config)
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
            SKTypeface typeface = null;

            try
            {
                typeface = (loadTypeface ?? SkiaFontService.Instance.GetTypeface)(familyName, isBold, isItalic);
            }
            catch
            {
                typeface = null;
            }

            return typeface
                ?? SKTypeface.FromFamilyName(DefaultPopupFontFamily, GetFontStyle(isBold, isItalic))
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

        private static SKFontStyle GetFontStyle(bool isBold, bool isItalic)
        {
            if (isBold && isItalic)
            {
                return SKFontStyle.BoldItalic;
            }

            if (isBold)
            {
                return SKFontStyle.Bold;
            }

            return isItalic ? SKFontStyle.Italic : SKFontStyle.Normal;
        }
    }
}
