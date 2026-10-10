using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace c2flux
{
    // Port of the WinForms Chart_Treemap: the path of the folder shown, a
    // table of its entries and, below a splitter, the treemap. Clicking a
    // tile selects it in the table; double clicking a folder zooms into it.
    public sealed class TreemapView : UserControl
    {
        private const double TreemapHeightPercent = 28;

        private readonly TextBlock _breadcrumb = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        private readonly DrawnTable<Row> _table = new DrawnTable<Row>();
        private readonly Treemap _treemap = new Treemap();
        private readonly Grid _layout;
        private FileSystemEntry _entry;
        private FileSystemEntry _rootEntry;
        private FileSystemEntry _tableDirectoryEntry;
        private string _selectedTableEntryPath;
        private string _suppressedSetEntryPath;

        public TreemapView()
        {
            _table.Columns.Add(new TableColumn<Row>(LocalizationService.GetText("Common.Name"), row => row.Entry.Name) { Width = 120 });
            _table.Columns.Add(new TableColumn<Row>(LocalizationService.GetText("Common.Size"), row => SizeFormatter.Format(row.Entry.SizeBytes))
            {
                Width = 90,
                Alignment = HorizontalAlignment.Right,
                SortKey = row => row.Entry.SizeBytes,
            });
            _table.Columns.Add(new TableColumn<Row>(LocalizationService.GetText("Chart.TableUsage"), row => row.Percent.ToString("0.0", CultureInfo.CurrentCulture))
            {
                Width = 116,
                Alignment = HorizontalAlignment.Center,
                SortKey = row => row.Percent,
                Paint = (context, cell, row, selected) => TableCells.Percent(this, context, cell, row.Percent, selected),
            });
            _table.Columns.Add(new TableColumn<Row>(LocalizationService.GetText("Common.Path"), row => row.Entry.FullPath) { Width = 180 });
            _table.RowPressed += (row, e) =>
            {
                if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
                {
                    RevealMenu.Show(this, row.Entry);
                }
            };

            _treemap.EntryActivated += OnTreemapEntryActivated;
            _treemap.DirectoryZoomRequested += OnDirectoryZoomRequested;

            Border breadcrumbBar = new Border { Height = 24, Padding = new Thickness(6, 0), Child = _breadcrumb };
            breadcrumbBar.Bind(Border.BackgroundProperty, breadcrumbBar.GetResourceObservable("BackgroundSecondaryBrush"));
            DockPanel upper = new DockPanel();
            DockPanel.SetDock(breadcrumbBar, Dock.Top);
            upper.Children.Add(breadcrumbBar);
            upper.Children.Add(_table);

            GridSplitter splitter = new GridSplitter { ResizeDirection = GridResizeDirection.Rows, MinHeight = 4 };
            splitter.Bind(GridSplitter.BackgroundProperty, splitter.GetResourceObservable("BorderBrush"));

            _layout = new Grid { RowDefinitions = new RowDefinitions("*,4,120") };
            Grid.SetRow(splitter, 1);
            Grid.SetRow(_treemap, 2);
            _layout.Children.Add(upper);
            _layout.Children.Add(splitter);
            _layout.Children.Add(_treemap);
            Content = _layout;
        }

        // Left click on a tile, or a zoom into a folder.
        public event Action<FileSystemEntry> EntryActivated;

        public void SetRootEntry(FileSystemEntry rootEntry)
        {
            _rootEntry = rootEntry;
            _treemap.SetRootEntry(rootEntry);
            RefreshCurrentContext();
        }

        // A folder shows itself; a file shows its folder with the file
        // selected. Ignored once right after this control reported entry.
        public void SetEntry(FileSystemEntry entry)
        {
            if (entry == null)
            {
                _entry = null;
                _tableDirectoryEntry = null;
                _selectedTableEntryPath = null;
                _suppressedSetEntryPath = null;
                RefreshCurrentContext();
                return;
            }

            if (_suppressedSetEntryPath != null && PathsEqual(entry.FullPath, _suppressedSetEntryPath))
            {
                _suppressedSetEntryPath = null;
                return;
            }

            if (entry.IsDirectory)
            {
                _entry = entry;
                _tableDirectoryEntry = entry;
                _selectedTableEntryPath = null;
            }
            else
            {
                FileSystemEntry parent = FindParentDirectory(entry);
                _entry = parent ?? _entry;
                _tableDirectoryEntry = parent;
                _selectedTableEntryPath = entry.FullPath;
            }

            RefreshCurrentContext();
        }

        // WinForms keeps the treemap at 28 % of the height on every resize.
        protected override void OnSizeChanged(SizeChangedEventArgs e)
        {
            base.OnSizeChanged(e);
            double height = Math.Max(120, Math.Floor(e.NewSize.Height * TreemapHeightPercent / 100));
            _layout.RowDefinitions[2].Height = new GridLength(Math.Min(height, Math.Max(0, e.NewSize.Height - 80 - 4)));
        }

        private void RefreshCurrentContext()
        {
            _breadcrumb.Text = _tableDirectoryEntry?.FullPath ?? _entry?.FullPath ?? string.Empty;
            PopulateTable();
            _treemap.SetRootEntry(_rootEntry);
            _treemap.SetEntry(_entry);
        }

        private void PopulateTable()
        {
            if (_tableDirectoryEntry == null)
            {
                _table.SetItems(null);
                return;
            }

            double parentSize = Math.Max(0, _tableDirectoryEntry.SizeBytes);
            List<Row> rows = GetImmediateEntries(_tableDirectoryEntry)
                .OrderByDescending(item => item.SizeBytes)
                .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(item => new Row(item, parentSize <= 0 ? 0 : item.SizeBytes / parentSize * 100))
                .ToList();

            _table.SetItems(rows);
            _table.Select(_selectedTableEntryPath == null ? null : rows.FirstOrDefault(row => PathsEqual(row.Entry.FullPath, _selectedTableEntryPath)));
        }

        // Children, plus files a scan in progress has listed in the root's
        // AllFiles but not yet attached to this folder.
        private IEnumerable<FileSystemEntry> GetImmediateEntries(FileSystemEntry directory)
        {
            HashSet<string> yielded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<FileSystemEntry> children;

            lock (directory.Children)
            {
                children = new List<FileSystemEntry>(directory.Children);
            }

            foreach (FileSystemEntry child in children)
            {
                if (!string.IsNullOrWhiteSpace(child?.FullPath) && yielded.Add(NormalizePath(child.FullPath)))
                {
                    yield return child;
                }
            }

            FileSystemEntry fileSourceRoot = _rootEntry ?? directory;
            List<FileSystemEntry> allFiles;

            lock (fileSourceRoot.AllFiles)
            {
                allFiles = new List<FileSystemEntry>(fileSourceRoot.AllFiles);
            }

            foreach (FileSystemEntry file in allFiles)
            {
                if (file != null && !file.IsDirectory && !string.IsNullOrWhiteSpace(file.FullPath) &&
                    PathsEqual(Path.GetDirectoryName(file.FullPath), directory.FullPath) &&
                    yielded.Add(NormalizePath(file.FullPath)))
                {
                    yield return file;
                }
            }
        }

        private FileSystemEntry FindParentDirectory(FileSystemEntry file)
        {
            string parentPath = string.IsNullOrWhiteSpace(file.FullPath) ? null : Path.GetDirectoryName(file.FullPath);
            return _rootEntry == null || string.IsNullOrWhiteSpace(parentPath) ? null : FindDirectory(_rootEntry, parentPath);
        }

        private static FileSystemEntry FindDirectory(FileSystemEntry entry, string fullPath)
        {
            if (entry.IsDirectory && PathsEqual(entry.FullPath, fullPath))
            {
                return entry;
            }

            List<FileSystemEntry> children;

            lock (entry.Children)
            {
                children = new List<FileSystemEntry>(entry.Children);
            }

            return children
                .Where(child => child.IsDirectory && EntryTreeCanvas.IsSameOrDescendantPath(fullPath, child.FullPath))
                .Select(child => FindDirectory(child, fullPath))
                .FirstOrDefault(found => found != null);
        }

        private void OnTreemapEntryActivated(FileSystemEntry entry)
        {
            if (entry.IsDirectory)
            {
                _tableDirectoryEntry = entry;
                _selectedTableEntryPath = null;
            }
            else
            {
                _tableDirectoryEntry = FindParentDirectory(entry) ?? _tableDirectoryEntry;
                _selectedTableEntryPath = entry.FullPath;
            }

            _suppressedSetEntryPath = entry.FullPath;
            PopulateTable();
            _breadcrumb.Text = _tableDirectoryEntry?.FullPath ?? string.Empty;
            EntryActivated?.Invoke(entry);
        }

        private void OnDirectoryZoomRequested(FileSystemEntry entry)
        {
            _entry = entry;
            _tableDirectoryEntry = entry;
            _selectedTableEntryPath = null;
            _suppressedSetEntryPath = entry.FullPath;
            RefreshCurrentContext();
            EntryActivated?.Invoke(entry);
        }

        private static bool PathsEqual(string left, string right) =>
            string.Equals(NormalizePath(left), NormalizePath(right), StringComparison.OrdinalIgnoreCase);

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            string native = EntryPaths.ToNativeSeparators(path.Trim());

            if (native.Length == 3 && native[1] == ':' && native[2] == '\\')
            {
                return char.ToUpperInvariant(native[0]) + @":\";
            }

            string trimmed = native.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return trimmed.Length == 0 ? native : trimmed;
        }

        private sealed class Row
        {
            public Row(FileSystemEntry entry, double percent)
            {
                Entry = entry;
                Percent = percent;
            }

            public FileSystemEntry Entry { get; }
            public double Percent { get; }
        }
    }
}
