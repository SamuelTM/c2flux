using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

namespace c2flux
{
    // Port of the WinForms StorageHistoryForm as the main window shows it
    // (embedded mode): the recorded free or used space of a drive over a
    // range of days, as a table and a chart, with details of each data point.
    public sealed class StorageHistoryView : Grid
    {
        private const string RangeLast7Days = "Last7Days";
        private const string RangeLast14Days = "Last14Days";
        private const string RangeLast30Days = "Last30Days";
        private const string RangeLast90Days = "Last90Days";
        private const string RangeLast365Days = "Last365Days";
        private const string RangeAll = "All";
        private const string RangeCustom = "Custom";
        private static readonly string[] Ranges = { RangeLast7Days, RangeLast14Days, RangeLast30Days, RangeLast90Days, RangeLast365Days, RangeAll, RangeCustom };

        private sealed record PathItem(string Path, string DisplayName)
        {
            public override string ToString() => DisplayName;
        }

        private sealed record RangeItem(string Mode, string Text)
        {
            public override string ToString() => Text;
        }

        internal sealed class Row
        {
            public StorageHistoryRecord Record { get; init; }
            public DateTime DateValue { get; init; }
            public long SizeValue { get; init; }
            public long? ChangeValue { get; init; }
            public string Change { get; init; }
        }

