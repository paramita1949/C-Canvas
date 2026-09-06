using System;
using System.Collections.Generic;

namespace ImageColorChanger.Core
{
    public sealed class ImageBackgroundColorPreset
    {
        public string Name { get; init; }
        public string Hex { get; init; }
    }

    public static class ImageBackgroundColorCatalog
    {
        public const string DefaultColor = "#000000";

        public static IReadOnlyList<ImageBackgroundColorPreset> Presets { get; } =
            new List<ImageBackgroundColorPreset>
            {
                new ImageBackgroundColorPreset { Name = "黑色", Hex = "#000000" },
                new ImageBackgroundColorPreset { Name = "白色", Hex = "#FFFFFF" },
                new ImageBackgroundColorPreset { Name = "深灰", Hex = "#202020" },
                new ImageBackgroundColorPreset { Name = "深蓝", Hex = "#102A43" },
                new ImageBackgroundColorPreset { Name = "深绿", Hex = "#0B3D2E" },
                new ImageBackgroundColorPreset { Name = "深棕", Hex = "#3B2F2F" }
            };

        public static string Normalize(string colorHex)
        {
            if (string.IsNullOrWhiteSpace(colorHex))
            {
                return DefaultColor;
            }

            string value = colorHex.Trim();
            if (value.Length != 7 || value[0] != '#')
            {
                return DefaultColor;
            }

            for (int i = 1; i < value.Length; i++)
            {
                if (!Uri.IsHexDigit(value[i]))
                {
                    return DefaultColor;
                }
            }

            return value.ToUpperInvariant();
        }

        public static string FindPresetName(string colorHex)
        {
            string normalized = Normalize(colorHex);
            foreach (var preset in Presets)
            {
                if (preset.Hex == normalized)
                {
                    return preset.Name;
                }
            }

            return null;
        }
    }
}
