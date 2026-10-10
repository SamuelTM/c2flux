using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace c2flux
{
    public enum ToolbarIconKind
    {
        Scan,
        Stop,
        Pause,
        OpenFolder,
        Table,
        PieChart,
        BarChart,
        Sunburst,
        Treemap,
        Export,
        Analysis,
        StorageHistory,
        ScanHistory,
        Search,
    }

    // The 16x16 icons the WinForms app drew for its toolbar buttons
    // (AntdThemeService.CreateMain*ButtonIcon, MainForm.Create*ButtonImage).
    // Line icons use the inactive blue, or white while their toggle is on.
    public sealed class ToolbarIcon : Control
    {
        public static readonly StyledProperty<ToolbarIconKind> KindProperty =
            AvaloniaProperty.Register<ToolbarIcon, ToolbarIconKind>(nameof(Kind));

        public static readonly StyledProperty<bool> IsActiveProperty =
            AvaloniaProperty.Register<ToolbarIcon, bool>(nameof(IsActive));

        private const double LineWidth = 1.8;

        static ToolbarIcon()
        {
            AffectsRender<ToolbarIcon>(KindProperty, IsActiveProperty, IsEnabledProperty);
        }

        public ToolbarIcon()
        {
            Width = 16;
            Height = 16;
        }

        public ToolbarIconKind Kind
        {
            get => GetValue(KindProperty);
            set => SetValue(KindProperty, value);
        }

        public bool IsActive
        {
            get => GetValue(IsActiveProperty);
            set => SetValue(IsActiveProperty, value);
        }

        public override void Render(DrawingContext context)
        {
            // GDI+ (anti-aliased, no pixel offset) puts coordinates on pixel
            // centers.
            using DrawingContext.PushedState centers = context.PushTransform(Matrix.CreateTranslation(0.5, 0.5));
            IBrush line = IsActive
                ? Brushes.White
                : !IsEffectivelyEnabled
                    ? this.FindResource(ActualThemeVariant, "DisabledTextBrush") as IBrush ?? Brushes.Gray
                    : this.FindResource(ActualThemeVariant, "IconInactiveBrush") as IBrush ?? Brushes.SteelBlue;
            Pen pen = new Pen(line, LineWidth);

            switch (Kind)
            {
                case ToolbarIconKind.Scan:
                    Polygon(context, Rgb(0, 120, 215), Rgb(0, 84, 153), (4, 2), (13, 8), (4, 14));
                    break;

                case ToolbarIconKind.Stop:
                    context.DrawRectangle(new SolidColorBrush(Rgb(196, 43, 28)), new Pen(new SolidColorBrush(Rgb(135, 24, 15)), 1), new Rect(4, 4, 8, 8));
                    break;

                case ToolbarIconKind.Pause:
                    // AntdUI drew the "⏸" text; two bars in a rounded square.
                    context.DrawRectangle(null, new Pen(line, 1), new RoundedRect(new Rect(2, 2, 12, 12), 2));
                    context.FillRectangle(line, new Rect(5.5, 5, 1.5, 6));
                    context.FillRectangle(line, new Rect(9, 5, 1.5, 6));
                    break;

                case ToolbarIconKind.OpenFolder:
                    // The shell's "open folder" stock icon, as in WinForms.
                    context.DrawImage(FileIconCache.OpenFolder, new Rect(-0.5, -0.5, 16, 16));
                    break;

                case ToolbarIconKind.Table:
                    context.DrawRectangle(null, pen, new Rect(2, 3, 12, 10));
                    context.DrawLine(pen, new Point(2, 7), new Point(14, 7));
                    context.DrawLine(pen, new Point(6, 3), new Point(6, 13));
                    break;

                case ToolbarIconKind.PieChart:
                    Arc(context, pen, new Rect(2, 2, 12, 12), 0, 270);
                    context.DrawLine(pen, new Point(8, 8), new Point(8, 2));
                    context.DrawLine(pen, new Point(8, 8), new Point(14, 8));
                    break;

                case ToolbarIconKind.BarChart:
                    context.DrawRectangle(null, pen, new Rect(2, 8, 2, 5));
                    context.DrawRectangle(null, pen, new Rect(7, 5, 2, 8));
                    context.DrawRectangle(null, pen, new Rect(12, 2, 2, 11));
                    break;

                case ToolbarIconKind.Sunburst:
                    Arc(context, pen, new Rect(2, 2, 12, 12), -90, 285);
                    Arc(context, pen, new Rect(5, 5, 6, 6), -90, 240);
                    context.DrawLine(pen, new Point(8, 2), new Point(8, 5));
                    context.DrawLine(pen, new Point(11, 8), new Point(14, 8));
                    break;

                case ToolbarIconKind.Treemap:
                    context.DrawRectangle(null, pen, new Rect(2, 2, 12, 12));
                    context.DrawLine(pen, new Point(8, 2), new Point(8, 14));
                    context.DrawLine(pen, new Point(2, 8), new Point(8, 8));
                    context.DrawLine(pen, new Point(11, 2), new Point(11, 9));
                    context.DrawLine(pen, new Point(8, 9), new Point(14, 9));
                    break;

                case ToolbarIconKind.Export:
                    context.DrawRectangle(new SolidColorBrush(Rgb(245, 245, 245)), new Pen(new SolidColorBrush(Rgb(90, 90, 90)), 1), new Rect(2, 2, 8, 12));
                    Polygon(context, Rgb(0, 120, 215), Rgb(0, 84, 153), (8, 5), (14, 8), (8, 11));
                    context.DrawLine(new Pen(new SolidColorBrush(Rgb(0, 84, 153)), 1), new Point(5, 8), new Point(12, 8));
                    break;

                case ToolbarIconKind.Analysis:
                    context.DrawLine(pen, new Point(2, 2), new Point(2, 13));
                    context.DrawLine(pen, new Point(2, 13), new Point(14, 13));
                    Polyline(context, pen, (3, 11), (6, 8), (9, 10), (13, 4));
                    break;

                case ToolbarIconKind.StorageHistory:
                    context.DrawEllipse(null, pen, new Rect(2, 2, 12, 12));
                    context.DrawLine(pen, new Point(8, 8), new Point(8, 4));
                    context.DrawLine(pen, new Point(8, 8), new Point(11, 10));
                    break;

                case ToolbarIconKind.ScanHistory:
                    context.DrawRectangle(null, pen, new Rect(2, 3, 8, 10));
                    context.DrawLine(pen, new Point(4, 6), new Point(8, 6));
                    context.DrawLine(pen, new Point(4, 9), new Point(8, 9));
                    context.DrawEllipse(null, pen, new Rect(10, 2, 4, 4));
                    context.DrawEllipse(null, pen, new Rect(10, 10, 4, 4));
                    context.DrawLine(pen, new Point(12, 6), new Point(12, 10));
                    break;

                case ToolbarIconKind.Search:
                    context.DrawEllipse(null, pen, new Rect(2, 2, 9, 9));
                    context.DrawLine(pen, new Point(10, 10), new Point(14, 14));
                    break;
            }
        }

        private static Color Rgb(byte r, byte g, byte b) => Color.FromRgb(r, g, b);

        private static void Polygon(DrawingContext context, Color fill, Color outline, params (double X, double Y)[] points)
        {
            context.DrawGeometry(new SolidColorBrush(fill), new Pen(new SolidColorBrush(outline), 1), Path(points, closed: true));
        }

        private static void Polyline(DrawingContext context, Pen pen, params (double X, double Y)[] points)
        {
            context.DrawGeometry(null, pen, Path(points, closed: false));
        }

        private static StreamGeometry Path((double X, double Y)[] points, bool closed)
        {
            StreamGeometry geometry = new StreamGeometry();

            using (StreamGeometryContext path = geometry.Open())
            {
                path.BeginFigure(new Point(points[0].X, points[0].Y), closed);

                for (int index = 1; index < points.Length; index++)
                {
                    path.LineTo(new Point(points[index].X, points[index].Y));
                }

                path.EndFigure(closed);
            }

            return geometry;
        }

        // GDI+ DrawArc: degrees, clockwise from the x axis.
        private static void Arc(DrawingContext context, Pen pen, Rect bounds, double startAngle, double sweepAngle)
        {
            Point At(double angle)
            {
                double radians = angle * Math.PI / 180;
                return new Point(bounds.Center.X + bounds.Width / 2 * Math.Cos(radians), bounds.Center.Y + bounds.Height / 2 * Math.Sin(radians));
            }

            StreamGeometry geometry = new StreamGeometry();

            using (StreamGeometryContext path = geometry.Open())
            {
                path.BeginFigure(At(startAngle), false);
                path.ArcTo(At(startAngle + sweepAngle), new Size(bounds.Width / 2, bounds.Height / 2), 0, sweepAngle > 180, SweepDirection.Clockwise);
                path.EndFigure(false);
            }

            context.DrawGeometry(null, pen, geometry);
        }
    }
}
