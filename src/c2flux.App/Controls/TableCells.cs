using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace c2flux
{
    // Cell painters shared by the tables.
    public static class TableCells
    {
        private const double ProgressWidth = 100;
        private const double ProgressHeight = 16;
        private const double ProgressRadius = 4;

        // AntdUI.CellProgress with the percentage written over it: a 100x16
        // rounded track centered in the cell, filled up to percent.
        public static void Percent(Control owner, DrawingContext context, Rect cell, double percent, bool selected)
        {
            IBrush back = owner.FindResource(owner.ActualThemeVariant, "TableProgressBackBrush") as IBrush ?? Brushes.Gray;
            IBrush fill = owner.FindResource(owner.ActualThemeVariant, "TableProgressFillBrush") as IBrush ?? Brushes.SteelBlue;
            IBrush foreground = selected ? Brushes.White : owner.FindResource(owner.ActualThemeVariant, "TextPrimaryBrush") as IBrush ?? Brushes.White;
            Rect track = new Rect(
                Math.Round(cell.X + (cell.Width - ProgressWidth) / 2),
                Math.Round(cell.Y + (cell.Height - ProgressHeight) / 2),
                ProgressWidth,
                ProgressHeight);
            double value = Math.Clamp(percent / 100D, 0, 1);

            context.DrawRectangle(back, null, new RoundedRect(track, ProgressRadius));

            if (value > 0)
            {
                context.DrawRectangle(fill, null, new RoundedRect(track.WithWidth(Math.Max(ProgressRadius * 2, track.Width * value)), ProgressRadius));
            }

            FormattedText text = new FormattedText(
                percent.ToString("0.0", CultureInfo.CurrentCulture) + " %",
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface(FontFamily.Default),
                owner.GetValue(TextElement.FontSizeProperty),
                foreground);
            context.DrawText(text, new Point(track.Center.X - text.Width / 2, Math.Round(track.Center.Y - text.Height / 2)));
        }
    }
}
