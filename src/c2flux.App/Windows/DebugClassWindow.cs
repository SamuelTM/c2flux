using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;

namespace c2flux
{
    // Port of the WinForms DebugClassForm (Ctrl+Shift+Alt+D in the
    // settings): a preview of the status symbols.
    public sealed class DebugClassWindow : Window
    {
        public DebugClassWindow()
        {
            Title = "DebugClassForm";
            Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://c2flux.App/Assets/c2flux.png")));
            Width = 520;
            Height = 280;
            MinWidth = 520;
            MinHeight = 280;
            CanMinimize = false;
            CanMaximize = false;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Content = new SymbolPreview();
        }

        private sealed class SymbolPreview : DrawnControl
        {
            private const double RowHeight = 48;

            // SystemColors.ControlLight.
            private static readonly Pen Separator = new Pen(new SolidColorBrush(Color.FromRgb(227, 227, 227)), 1);

            public override void Render(DrawingContext context)
            {
                context.FillRectangle(Resource("BackgroundPrimaryBrush"), new Rect(Bounds.Size));
                DrawTextLine(context, "Symbol-Preview", new Rect(16, 12, Bounds.Width - 32, 24), Foreground);
                DrawRow(context, 0, "Information", StatusSymbolKind.Information);
                DrawRow(context, 1, "Warnung", StatusSymbolKind.Warning);
                DrawRow(context, 2, "Fehler", StatusSymbolKind.Error);
                DrawRow(context, 3, "Systemordner-Hinweis", StatusSymbolKind.SystemDirectory);
            }

            private void DrawRow(DrawingContext context, int index, string label, StatusSymbolKind kind)
            {
                double y = 48 + index * RowHeight;
                double size = StatusSymbolRenderer.DefaultSymbolSize;

                // GDI+ DrawLine includes the end point.
                context.DrawLine(Separator, new Point(16, y + RowHeight - 0.5), new Point(Bounds.Width - 15, y + RowHeight - 0.5));
                StatusSymbolRenderer.DrawSymbol(context, new Rect(Math.Round(24 + (24 - size) / 2), Math.Round(y + (RowHeight - size) / 2), size, size), kind);
                DrawTextLine(context, label, new Rect(72, y, Bounds.Width - 88, RowHeight), Foreground);
            }
        }
    }
}
