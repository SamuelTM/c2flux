using System;
using Avalonia.Media;

namespace c2flux
{
    // The chart palette of the WinForms AntdThemeService: family colors,
    // gradients and name-based shades, with the same rounding.
    public static class ChartColors
    {
        private static readonly Color[] FamilyColors =
        {
            Color.FromRgb(232, 126, 36),
            Color.FromRgb(190, 185, 0),
            Color.FromRgb(205, 54, 113),
            Color.FromRgb(99, 88, 214),
            Color.FromRgb(29, 142, 207),
            Color.FromRgb(89, 170, 72),
            Color.FromRgb(150, 72, 196),
            Color.FromRgb(220, 75, 75),
            Color.FromRgb(25, 175, 157),
            Color.FromRgb(210, 143, 38),
            Color.FromRgb(76, 127, 215),
            Color.FromRgb(175, 74, 155),
        };

        public const double GradientTopFactor = 1.12D;
        public const double GradientBottomFactor = 0.88D;
        public const byte SegmentLabelBackgroundAlpha = 110;

        public static int FamilyColorCount => FamilyColors.Length;

        public static Color GetFamilyColor(int index)
        {
            int normalizedIndex = index % FamilyColors.Length;
            return FamilyColors[normalizedIndex < 0 ? normalizedIndex + FamilyColors.Length : normalizedIndex];
        }

        public static int GetFamilyStartIndex(string familyName)
        {
            return Math.Abs(GetStableNameHash(familyName) % FamilyColors.Length);
        }

        public static Color GetFamilyShade(Color familyColor, string name, int depth)
        {
            double factor =
                0.72D +
                Math.Min(0.2D, depth * 0.03D) +
                Math.Abs(GetStableNameHash(name) % 11) / 100D;

            return Color.FromRgb(Scale(familyColor.R, factor), Scale(familyColor.G, factor), Scale(familyColor.B, factor));
        }

        public static Color Lighten(Color color, double factor)
        {
            return Color.FromRgb(
                (byte)Math.Min(255, (int)Math.Round(color.R * factor)),
                (byte)Math.Min(255, (int)Math.Round(color.G * factor)),
                (byte)Math.Min(255, (int)Math.Round(color.B * factor)));
        }

        public static Color Darken(Color color, double factor)
        {
            return Color.FromArgb(
                color.A,
                (byte)Math.Max(0, (int)Math.Round(color.R * factor)),
                (byte)Math.Max(0, (int)Math.Round(color.G * factor)),
                (byte)Math.Max(0, (int)Math.Round(color.B * factor)));
        }

        // FNV-1a over the upper-cased name: the same colors in every run.
        private static int GetStableNameHash(string name)
        {
            unchecked
            {
                uint hash = 2166136261;

                foreach (char character in (name ?? string.Empty).ToUpperInvariant())
                {
                    hash ^= character;
                    hash *= 16777619;
                }

                return (int)(hash & 0x7FFFFFFF);
            }
        }

        private static byte Scale(byte value, double factor)
        {
            return (byte)Math.Max(24, Math.Min(235, (int)Math.Round(value * factor)));
        }
    }
}
