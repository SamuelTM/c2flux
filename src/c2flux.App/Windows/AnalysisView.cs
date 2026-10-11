using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace c2flux
{
    // Port of the WinForms AdvancedFeaturesForm as the main window embeds it
    // ("Analysis"): the files of a scan by extension and by type, the 1000
    // largest, and the redundant copies, found in the background the first
    // time that tab opens.
    public sealed class AnalysisView : DockPanel
    {
        internal sealed record SizeRow(string Name, double Percent, long SizeBytes);

        internal sealed record LargestFileRow(string Name, double Percent, long SizeBytes, DateTime LastWriteTime, string FullPath);

        internal sealed class RedundancyRow
        {
            public string Name { get; init; }
            public double Percent { get; init; }
            public int Count { get; init; }
            public long SizeBytes { get; init; }
            public long TotalSizeBytes { get; init; }
            public bool IsLocation { get; init; }
            public RedundancyRow Group { get; init; }
            public List<RedundancyRow> Children { get; } = new List<RedundancyRow>();
        }

        private enum SizeUnit
        {
            Bytes,
            KB,
            MB,
            GB,
            TB,
        }

        private readonly FileSystemEntry _root;
        private readonly AnalysisTabs _tabs;
        private readonly Panel _pages = new Panel();
        private readonly DrawnTable<SizeRow> _extensions = new DrawnTable<SizeRow>();
        private readonly DrawnTable<SizeRow> _categories = new DrawnTable<SizeRow>();
        private readonly DrawnTable<LargestFileRow> _largest = new DrawnTable<LargestFileRow>();
        private readonly DrawnTable<RedundancyRow> _redundancies = new DrawnTable<RedundancyRow>();
        private readonly TableColumn<LargestFileRow> _unitColumn;
        private readonly RedundancyProgress _progress = new RedundancyProgress();
        private readonly List<RedundancyRow> _groups = new List<RedundancyRow>();
        private readonly HashSet<RedundancyRow> _expanded = new HashSet<RedundancyRow>();
        private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();
        private SizeUnit _sizeUnit = SizeUnit.MB;
        private bool _redundanciesStarted;
        private RedundancyAnalysisPhase _shownPhase = RedundancyAnalysisPhase.SizeGrouping;
        private RedundancyAnalysisPhase _pendingPhase = RedundancyAnalysisPhase.SizeGrouping;
        private DateTime _pendingPhaseSince = DateTime.MinValue;

        public AnalysisView(FileSystemEntry root)
        {
            _root = root;
            _tabs = new AnalysisTabs(new[] { "Advanced.FileTypes", "Advanced.FileCategories", "Advanced.LargestFiles", "Advanced.Redundancies" }.Select(LocalizationService.GetText).ToArray());
            _tabs.SelectedChanged += ShowPage;
            DockPanel.SetDock(_tabs, Dock.Top);
            Children.Add(_tabs);
            // The tab page's inset, measured.
            _pages.Margin = new Thickness(7, 0, 3, 3);
            Children.Add(_pages);

            CreateSizeColumns(_extensions, "Advanced.FileType", 0.14);
            CreateSizeColumns(_categories, "Advanced.FileCategories", 0.24);

            _largest.Columns.Add(new TableColumn<LargestFileRow>(LocalizationService.GetText("Common.Name"), row => row.Name) { Width = 120, Percent = 0.22 });
            _largest.Columns.Add(PercentColumn<LargestFileRow>(row => row.Percent));
            _largest.Columns.Add(new TableColumn<LargestFileRow>(LocalizationService.GetText("Advanced.SizeGb"), row => SizeFormatter.Format(row.SizeBytes)) { AutoWidth = true, Alignment = HorizontalAlignment.Right, SortKey = row => row.SizeBytes });
            _unitColumn = new TableColumn<LargestFileRow>(UnitHeader(), row => FormatInUnit(row.SizeBytes)) { AutoWidth = true, Alignment = HorizontalAlignment.Right, SortKey = row => row.SizeBytes };
            _largest.Columns.Add(_unitColumn);
            _largest.Columns.Add(new TableColumn<LargestFileRow>(LocalizationService.GetText("Advanced.Modified"), row => row.LastWriteTime == DateTime.MinValue ? string.Empty : row.LastWriteTime.ToString("g", CultureInfo.CurrentCulture)) { AutoWidth = true, SortKey = row => row.LastWriteTime });
            _largest.Columns.Add(new TableColumn<LargestFileRow>(LocalizationService.GetText("Common.Path"), row => row.FullPath) { Width = 180 });
            // A click on the size header also steps its unit (B, KB, MB, GB, TB).
            _largest.SortChanged += (column, _) =>
            {
                if (column == _unitColumn)
                {
                    _sizeUnit = _sizeUnit == SizeUnit.TB ? SizeUnit.Bytes : _sizeUnit + 1;
                    _unitColumn.Title = UnitHeader();
                    _largest.RefreshColumns();
                }
            };
            _largest.RowActivated += row => Reveal(row.FullPath);
            _largest.RowPressed += (row, e) =>
            {
                if (e.GetCurrentPoint(_largest).Properties.IsRightButtonPressed)
                {
                    OpenParentMenu(_largest, row.FullPath);
                }
            };

            CreateRedundancyColumns();
            DockPanel redundancyPage = new DockPanel();
            DockPanel.SetDock(_progress, Dock.Top);
            redundancyPage.Children.Add(_progress);
            redundancyPage.Children.Add(_redundancies);

            _pages.Children.Add(_extensions);
            _pages.Children.Add(_categories);
            _pages.Children.Add(_largest);
            _pages.Children.Add(redundancyPage);
            RefreshData();
            ShowPage(0);
        }

        internal void ShowPageForTest(int index) => ShowPage(index);

        // Cancels a running redundancy analysis (the scan was replaced).
        public void Cancel() => _cancellation.Cancel();

        private void ShowPage(int index)
        {
            for (int page = 0; page < _pages.Children.Count; page++)
            {
                _pages.Children[page].IsVisible = page == index;
            }

            _tabs.Selected = index;

            if (index == 3)
            {
                _ = LoadRedundanciesAsync();
            }
        }

        // ----- columns ----------------------------------------------------

        // Name (a share of the width), usage, GB and MB, then free space:
        // AntdUI sizes "auto" columns to their text and leaves the rest empty.
        private void CreateSizeColumns(DrawnTable<SizeRow> table, string titleKey, double share)
        {
            table.Columns.Add(new TableColumn<SizeRow>(LocalizationService.GetText(titleKey), row => row.Name) { Width = 80, Percent = share });
            table.Columns.Add(PercentColumn<SizeRow>(row => row.Percent));
            table.Columns.Add(new TableColumn<SizeRow>(LocalizationService.GetText("Advanced.SizeGb"), row => FormatGb(row.SizeBytes)) { AutoWidth = true, Alignment = HorizontalAlignment.Right, SortKey = row => row.SizeBytes });
            table.Columns.Add(new TableColumn<SizeRow>(LocalizationService.GetText("Advanced.SizeMb"), row => (row.SizeBytes / (1024D * 1024D)).ToString("N0", CultureInfo.CurrentCulture) + " MB") { AutoWidth = true, Alignment = HorizontalAlignment.Right, SortKey = row => row.SizeBytes });
            table.Columns.Add(Filler<SizeRow>());
        }

        private void CreateRedundancyColumns()
        {
            TableColumn<RedundancyRow> name = new TableColumn<RedundancyRow>(LocalizationService.GetText("Common.Name"), row => row.Name) { Width = 120, Percent = 0.42 };
            name.Paint = (context, cell, row, selected) => PaintRedundancyName(context, cell, row, selected);
            _redundancies.Columns.Add(name);
            TableColumn<RedundancyRow> usage = PercentColumn<RedundancyRow>(row => row.Percent);
            usage.Paint = (context, cell, row, selected) =>
            {
                if (!row.IsLocation)
                {
                    TableCells.Percent(this, context, cell, row.Percent, selected);
                }
            };
            _redundancies.Columns.Add(usage);
            _redundancies.Columns.Add(new TableColumn<RedundancyRow>(LocalizationService.GetText("Advanced.Count"), row => row.IsLocation ? string.Empty : row.Count.ToString(CultureInfo.CurrentCulture)) { AutoWidth = true, Alignment = HorizontalAlignment.Right, SortKey = row => row.Count });
            _redundancies.Columns.Add(new TableColumn<RedundancyRow>(LocalizationService.GetText("Advanced.SizeGb"), row => row.IsLocation ? string.Empty : FormatGb(row.SizeBytes)) { AutoWidth = true, Alignment = HorizontalAlignment.Right, SortKey = row => row.SizeBytes });
            _redundancies.Columns.Add(new TableColumn<RedundancyRow>(LocalizationService.GetText("ScanHistory.TotalSize") + " (GB)", row => row.IsLocation ? string.Empty : FormatGb(row.TotalSizeBytes)) { AutoWidth = true, Alignment = HorizontalAlignment.Right, SortKey = row => row.TotalSizeBytes });
            _redundancies.Columns.Add(Filler<RedundancyRow>());

            // Sorting keeps each group's locations right below it.
            foreach (TableColumn<RedundancyRow> column in _redundancies.Columns)
            {
                Func<RedundancyRow, IComparable> key = column.SortKey ?? (row => column.Text(row));
                column.SortKey = row => new GroupedKey(key(row.Group ?? row), row.Group == null ? -1 : (row.Group.Children.IndexOf(row)));
            }

            _redundancies.RowPressed += (row, e) =>
            {
                PointerPoint point = e.GetCurrentPoint(_redundancies);

                if (point.Properties.IsRightButtonPressed && row.IsLocation)
                {
                    OpenParentMenu(_redundancies, row.Name);
                }
                else if (point.Properties.IsLeftButtonPressed && !row.IsLocation && point.Position.X < 40)
                {
                    ToggleGroup(row);
                }
            };
            _redundancies.RowActivated += row =>
            {
                if (!row.IsLocation)
                {
                    ToggleGroup(row);
                }
            };
        }

        private TableColumn<TRow> PercentColumn<TRow>(Func<TRow, double> percent)
        {
            return new TableColumn<TRow>(LocalizationService.GetText("Advanced.Usage"), row => percent(row).ToString("0.0", CultureInfo.CurrentCulture))
            {
                Width = 116,
                Alignment = HorizontalAlignment.Center,
                SortKey = row => percent(row),
                Paint = (context, cell, row, selected) => TableCells.Percent(this, context, cell, percent(row), selected),
            };
        }

        private TableColumn<TRow> Filler<TRow>()
        {
            TableColumn<TRow> filler = new TableColumn<TRow>(string.Empty, _ => string.Empty) { Width = 0, Sortable = false };
            filler.Paint = (_, _, _, _) => { };
            return filler;
        }

        // The expand box of a group (AntdUI tree column), or a location
        // indented under it.
        private void PaintRedundancyName(DrawingContext context, Rect cell, RedundancyRow row, bool selected)
        {
            IBrush foreground = selected ? Brushes.White : this.FindResource(ActualThemeVariant, "TextPrimaryBrush") as IBrush ?? Brushes.White;

            if (!row.IsLocation)
            {
                Rect box = new Rect(cell.X + 6, Math.Round(cell.Center.Y - 7), 14, 14);
                context.DrawRectangle(new SolidColorBrush(Color.FromRgb(20, 20, 20)), new Pen(new SolidColorBrush(Color.FromRgb(66, 66, 66)), 1), new RoundedRect(box.Deflate(0.5), 3));
                Pen chevron = new Pen(foreground, 1.5, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
                StreamGeometry glyph = new StreamGeometry();

                using (StreamGeometryContext path = glyph.Open())
                {
                    if (_expanded.Contains(row))
                    {
                        path.BeginFigure(new Point(box.X + 4, box.Y + 5.5), false);
                        path.LineTo(new Point(box.Center.X, box.Y + 9));
                        path.LineTo(new Point(box.Right - 4, box.Y + 5.5));
                    }
                    else
                    {
                        path.BeginFigure(new Point(box.X + 5.5, box.Y + 4), false);
                        path.LineTo(new Point(box.X + 9, box.Center.Y));
                        path.LineTo(new Point(box.X + 5.5, box.Bottom - 4));
                    }

                    path.EndFigure(false);
                }

                context.DrawGeometry(null, chevron, glyph);
            }

            double left = cell.X + (row.IsLocation ? 52 : 36);
            FormattedText text = new FormattedText(row.Name ?? string.Empty, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default), 12, foreground)
            {
                MaxTextWidth = Math.Max(1, cell.Right - left - 8),
                MaxLineCount = 1,
                Trimming = TextTrimming.CharacterEllipsis,
            };
            context.DrawText(text, new Point(left, Math.Round(cell.Center.Y - text.Height / 2)));
        }

        // ----- data -------------------------------------------------------

        private void RefreshData()
        {
            List<FileSystemEntry> files = GetFiles();
            long total = files.Sum(file => file.SizeBytes);
            double Share(long bytes) => total > 0 ? bytes * 100D / total : 0D;

            _extensions.SetItems(files
                .GroupBy(file => string.IsNullOrWhiteSpace(Path.GetExtension(file.Name)) ? LocalizationService.GetText("Advanced.NoExtension") : Path.GetExtension(file.Name).ToLowerInvariant())
                .Select(group => new SizeRow(group.Key, Share(group.Sum(file => file.SizeBytes)), group.Sum(file => file.SizeBytes)))
                .OrderByDescending(row => row.SizeBytes));
            _categories.SetItems(files
                .GroupBy(file => FileTypeCategories.GetCategoryKey(file.Name))
                .Select(group => new SizeRow(LocalizationService.GetText(group.Key), Share(group.Sum(file => file.SizeBytes)), group.Sum(file => file.SizeBytes)))
                .OrderByDescending(row => row.SizeBytes));
            _largest.SetItems(files
                .OrderByDescending(file => file.SizeBytes)
                .Take(1000)
                .Select(file => new LargestFileRow(file.Name, Share(file.SizeBytes), file.SizeBytes, file.LastWriteTimeUtc == DateTime.MinValue ? DateTime.MinValue : file.LastWriteTimeUtc.ToLocalTime(), file.FullPath)));
        }

        // The scan's flat file list, or the tree's files.
        private List<FileSystemEntry> GetFiles()
        {
            if (_root.AllFiles.Count > 0)
            {
                lock (_root.AllFiles)
                {
                    return _root.AllFiles.Where(file => file != null && !file.IsDirectory).ToList();
                }
            }

            List<FileSystemEntry> files = new List<FileSystemEntry>();
            Stack<FileSystemEntry> pending = new Stack<FileSystemEntry>();
            pending.Push(_root);

            while (pending.Count > 0)
            {
                foreach (FileSystemEntry child in pending.Pop().Children)
                {
                    if (child.IsDirectory)
                    {
                        pending.Push(child);
                    }
                    else
                    {
                        files.Add(child);
                    }
                }
            }

            return files;
        }

        // Once per view: groups arrive while the hashes are read, sorted by
        // their total size.
        private async Task LoadRedundanciesAsync()
        {
            if (_redundanciesStarted)
            {
                return;
            }

            _redundanciesStarted = true;
            ConcurrentQueue<RedundancyAnalysisGroup> pending = new ConcurrentQueue<RedundancyAnalysisGroup>();
            List<FileSystemEntry> files = GetFiles();
            long total = files.Sum(file => file.SizeBytes);
            DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            timer.Tick += (_, _) => Flush(pending, total);
            _progress.Set(0, PhaseText(RedundancyAnalysisPhase.SizeGrouping));
            _pendingPhaseSince = DateTime.UtcNow;
            Progress<RedundancyAnalysisProgressInfo> analysisProgress = new Progress<RedundancyAnalysisProgressInfo>(info =>
            {
                if (info == null)
                {
                    return;
                }

                UpdatePhase(info.Phase);
                int percent = Math.Clamp(info.Percentage, 0, 100);
                _progress.Set(percent / 100D, PhaseText(_shownPhase), percent);
            });
            timer.Start();

            try
            {
                await Task.Run(() => RedundancyAnalysisService.Analyze(files, _cancellation.Token, new QueueProgress(pending), analysisProgress), _cancellation.Token);
                Flush(pending, total);
                _progress.Set(1, PhaseText(RedundancyAnalysisPhase.Completed), 100);
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                timer.Stop();
            }
        }

        private void Flush(ConcurrentQueue<RedundancyAnalysisGroup> pending, long total)
        {
            bool added = false;

            while (pending.TryDequeue(out RedundancyAnalysisGroup group))
            {
                RedundancyRow row = new RedundancyRow
                {
                    Name = group.Name,
                    Percent = total > 0 ? group.TotalSizeBytes * 100D / total : 0D,
                    Count = group.PhysicalCopyCount,
                    SizeBytes = group.SizeBytes,
                    TotalSizeBytes = group.TotalSizeBytes,
                };

                foreach (string location in group.Locations)
                {
                    row.Children.Add(new RedundancyRow { Name = location, IsLocation = true, Group = row });
                }

                // Largest total first; equal totals keep their order.
                int index = _groups.FindIndex(existing => existing.TotalSizeBytes < row.TotalSizeBytes);
                _groups.Insert(index < 0 ? _groups.Count : index, row);
                added = true;
            }

            if (added)
            {
                BindRedundancies();
            }
        }

        private void ToggleGroup(RedundancyRow row)
        {
            if (!_expanded.Remove(row))
            {
                _expanded.Add(row);
            }

            BindRedundancies();
        }

        private void BindRedundancies()
        {
            _redundancies.SetItems(_groups.SelectMany(group => _expanded.Contains(group) ? group.Children.Prepend(group) : new[] { group }));
        }

        // Read phases (first, last block, full hash) show as one; a new phase
        // shows after a second, so the text does not flicker.
        private void UpdatePhase(RedundancyAnalysisPhase phase)
        {
            if (phase == RedundancyAnalysisPhase.Completed)
            {
                _shownPhase = _pendingPhase = phase;
                return;
            }

            RedundancyAnalysisPhase display = phase is RedundancyAnalysisPhase.FirstBlock or RedundancyAnalysisPhase.LastBlock ? RedundancyAnalysisPhase.FullHashLive : phase;
            bool isRead = display is RedundancyAnalysisPhase.FullHashLive or RedundancyAnalysisPhase.FullHashCache;
            bool pendingIsRead = _pendingPhase is RedundancyAnalysisPhase.FullHashLive or RedundancyAnalysisPhase.FullHashCache;

            if (!isRead && pendingIsRead)
            {
                return;
            }

            if (display != _pendingPhase)
            {
                _pendingPhase = display;
                _pendingPhaseSince = DateTime.UtcNow;
                return;
            }

            if (_shownPhase != _pendingPhase && DateTime.UtcNow - _pendingPhaseSince >= TimeSpan.FromSeconds(1))
            {
                _shownPhase = _pendingPhase;
            }
        }

        private static string PhaseText(RedundancyAnalysisPhase phase)
        {
            return LocalizationService.GetText(phase switch
            {
                RedundancyAnalysisPhase.SizeGrouping => "Advanced.Redundancy.Progress.SizeGrouping",
                RedundancyAnalysisPhase.FirstBlock or RedundancyAnalysisPhase.LastBlock or RedundancyAnalysisPhase.FullHashLive => "Advanced.Redundancy.Progress.LiveRead",
                RedundancyAnalysisPhase.FullHashCache => "Advanced.Redundancy.Progress.CacheRead",
                RedundancyAnalysisPhase.FileIdentity => "Advanced.Redundancy.Progress.FileIdentity",
                RedundancyAnalysisPhase.Cache => "Advanced.Redundancy.Progress.CacheSave",
                RedundancyAnalysisPhase.Completed => "Advanced.Redundancy.Progress.Completed",
                _ => "Advanced.Redundancies",
            });
        }

        // ----- helpers ----------------------------------------------------

        private string UnitHeader() => $"{LocalizationService.GetText("Common.Size")} ({_sizeUnit})";

        private string FormatInUnit(long bytes)
        {
            return _sizeUnit switch
            {
                SizeUnit.Bytes => bytes.ToString("N0", CultureInfo.CurrentCulture),
                SizeUnit.MB => (bytes / (1024D * 1024D)).ToString("N0", CultureInfo.CurrentCulture) + " MB",
                _ => (bytes / Math.Pow(1024, (int)_sizeUnit)).ToString("N2", CultureInfo.CurrentCulture),
            };
        }

        private static string FormatGb(long bytes) => (bytes / (1024D * 1024D * 1024D)).ToString("N2", CultureInfo.CurrentCulture) + " GB";

        private static void Reveal(string path)
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                if (File.Exists(path))
                {
                    FileManager.Reveal(path);
                }
                else
                {
                    FileManager.Open(path);
                }
            }
        }

        private static void OpenParentMenu(Control target, string path)
        {
            MenuItem open = new MenuItem { Header = LocalizationService.GetText("Search.OpenParentFolder") };
            open.Click += (_, _) => Reveal(path);
            new ContextMenu { ItemsSource = new[] { open } }.Open(target);
        }

        private sealed class QueueProgress : IProgress<RedundancyAnalysisGroup>
        {
            private readonly ConcurrentQueue<RedundancyAnalysisGroup> _queue;

            public QueueProgress(ConcurrentQueue<RedundancyAnalysisGroup> queue) => _queue = queue;

            public void Report(RedundancyAnalysisGroup value)
            {
                if (value != null)
                {
                    _queue.Enqueue(value);
                }
            }
        }

        // Sorts by the group's value, then keeps the group before its
        // locations in their order.
        private readonly record struct GroupedKey(IComparable Value, int Position) : IComparable
        {
            public int CompareTo(object other)
            {
                GroupedKey key = (GroupedKey)other;
                int compared = Value == null ? (key.Value == null ? 0 : -1) : Value.CompareTo(key.Value);
                return compared != 0 ? compared : Position.CompareTo(key.Position);
            }
        }

        // AntdUI.Progress of the redundancy tab: a 10 px track with the
        // percentage and phase centered.
        private sealed class RedundancyProgress : DrawnControl
        {
            private double _value;
            private string _text = string.Empty;

            public RedundancyProgress()
            {
                Height = 28;
            }

            public void Set(double value, string phase, int percent = 0)
            {
                _value = Math.Clamp(value, 0, 1);
                _text = $"{percent} % ({phase})";
                InvalidateVisual();
            }

            public override void Render(DrawingContext context)
            {
                Rect track = new Rect(0, Math.Round((Bounds.Height - 10) / 2), Bounds.Width, 10);
                context.DrawRectangle(Resource("TableProgressBackBrush"), null, new RoundedRect(track, 4));

                if (_value > 0)
                {
                    context.DrawRectangle(Resource("TableProgressFillBrush"), null, new RoundedRect(track.WithWidth(Math.Max(8, track.Width * _value)), 4));
                }

                FormattedText text = CreateText(_text, Foreground);
                context.DrawText(text, new Point(Math.Round((Bounds.Width - text.Width) / 2), Math.Round((Bounds.Height - text.Height) / 2)));
            }
        }

        // AntdUI.Tabs, card style: 146 px tabs, the selected one filled with
        // the accent, a 1 px accent line below.
        private sealed class AnalysisTabs : DrawnControl
        {
            private const double TabWidth = 146;
            private const double FirstTabLeft = 9;
            private readonly string[] _titles;
            private int _selected;

            public AnalysisTabs(string[] titles)
            {
                _titles = titles;
                Height = 31;
                Cursor = new Cursor(StandardCursorType.Hand);
            }

            public event Action<int> SelectedChanged;

            public int Selected
            {
                get => _selected;
                set
                {
                    _selected = value;
                    InvalidateVisual();
                }
            }

            public override void Render(DrawingContext context)
            {
                IBrush accent = Resource("AccentBrush");
                Pen border = new Pen(Resource("FieldBorderBrush"), 2);

                for (int index = 0; index < _titles.Length; index++)
                {
                    Rect tab = new Rect(FirstTabLeft + index * TabWidth, 2, TabWidth - 1, 28);
                    StreamGeometry shape = TopRounded(tab, 6);

                    if (index == _selected)
                    {
                        context.DrawGeometry(accent, null, shape);
                    }
                    else
                    {
                        context.DrawGeometry(null, border, shape);
                    }

                    FormattedText title = CreateText(_titles[index], index == _selected ? Brushes.White : Foreground);
                    context.DrawText(title, new Point(Math.Round(tab.Center.X - title.Width / 2), Math.Round(tab.Center.Y - title.Height / 2)));
                }

                context.FillRectangle(accent, new Rect(7, Bounds.Height - 1, Math.Max(0, Bounds.Width - 10), 1));
            }

            protected override void OnPointerPressed(PointerPressedEventArgs e)
            {
                base.OnPointerPressed(e);
                int index = (int)Math.Floor((e.GetPosition(this).X - FirstTabLeft) / TabWidth);

                if (index >= 0 && index < _titles.Length && index != _selected)
                {
                    SelectedChanged?.Invoke(index);
                }
            }

            private static StreamGeometry TopRounded(Rect rect, double radius)
            {
                StreamGeometry geometry = new StreamGeometry();

                using (StreamGeometryContext path = geometry.Open())
                {
                    path.BeginFigure(rect.BottomLeft, true);
                    path.LineTo(new Point(rect.X, rect.Y + radius));
                    path.ArcTo(new Point(rect.X + radius, rect.Y), new Size(radius, radius), 0, false, SweepDirection.Clockwise);
                    path.LineTo(new Point(rect.Right - radius, rect.Y));
                    path.ArcTo(new Point(rect.Right, rect.Y + radius), new Size(radius, radius), 0, false, SweepDirection.Clockwise);
                    path.LineTo(rect.BottomRight);
                    path.EndFigure(true);
                }

                return geometry;
            }
        }
    }
}
