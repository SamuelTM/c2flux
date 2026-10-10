using System.Globalization;
using Avalonia;
using Avalonia.Media;

namespace c2flux
{
    public enum StatusSymbolKind
    {
        Information,
        Warning,
        Error,
        SystemDirectory
    }

    // Port of the WinForms StatusSymbolRenderer: alert symbols of the status
    // bar and alert history, and the +/- glyph of the tree.
    public static class StatusSymbolRenderer
    {
        public const int DefaultSymbolSize = 14;

        private static readonly Typeface SymbolTypeface = new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.Bold);

        public static void DrawSymbol(DrawingContext context, Rect symbolBox, StatusSymbolKind symbolKind)
        {
            switch (symbolKind)
            {
                case StatusSymbolKind.Information:
                    DrawCircleSymbol(context, symbolBox, Color.FromRgb(0, 120, 212), Color.FromRgb(0, 90, 158), Colors.White, "i", 0);
                    break;

                case StatusSymbolKind.Warning:
                    DrawWarningTriangleSymbol(context, symbolBox);
                    break;

                case StatusSymbolKind.Error:
                    DrawSquareSymbol(context, symbolBox, Color.FromRgb(196, 43, 28), Color.FromRgb(135, 24, 15), Colors.White, "×", 0);
                    break;

                default:
                    DrawCircleSymbol(context, symbolBox, Color.FromRgb(255, 140, 0), Color.FromRgb(178, 92, 0), Colors.Black, "!", 0);
                    break;
            }
        }

        // WinForms drew it with SystemColors Window, ControlDark and
        // WindowText, which stay light in the dark theme.
        public static void DrawTreeExpandGlyph(DrawingContext context, Rect glyphBox, bool expanded)
        {
            Rect bounds = GetShapeBounds(glyphBox);
            Pen linePen = new Pen(Brushes.Black, 1);

            context.DrawRectangle(Brushes.White, new Pen(new SolidColorBrush(Color.FromRgb(160, 160, 160)), 1), bounds);

            double centerX = bounds.Left + bounds.Width / 2;
            double centerY = bounds.Top + bounds.Height / 2;

            context.DrawLine(linePen, new Point(bounds.Left + 2, centerY), new Point(bounds.Right - 2, centerY));

            if (!expanded)
            {
                context.DrawLine(linePen, new Point(centerX, bounds.Top + 2), new Point(centerX, bounds.Bottom - 2));
            }
        }

        private static void DrawCircleSymbol(DrawingContext context, Rect symbolBox, Color fill, Color border, Color text, string symbolText, double textOffsetY)
        {
            Rect bounds = GetShapeBounds(symbolBox);
            context.DrawEllipse(new SolidColorBrush(fill), new Pen(new SolidColorBrush(border), 1), bounds);
            DrawCenteredSymbolText(context, symbolBox, text, symbolText, textOffsetY);
        }

        private static void DrawWarningTriangleSymbol(DrawingContext context, Rect symbolBox)
        {
            Rect bounds = GetShapeBounds(symbolBox);
            StreamGeometry triangle = new StreamGeometry();

            using (StreamGeometryContext path = triangle.Open())
            {
                path.BeginFigure(new Point(bounds.Left + bounds.Width / 2, bounds.Top), true);
                path.LineTo(bounds.BottomRight);
                path.LineTo(bounds.BottomLeft);
                path.EndFigure(true);
            }

            context.DrawGeometry(
                new SolidColorBrush(Color.FromRgb(255, 185, 0)),
                new Pen(new SolidColorBrush(Color.FromRgb(180, 125, 0)), 1),
                triangle);
            DrawCenteredSymbolText(context, symbolBox, Colors.Black, "!", 1);
        }

        private static void DrawSquareSymbol(DrawingContext context, Rect symbolBox, Color fill, Color border, Color text, string symbolText, double textOffsetY)
        {
            context.DrawRectangle(new SolidColorBrush(fill), new Pen(new SolidColorBrush(border), 1), GetShapeBounds(symbolBox));
            DrawCenteredSymbolText(context, symbolBox, text, symbolText, textOffsetY);
        }

        private static Rect GetShapeBounds(Rect symbolBox)
        {
            return new Rect(symbolBox.X + 0.5, symbolBox.Y + 0.5, symbolBox.Width - 1, symbolBox.Height - 1);
        }

        // Like WinForms: the glyph outline (bold, 9 px) is centered by its ink
        // bounds, not by the font's line box.
        private static void DrawCenteredSymbolText(DrawingContext context, Rect symbolBox, Color color, string symbolText, double offsetY)
        {
            SolidColorBrush brush = new SolidColorBrush(color);
            // BuildGeometry returns null when the text has no brush.
            FormattedText text = new FormattedText(
                symbolText, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, SymbolTypeface, 9, brush);
            Geometry geometry = text.BuildGeometry(new Point(0, 0));

            if (geometry == null)
            {
                return;
            }

            Rect ink = geometry.Bounds;
            geometry.Transform = new TranslateTransform(
                symbolBox.Center.X - ink.Center.X,
                symbolBox.Center.Y + offsetY - ink.Center.Y);
            context.DrawGeometry(brush, null, geometry);
        }
    }
}
