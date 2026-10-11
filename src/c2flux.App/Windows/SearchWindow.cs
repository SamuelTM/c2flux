using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;

namespace c2flux
{
    // Port of the WinForms SearchForm: searches the current scan, a saved
    // scan or a drive it scans first, by name, size, date and file type. The
    // results arrive while the search runs and are sorted when it ends; the
    // criteria and the window bounds are kept in the settings.
    public sealed class SearchWindow : Window
    {
        private enum SourceKind
        {
            CurrentScan,
            SavedScan,
            LocalDrive,
        }

        private sealed record SourceItem(SourceKind Kind, string DisplayName, string DrivePath = null)
        {
            public override string ToString() => DisplayName ?? string.Empty;
        }

        private readonly AppSettings _settings;
        private readonly Func<FileSystemEntry> _currentRoot;
        private readonly Func<string, Task<FileSystemEntry>> _scanDrive;
        private readonly string _initialDrivePath;
        private readonly SearchService _searchService = new SearchService();
        private readonly ConcurrentQueue<SearchResult> _pending = new ConcurrentQueue<SearchResult>();
        private readonly List<SearchResult> _results = new List<SearchResult>();
        private readonly DispatcherTimer _resultTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };

        private readonly ComboBox _source = Select();
        private readonly TextBlock _savedScanLabel = Label("Search.SavedScan");
        private readonly ComboBox _savedScan = Select();
        private readonly TextBox _searchText = new TextBox { Classes = { "ant" } };
        private readonly ComboBox _matchMode = Select();
        private readonly Button _toggleFilters = new Button { HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(7, 0), Classes = { "ant", "dialog", "bordered" } };
        private readonly FilterGlyph _filterGlyph = new FilterGlyph();
        private readonly Grid _filters = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,*"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto"), Margin = new Thickness(11, 7, 3, 11) };
        private readonly CheckBox _minimumSize = Check("Search.MinimumSize");
        private readonly NumericUpDown _minimumSizeValue = SizeInput();
        private readonly CheckBox _maximumSize = Check("Search.MaximumSize");
        private readonly NumericUpDown _maximumSizeValue = SizeInput();
        private readonly CheckBox _modifiedAfter = Check("Search.ModifiedAfter");
        private readonly CalendarDatePicker _modifiedAfterValue = DateInput();
        private readonly CheckBox _modifiedBefore = Check("Search.ModifiedBefore");
        private readonly CalendarDatePicker _modifiedBeforeValue = DateInput();
        private readonly TextBox _fileTypes = new TextBox { Classes = { "ant" } };
        private readonly DrawnTable<SearchResult> _table = new DrawnTable<SearchResult> { Style = TableStyle.Classic, Responsive = true };
        private readonly ProgressBar _progress = new ProgressBar { IsIndeterminate = true, Height = 4, MinHeight = 4, IsVisible = false };
        private readonly TextBlock _status = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 3) };
        private readonly Button _search = FooterButton("Search.Start", primary: true);
        private readonly Button _cancel = FooterButton("Search.Cancel", primary: false);

        private IReadOnlyList<ScanHistoryInfo> _savedScans = Array.Empty<ScanHistoryInfo>();
        private CancellationTokenSource _cancellation;
        private int _processedCount;
        private bool _running;
        private bool _suspendSave = true;

        public SearchWindow(AppSettings settings, Func<FileSystemEntry> currentRoot, Func<string, Task<FileSystemEntry>> scanDrive, string initialDrivePath = null)
        {
            _settings = settings;
            _currentRoot = currentRoot;
            _scanDrive = scanDrive;
            _initialDrivePath = NormalizeDrivePath(initialDrivePath);

            Title = LocalizationService.GetText("Search.Title");
            Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://c2flux.App/Assets/c2flux.png")));
            // The WinForms window is 704x720 (550x560 at least) with its frame.
            Width = 688;
            Height = 681;
            MinWidth = 534;
            MinHeight = 521;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            Content = CreateLayout();
            CreateColumns();
            RestoreSettings();
            LoadSavedScans();
            LoadSources();
            UpdateSourceState();
            UpdateFilters();
            UpdateSearchButton();
            _suspendSave = false;

            _resultTimer.Tick += (_, _) => OnResultTimer();
            Closing += (_, _) => OnClosingWindow();
        }

        internal string SearchText
        {
            get => _searchText.Text;
            set => _searchText.Text = value;
        }

        // ----- layout -----------------------------------------------------

        private Control CreateLayout()
        {
            _matchMode.ItemsSource = new[] { "Search.MatchMode.Contains", "Search.MatchMode.StartsWith", "Search.MatchMode.ExactName", "Search.MatchMode.FileExtension" }
                .Select(LocalizationService.GetText).ToList();
            _savedScan.DisplayMemberBinding = new Binding(nameof(ScanHistoryInfo.DisplayName));

            // Labels: the label's margin (3, 7, 8, 3) plus the 3 px text padding.
            Grid search = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto") };
            AddRow(search, 0, Label("Search.Source"), _source);
            AddRow(search, 1, Label("Search.Text"), _searchText);
            AddRow(search, 2, Label("Search.MatchMode"), _matchMode);
            AddRow(search, 3, _savedScanLabel, _savedScan);

            _toggleFilters.Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, Children = { _filterGlyph, new TextBlock { Text = LocalizationService.GetText("Search.Filters"), VerticalAlignment = VerticalAlignment.Center } } };
            _toggleFilters.Click += (_, _) =>
            {
                _filters.IsVisible = !_filters.IsVisible;
                UpdateFilters();
                SaveSettings();
            };
            Field(_toggleFilters);

            Button reset = new Button { Content = LocalizationService.GetText("Search.ResetFilters"), Width = 110, HorizontalAlignment = HorizontalAlignment.Right, HorizontalContentAlignment = HorizontalAlignment.Center, Classes = { "ant", "dialog", "bordered" } };
            reset.Click += (_, _) => ResetFilters();
            AddFilter(_minimumSize, 0, 0);
            AddFilter(_minimumSizeValue, 1, 0);
            AddFilter(_maximumSize, 2, 0);
            AddFilter(_maximumSizeValue, 3, 0);
            AddFilter(_modifiedAfter, 0, 1);
            AddFilter(_modifiedAfterValue, 1, 1);
            AddFilter(_modifiedBefore, 2, 1);
            AddFilter(_modifiedBeforeValue, 3, 1);
            AddFilter(Label("Search.FileTypes"), 0, 2);
            AddFilter(_fileTypes, 1, 2, span: 3);
            AddFilter(reset, 3, 3);

            _table.Margin = new Thickness(3);
            _table.RowPressed += OnRowPressed;
            _table.SortChanged += (column, descending) =>
            {
                _settings.SearchSortColumnIndex = _table.Columns.IndexOf(column);
                _settings.SearchSortDescending = descending;
                _settings.Save();
            };

            _search.IsDefault = true;
            _cancel.IsEnabled = false;
            _search.Click += async (_, _) => await SearchAsync();
            _cancel.Click += (_, _) => _cancellation?.Cancel();
            Grid footer = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto") };
            Grid.SetColumn(_cancel, 1);
            Grid.SetColumn(_search, 2);
            footer.Children.Add(_status);
            footer.Children.Add(_cancel);
            footer.Children.Add(_search);

            _searchText.TextChanged += (_, _) => OnInputChanged();
            _fileTypes.TextChanged += (_, _) => OnInputChanged();
            _source.SelectionChanged += (_, _) => { UpdateSourceState(); OnInputChanged(); };
            _savedScan.SelectionChanged += (_, _) => OnInputChanged();
            _matchMode.SelectionChanged += (_, _) => OnInputChanged();

            foreach (CheckBox check in new[] { _minimumSize, _maximumSize, _modifiedAfter, _modifiedBefore })
            {
                check.IsCheckedChanged += (_, _) => OnInputChanged();
            }

            _minimumSizeValue.ValueChanged += (_, _) => OnInputChanged();
            _maximumSizeValue.ValueChanged += (_, _) => OnInputChanged();
            _modifiedAfterValue.SelectedDateChanged += (_, _) => OnInputChanged();
            _modifiedBeforeValue.SelectedDateChanged += (_, _) => OnInputChanged();

            Grid layout = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*,Auto,Auto"), Margin = new Thickness(12) };
            // TableLayoutPanel cells keep a 3 px margin around each nested panel.
            search.Margin = new Thickness(3);
            footer.Margin = new Thickness(3);
            Grid.SetRow(_toggleFilters, 1);
            Grid.SetRow(_filters, 2);
            Grid.SetRow(_table, 3);
            Grid.SetRow(_progress, 4);
            Grid.SetRow(footer, 5);
            layout.Children.Add(search);
            layout.Children.Add(_toggleFilters);
            layout.Children.Add(_filters);
            layout.Children.Add(_table);
            layout.Children.Add(_progress);
            layout.Children.Add(footer);
            return layout;
        }

        private static void AddRow(Grid grid, int row, TextBlock label, Control field)
        {
            label.Margin = new Thickness(6, 7, 11, 3);
            Grid.SetRow(label, row);
            Grid.SetRow(field, row);
            Grid.SetColumn(field, 1);
            grid.Children.Add(label);
            grid.Children.Add(Field(field));
        }

        private void AddFilter(Control control, int column, int row, int span = 1)
        {
            if (control is CheckBox || control is TextBlock)
            {
                control.Margin = new Thickness(3);
                control.VerticalAlignment = VerticalAlignment.Center;
                control.Height = control is CheckBox ? 28 : double.NaN;
            }
            else
            {
                Field(control);
            }

            Grid.SetColumn(control, column);
            Grid.SetRow(control, row);
            Grid.SetColumnSpan(control, span);
            _filters.Children.Add(control);
        }

        // A 32 px field in its 3 px cell margin.
        private static Control Field(Control control)
        {
            control.Margin = new Thickness(3);
            control.Height = 32;

            if (control.HorizontalAlignment != HorizontalAlignment.Right)
            {
                control.HorizontalAlignment = HorizontalAlignment.Stretch;
            }

            return control;
        }

        private void CreateColumns()
        {
            _table.Columns.Add(new TableColumn<SearchResult>(LocalizationService.GetText("Search.Drive"), result => result.Drive) { Percent = 10 });
            _table.Columns.Add(new TableColumn<SearchResult>(LocalizationService.GetText("Search.FullPath"), result => result.FullPath) { Percent = 43, SortKey = result => result.FullPath?.ToUpperInvariant() });
            _table.Columns.Add(new TableColumn<SearchResult>(LocalizationService.GetText("Common.Name"), result => result.Name) { Percent = 22, SortKey = result => result.Name?.ToUpperInvariant() });
            _table.Columns.Add(new TableColumn<SearchResult>(LocalizationService.GetText("Common.Size"), result => FormatSize(result.SizeBytes)) { Percent = 12, SortKey = result => result.SizeBytes, DescendingFirst = true });
            _table.Columns.Add(new TableColumn<SearchResult>(LocalizationService.GetText("Search.Modified"), result => result.ModifiedLocal.ToString("g", CultureInfo.CurrentCulture)) { Percent = 16, SortKey = result => result.ModifiedLocal, DescendingFirst = true });
        }

        // ----- sources ----------------------------------------------------

        private void LoadSources()
        {
            List<SourceItem> items = new List<SourceItem>
            {
                new SourceItem(SourceKind.CurrentScan, LocalizationService.GetText("Search.Source.CurrentScan")),
                new SourceItem(SourceKind.SavedScan, LocalizationService.GetText("Search.Source.SavedScan")),
            };

            foreach (VolumeInfo volume in Volumes.List().OrderBy(volume => volume.RootPath, StringComparer.OrdinalIgnoreCase))
            {
                string name = string.IsNullOrWhiteSpace(volume.Label) ? volume.RootPath : $"{volume.RootPath} ({volume.Label})";
                items.Add(new SourceItem(SourceKind.LocalDrive, name, volume.RootPath));
            }

            _source.ItemsSource = items;
            int initial = _initialDrivePath == null ? -1 : items.FindIndex(item => item.Kind == SourceKind.LocalDrive && string.Equals(NormalizeDrivePath(item.DrivePath), _initialDrivePath, StringComparison.OrdinalIgnoreCase));
            _source.SelectedIndex = Math.Max(0, initial);
        }

        private void LoadSavedScans()
        {
            try
            {
                _savedScans = ScanHistoryService.List();
            }
            catch
            {
                _savedScans = Array.Empty<ScanHistoryInfo>();
            }

            _savedScan.ItemsSource = _savedScans;
            _savedScan.SelectedIndex = _savedScans.Count > 0 ? 0 : -1;
        }

        private SourceItem SelectedSource => _source.SelectedItem as SourceItem;

        private void UpdateSourceState()
        {
            bool saved = SelectedSource?.Kind == SourceKind.SavedScan;
            _savedScanLabel.IsVisible = saved;
            _savedScan.IsVisible = saved;
            _savedScan.IsEnabled = !_running && saved && _savedScans.Count > 0;

            if (saved && _savedScans.Count == 0)
            {
                _status.Text = LocalizationService.GetText("Search.NoSavedScansAvailable");
            }
            else if (!_running)
            {
                _status.Text = string.Empty;
            }
        }

        // The volume root of path ("C:" becomes "C:\").
        private static string NormalizeDrivePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            string normalized = path.Trim().Trim('"');

            if (normalized.Length == 2 && normalized[1] == ':')
            {
                normalized += "\\";
            }

            try
            {
                return Path.GetPathRoot(normalized);
            }
            catch
            {
                return null;
            }
        }

        // ----- searching --------------------------------------------------

        private SearchCriteria CreateCriteria()
        {
            return new SearchCriteria
            {
                SearchText = _searchText.Text ?? string.Empty,
                MatchMode = (SearchMatchMode)Math.Max(0, _matchMode.SelectedIndex),
                MinimumSizeBytes = _minimumSize.IsChecked == true ? MegabytesToBytes(_minimumSizeValue.Value ?? 0) : null,
                MaximumSizeBytes = _maximumSize.IsChecked == true ? MegabytesToBytes(_maximumSizeValue.Value ?? 0) : null,
                ModifiedAfterLocal = _modifiedAfter.IsChecked == true ? (_modifiedAfterValue.SelectedDate ?? DateTime.Today).Date : null,
                ModifiedBeforeLocal = _modifiedBefore.IsChecked == true ? (_modifiedBeforeValue.SelectedDate ?? DateTime.Today).Date.AddDays(1).AddTicks(-1) : null,
                FileExtensions = SearchCriteria.ParseFileExtensions(_fileTypes.Text),
            };
        }

        internal async Task SearchAsync()
        {
            if (_running)
            {
                return;
            }

            string title = LocalizationService.GetText("Search.Title");
            SearchCriteria criteria = CreateCriteria();

            if (!criteria.IsValid)
            {
                await AppDialogs.ShowInfoOkAsync(this, LocalizationService.GetText("Search.EnterCriteria"), title);
                return;
            }

            if (SelectedSource is not SourceItem source)
            {
                return;
            }

            FileSystemEntry root;

            if (source.Kind == SourceKind.SavedScan)
            {
                if (_savedScan.SelectedItem is not ScanHistoryInfo scan)
                {
                    await AppDialogs.ShowInfoOkAsync(this, LocalizationService.GetText("Search.NoSavedScan"), title);
                    return;
                }

                try
                {
                    _status.Text = LocalizationService.GetText("Search.LoadingSavedScan");
                    Cursor = new Cursor(StandardCursorType.Wait);
                    root = (await Task.Run(() => ScanHistoryService.Load(scan.ScanId)))?.RootEntry;
                }
                catch (Exception exception)
                {
                    await AppDialogs.ShowErrorOkAsync(this, LocalizationService.GetText("Search.LoadSavedScanFailed") + Environment.NewLine + exception.Message, title);
                    return;
                }
                finally
                {
                    Cursor = Cursor.Default;
                }
            }
            else if (source.Kind == SourceKind.LocalDrive)
            {
                try
                {
                    _status.Text = LocalizationService.GetText("Status.Scanning");
                    Cursor = new Cursor(StandardCursorType.Wait);
                    _search.IsEnabled = false;
                    _source.IsEnabled = false;
                    root = await _scanDrive(source.DrivePath);
                }
                catch (Exception exception)
                {
                    await AppDialogs.ShowErrorOkAsync(this, exception.Message, title);
                    return;
                }
                finally
                {
                    Cursor = Cursor.Default;
                    _source.IsEnabled = true;
                    UpdateSearchButton();
                }
            }
            else
            {
                root = _currentRoot();
            }

            if (root == null)
            {
                await AppDialogs.ShowInfoOkAsync(this, LocalizationService.GetText("Search.NoData"), title);
                return;
            }

            SaveSettings();
            ClearResults();
            SetRunning(true);
            _cancellation = new CancellationTokenSource();
            CancellationToken token = _cancellation.Token;
            Stopwatch stopwatch = Stopwatch.StartNew();
            bool canceled = false;

            try
            {
                await Task.Run(() => _searchService.Search(root, criteria, _pending.Enqueue, processed => Interlocked.Exchange(ref _processedCount, processed), token), token);
            }
            catch (OperationCanceledException)
            {
                canceled = true;
            }
            finally
            {
                stopwatch.Stop();

                while (_pending.TryDequeue(out SearchResult result))
                {
                    _results.Add(result);
                }

                SetRunning(false);
                _table.SetItems(_results);
                int column = Math.Clamp(_settings.SearchSortColumnIndex, 0, _table.Columns.Count - 1);
                _table.SetSort(_results.Count > 0 ? _table.Columns[column] : null, _settings.SearchSortDescending);

                // A DataGridView starts with its first row selected.
                if (_table.Items.Count > 0)
                {
                    _table.Select(_table.SortedItems[0]);
                }
                _status.Text = LocalizationService.Format(canceled ? "Search.Canceled" : "Search.Completed", _results.Count, stopwatch.Elapsed.TotalSeconds);
            }
        }

        // Up to 1000 new results per tick, unsorted while the search runs.
        private void OnResultTimer()
        {
            int added = 0;

            while (added < 1000 && _pending.TryDequeue(out SearchResult result))
            {
                _results.Add(result);
                added++;
            }

            if (added > 0)
            {
                _table.SetItems(_results);
            }

            if (_running)
            {
                _status.Text = LocalizationService.GetText("Search.Searching") + " " +
                    _processedCount.ToString("N0", CultureInfo.CurrentCulture) + " / " +
                    _results.Count.ToString("N0", CultureInfo.CurrentCulture);
            }
        }

        private void ClearResults()
        {
            _pending.Clear();
            _results.Clear();
            _table.SetSort(null, false);
            _table.SetItems(_results);
            _processedCount = 0;
            _status.Text = string.Empty;
        }

        private void SetRunning(bool running)
        {
            _running = running;
            _cancel.IsEnabled = running;
            _progress.IsVisible = running;
            _searchText.IsEnabled = !running;
            _source.IsEnabled = !running;
            _savedScan.IsEnabled = !running && SelectedSource?.Kind == SourceKind.SavedScan && _savedScans.Count > 0;
            _matchMode.IsEnabled = !running;
            _filters.IsEnabled = !running;
            UpdateSearchButton();

            if (running)
            {
                _status.Text = LocalizationService.GetText("Search.Searching");
                _resultTimer.Start();
            }
            else
            {
                _resultTimer.Stop();
            }
        }

        private void UpdateSearchButton()
        {
            SourceItem source = SelectedSource;
            bool available = source != null && (
                source.Kind == SourceKind.LocalDrive ||
                source.Kind == SourceKind.CurrentScan && _currentRoot() != null ||
                source.Kind == SourceKind.SavedScan && _savedScan.SelectedItem is ScanHistoryInfo);
            _search.IsEnabled = !_running && available && CreateCriteria().IsValid;
        }

        private void OnInputChanged()
        {
            if (_suspendSave)
            {
                return;
            }

            UpdateSearchButton();
            SaveSettings();
        }

        private void UpdateFilters()
        {
            _filterGlyph.Expanded = _filters.IsVisible;
        }

        private void ResetFilters()
        {
            _suspendSave = true;
            _minimumSize.IsChecked = false;
            _minimumSizeValue.Value = 0;
            _maximumSize.IsChecked = false;
            _maximumSizeValue.Value = 0;
            _modifiedAfter.IsChecked = false;
            _modifiedAfterValue.SelectedDate = DateTime.Today;
            _modifiedBefore.IsChecked = false;
            _modifiedBeforeValue.SelectedDate = DateTime.Today;
            _fileTypes.Text = string.Empty;
            _suspendSave = false;
            SaveSettings();
            UpdateSearchButton();
        }

        // ----- results menu -----------------------------------------------

        private void OnRowPressed(SearchResult result, PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(this).Properties.IsRightButtonPressed || result == null)
            {
                return;
            }

            MenuItem openParent = new MenuItem { Header = LocalizationService.GetText("Search.OpenParentFolder") };
            MenuItem copyPath = new MenuItem { Header = LocalizationService.GetText("Search.CopyFullPath") };
            MenuItem copyName = new MenuItem { Header = LocalizationService.GetText("Search.CopyName") };
            openParent.Click += async (_, _) => await OpenParentFolderAsync(result);
            copyPath.Click += async (_, _) => await CopyAsync(result.FullPath);
            copyName.Click += async (_, _) => await CopyAsync(result.Name);
            ContextMenu menu = new ContextMenu { ItemsSource = new[] { openParent, copyPath, copyName } };
            menu.Open(_table);
        }

        private async Task OpenParentFolderAsync(SearchResult result)
        {
            string parent = result.IsDirectory ? Directory.GetParent(result.FullPath)?.FullName : Path.GetDirectoryName(result.FullPath);
            bool shown = !string.IsNullOrWhiteSpace(parent) && Directory.Exists(parent) &&
                (File.Exists(result.FullPath) || Directory.Exists(result.FullPath) ? FileManager.Reveal(result.FullPath) : FileManager.Open(parent));

            if (!shown)
            {
                await AppDialogs.ShowInfoOkAsync(this, LocalizationService.GetText("Search.ItemMissing"), LocalizationService.GetText("Search.Title"));
            }
        }

        private async Task CopyAsync(string text)
        {
            if (!string.IsNullOrWhiteSpace(text) && Clipboard != null)
            {
                await Clipboard.SetTextAsync(text);
            }
        }

        // ----- settings ---------------------------------------------------

        private void RestoreSettings()
        {
            _matchMode.SelectedIndex = Math.Clamp((int)_settings.SearchMatchMode, 0, 3);
            _filters.IsVisible = _settings.SearchFiltersExpanded;
            _minimumSize.IsChecked = _settings.SearchMinimumSizeEnabled;
            _minimumSizeValue.Value = BytesToMegabytes(_settings.SearchMinimumSizeBytes);
            _maximumSize.IsChecked = _settings.SearchMaximumSizeEnabled;
            _maximumSizeValue.Value = BytesToMegabytes(_settings.SearchMaximumSizeBytes);
            _modifiedAfter.IsChecked = _settings.SearchModifiedAfterEnabled;
            _modifiedAfterValue.SelectedDate = NormalizeDate(_settings.SearchModifiedAfter);
            _modifiedBefore.IsChecked = _settings.SearchModifiedBeforeEnabled;
            _modifiedBeforeValue.SelectedDate = NormalizeDate(_settings.SearchModifiedBefore);
            _fileTypes.Text = _settings.SearchFileTypes ?? string.Empty;

            if (_settings.HasSearchWindowBounds && _settings.SearchWindowWidth >= MinWidth && _settings.SearchWindowHeight >= MinHeight)
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Position = new PixelPoint(_settings.SearchWindowLeft, _settings.SearchWindowTop);
                Width = _settings.SearchWindowWidth;
                Height = _settings.SearchWindowHeight;
            }
        }

        private void SaveSettings()
        {
            if (_suspendSave)
            {
                return;
            }

            _settings.SearchMatchMode = (SearchMatchMode)Math.Max(0, _matchMode.SelectedIndex);
            _settings.SearchFiltersExpanded = _filters.IsVisible;
            _settings.SearchMinimumSizeEnabled = _minimumSize.IsChecked == true;
            _settings.SearchMinimumSizeBytes = MegabytesToBytes(_minimumSizeValue.Value ?? 0);
            _settings.SearchMaximumSizeEnabled = _maximumSize.IsChecked == true;
            _settings.SearchMaximumSizeBytes = MegabytesToBytes(_maximumSizeValue.Value ?? 0);
            _settings.SearchModifiedAfterEnabled = _modifiedAfter.IsChecked == true;
            _settings.SearchModifiedAfter = (_modifiedAfterValue.SelectedDate ?? DateTime.Today).Date;
            _settings.SearchModifiedBeforeEnabled = _modifiedBefore.IsChecked == true;
            _settings.SearchModifiedBefore = (_modifiedBeforeValue.SelectedDate ?? DateTime.Today).Date;
            _settings.SearchFileTypes = _fileTypes.Text;
            _settings.Save();
        }

        private void OnClosingWindow()
        {
            _cancellation?.Cancel();

            if (WindowState == WindowState.Normal)
            {
                _settings.HasSearchWindowBounds = true;
                _settings.SearchWindowLeft = Position.X;
                _settings.SearchWindowTop = Position.Y;
                _settings.SearchWindowWidth = (int)Width;
                _settings.SearchWindowHeight = (int)Height;
            }

            SaveSettings();
        }

        // ----- helpers ----------------------------------------------------

        private static DateTime NormalizeDate(DateTime value) => value.Year < 1900 ? DateTime.Today : value.Date;

        private static long MegabytesToBytes(decimal megabytes)
        {
            decimal bytes = megabytes * 1024M * 1024M;
            return bytes > long.MaxValue ? long.MaxValue : decimal.ToInt64(bytes);
        }

        private static decimal BytesToMegabytes(long bytes) => bytes <= 0 ? 0M : Math.Min(1048576M, bytes / (1024M * 1024M));

        // B, KB, MB, GB, TB with two decimals (no decimals for bytes).
        internal static string FormatSize(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double value = Math.Max(0, bytes);
            int unit = 0;

            while (value >= 1024D && unit < units.Length - 1)
            {
                value /= 1024D;
                unit++;
            }

            return value.ToString(unit == 0 ? "N0" : "N2", CultureInfo.CurrentCulture) + " " + units[unit];
        }

        private static ComboBox Select() => new ComboBox { Classes = { "ant", "field" } };

        private static CheckBox Check(string textKey) => new CheckBox { Content = LocalizationService.GetText(textKey), Classes = { "ant" } };

        private static TextBlock Label(string textKey) => new TextBlock { Text = LocalizationService.GetText(textKey), Classes = { "ant" } };

        // AntdUI.InputNumber: MB with two decimals and spin buttons.
        private static NumericUpDown SizeInput() => new NumericUpDown { Minimum = 0, Maximum = 1048576, Increment = 1, FormatString = "N2", Value = 0 };

        private static CalendarDatePicker DateInput() => new CalendarDatePicker { SelectedDateFormat = CalendarDatePickerFormat.Custom, CustomDateFormatString = "dd.MM.yyyy", SelectedDate = DateTime.Today };

        private static Button FooterButton(string textKey, bool primary)
        {
            Button button = new Button
            {
                Content = LocalizationService.GetText(textKey),
                Width = 90,
                Height = 32,
                Margin = new Thickness(3),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                Classes = { "ant", "dialog", "bordered" },
            };

            if (primary)
            {
                button.Classes.Add("secondary");
            }

            return button;
        }

        // The "▶" / "▼" of the Filters button: the Segoe UI Emoji glyph, a
        // light blue rounded square with a white triangle.
        private sealed class FilterGlyph : Control
        {
            private bool _expanded;

            public FilterGlyph()
            {
                Width = 14;
                Height = 14;
                VerticalAlignment = VerticalAlignment.Center;
            }

            public bool Expanded
            {
                get => _expanded;
                set
                {
                    _expanded = value;
                    InvalidateVisual();
                }
            }

            public override void Render(DrawingContext context)
            {
                context.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromRgb(20, 121, 192)), 1.5), new RoundedRect(new Rect(0.75, 0.75, 12.5, 12.5), 2.5));
                StreamGeometry triangle = new StreamGeometry();

                using (StreamGeometryContext path = triangle.Open())
                {
                    if (_expanded)
                    {
                        path.BeginFigure(new Point(3.5, 5), true);
                        path.LineTo(new Point(10.5, 5));
                        path.LineTo(new Point(7, 10));
                    }
                    else
                    {
                        path.BeginFigure(new Point(5, 3.5), true);
                        path.LineTo(new Point(10, 7));
                        path.LineTo(new Point(5, 10.5));
                    }

                    path.EndFigure(true);
                }

                context.DrawGeometry(Brushes.White, null, triangle);
            }
        }
    }
}
