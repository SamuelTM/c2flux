using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace c2flux
{
    public enum StorageHistoryDisplayMode
    {
        UsedSpace,
        FreeSpace
    }

    // Port of the WinForms StorageHistoryChart: used or free space of a drive
    // over time, on a background that turns red as free space runs out.
    public sealed class StorageHistoryChart : DrawnControl
    {
        private const int TargetYAxisIntervalCount = 17;
        private const int CriticalFreeSpaceGigabytes = 10;
        private const long BytesPerGigabyte = 1024L * 1024L * 1024L;

        private static readonly Color HealthyColor = Color.FromRgb(120, 190, 120);
        private static readonly Color WarningColor = Color.FromRgb(225, 140, 140);
        private static readonly Color CriticalColor = Color.FromRgb(210, 70, 70);
        // SystemColors.Highlight in WinForms (Windows 10/11: #0078D4).
        private static readonly IBrush GraphBrush = new SolidColorBrush(Color.FromRgb(0, 120, 212));

        private IReadOnlyList<StorageHistoryRecord> _records = Array.Empty<StorageHistoryRecord>();
        private StorageHistoryDisplayMode _displayMode = StorageHistoryDisplayMode.FreeSpace;
        private Point[] _points = Array.Empty<Point>();
        private int _hoveredPointIndex = -1;
        private int _gradientIntensityPercent = 55;

        public StorageHistoryChart()
        {
            ClipToBounds = true;
        }

        public void SetRecords(IReadOnlyList<StorageHistoryRecord> records, StorageHistoryDisplayMode displayMode)
        {
            _records = (records ?? Array.Empty<StorageHistoryRecord>()).OrderBy(record => record.RecordedAtUtc).ToList();
            _displayMode = displayMode;
            _hoveredPointIndex = -1;
            ToolTip.SetTip(this, null);
            InvalidateVisual();
        }

        public void SetGradientIntensity(int gradientIntensityPercent)
        {
            int clampedValue = Math.Clamp(gradientIntensityPercent, 0, 100);

            if (_gradientIntensityPercent != clampedValue)
            {
                _gradientIntensityPercent = clampedValue;
                InvalidateVisual();
            }
        }

        // The measurement within 12 px of location, if any.
        public StorageHistoryRecord GetRecordAt(Point location)
        {
            int index = FindNearestPoint(location);
            return index < 0 ? null : _records[index];
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);

            int nearestIndex = FindNearestPoint(e.GetPosition(this));

            if (nearestIndex == _hoveredPointIndex)
            {
                return;
            }

            _hoveredPointIndex = nearestIndex;
            ToolTip.SetTip(this, nearestIndex < 0 ? null : FormatToolTip(nearestIndex));
        }

        protected override void OnPointerExited(PointerEventArgs e)
        {
            base.OnPointerExited(e);
            _hoveredPointIndex = -1;
            ToolTip.SetTip(this, null);
        }

        private int FindNearestPoint(Point location)
        {
            if (_points.Length == 0 || _records.Count != _points.Length)
            {
                return -1;
            }

            int nearestIndex = -1;
            double nearestDistance = double.MaxValue;

            for (int index = 0; index < _points.Length; index++)
            {
                Vector delta = _points[index] - location;
                double distance = delta.X * delta.X + delta.Y * delta.Y;

                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearestIndex = index;
                }
            }

            return nearestDistance > 144 ? -1 : nearestIndex;
        }

        private string FormatToolTip(int index)
        {
            StorageHistoryRecord record = _records[index];
            long value = GetDisplayValue(record);
            string hint = string.Format(
                CultureInfo.CurrentCulture,
                "{0}\n{1}: {2}",
                record.RecordedAtUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture),
                LocalizationService.GetText(_displayMode == StorageHistoryDisplayMode.FreeSpace ? "StorageHistory.Free" : "StorageHistory.Used"),
                SizeFormatter.Format(value));

            if (index == 0)
            {
                return hint;
            }

            long change = value - GetDisplayValue(_records[index - 1]);
            bool german = string.Equals(LocalizationService.CurrentLanguageCode, LocalizationService.GermanLanguageCode, StringComparison.OrdinalIgnoreCase);
            // Not in the language files in WinForms either.
            string description = german
                ? change > 0 ? "Seit letzter Messung gestiegen" : change < 0 ? "Seit letzter Messung gesunken" : "Seit letzter Messung unverändert"
                : change > 0 ? "Increased since last measurement" : change < 0 ? "Decreased since last measurement" : "Unchanged since last measurement";

            return hint + string.Format(CultureInfo.CurrentCulture, "\n- {0}: {1}", description, SizeFormatter.Format(Math.Abs(change)));
        }

        public override void Render(DrawingContext context)
        {
            IBrush background = Resource("BackgroundPrimaryBrush");
            context.FillRectangle(background, new Rect(Bounds.Size));

            Rect plotArea = new Rect(80, 45, Math.Max(1, Math.Floor(Bounds.Width) - 80 - 25), Math.Max(1, Math.Floor(Bounds.Height) - 45 - 60));

            // GDI+ DrawString starts the text about a sixth of an em in.
            FormattedText title = CreateText(LocalizationService.GetText("StorageHistory.Graph"), Foreground);
            title.SetFontWeight(FontWeight.Bold);
            context.DrawText(title, new Point(12 + Math.Round(GetFontSize() / 6), 12));

            if (_records.Count == 0)
            {
                _points = Array.Empty<Point>();
                FormattedText empty = CreateText(LocalizationService.GetText("StorageHistory.NoData"), Foreground);
                context.DrawText(empty, new Point((Bounds.Width - empty.Width) / 2, (Bounds.Height - empty.Height) / 2));
                return;
            }

            long maximumCapacity = _records.Max(record => record.TotalCapacityBytes);
            long maximumValue = _records.Max(GetDisplayValue);
            double sourceMaximumGigabytes = Math.Max(1L, Math.Max(maximumCapacity, maximumValue)) / (double)BytesPerGigabyte;
            long axisStepGigabytes = GetNiceAxisStepInGigabytes(sourceMaximumGigabytes, TargetYAxisIntervalCount);
            long axisMaximumGigabytes = Math.Max(axisStepGigabytes, (long)Math.Ceiling(sourceMaximumGigabytes / axisStepGigabytes) * axisStepGigabytes);
            long axisMaximum = Math.Max(1L, axisMaximumGigabytes * BytesPerGigabyte);
            int yAxisLabelCount = (int)(axisMaximumGigabytes / axisStepGigabytes) + 1;

            context.FillRectangle(CreateBackgroundBrush(plotArea, axisMaximum, (background as ISolidColorBrush)?.Color ?? Colors.Black), plotArea);

            DateTime minimumTime = _records.Min(record => record.RecordedAtUtc);
            DateTime maximumTime = _records.Max(record => record.RecordedAtUtc);
            double timeRangeTicks = Math.Max(1D, (maximumTime - minimumTime).Ticks);
            // GDI+ draws the dotted alpha-110 grid anti-aliased at fractional
            // positions, which blurs it into an even line of half the alpha.
            // Coordinates get +0.5 throughout: GDI+ puts them on pixel centers.
            Pen gridPen = new Pen(GetLineBrush(55), 1);
            Pen axisPen = new Pen(GetLineBrush(180), 1);
            double labelHeight = FontHeight;

            for (int index = 0; index < yAxisLabelCount; index++)
            {
                double ratio = yAxisLabelCount == 1 ? 0 : index / (double)(yAxisLabelCount - 1);
                double y = plotArea.Bottom - ratio * plotArea.Height;
                context.DrawLine(gridPen, new Point(plotArea.Left, y + 0.5), new Point(plotArea.Right, y + 0.5));

                string label = string.Format(CultureInfo.CurrentCulture, "{0} GB", (axisStepGigabytes * index).ToString("N0", CultureInfo.CurrentCulture));
                double labelWidth = MeasureWidth(label);
                DrawTextLine(context, label, new Rect(plotArea.Left - labelWidth - 6, (int)y - Math.Floor(labelHeight / 2), labelWidth, labelHeight), Foreground);
            }

            int verticalGridLineCount = Math.Max(2, Math.Min(6, (int)plotArea.Width / 140));

            for (int index = 0; index <= verticalGridLineCount; index++)
            {
                double ratio = index / (double)verticalGridLineCount;
                double x = plotArea.Left + ratio * plotArea.Width;
                context.DrawLine(gridPen, new Point(x + 0.5, plotArea.Top), new Point(x + 0.5, plotArea.Bottom));

                DateTime labelTime = minimumTime.AddTicks((long)Math.Round((maximumTime - minimumTime).Ticks * ratio));
                string label = FormatAxisTime(labelTime, minimumTime, maximumTime);
                double labelWidth = MeasureWidth(label);
                double labelX = Math.Max(plotArea.Left, Math.Min(plotArea.Right - labelWidth, (int)x - Math.Floor(labelWidth / 2)));
                DrawTextLine(context, label, new Rect(labelX, plotArea.Bottom + 8, labelWidth, labelHeight), Foreground);
            }

            context.DrawLine(axisPen, new Point(plotArea.Left + 0.5, plotArea.Top), new Point(plotArea.Left + 0.5, plotArea.Bottom));
            context.DrawLine(axisPen, new Point(plotArea.Left, plotArea.Bottom + 0.5), new Point(plotArea.Right, plotArea.Bottom + 0.5));

            _points = new Point[_records.Count];

            for (int index = 0; index < _records.Count; index++)
            {
                double normalizedTime = _records.Count == 1 ? 0.5 : (_records[index].RecordedAtUtc - minimumTime).Ticks / timeRangeTicks;
                double normalizedValue = GetDisplayValue(_records[index]) / (double)axisMaximum;
                _points[index] = new Point(plotArea.Left + normalizedTime * plotArea.Width, plotArea.Bottom - normalizedValue * plotArea.Height);
            }

            Pen graphPen = new Pen(GraphBrush, 2);
            Vector pixelCenter = new Vector(0.5, 0.5);

            for (int index = 1; index < _points.Length; index++)
            {
                context.DrawLine(graphPen, _points[index - 1] + pixelCenter, _points[index] + pixelCenter);
            }

            foreach (Point point in _points)
            {
                context.DrawEllipse(GraphBrush, null, point + pixelCenter, 3, 3);
            }
        }

        private double GetFontSize() => GetValue(Avalonia.Controls.Documents.TextElement.FontSizeProperty);

        // Green at the top, red at the bottom: free space running out (or,
        // for used space, the drive filling up) is the danger zone.
        private IBrush CreateBackgroundBrush(Rect plotArea, long axisMaximum, Color background)
        {
            Color Blend(Color target)
            {
                double ratio = _gradientIntensityPercent / 100D;
                return Color.FromRgb(
                    (byte)Math.Round(background.R + (target.R - background.R) * ratio),
                    (byte)Math.Round(background.G + (target.G - background.G) * ratio),
                    (byte)Math.Round(background.B + (target.B - background.B) * ratio));
            }

            Color pastelGreen = Blend(HealthyColor);
            Color pastelRed = Blend(WarningColor);
            Color strongRed = Blend(CriticalColor);
            LinearGradientBrush brush = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(plotArea.TopLeft, RelativeUnit.Absolute),
                EndPoint = new RelativePoint(plotArea.BottomLeft, RelativeUnit.Absolute),
            };

            if (_displayMode == StorageHistoryDisplayMode.FreeSpace)
            {
                double criticalPosition = Math.Clamp(1 - Math.Min(1, CriticalFreeSpaceGigabytes * (double)BytesPerGigabyte / Math.Max(1D, axisMaximum)), 0, 1);
                brush.GradientStops.Add(new GradientStop(pastelGreen, 0));

                if (criticalPosition > 0 && criticalPosition < 1)
                {
                    brush.GradientStops.Add(new GradientStop(pastelRed, criticalPosition));
                }

                brush.GradientStops.Add(new GradientStop(strongRed, 1));
            }
            else
            {
                brush.GradientStops.Add(new GradientStop(pastelRed, 0));
                brush.GradientStops.Add(new GradientStop(pastelGreen, 1));
            }

            return brush;
        }

        private IBrush GetLineBrush(byte alpha)
        {
            Color baseColor = ActualThemeVariant == Avalonia.Styling.ThemeVariant.Light ? Colors.Black : Colors.White;
            return new SolidColorBrush(Color.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B));
        }

        private static string FormatAxisTime(DateTime valueUtc, DateTime minimumTime, DateTime maximumTime)
        {
            double days = (maximumTime - minimumTime).TotalDays;
            string format = days < 1 ? "t" : days < 7 ? "g" : "d";
            return valueUtc.ToLocalTime().ToString(format, CultureInfo.CurrentCulture);
        }

        private long GetDisplayValue(StorageHistoryRecord record)
        {
            if (_displayMode == StorageHistoryDisplayMode.FreeSpace)
            {
                return record.TotalCapacityBytes > 0 ? Math.Max(0, Math.Min(record.TotalCapacityBytes, record.FreeSpaceBytes)) : 0;
            }

            return record.TotalCapacityBytes > 0
                ? Math.Max(0, Math.Min(record.TotalCapacityBytes, record.TotalCapacityBytes - record.FreeSpaceBytes))
                : Math.Max(0, record.SizeBytes);
        }

        // 1, 2, 2.5 or 5 times a power of ten, giving about 17 intervals.
        internal static long GetNiceAxisStepInGigabytes(double maximumValueGigabytes, int targetIntervalCount)
        {
            if (maximumValueGigabytes <= 0)
            {
                return 1;
            }

            double rawStep = maximumValueGigabytes / Math.Max(1, targetIntervalCount);
            double magnitude = Math.Pow(10, Math.Floor(Math.Log10(rawStep)));
            double normalized = rawStep / magnitude;

            foreach (double niceStep in new[] { 1D, 2D, 2.5D, 5D, 10D })
            {
                if (normalized <= niceStep)
                {
                    return Math.Max(1, (long)Math.Ceiling(niceStep * magnitude));
                }
            }

            return Math.Max(1, (long)Math.Ceiling(10 * magnitude));
        }
    }
}
