using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Media;

namespace c2flux
{
    // Port of the WinForms Chart_BarChart: the 18 largest children of an
    // entry as horizontal bars scaled to the largest one.
    public sealed class BarChart : ChartControl
    {
        private const int MaxItems = 18;
        private const double BarHeight = 14;
        private const double IconSize = 16;
        private const double IconToTextGap = 6;

        protected override void RenderChart(DrawingContext context)
        {
            List<FileSystemEntry> items = Entry == null
                ? new List<FileSystemEntry>()
                : Entry.Children
                    .Where(child => child.SizeBytes > 0)
                    .OrderByDescending(child => child.SizeBytes)
                    .Take(MaxItems)
                    .ToList();

            if (items.Count == 0)
            {
                DrawNoData(context);
                return;
            }

            const double leftMargin = 20;
            const double rightMargin = 20;
            const double topMargin = 18;
            const double labelToBarGap = 20;
            const double textPaddingLeft = 10;
            const double textGapRightOfBar = 8;

            long maxSize = items[0].SizeBytes;
            double rowHeight = Math.Max(FontHeight + 6, BarHeight + 7);
            double contentLeft = leftMargin;
            double contentRight = Bounds.Width - rightMargin;

            if (contentRight <= contentLeft)
            {
                return;
            }

            double longestLabelWidth = items.Max(item => IconSize + IconToTextGap + MeasureWidth(item.Name));
            double labelWidth = Math.Min(longestLabelWidth, Math.Max(100, Math.Min(260, Math.Floor(Bounds.Width / 3))));
            double barLeft = contentLeft + labelWidth + labelToBarGap;
            double maximumBarWidth = contentRight - barLeft;

            if (maximumBarWidth <= 0)
            {
                return;
            }

            for (int index = 0; index < items.Count; index++)
            {
                FileSystemEntry item = items[index];
                double y = topMargin + index * rowHeight;

                if (y + rowHeight > Bounds.Height)
                {
                    break;
                }

                Rect labelBounds = new Rect(contentLeft, y, labelWidth, rowHeight);
                AddHitArea(labelBounds, item);
                // shortcut: no file icon yet, only its space; draw it once the
                // file icon service of phase 5 exists.
                DrawTextLine(
                    context,
                    item.Name,
                    new Rect(labelBounds.X + IconSize + IconToTextGap, y, Math.Max(0, labelWidth - IconSize - IconToTextGap), rowHeight),
                    Foreground);

                double barWidth = Math.Max(1, Math.Min(maximumBarWidth, Math.Round(maximumBarWidth * ((double)item.SizeBytes / maxSize))));
                Rect barBounds = new Rect(barLeft, y + Math.Floor((rowHeight - BarHeight) / 2), barWidth, BarHeight);
                AddHitArea(barBounds, item);
                context.FillRectangle(FamilyGradient(ChartColors.GetFamilyColor(index), barBounds), barBounds);

                string sizeText = SizeFormatter.Format(item.SizeBytes);
                Rect textBounds = barBounds.Width >= MeasureWidth(sizeText) + textPaddingLeft
                    ? new Rect(barBounds.X + textPaddingLeft, y, Math.Max(0, barBounds.Width - textPaddingLeft), rowHeight)
                    : new Rect(barBounds.Right + textGapRightOfBar, y, Math.Max(0, contentRight - barBounds.Right - textGapRightOfBar), rowHeight);
                AddHitArea(textBounds, item);
                DrawTextLine(context, sizeText, textBounds, Foreground);
            }
        }

        protected override string GetToolTip(FileSystemEntry entry)
        {
            return FormatDates(entry, (created, modified, accessed) => string.Format(
                LocalizationService.GetText("Chart.TooltipDates"), created, Environment.NewLine, modified, accessed));
        }
    }
}
