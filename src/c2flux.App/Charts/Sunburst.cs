using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Media;

namespace c2flux
{
    // Port of the WinForms Chart_Sunburst: rings of descendants around the
    // entry, colored by family (a large child and its subtree share a color).
    public sealed class Sunburst : ChartControl
    {
        private const double FamilySplitMinimumShare = 0.10D;
        private const double FamilyPromotionMinimumShare = 0.50D;
        private const double DominantChildMinimumShare = 0.60D;
        private const int FamilyPromotionMaximumDepth = 8;

        private int _depth = 3;
        private int _maxItems = 1000;

        public void SetDisplayOptions(int depth, int maxItems)
        {
            _depth = Math.Max(0, depth);
            _maxItems = Math.Max(100, maxItems);
            InvalidateVisual();
        }

        protected override string GetToolTip(FileSystemEntry entry)
        {
            return entry.Name + Environment.NewLine + SizeFormatter.Format(entry.SizeBytes) + Environment.NewLine + entry.FullPath;
        }

        protected override void RenderChart(DrawingContext context)
        {
            if (Entry == null || Entry.Children.Count == 0)
            {
                DrawNoData(context);
                return;
            }

            int availableDepth = GetAvailableDepth(Entry);
            int visibleDepth = _depth == 0 ? availableDepth : Math.Min(_depth, availableDepth);

            if (visibleDepth <= 0)
            {
                DrawNoData(context);
                return;
            }

            // WinForms works in whole pixels here: Width, Height and the
            // diameter are ints.
            int width = (int)Bounds.Width;
            int height = (int)Bounds.Height;
            int diameter = Math.Max(0, Math.Min(width, height) - 40);

            if (diameter < 80)
            {
                return;
            }

            Point center = new Point((width - diameter) / 2 + diameter / 2D, (height - diameter) / 2 + diameter / 2D);
            double centerRadius = Math.Max(24, diameter * 0.10);
            double ringWidth = Math.Max(3, (diameter / 2D - centerRadius) / visibleDepth);
            int remainingItems = _maxItems;

            DrawChildren(context, Entry, center, centerRadius, ringWidth, 0, visibleDepth, -90, 360, ref remainingItems, ChartColors.GetFamilyColor(0), false);
            DrawCenter(context, center, centerRadius);
        }

        private void DrawChildren(
            DrawingContext context,
            FileSystemEntry parent,
            Point center,
            double centerRadius,
            double ringWidth,
            int level,
            int visibleDepth,
            double startAngle,
            double sweepAngle,
            ref int remainingItems,
            Color inheritedFamilyColor,
            bool familyLocked)
        {
            if (level >= visibleDepth || remainingItems <= 0)
            {
                return;
            }

            List<FileSystemEntry> children = SortedChildren(parent);
            long totalSize = children.Sum(child => child.SizeBytes);

            if (totalSize <= 0)
            {
                return;
            }

            bool createFamilies = !familyLocked && HasMeaningfulFamilySplit(parent, children);
            int familyIndex = ChartColors.GetFamilyStartIndex(parent.Name);
            double currentAngle = startAngle;
            Pen borderPen = new Pen(Resource("BackgroundPrimaryBrush"), 1);

            foreach (FileSystemEntry child in children)
            {
                if (remainingItems <= 0)
                {
                    break;
                }

                double childSweep = sweepAngle * child.SizeBytes / totalSize;

                if (childSweep < 0.15)
                {
                    currentAngle += childSweep;
                    continue;
                }

                Color childFamilyColor = inheritedFamilyColor;
                bool childFamilyLocked = familyLocked;

                if (createFamilies && !ShouldPromoteDescendantFamilies(parent, child))
                {
                    childFamilyColor = ChartColors.GetFamilyColor(familyIndex);
                    familyIndex++;
                    childFamilyLocked = true;
                }

                double innerRadius = centerRadius + level * ringWidth;
                double outerRadius = innerRadius + ringWidth;
                Color fill = ChartColors.GetFamilyShade(childFamilyColor, child.Name, level);
                Geometry segment = CreateRingSegment(center, innerRadius, outerRadius, currentAngle, childSweep);

                context.DrawGeometry(SegmentGradient(fill, center, innerRadius, outerRadius, currentAngle, childSweep), borderPen, segment);
                DrawSegmentLabel(context, segment, child, center, innerRadius, outerRadius, currentAngle, childSweep);
                AddHitArea(point => segment.FillContains(point), child);
                remainingItems--;

                if (child.IsDirectory && child.Children.Count > 0)
                {
                    DrawChildren(context, child, center, centerRadius, ringWidth, level + 1, visibleDepth, currentAngle, childSweep, ref remainingItems, childFamilyColor, childFamilyLocked);
                }

                currentAngle += childSweep;
            }
        }

