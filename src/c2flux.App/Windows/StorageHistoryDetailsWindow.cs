using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform;

namespace c2flux
{
    // Port of the WinForms StorageHistoryDetailsForm: the files added and
    // removed at one data point of the storage history.
    public sealed class StorageHistoryDetailsWindow : Window
    {
        internal sealed record Row(DateTime DateValue, string FilePath, string ChangeType, long ChangeValue, string Change);

        private readonly DrawnTable<Row> _table = new DrawnTable<Row> { Style = TableStyle.Classic, Responsive = true, ResponsiveReserve = 0, ClassicHeaderHeight = 32, ClassicRowHeight = 28 };

        public StorageHistoryDetailsWindow(string path, StorageHistoryRecord record, StorageHistoryDisplayMode displayMode)
        {
            Title = LocalizationService.GetText("StorageHistory.Details.Title");
            Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://c2flux.App/Assets/c2flux.png")));
            Width = 980;
            Height = 440;
            MinWidth = 760;
            MinHeight = 320;
            CanMinimize = false;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            _table.Columns.Add(new TableColumn<Row>(LocalizationService.GetText("StorageHistory.Details.Date"), row => row.DateValue.ToString("g", CultureInfo.CurrentCulture)) { Percent = 18, SortKey = row => row.DateValue, DescendingFirst = true });
            _table.Columns.Add(new TableColumn<Row>(LocalizationService.GetText("StorageHistory.Details.FilePath"), row => row.FilePath) { Percent = 52 });
            _table.Columns.Add(new TableColumn<Row>(LocalizationService.GetText("StorageHistory.Details.ChangeType"), row => row.ChangeType) { Percent = 15 });
            _table.Columns.Add(new TableColumn<Row>(LocalizationService.GetText("StorageHistory.Details.Change"), row => row.Change) { Percent = 15, SortKey = row => Math.Abs(row.ChangeValue) });
            _table.SetItems(StorageHistoryDetailsService.GetChanges(path, record.RecordedAtUtc).Select(change => CreateRow(change, displayMode)));
            _table.RowPressed += (row, e) =>
            {
                if (e.GetCurrentPoint(_table).Properties.IsRightButtonPressed && !string.IsNullOrWhiteSpace(row.FilePath))
                {
                    MenuItem open = new MenuItem { Header = LocalizationService.GetText("Context.OpenInExplorer") };
                    open.Click += (_, _) => OpenInFileManager(row.FilePath);
                    new ContextMenu { ItemsSource = new[] { open } }.Open(_table);
                }
            };

            // A DataGridView starts with its first row selected.
            if (_table.Items.Count > 0)
            {
                _table.Select(_table.SortedItems[0]);
            }

            Button close = new Button
            {
                Content = LocalizationService.GetText("Common.Close"),
                Width = 84,
                Height = 32,
                IsDefault = true,
                IsCancel = true,
                HorizontalAlignment = HorizontalAlignment.Right,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(4, 8, 8, 12),
                Classes = { "ant", "dialog", "bordered" },
            };
            close.Click += (_, _) => Close();

            Grid layout = new Grid { RowDefinitions = new RowDefinitions("*,52"), Margin = new Thickness(12) };
            Grid.SetRow(close, 1);
            layout.Children.Add(_table);
            layout.Children.Add(close);
            Content = layout;
        }

        // In free space mode an added file is a loss: "-".
        internal static Row CreateRow(StorageHistoryDetailsChange change, StorageHistoryDisplayMode displayMode)
        {
            long bytes = change.SizeDeltaBytes == long.MinValue ? long.MaxValue : Math.Abs(change.SizeDeltaBytes);
            bool positive = displayMode == StorageHistoryDisplayMode.FreeSpace ? !change.IsAdded : change.IsAdded;
            DateTime local = change.RecordedAtUtc.ToLocalTime();

            return new Row(
                local,
                change.FilePath ?? string.Empty,
                LocalizationService.GetText(change.IsAdded ? "StorageHistory.Details.Added" : "StorageHistory.Details.Removed"),
                change.IsAdded ? bytes : -bytes,
                (positive ? "+" : "-") + SizeFormatter.Format(bytes));
        }

        // The file, or its nearest folder that still exists.
        private static void OpenInFileManager(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    FileManager.Reveal(filePath);
                    return;
                }

                for (string folder = Path.GetDirectoryName(filePath); !string.IsNullOrWhiteSpace(folder); folder = Path.GetDirectoryName(folder))
                {
                    if (Directory.Exists(folder))
                    {
                        FileManager.Open(folder);
                        return;
                    }
                }
            }
            catch (Exception exception)
            {
                AppAlertLog.AddError("StorageHistory", "Storage history detail path could not be opened.", "Path: " + filePath + Environment.NewLine + exception);
            }
        }
    }
}
