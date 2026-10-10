using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Layout;

namespace c2flux
{
    // Port of the WinForms Chart_TableGridChart: the subfolders of an entry
    // (or its 100 largest files) with their share, size and path.
    public sealed class EntryTable : UserControl
    {
        public const string NameColumn = "Name";
        public const string PercentColumn = "Percent";
        public const string SizeColumn = "SizeBytes";
        public const string SizeMbColumn = "SizeMb";
        public const string PathColumn = "FullPath";

        private readonly DrawnTable<Row> _table = new DrawnTable<Row>();
        private readonly Dictionary<string, TableColumn<Row>> _columns;
        private FileSystemEntry _entry;
        private bool _showFiles;

        public EntryTable()
        {
            // AntdUI computed the percentage widths against a tiny control,
            // so in practice every column had its minimum width.
            _columns = new Dictionary<string, TableColumn<Row>>
            {
                [NameColumn] = new TableColumn<Row>(LocalizationService.GetText("Common.Name"), row => row.Entry.Name) { Width = 120 },
                [PercentColumn] = new TableColumn<Row>(LocalizationService.GetText("Chart.TableUsage"), row => row.Percent.ToString("0.0", CultureInfo.CurrentCulture))
                {
                    Width = 116,
                    Alignment = HorizontalAlignment.Center,
                    SortKey = row => row.Percent,
                },
                [SizeColumn] = new TableColumn<Row>(LocalizationService.GetText("Advanced.SizeGb"), row => (row.Entry.SizeBytes / (1024D * 1024D * 1024D)).ToString("N2", CultureInfo.CurrentCulture) + " GB")
                {
                    AutoWidth = true,
                    Alignment = HorizontalAlignment.Right,
                    SortKey = row => row.Entry.SizeBytes,
                },
                [SizeMbColumn] = new TableColumn<Row>(LocalizationService.GetText("Advanced.SizeMb"), row => (row.Entry.SizeBytes / (1024D * 1024D)).ToString("N0", CultureInfo.CurrentCulture) + " MB")
                {
                    AutoWidth = true,
                    Alignment = HorizontalAlignment.Right,
                    SortKey = row => row.Entry.SizeBytes,
                },
                [PathColumn] = new TableColumn<Row>(LocalizationService.GetText("Common.Path"), row => row.Entry.FullPath) { Width = 180 },
            };

            _columns[PercentColumn].Paint = (context, cell, row, selected) => TableCells.Percent(this, context, cell, row.Percent, selected);
            _table.Columns.AddRange(_columns.Values);
            _table.RowPressed += (row, e) =>
            {
                if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
                {
                    RevealMenu.Show(this, row.Entry);
                }
            };
            Content = _table;
        }

        public void SetEntry(FileSystemEntry entry)
        {
            _entry = entry;
            BindRows();
        }

        // The 100 largest files below the entry instead of its subfolders.
        public void SetShowFiles(bool showFiles)
        {
            if (_showFiles != showFiles)
            {
                _showFiles = showFiles;
                BindRows();
            }
        }

        // Size (GB) and Size (MB) are shown and hidden together.
        public void SetColumnVisible(string columnKey, bool visible)
        {
            if (!_columns.TryGetValue(columnKey, out TableColumn<Row> column))
            {
                return;
            }

            column.Visible = visible;

            if (columnKey == SizeColumn)
            {
                _columns[SizeMbColumn].Visible = visible;
            }

            _table.RefreshColumns();
        }

        public void ApplyLocalizedTexts()
        {
            _columns[NameColumn].Title = LocalizationService.GetText("Common.Name");
            _columns[PercentColumn].Title = LocalizationService.GetText("Chart.TableUsage");
            _columns[SizeColumn].Title = LocalizationService.GetText("Advanced.SizeGb");
            _columns[SizeMbColumn].Title = LocalizationService.GetText("Advanced.SizeMb");
            _columns[PathColumn].Title = LocalizationService.GetText("Common.Path");
            _table.RefreshColumns();
        }

        private void BindRows()
        {
            if (_entry == null)
            {
                _table.SetItems(null);
                return;
            }

            List<FileSystemEntry> entries = _showFiles
                ? GetLargestFiles(_entry, 100)
                : _entry.Children.Where(child => child.IsDirectory).ToList();
            long totalSize = entries.Sum(child => child.SizeBytes);

            _table.SetItems(entries
                .Select(child => new Row(child, totalSize <= 0 ? 0 : child.SizeBytes * 100D / totalSize))
                .OrderByDescending(row => row.Entry.SizeBytes));
        }

        internal static List<FileSystemEntry> GetLargestFiles(FileSystemEntry root, int count)
        {
            PriorityQueue<FileSystemEntry, long> largest = new PriorityQueue<FileSystemEntry, long>();
            Stack<FileSystemEntry> pending = new Stack<FileSystemEntry>();
            pending.Push(root);

            while (pending.Count > 0)
            {
                foreach (FileSystemEntry child in pending.Pop().Children)
                {
                    if (child.IsDirectory)
                    {
                        pending.Push(child);
                        continue;
                    }

                    largest.Enqueue(child, child.SizeBytes);

                    if (largest.Count > count)
                    {
                        largest.Dequeue();
                    }
                }
            }

            List<FileSystemEntry> result = new List<FileSystemEntry>(largest.Count);

            while (largest.Count > 0)
            {
                result.Add(largest.Dequeue());
            }

            return result;
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
