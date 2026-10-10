using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Media;

namespace c2flux
{
    // Port of the WinForms Chart_PieChart: the ten largest children of an
    // entry plus "Other", with a legend on the right.
    public sealed class PieChart : ChartControl
    {
        private const int MaxItems = 10;

        protected override void RenderChart(DrawingContext context)
        {
            List<ChartItem> items = Entry == null ? new List<ChartItem>() : CreateItems(Entry);
            long totalSize = items.Sum(item => item.SizeBytes);

            if (totalSize <= 0)
            {
                DrawNoData(context);
                return;
            }

            const double chartLeft = 24;
            const double chartTop = 24;
            const double chartLegendGap = 24;
            const double rightPadding = 8;

            double legendWidth = Math.Min(
                items.Max(item => MeasureWidth(FormatItemLabel(item, totalSize)) + 30),
                Math.Max(0, Bounds.Width / 2));
            double chartSize = Math.Max(0, Math.Min(
                Bounds.Width - chartLeft - chartLegendGap - legendWidth - rightPadding,
                Bounds.Height - chartTop * 2));
            Rect chartBounds = new Rect(chartLeft, chartTop, chartSize, chartSize);
            double startAngle = -90;

            for (int index = 0; index < items.Count; index++)
            {
                ChartItem item = items[index];
                double sweepAngle = item.SizeBytes * 360D / totalSize;
                Geometry slice = CreateSlice(chartBounds, startAngle, sweepAngle);

                if (slice.Bounds.Width > 0 && slice.Bounds.Height > 0)
                {
                    context.DrawGeometry(FamilyGradient(ChartColors.GetFamilyColor(index), slice.Bounds), null, slice);
                }

                double sliceStart = startAngle;
                AddHitArea(point => SliceContains(chartBounds, sliceStart, sweepAngle, point), item.Entry);
                startAngle += sweepAngle;
            }

            context.DrawEllipse(null, new Pen(Foreground, 1), chartBounds);
            DrawLegend(context, items, totalSize, chartBounds.Right + chartLegendGap, chartTop);
        }

        protected override string FormatToolTip(FileSystemEntry entry, DateTime created, DateTime modified, DateTime accessed)
        {
            return string.Format(
                LocalizationService.GetText("Chart.PieTooltip"),
                entry.FullPath, Environment.NewLine, created, modified, accessed);
        }

        // Top ten children by size (zero-byte ones left out), then "Other"
        // with the rest.
        internal static List<ChartItem> CreateItems(FileSystemEntry entry)
        {
            List<ChartItem> items = entry.Children
                .Where(child => child.SizeBytes > 0)
                .OrderByDescending(child => child.SizeBytes)
                .Take(MaxItems)
                .Select(child => new ChartItem(child.Name, child.SizeBytes, child))
                .ToList();

            long otherSize = entry.Children.Sum(child => child.SizeBytes) - items.Sum(item => item.SizeBytes);

            if (otherSize > 0)
            {
                items.Add(new ChartItem(LocalizationService.GetText("Chart.Other"), otherSize, null));
            }

            return items;
        }

        private static string FormatItemLabel(ChartItem item, long totalSize)
        {
            return string.Format(
                LocalizationService.GetText("Chart.ItemLabel"),
                item.Name,
                SizeFormatter.Format(item.SizeBytes),
                item.SizeBytes * 100D / totalSize);
        }

        private void DrawLegend(DrawingContext context, List<ChartItem> items, long totalSize, double left, double top)
        {
            double y = top;
            double rowHeight = Math.Max(FontHeight + 4, 22);

            for (int index = 0; index < items.Count; index++)
            {
                ChartItem item = items[index];
                context.FillRectangle(new SolidColorBrush(ChartColors.GetFamilyColor(index)), new Rect(left, y + 3, 14, 14));
                AddHitArea(new Rect(left, y, Math.Max(0, Bounds.Width - left - 8), rowHeight), item.Entry);
                DrawTextLine(
                    context,
                    FormatItemLabel(item, totalSize),
                    new Rect(left + 22, y, Math.Max(0, Bounds.Width - left - 30), rowHeight),
                    Foreground);
                y += Math.Max(rowHeight + 2, 24);
            }
        }

        // GDI+ AddPie: angles in degrees, clockwise from the x axis.
        private static Geometry CreateSlice(Rect bounds, double startAngle, double sweepAngle)
        {
            if (sweepAngle >= 360)
            {
                return new EllipseGeometry(bounds);
            }

            StreamGeometry geometry = new StreamGeometry();

            using (StreamGeometryContext path = geometry.Open())
            {
                path.BeginFigure(bounds.Center, true);
                path.LineTo(PointOnEllipse(bounds, startAngle));
                path.ArcTo(
                    PointOnEllipse(bounds, startAngle + sweepAngle),
                    new Size(bounds.Width / 2, bounds.Height / 2),
                    0,
                    sweepAngle > 180,
                    SweepDirection.Clockwise);
                path.EndFigure(true);
            }

            return geometry;
        }

        private static Point PointOnEllipse(Rect bounds, double angle)
        {
            double radians = angle * Math.PI / 180;
            return new Point(
                bounds.Center.X + bounds.Width / 2 * Math.Cos(radians),
                bounds.Center.Y + bounds.Height / 2 * Math.Sin(radians));
        }

        internal static bool SliceContains(Rect bounds, double startAngle, double sweepAngle, Point point)
        {
            double radiusX = bounds.Width / 2;
            double radiusY = bounds.Height / 2;

            if (!bounds.Contains(point) || radiusX <= 0 || radiusY <= 0)
            {
                return false;
            }

            double x = (point.X - bounds.Center.X) / radiusX;
            double y = (point.Y - bounds.Center.Y) / radiusY;

            if (x * x + y * y > 1)
            {
                return false;
            }

            if (sweepAngle >= 360)
            {
                return true;
            }

            double start = NormalizeAngle(startAngle);
            double end = NormalizeAngle(startAngle + sweepAngle);
            double angle = NormalizeAngle(Math.Atan2(point.Y - bounds.Center.Y, point.X - bounds.Center.X) * 180 / Math.PI);

            return start <= end
                ? angle >= start && angle <= end
                : angle >= start || angle <= end;
        }

        private static double NormalizeAngle(double angle)
        {
            angle %= 360;
            return angle < 0 ? angle + 360 : angle;
        }

        internal sealed class ChartItem
        {
            public ChartItem(string name, long sizeBytes, FileSystemEntry entry)
            {
                Name = name;
                SizeBytes = sizeBytes;
                Entry = entry;
            }

            public string Name { get; }
            public long SizeBytes { get; }
            public FileSystemEntry Entry { get; }
        }
    }
}