        // shortcut: GDI+ PathGradientBrush (light at the segment's centroid,
        // dark along its outline) has no Avalonia equivalent; a radial
        // gradient from the segment's middle comes close. Revisit if the
        // captures differ visibly.
        private static IBrush SegmentGradient(Color fill, Point center, double innerRadius, double outerRadius, double startAngle, double sweepAngle)
        {
            double middleAngle = (startAngle + sweepAngle / 2) * Math.PI / 180;
            double middleRadius = (innerRadius + outerRadius) / 2;
            Point middle = new Point(center.X + middleRadius * Math.Cos(middleAngle), center.Y + middleRadius * Math.Sin(middleAngle));
            double reach = Math.Max(
                (outerRadius - innerRadius) / 2,
                Math.Min(middleRadius * Math.Sin(Math.Min(Math.PI, sweepAngle * Math.PI / 360)), outerRadius));

            return new RadialGradientBrush
            {
                Center = new RelativePoint(middle, RelativeUnit.Absolute),
                GradientOrigin = new RelativePoint(middle, RelativeUnit.Absolute),
                RadiusX = new RelativeScalar(reach, RelativeUnit.Absolute),
                RadiusY = new RelativeScalar(reach, RelativeUnit.Absolute),
                GradientStops =
                {
                    new GradientStop(ChartColors.Lighten(fill, ChartColors.GradientTopFactor), 0),
                    new GradientStop(ChartColors.Darken(fill, ChartColors.GradientBottomFactor), 1),
                },
            };
        }

        private static List<FileSystemEntry> SortedChildren(FileSystemEntry parent)
        {
            return parent.Children
                .Where(child => child != null && child.SizeBytes > 0)
                .OrderByDescending(child => child.SizeBytes)
                .ToList();
        }

        // At least two children with 10 % of the parent each.
        private static bool HasMeaningfulFamilySplit(FileSystemEntry parent, List<FileSystemEntry> children)
        {
            return parent.SizeBytes > 0 &&
                children.Count(child => child.SizeBytes / (double)parent.SizeBytes >= FamilySplitMinimumShare) >= 2;
        }

        // A folder with half of its parent whose own subtree splits into
        // families further down passes the choice of colors on to it.
        private static bool ShouldPromoteDescendantFamilies(FileSystemEntry parent, FileSystemEntry child)
        {
            return child.IsDirectory &&
                parent.SizeBytes > 0 &&
                child.SizeBytes / (double)parent.SizeBytes >= FamilyPromotionMinimumShare &&
                HasDescendantMeaningfulFamilySplit(child, 0);
        }

        private static bool HasDescendantMeaningfulFamilySplit(FileSystemEntry entry, int depth)
        {
            if (entry.Children.Count == 0 || entry.SizeBytes <= 0 || depth >= FamilyPromotionMaximumDepth)
            {
                return false;
            }

            List<FileSystemEntry> children = SortedChildren(entry);

            if (HasMeaningfulFamilySplit(entry, children))
            {
                return true;
            }

            FileSystemEntry dominantChild = children.FirstOrDefault(child => child.IsDirectory);

            return dominantChild != null &&
                dominantChild.SizeBytes / (double)entry.SizeBytes >= DominantChildMinimumShare &&
                HasDescendantMeaningfulFamilySplit(dominantChild, depth + 1);
        }

        private void DrawCenter(DrawingContext context, Point center, double centerRadius)
        {
            Rect bounds = new Rect(center.X - centerRadius, center.Y - centerRadius, centerRadius * 2, centerRadius * 2);
            context.DrawEllipse(Resource("BackgroundPrimaryBrush"), new Pen(Resource("BorderBrush"), 1), bounds);
            DrawCenteredText(context, Entry?.Name ?? string.Empty, bounds, Foreground, wrap: true);
        }