        private readonly AppSettings _settings;
        private readonly ComboBox _paths = Select();
        private readonly ComboBox _displayMode = Select();
        private readonly ComboBox _range = Select();
        private readonly Button _calendar = new Button { Content = new CalendarGlyph(), Padding = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Center, Classes = { "ant", "dialog", "bordered" } };
        private readonly TextBlock _intensityLabel = new TextBlock { Text = LocalizationService.GetText("StorageHistory.Intensity"), VerticalAlignment = VerticalAlignment.Center };
        private readonly Slider _intensity = new Slider { Minimum = 0, Maximum = 100, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0) };
        private readonly TextBlock _intensityValue = new TextBlock { Width = 48, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        private readonly Button _delete = new Button { Content = LocalizationService.GetText("StorageHistory.Delete"), HorizontalContentAlignment = HorizontalAlignment.Center, Classes = { "ant", "dialog", "bordered" } };
        private readonly Grid _header = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,Auto,Auto,Auto,*,Auto,Auto,Auto,Auto"), Margin = new Thickness(8), Height = 36 };
        private readonly DrawnTable<Row> _table = new DrawnTable<Row> { Style = TableStyle.Classic, Responsive = true, ResponsiveReserve = 0, ClassicHeaderHeight = 32, ClassicRowHeight = 28 };
        private readonly TableColumn<Row> _sizeColumn;
        private readonly StorageHistoryChart _chart = new StorageHistoryChart();
        private IReadOnlyList<StorageHistoryRecord> _records = Array.Empty<StorageHistoryRecord>();
        private bool _loading;

        public StorageHistoryView(AppSettings settings)
        {
            _settings = settings;
            RowDefinitions = new RowDefinitions("Auto,*");
            ColumnDefinitions = new ColumnDefinitions("420,4,*");

            _displayMode.ItemsSource = new[] { LocalizationService.GetText("StorageHistory.Used"), LocalizationService.GetText("StorageHistory.Free") };
            _range.ItemsSource = Ranges.Select(mode => new RangeItem(mode, LocalizationService.GetText("StorageHistory.Range." + mode))).ToList();
            _intensity.Value = Math.Clamp(settings.StorageHistoryGradientIntensityPercent, 0, 100);
            _intensityValue.Text = (int)_intensity.Value + "%";

            // Columns as WinForms sizes them: label text + 5, select 200 + 15,
            // the selects as wide as their longest item + 48 (150..240,
            // 190..260) + 8.
            AddHeader(Label("StorageHistory.Drive"), 0, new Thickness(0, 0, 5, 0));
            AddHeader(Sized(_paths, 200), 1, new Thickness(4, 2, 19, 2));
            AddHeader(Label("StorageHistory.Display"), 2, new Thickness(0));
            AddHeader(Sized(_displayMode, FitWidth(_displayMode, 150, 240)), 3, new Thickness(4, 2, 12, 2));
            AddHeader(Sized(_calendar, 40), 4, new Thickness(4, 2, 12, 2));
            AddHeader(Sized(_range, FitWidth(_range, 190, 260)), 5, new Thickness(4, 2, 12, 2));
            AddHeader(_intensityLabel, 7, new Thickness(0));
            AddHeader(Sized(_intensity, 140), 8, new Thickness(4, 2, 12, 2));
            AddHeader(_intensityValue, 9, new Thickness(0));
            AddHeader(Sized(_delete, 112), 10, new Thickness(4, 2, 4, 2));
            Grid.SetColumnSpan(_header, 3);
            Children.Add(_header);
            _header.SizeChanged += (_, _) => FitHeader();

            _table.Columns.Add(new TableColumn<Row>(LocalizationService.GetText("StorageHistory.Date"), row => row.DateValue.ToString("g", CultureInfo.CurrentCulture)) { Percent = 45, SortKey = row => row.DateValue, DescendingFirst = true });
            _sizeColumn = new TableColumn<Row>(string.Empty, row => SizeFormatter.Format(row.SizeValue)) { Percent = 30, SortKey = row => row.SizeValue };
            _table.Columns.Add(_sizeColumn);
            // Rows without a change (the first one) stay last.
            _table.Columns.Add(new TableColumn<Row>(LocalizationService.GetText("StorageHistory.Change"), row => row.Change) { Percent = 25, SortKey = row => row.ChangeValue ?? long.MinValue });
            // SplitContainer panel padding (16, 0, 0, 8) plus the 3 px of its frame.
            _table.Margin = new Thickness(19, 3, 1, 8);
            _table.RowPressed += (row, e) =>
            {
                if (e.GetCurrentPoint(_table).Properties.IsRightButtonPressed)
                {
                    ShowRecordMenu(row.Record, _table);
                }
            };
            Grid.SetRow(_table, 1);
            Children.Add(_table);

            GridSplitter splitter = new GridSplitter { ResizeDirection = GridResizeDirection.Columns, Background = Brushes.Transparent };
            Grid.SetRow(splitter, 1);
            Grid.SetColumn(splitter, 1);
            Children.Add(splitter);

            // Panel padding (12, 0, 8, 0) and, below, the empty button row of the
            // standalone window; measured.
            _chart.Margin = new Thickness(14, 7, 18, 31);
            _chart.SetGradientIntensity((int)_intensity.Value);
            _chart.PointerPressed += (_, e) =>
            {
                if (e.GetCurrentPoint(_chart).Properties.IsRightButtonPressed && _chart.GetRecordAt(e.GetPosition(_chart)) is StorageHistoryRecord record)
                {
                    ShowRecordMenu(record, _chart);
                }
            };
            Grid.SetRow(_chart, 1);
            Grid.SetColumn(_chart, 2);
            Children.Add(_chart);

            _paths.SelectionChanged += (_, _) =>
            {
                if (!_loading)
                {
                    BindRecords(StorageHistoryService.GetRecords(SelectedPath));
                }
            };
            _displayMode.SelectionChanged += (_, _) => BindRecords(_records);
            _range.SelectionChanged += (_, _) =>
            {
                if (_loading)
                {
                    return;
                }

                _settings.StorageHistoryRangeMode = SelectedRange;
                _settings.Save();
                BindRecords(_records);
            };
            _intensity.ValueChanged += (_, _) =>
            {
                int value = (int)Math.Round(_intensity.Value);
                _intensityValue.Text = value + "%";
                _settings.StorageHistoryGradientIntensityPercent = value;
                _chart.SetGradientIntensity(value);
            };
            _calendar.Click += async (_, _) => await PickRangeAsync();
            _delete.Click += async (_, _) => await DeleteHistoryAsync();

            _loading = true;
            _displayMode.SelectedIndex = 1;
            _range.SelectedIndex = Array.IndexOf(Ranges, NormalizeRange(settings.StorageHistoryRangeMode));
            _loading = false;
        }

        // Reloads the drives and the records (after a scan, or when shown).
        public void RefreshHistory()
        {
            string selected = SelectedPath;
            IReadOnlyList<string> paths = StorageHistoryService.GetPaths();
            List<PathItem> items = paths.Select(path => new PathItem(path, GetPathDisplayName(path))).ToList();
            _loading = true;

            try
            {
                _paths.ItemsSource = items;
                int index = items.FindIndex(item => string.Equals(item.Path, selected, StringComparison.OrdinalIgnoreCase));
                _paths.SelectedIndex = items.Count == 0 ? -1 : Math.Max(0, index);
            }
            finally
            {
                _loading = false;
            }

            _delete.IsEnabled = items.Count > 0;
            BindRecords(items.Count == 0 ? Array.Empty<StorageHistoryRecord>() : StorageHistoryService.GetRecords(SelectedPath));
        }

        private string SelectedPath => (_paths.SelectedItem as PathItem)?.Path;

        private string SelectedRange => (_range.SelectedItem as RangeItem)?.Mode ?? NormalizeRange(_settings.StorageHistoryRangeMode);

        private StorageHistoryDisplayMode DisplayMode => _displayMode.SelectedIndex == 0 ? StorageHistoryDisplayMode.UsedSpace : StorageHistoryDisplayMode.FreeSpace;

        private void BindRecords(IReadOnlyList<StorageHistoryRecord> records)
        {
            _records = records ?? Array.Empty<StorageHistoryRecord>();
            StorageHistoryDisplayMode mode = DisplayMode;
            List<StorageHistoryRecord> ordered = FilterByRange(_records, SelectedRange, _settings, DateTime.Now).OrderBy(record => record.RecordedAtUtc).ToList();
            // Newest first, as WinForms binds them (no sort glyph until a click).
            List<Row> rows = CreateRows(ordered, mode);
            rows.Reverse();
            _table.SetItems(rows);
            _sizeColumn.Title = LocalizationService.GetText(mode == StorageHistoryDisplayMode.FreeSpace ? "StorageHistory.Free" : "StorageHistory.Used");
            _table.RefreshColumns();

            if (_table.Items.Count > 0 && _table.SelectedItem == null)
            {
                _table.Select(_table.SortedItems[0]);
            }

            _chart.SetRecords(ordered, mode);
        }

        // Records in time order with their change to the previous one.
        internal static List<Row> CreateRows(IReadOnlyList<StorageHistoryRecord> ordered, StorageHistoryDisplayMode mode)
        {
            List<Row> rows = new List<Row>();
            long? previous = null;

            foreach (StorageHistoryRecord record in ordered)
            {
                long value = GetDisplayValue(record, mode);
                long? change = previous.HasValue ? value - previous.Value : null;
                rows.Add(new Row
                {
                    Record = record,
                    DateValue = record.RecordedAtUtc.ToLocalTime(),
                    SizeValue = value,
                    ChangeValue = change,
                    Change = change.HasValue ? (change.Value >= 0 ? "+" : "-") + SizeFormatter.Format(Math.Abs(change.Value)) : string.Empty,
                });
                previous = value;
            }

            return rows;
        }

        // Free space, or used space (capacity less free, else the scanned
        // size), within the capacity.
        internal static long GetDisplayValue(StorageHistoryRecord record, StorageHistoryDisplayMode mode)
        {
            if (record.TotalCapacityBytes > 0)
            {
                long value = mode == StorageHistoryDisplayMode.FreeSpace ? record.FreeSpaceBytes : record.TotalCapacityBytes - record.FreeSpaceBytes;
                return Math.Clamp(value, 0, record.TotalCapacityBytes);
            }

            return mode == StorageHistoryDisplayMode.FreeSpace ? 0 : Math.Max(0, record.SizeBytes);
        }

        // The last n days (today included), all, or the custom dates.
        internal static IEnumerable<StorageHistoryRecord> FilterByRange(IEnumerable<StorageHistoryRecord> records, string range, AppSettings settings, DateTime now)
        {
            if (range == RangeAll)
            {
                return records;
            }

            DateTime start;
            DateTime end;

            if (range == RangeCustom)
            {
                DateTime from = settings.StorageHistoryCustomFromDate.Date;
                DateTime to = settings.StorageHistoryCustomToDate.Date;
                start = from <= to ? from : to;
                end = (from <= to ? to : from).AddDays(1);
            }
            else
            {
                int days = range switch
                {
                    RangeLast7Days => 7,
                    RangeLast14Days => 14,
                    RangeLast90Days => 90,
                    RangeLast365Days => 365,
                    _ => 30,
                };
                start = now.Date.AddDays(-(days - 1));
                end = now.AddTicks(1);
            }

            return records.Where(record =>
            {
                DateTime local = record.RecordedAtUtc.ToLocalTime();
                return local >= start && local < end;
            });
        }

        private static string NormalizeRange(string range) => Ranges.Contains(range) ? range : RangeLast30Days;

        // "(C:\ Label)"; macOS and Linux show the mount point and its name.
        private static string GetPathDisplayName(string path)
        {
            VolumeInfo volume = Volumes.Find(Path.GetPathRoot(path) ?? path) ?? Volumes.Find(path);

            if (volume != null)
            {
                string label = string.IsNullOrWhiteSpace(volume.Label) ? LocalizationService.GetText("Drive.LocalDisk") : volume.Label;
                return "(" + volume.RootPath + " " + label + ")";
            }

            return path;
        }

        // ----- actions ----------------------------------------------------

        private void ShowRecordMenu(StorageHistoryRecord record, Control target)
        {
            if (record == null)
            {
                return;
            }

            MenuItem details = new MenuItem { Header = LocalizationService.GetText("StorageHistory.Details.Menu") };
            MenuItem delete = new MenuItem { Header = LocalizationService.GetText("StorageHistory.DeleteRecord") };
            details.Click += async (_, _) =>
            {
                string path = string.IsNullOrWhiteSpace(record.Path) ? SelectedPath : record.Path;

                if (!string.IsNullOrWhiteSpace(path) && TopLevel.GetTopLevel(this) is Window owner)
                {
                    await new StorageHistoryDetailsWindow(path, record, DisplayMode).ShowDialog(owner);
                }
            };
            delete.Click += async (_, _) => await DeleteRecordAsync(record);
            new ContextMenu { ItemsSource = new[] { details, delete } }.Open(target);
        }

        private async Task DeleteRecordAsync(StorageHistoryRecord record)
        {
            string path = SelectedPath;

            if (string.IsNullOrWhiteSpace(path) ||
                !await AppDialogs.ShowWarningYesNoAsync(TopLevel.GetTopLevel(this) as Window, LocalizationService.GetText("StorageHistory.DeleteRecordConfirm"), LocalizationService.GetText("StorageHistory.Title")))
            {
                return;
            }

            StorageHistoryService.DeleteRecord(path, record.RecordedAtUtc);
            StorageHistoryDetailsService.DeleteRecord(path, record.RecordedAtUtc);
            RefreshHistory();
        }

        private async Task DeleteHistoryAsync()
        {
            string path = SelectedPath;

            if (string.IsNullOrWhiteSpace(path) ||
                !await AppDialogs.ShowWarningYesNoAsync(TopLevel.GetTopLevel(this) as Window, LocalizationService.GetText("StorageHistory.DeleteConfirm"), LocalizationService.GetText("StorageHistory.Title")))
            {
                return;
            }

            StorageHistoryService.DeleteRecords(path);
            StorageHistoryDetailsService.DeleteRecords(path);
            RefreshHistory();
        }

        // The custom range: From and To dates, OK selects "Custom range".
        private async Task PickRangeAsync()
        {
            if (TopLevel.GetTopLevel(this) is not Window owner)
            {
                return;
            }

            DateTime from = _settings.StorageHistoryCustomFromDate.Date;
            DateTime to = _settings.StorageHistoryCustomToDate.Date;
            CalendarDatePicker fromPicker = new CalendarDatePicker { SelectedDate = from <= to ? from : to, Width = 140, Height = 32, HorizontalAlignment = HorizontalAlignment.Left };
            CalendarDatePicker toPicker = new CalendarDatePicker { SelectedDate = from <= to ? to : from, Width = 140, Height = 32, HorizontalAlignment = HorizontalAlignment.Left };
            bool accepted = false;
            Window dialog = new Window
            {
                Title = LocalizationService.GetText("StorageHistory.Calendar"),
                Width = 430,
                Height = 176,
                CanResize = false,
                CanMinimize = false,
                CanMaximize = false,
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
            };
            Button ok = new Button { Content = LocalizationService.GetText("Common.OK"), Width = 90, Height = 32, IsDefault = true, HorizontalContentAlignment = HorizontalAlignment.Center, Classes = { "ant", "dialog", "primary" } };
            Button cancel = new Button { Content = LocalizationService.GetText("Common.Cancel"), Width = 90, Height = 32, IsCancel = true, HorizontalContentAlignment = HorizontalAlignment.Center, Classes = { "ant", "dialog", "bordered" } };
            ok.Click += (_, _) => { accepted = true; dialog.Close(); };
            cancel.Click += (_, _) => dialog.Close();
            Grid layout = new Grid { ColumnDefinitions = new ColumnDefinitions("70,*"), RowDefinitions = new RowDefinitions("42,42,52"), Margin = new Thickness(16) };
            AddCell(layout, Label("StorageHistory.From"), 0, 0);
            AddCell(layout, fromPicker, 1, 0);
            AddCell(layout, Label("StorageHistory.To"), 0, 1);
            AddCell(layout, toPicker, 1, 1);
            StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(70, 8, 0, 0), Children = { cancel, ok } };
            AddCell(layout, buttons, 0, 2);
            Grid.SetColumnSpan(buttons, 2);
            dialog.Content = layout;
            await dialog.ShowDialog(owner);

            if (!accepted)
            {
                return;
            }

            DateTime newFrom = (fromPicker.SelectedDate ?? from).Date;
            DateTime newTo = (toPicker.SelectedDate ?? to).Date;
            _settings.StorageHistoryCustomFromDate = newFrom <= newTo ? newFrom : newTo;
            _settings.StorageHistoryCustomToDate = newFrom <= newTo ? newTo : newFrom;
            _settings.StorageHistoryRangeMode = RangeCustom;
            _settings.Save();
            _loading = true;
            _range.SelectedIndex = Array.IndexOf(Ranges, RangeCustom);
            _loading = false;
            BindRecords(_records);
        }

