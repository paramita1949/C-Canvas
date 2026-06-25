using System.Collections.Generic;

namespace ImageColorChanger.UI.Modules
{
    public static class BibleSettingsFontSizeOptions
    {
        public const int DefaultValue = 46;

        public static IReadOnlyList<int> Values { get; } = new[]
        {
            30, 32, 34, 36, 38,
            40, 42, 44, 46, 48,
            50, 52, 54, 56, 58,
            60, 64, 68, 72, 76,
            80, 88, 96, 104, 112,
            120, 128, 136, 144, 152,
            160
        };
    }
}
