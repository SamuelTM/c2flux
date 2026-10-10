using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace c2flux
{
    // A control drawn by hand like the WinForms ones: theme brushes and text
    // laid out with the metrics of Segoe UI and TextRenderer.
    public abstract class DrawnControl : Control
    {
        protected void ShowRevealMenu(FileSystemEntry entry) => RevealMenu.Show(this, entry);

        protected IBrush Resource(string key) =>
            this.FindResource(ActualThemeVariant, key) as IBrush ?? Brushes.Transparent;

        protected IBrush Foreground => Resource("TextPrimaryBrush");

        protected FormattedText CreateText(string text, IBrush brush)
        {
            return new FormattedText(
                text,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface(FontFamily.Default),
                GetValue(TextElement.FontSizeProperty),
                brush);
        }

        // WinForms Font.Height of Segoe UI (line spacing 1.33 em: 16 px at
        // 12 px), so rows keep the original height with any font.
        protected double FontHeight => Math.Ceiling(GetValue(TextElement.FontSizeProperty) * 1.33);

        // TextRenderer pads text by about a sixth of the line height.
        protected double TextPadding => Math.Ceiling(FontHeight / 6);

        // TextRenderer.MeasureText: text width plus padding on both sides.
        protected double MeasureWidth(string text)
        {
            return Math.Ceiling(CreateText(text, null).WidthIncludingTrailingWhitespace + TextPadding * 2);
        }

        // TextRenderer.DrawText with Left | VerticalCenter | EndEllipsis
        // (and NoPadding when padded is false).
        protected void DrawTextLine(DrawingContext context, string text, Rect bounds, IBrush brush, bool padded = true)
        {
            double padding = padded ? TextPadding : 0;
            double width = bounds.Width - padding * 2;

            if (width <= 0)
            {
                return;
            }

            FormattedText formatted = CreateText(text, brush);
            formatted.MaxTextWidth = width;
            formatted.MaxLineCount = 1;
            formatted.Trimming = TextTrimming.CharacterEllipsis;
            context.DrawText(formatted, new Point(bounds.X + padding, bounds.Y + (bounds.Height - formatted.Height) / 2));
        }
    }
}