        // ----- layout helpers ---------------------------------------------

        // WinForms squeezes the intensity slider out when the header is too
        // narrow; here the label and slider hide until they fit.
        private void FitHeader()
        {
            _intensityLabel.IsVisible = true;
            _intensity.IsVisible = true;
            double required = 0;

            foreach (Control child in _header.Children)
            {
                child.Measure(Size.Infinity);
                required += child.DesiredSize.Width;
            }

            bool fits = required <= _header.Bounds.Width;
            _intensityLabel.IsVisible = fits;
            _intensity.IsVisible = fits;
        }

        private void AddHeader(Control control, int column, Thickness margin)
        {
            control.Margin = margin;
            control.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(control, column);
            _header.Children.Add(control);
        }

        private static void AddCell(Grid grid, Control control, int column, int row)
        {
            control.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(control, column);
            Grid.SetRow(control, row);
            grid.Children.Add(control);
        }

        private static Control Sized(Control control, double width)
        {
            control.Width = width;
            control.Height = 32;
            return control;
        }

        // The widest item text + 48, between minimum and maximum.
        private static double FitWidth(ComboBox select, double minimum, double maximum)
        {
            double widest = select.ItemsSource.Cast<object>()
                .Select(item => new FormattedText(item?.ToString() ?? string.Empty, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default), 12, null).WidthIncludingTrailingWhitespace)
                .DefaultIfEmpty(0)
                .Max();
            return Math.Clamp(Math.Ceiling(widest) + 6 + 48, minimum, maximum);
        }

        private static ComboBox Select() => new ComboBox { Classes = { "ant", "field" } };

        private static TextBlock Label(string textKey) => new TextBlock { Text = LocalizationService.GetText(textKey), VerticalAlignment = VerticalAlignment.Center };

        // The "📅" of the calendar button, drawn: a page with rings and a grid.
        private sealed class CalendarGlyph : Control
        {
            public CalendarGlyph()
            {
                Width = 14;
                Height = 14;
            }

            public override void Render(DrawingContext context)
            {
                IBrush brush = this.FindResource(ActualThemeVariant, "TextPrimaryBrush") as IBrush ?? Brushes.White;
                Pen pen = new Pen(brush, 1);
                context.DrawRectangle(null, pen, new RoundedRect(new Rect(1.5, 2.5, 11, 10), 1.5));
                context.FillRectangle(brush, new Rect(1, 2, 12, 3));
                context.FillRectangle(brush, new Rect(4, 0.5, 1, 3));
                context.FillRectangle(brush, new Rect(9, 0.5, 1, 3));

                for (int row = 0; row < 2; row++)
                {
                    for (int column = 0; column < 3; column++)
                    {
                        context.FillRectangle(brush, new Rect(3.5 + column * 2.5, 6.5 + row * 2.5, 1.5, 1.5));
                    }
                }
            }
        }
    }
}
