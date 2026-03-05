using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using WpfColor = System.Windows.Media.Color;

namespace ImageColorChanger.UI.Controls.Common
{
    internal readonly struct ColorBoardChipSelection
    {
        public bool IsGradient { get; init; }
        public string SolidColorHex { get; init; }
        public string GradientStartHex { get; init; }
        public string GradientEndHex { get; init; }
        public string ChipTag { get; init; }
    }

    internal static class SharedColorBoardBuilder
    {
        public static void BuildChips(
            System.Windows.Controls.Panel quickPanel,
            System.Windows.Controls.Panel palettePanel,
            IList<Border> chips,
            bool gradientMode,
            int opacity,
            string gradientDirection,
            Action<ColorBoardChipSelection> onChipSelected,
            double size = 21,
            double margin = 2)
        {
            if (quickPanel == null || palettePanel == null || chips == null)
            {
                return;
            }

            quickPanel.Children.Clear();
            palettePanel.Children.Clear();
            chips.Clear();

            if (gradientMode)
            {
                foreach (var preset in SharedColorModule.GradientQuickPresets)
                {
                    var chip = CreateGradientChip(preset.Start, preset.End, size, margin, opacity, gradientDirection, onChipSelected);
                    chips.Add(chip);
                    quickPanel.Children.Add(chip);
                }

                foreach (var preset in SharedColorModule.GradientPalettePresets)
                {
                    var chip = CreateGradientChip(preset.Start, preset.End, size, margin, opacity, gradientDirection, onChipSelected);
                    chips.Add(chip);
                    palettePanel.Children.Add(chip);
                }

                return;
            }

            foreach (var hex in SharedColorModule.SolidQuickColors)
            {
                var chip = CreateSolidChip(hex, size, margin, onChipSelected);
                chips.Add(chip);
                quickPanel.Children.Add(chip);
            }

            foreach (var hex in SharedColorModule.SolidPaletteColors)
            {
                var chip = CreateSolidChip(hex, size, margin, onChipSelected);
                chips.Add(chip);
                palettePanel.Children.Add(chip);
            }
        }

        public static void ApplyModeTabStyle(System.Windows.Controls.Button solidButton, System.Windows.Controls.Button gradientButton, bool isSolid)
        {
            if (solidButton == null || gradientButton == null)
            {
                return;
            }

            solidButton.Background = isSolid ? new SolidColorBrush(WpfColor.FromRgb(220, 232, 255)) : new SolidColorBrush(WpfColor.FromRgb(247, 247, 247));
            solidButton.BorderBrush = isSolid ? new SolidColorBrush(WpfColor.FromRgb(122, 163, 240)) : new SolidColorBrush(WpfColor.FromRgb(208, 208, 208));
            gradientButton.Background = isSolid ? new SolidColorBrush(WpfColor.FromRgb(247, 247, 247)) : new SolidColorBrush(WpfColor.FromRgb(220, 232, 255));
            gradientButton.BorderBrush = isSolid ? new SolidColorBrush(WpfColor.FromRgb(208, 208, 208)) : new SolidColorBrush(WpfColor.FromRgb(122, 163, 240));
        }

        public static string BuildGradientChipTag(string startHex, string endHex)
        {
            var start = SharedColorModule.ParseColor(startHex, Colors.Black);
            var end = SharedColorModule.ParseColor(endHex, Colors.Black);
            return $"G|#{start.R:X2}{start.G:X2}{start.B:X2}|#{end.R:X2}{end.G:X2}{end.B:X2}";
        }

        private static Border CreateSolidChip(
            string colorHex,
            double size,
            double margin,
            Action<ColorBoardChipSelection> onChipSelected)
        {
            var normalized = SharedColorModule.NormalizeColorHex(colorHex) ?? "#FFFFFF";
            var color = SharedColorModule.ParseColor(normalized, Colors.White);

            var chip = new Border
            {
                Width = size,
                Height = size,
                Margin = new Thickness(margin),
                CornerRadius = new CornerRadius(size / 2),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(WpfColor.FromRgb(198, 198, 198)),
                Background = new SolidColorBrush(color),
                Cursor = System.Windows.Input.Cursors.Hand,
                Tag = normalized
            };

            chip.MouseLeftButtonDown += (_, _) =>
            {
                onChipSelected?.Invoke(new ColorBoardChipSelection
                {
                    IsGradient = false,
                    SolidColorHex = normalized,
                    ChipTag = normalized
                });
            };

            return chip;
        }

        private static Border CreateGradientChip(
            string startHex,
            string endHex,
            double size,
            double margin,
            int opacity,
            string direction,
            Action<ColorBoardChipSelection> onChipSelected)
        {
            var startColor = SharedColorModule.ParseColor(startHex, Colors.Black);
            var endColor = SharedColorModule.ParseColor(endHex, Colors.Black);
            var startRgb = $"#{startColor.R:X2}{startColor.G:X2}{startColor.B:X2}";
            var endRgb = $"#{endColor.R:X2}{endColor.G:X2}{endColor.B:X2}";
            var tag = BuildGradientChipTag(startRgb, endRgb);

            var gradientSpec = SharedColorModule.BuildGradientSpec(
                SharedColorModule.ApplyOpacityToHex(startRgb, opacity),
                SharedColorModule.ApplyOpacityToHex(endRgb, opacity),
                direction);
            SharedColorModule.TryCreateBrush(gradientSpec, out var brush);

            var chip = new Border
            {
                Width = size,
                Height = size,
                Margin = new Thickness(margin),
                CornerRadius = new CornerRadius(size / 2),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(WpfColor.FromRgb(198, 198, 198)),
                Background = brush ?? System.Windows.Media.Brushes.Transparent,
                Cursor = System.Windows.Input.Cursors.Hand,
                Tag = tag
            };

            chip.MouseLeftButtonDown += (_, _) =>
            {
                onChipSelected?.Invoke(new ColorBoardChipSelection
                {
                    IsGradient = true,
                    GradientStartHex = startRgb,
                    GradientEndHex = endRgb,
                    ChipTag = tag
                });
            };

            return chip;
        }
    }
}
