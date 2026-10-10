using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace c2flux
{
    // Port of the WinForms ScanHistoryGrowthOverviewControl: totals of a scan
    // comparison, the drive before and after, and the folders or files that
    // changed the most.
    public sealed class GrowthOverview : UserControl
    {
        private readonly TextBlock _totalGrowth = new TextBlock();
        private readonly TextBlock _newFiles = new TextBlock();
        private readonly TextBlock _changedFiles = new TextBlock();
        private readonly TextBlock _deletedFiles = new TextBlock();
        private readonly TextBlock _driveTitle = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        private readonly ComboBox _view = new ComboBox { Width = 260, Margin = new Thickness(0, 2) };
        private readonly ComparisonChart _driveChart = new ComparisonChart();
        private readonly ComparisonChart _detailChart = new ComparisonChart();
        private ScanHistoryComparisonResult _result;

        public GrowthOverview()
        {
            Grid summary = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*") };
            TextBlock[] summaryLabels = { _totalGrowth, _newFiles, _changedFiles, _deletedFiles };

            for (int index = 0; index < summaryLabels.Length; index++)
            {
                summaryLabels[index].VerticalAlignment = VerticalAlignment.Center;
                summaryLabels[index].TextTrimming = TextTrimming.CharacterEllipsis;
                Border card = new Border { Margin = new Thickness(3), Padding = new Thickness(8), Child = summaryLabels[index] };
                card.Bind(Border.BackgroundProperty, card.GetResourceObservable("BackgroundSecondaryBrush"));
                Grid.SetColumn(card, index);
                summary.Children.Add(card);
            }

            _view.ItemsSource = new[]
            {
                LocalizationService.GetText("ScanHistory.OverviewFolders"),
                LocalizationService.GetText("ScanHistory.OverviewNewFilesView"),
                LocalizationService.GetText("ScanHistory.OverviewChangedFilesView"),
            };
            _view.SelectedIndex = 0;
            _view.SelectionChanged += (_, _) => UpdateDetailChart();

            _driveChart.PathActivated += (path, isFile) => PathActivated?.Invoke(path, isFile);
            _detailChart.PathActivated += (path, isFile) => PathActivated?.Invoke(path, isFile);

            DockPanel detail = new DockPanel();
            Panel viewHeader = new Panel { Height = 34, Children = { _view } };
            _view.HorizontalAlignment = HorizontalAlignment.Left;
            DockPanel.SetDock(viewHeader, Dock.Top);
            detail.Children.Add(viewHeader);
            detail.Children.Add(new ScrollViewer { Content = _detailChart });
            // WinForms: the detail panel keeps the default 3 px margin of a
            // TableLayoutPanel cell, and a 34 px top padding above its header.
            detail.Margin = new Thickness(3, 37, 3, 3);

            Grid root = new Grid { RowDefinitions = new RowDefinitions("68,24,96,*"), Margin = new Thickness(8) };
            Grid.SetRow(_driveTitle, 1);
            Grid.SetRow(_driveChart, 2);
            Grid.SetRow(detail, 3);
            root.Children.Add(summary);
            root.Children.Add(_driveTitle);
            root.Children.Add(_driveChart);
            root.Children.Add(detail);
            Content = root;
        }

        // Double click on a row: its path and whether it is a file.
        public event Action<string, bool> PathActivated;

        public void BindResult(ScanHistoryComparisonResult result)
        {
            _result = result;
            _totalGrowth.Text = LocalizationService.Format("ScanHistory.OverviewTotalGrowth", FormatSignedSize(result?.SizeDeltaBytes ?? 0));
            _newFiles.Text = LocalizationService.Format("ScanHistory.OverviewNewFiles", result?.NewFileCount ?? 0);
            _changedFiles.Text = LocalizationService.Format("ScanHistory.OverviewChangedFiles", result?.ChangedFileCount ?? 0);
            _deletedFiles.Text = LocalizationService.Format("ScanHistory.OverviewDeletedFiles", result?.DeletedFileCount ?? 0);
            _driveTitle.Text = LocalizationService.GetText("ScanHistory.OverviewDriveComparison");

            List<ComparisonItem> driveItems = new List<ComparisonItem>();

            if (result != null)
            {
                string drivePath = string.IsNullOrWhiteSpace(result.CompareScan?.RootPath) ? result.BaselineScan?.RootPath : result.CompareScan.RootPath;
                driveItems.Add(new ComparisonItem(drivePath, result.BaselineSizeBytes, result.CompareSizeBytes, false));
            }

            _driveChart.Bind(driveItems);
            UpdateDetailChart();
        }

        private void UpdateDetailChart()
        {
            _detailChart.Bind(_result == null ? new List<ComparisonItem>() : CreateDetailItems(_result, _view.SelectedIndex));
        }

        // The 20 largest changes of the chosen kind. Folders: the root's
        // direct subfolders, or any folder when none of them changed.
        internal static List<ComparisonItem> CreateDetailItems(ScanHistoryComparisonResult result, int view)
        {
            if (view == 1)
            {
                return result.NewFiles
                    .Where(item => item.CompareSizeBytes > 0)
                    .OrderByDescending(item => item.DeltaBytes)
                    .Take(20)
                    .Select(item => new ComparisonItem(item.Path, item.BaselineSizeBytes, item.CompareSizeBytes, true))
                    .ToList();
            }

            if (view == 2)
            {
                return result.ChangedFiles
                    .Where(item => item.DeltaBytes != 0)
                    .OrderByDescending(item => Math.Abs(item.DeltaBytes))
                    .Take(20)
                    .Select(item => new ComparisonItem(item.Path, item.BaselineSizeBytes, item.CompareSizeBytes, true))
                    .ToList();
            }

            string rootPath = NormalizePath(result.CompareScan?.RootPath);

            if (string.IsNullOrWhiteSpace(rootPath))
            {
                rootPath = NormalizePath(result.BaselineScan?.RootPath);
            }

            IEnumerable<ScanHistoryFolderGrowth> changed = result.FolderGrowth.Where(item => item.DeltaBytes != 0);
            List<ScanHistoryFolderGrowth> topLevel = changed.Where(item => PathsEqual(GetParentPath(item.Path), rootPath)).ToList();

            return (topLevel.Count > 0 ? topLevel : changed)
                .OrderByDescending(item => Math.Abs(item.DeltaBytes))
                .Take(20)
                .Select(item => new ComparisonItem(item.Path, item.BaselineSizeBytes, item.CompareSizeBytes, false))
                .ToList();
        }

        private static string GetParentPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            try
            {
                string normalizedPath = NormalizePath(path);
                return PathsEqual(normalizedPath, Path.GetPathRoot(normalizedPath)) ? string.Empty : NormalizePath(Path.GetDirectoryName(normalizedPath));
            }
            catch (ArgumentException)
            {
                return string.Empty;
            }
        }

        private static bool PathsEqual(string left, string right) =>
            string.Equals(NormalizePath(left), NormalizePath(right), StringComparison.OrdinalIgnoreCase);

        private static string NormalizePath(string path) =>
            string.IsNullOrWhiteSpace(path) ? string.Empty : EntryPaths.ToNativeSeparators(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        internal static string FormatSignedSize(long bytes)
        {
            return bytes > 0 ? "+" + SizeFormatter.Format(bytes)
                : bytes < 0 ? "-" + SizeFormatter.Format(Math.Abs(bytes))
                : SizeFormatter.Format(0);
        }

        internal sealed class ComparisonItem
        {
            public ComparisonItem(string path, long baselineSizeBytes, long compareSizeBytes, bool isFile)
            {
                Path = path ?? string.Empty;
                BaselineSizeBytes = Math.Max(0, baselineSizeBytes);
                CompareSizeBytes = Math.Max(0, compareSizeBytes);
                IsFile = isFile;
            }

            public string Path { get; }
            public long BaselineSizeBytes { get; }
            public long CompareSizeBytes { get; }
            public bool IsFile { get; }
            public long DeltaBytes => CompareSizeBytes - BaselineSizeBytes;
        }

        // Rows of "before" and "after" bars, all scaled to the largest size.
        private sealed class ComparisonChart : DrawnControl
        {
            private const double RowHeight = 74;
            private static readonly IBrush BeforeBrush = new SolidColorBrush(Color.FromRgb(120, 120, 120));
            // ControlPaint.Dark(BackgroundSecondary, 0.12) as measured in WinForms.
            private static readonly IBrush TrackBrush = new SolidColorBrush(Color.FromRgb(26, 26, 26));
            private List<ComparisonItem> _items = new List<ComparisonItem>();

            public ComparisonChart()
            {
                ClipToBounds = true;
            }

            public event Action<string, bool> PathActivated;

            public void Bind(List<ComparisonItem> items)
            {
                _items = items;
                InvalidateMeasure();
                InvalidateVisual();
            }

            protected override Size MeasureOverride(Size availableSize)
            {
                return new Size(0, _items.Count * RowHeight);
            }

            public override void Render(DrawingContext context)
            {
                context.FillRectangle(Resource("BackgroundPrimaryBrush"), new Rect(Bounds.Size));

                if (_items.Count == 0)
                {
                    FormattedText empty = CreateText(LocalizationService.GetText("ScanHistory.OverviewNoGrowth"), Foreground);
                    context.DrawText(empty, new Point((Bounds.Width - empty.Width) / 2, (Bounds.Height - empty.Height) / 2));
                    return;
                }

                long maximumSizeBytes = Math.Max(1, _items.Max(item => Math.Max(item.BaselineSizeBytes, item.CompareSizeBytes)));

                for (int index = 0; index < _items.Count; index++)
                {
                    DrawItem(context, _items[index], index, index * RowHeight, maximumSizeBytes);
                }
            }

            protected override void OnPointerPressed(PointerPressedEventArgs e)
            {
                base.OnPointerPressed(e);

                int index = (int)(e.GetPosition(this).Y / RowHeight);

                if (e.ClickCount == 2 && index >= 0 && index < _items.Count)
                {
                    PathActivated?.Invoke(_items[index].Path, _items[index].IsFile);
                    e.Handled = true;
                }
            }

            private void DrawItem(DrawingContext context, ComparisonItem item, int index, double top, long maximumSizeBytes)
            {
                const double horizontalPadding = 8;
                const double valueWidth = 112;
                const double deltaWidth = 96;
                const double captionWidth = 72;
                const double titleHeight = 22;
                const double barHeight = 16;
                double contentWidth = Math.Floor(Bounds.Width);
                double barLeft = horizontalPadding + captionWidth;
                double barWidth = Math.Max(20, contentWidth - horizontalPadding - valueWidth - deltaWidth - barLeft);
                double beforeTop = top + titleHeight + 2;
                double afterTop = beforeTop + 22;

                if (index % 2 == 1)
                {
                    context.FillRectangle(Resource("BackgroundSecondaryBrush"), new Rect(0, top, Math.Max(0, contentWidth - 1), RowHeight - 1));
                }

                DrawTextLine(context, item.Path, new Rect(horizontalPadding, top + 1, Math.Max(0, contentWidth - horizontalPadding * 2), titleHeight), Foreground);
                DrawBarLine(context, LocalizationService.GetText("ScanHistory.OverviewBefore"), item.BaselineSizeBytes, beforeTop, maximumSizeBytes, barLeft, barWidth, valueWidth, BeforeBrush);
                DrawBarLine(context, LocalizationService.GetText("ScanHistory.OverviewAfter"), item.CompareSizeBytes, afterTop, maximumSizeBytes, barLeft, barWidth, valueWidth, Resource("AccentBrush"));

                string deltaText = item.BaselineSizeBytes == 0 && item.CompareSizeBytes > 0
                    ? LocalizationService.GetText("ScanHistory.OverviewNew")
                    : FormatSignedSize(item.DeltaBytes);
                // TextFormatFlags.Right: right aligned inside the padded box.
                double deltaTextWidth = MeasureWidth(deltaText);
                DrawTextLine(context, deltaText, new Rect(contentWidth - horizontalPadding - deltaTextWidth, afterTop, deltaTextWidth, barHeight), Foreground);
            }

            private void DrawBarLine(DrawingContext context, string caption, long sizeBytes, double top, long maximumSizeBytes, double barLeft, double barWidth, double valueWidth, IBrush fill)
            {
                DrawTextLine(context, caption, new Rect(8, top, barLeft - 12, 16), Foreground);
                context.FillRectangle(TrackBrush, new Rect(barLeft, top, barWidth, 16));

                double ratio = Math.Clamp((double)sizeBytes / maximumSizeBytes, 0, 1);
                context.FillRectangle(fill, new Rect(barLeft, top, Math.Round(barWidth * ratio), 16));
                DrawTextLine(context, SizeFormatter.Format(sizeBytes), new Rect(barLeft + barWidth + 6, top, valueWidth - 6, 16), Foreground);
            }
        }
    }
}