        // GDI+ DrawString with centered alignment and ellipsis.
        private void DrawCenteredText(DrawingContext context, string text, Rect bounds, IBrush brush, bool wrap)
        {
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                return;
            }

            FormattedText formatted = CreateText(text, brush);
            formatted.MaxTextWidth = bounds.Width;
            formatted.MaxTextHeight = bounds.Height;
            formatted.TextAlignment = TextAlignment.Center;
            formatted.Trimming = TextTrimming.CharacterEllipsis;

            if (!wrap)
            {
                formatted.MaxLineCount = 1;
            }

            context.DrawText(formatted, new Point(bounds.X, bounds.Y + (bounds.Height - formatted.Height) / 2));
        }

        private static Geometry CreateRingSegment(Point center, double innerRadius, double outerRadius, double startAngle, double sweepAngle)
        {
            if (sweepAngle >= 360)
            {
                return new CombinedGeometry(
                    GeometryCombineMode.Exclude,
                    new EllipseGeometry(new Rect(center.X - outerRadius, center.Y - outerRadius, outerRadius * 2, outerRadius * 2)),
                    new EllipseGeometry(new Rect(center.X - innerRadius, center.Y - innerRadius, innerRadius * 2, innerRadius * 2)));
            }

            Point PointAt(double radius, double angle)
            {
                double radians = angle * Math.PI / 180;
                return new Point(center.X + radius * Math.Cos(radians), center.Y + radius * Math.Sin(radians));
            }

            bool isLargeArc = sweepAngle > 180;
            StreamGeometry geometry = new StreamGeometry();

            using (StreamGeometryContext path = geometry.Open())
            {
                path.BeginFigure(PointAt(outerRadius, startAngle), true);
                path.ArcTo(PointAt(outerRadius, startAngle + sweepAngle), new Size(outerRadius, outerRadius), 0, isLargeArc, SweepDirection.Clockwise);
                path.LineTo(PointAt(innerRadius, startAngle + sweepAngle));
                path.ArcTo(PointAt(innerRadius, startAngle), new Size(innerRadius, innerRadius), 0, isLargeArc, SweepDirection.CounterClockwise);
                path.EndFigure(true);
            }

            return geometry;
        }

        private void DrawSegmentLabel(DrawingContext context, Geometry segment, FileSystemEntry entry, Point center, double innerRadius, double outerRadius, double startAngle, double sweepAngle)
        {
            double middleRadius = (innerRadius + outerRadius) / 2;
            double arcLength = Math.Abs(sweepAngle) * Math.PI / 180 * middleRadius;
            double radialHeight = outerRadius - innerRadius;

            if (arcLength < 58 || radialHeight < 18 || string.IsNullOrWhiteSpace(entry.Name))
            {
                return;
            }

            bool drawTwoLines = radialHeight >= 36 && arcLength >= 84;
            double labelWidth = Math.Min(Math.Max(0, arcLength - 12), 180);
            double labelHeight = drawTwoLines ? 34 : 18;

            if (labelWidth < 48)
            {
                return;
            }

            double radians = (startAngle + sweepAngle / 2) * Math.PI / 180;
            Point labelCenter = new Point(center.X + middleRadius * Math.Cos(radians), center.Y + middleRadius * Math.Sin(radians));
            Rect label = new Rect(labelCenter.X - labelWidth / 2, labelCenter.Y - labelHeight / 2, labelWidth, labelHeight);

            using (context.PushGeometryClip(segment))
            {
                DrawCenteredText(context, entry.Name, new Rect(label.X + 4, label.Y, Math.Max(0, label.Width - 8), drawTwoLines ? 17 : label.Height), Brushes.White, wrap: false);

                if (drawTwoLines)
                {
                    DrawCenteredText(context, SizeFormatter.Format(entry.SizeBytes), new Rect(label.X + 4, label.Y + 17, Math.Max(0, label.Width - 8), 17), Brushes.White, wrap: false);
                }
            }
        }

        private static int GetAvailableDepth(FileSystemEntry entry)
        {
            if (entry.Children.Count == 0)
            {
                return 0;
            }

            return 1 + entry.Children
                .Where(child => child != null && child.SizeBytes > 0)
                .Select(GetAvailableDepth)
                .DefaultIfEmpty(0)
                .Max();
        }
    }
}
