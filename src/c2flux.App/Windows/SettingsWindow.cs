using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;

namespace c2flux
{
    // Port of the WinForms SettingsForm: General, UI, History, Export and
    // Logging pages behind tab buttons. Pages are rows sized by their text.
    // OK validates and writes the settings; the caller saves and applies
    // them when Saved is true. Windows-only options (NT query buffer,
    // elevation, Explorer context menu) are left out elsewhere.
    public sealed class SettingsWindow : Window
    {
        private static readonly (string Text, int Bytes)[] BufferSizes =
        {
            ("64 KiB", 64 * 1024),
            ("128 KiB", 128 * 1024),
            ("256 KiB", 256 * 1024),
            ("512 KiB", 512 * 1024),
            ("1 MiB", 1024 * 1024),
            ("2 MiB", 2 * 1024 * 1024),
            ("4 MiB", 4 * 1024 * 1024),
        };

        private readonly AppSettings _settings;
        private readonly List<(Button Tab, Control Page)> _pages = new List<(Button, Control)>();
        private readonly Border _pageHost;
        private readonly bool _windowsOptions;

        private readonly CheckBox _showFilesInTree = Check("Settings.ShowFilesInTree");
        private readonly ComboBox _bufferSize = Select();
        private readonly CheckBox _skipReparsePoints = Check("Settings.SkipReparsePoints");
        private readonly CheckBox _startElevated = Check("Settings.StartElevated");
        private readonly CheckBox _showElevationPrompt = Check("Settings.ShowElevationPrompt");
        private readonly CheckBox _shellContextMenu = Check("Settings.ShellContextMenu");
        private readonly CheckBox _shellSearchContextMenu = Check("Settings.ShellSearchContextMenu");
        private readonly CheckBox _autoCheckForUpdates = Check("Settings.AutoCheckForUpdates");
        private readonly TextBlock _redundancyCacheSize = Label();
        private readonly Button _clearRedundancyCache = Button("Settings.ClearRedundancyCache");
        private readonly ComboBox _language = Select();

        private readonly Button _fillColor = Button("Settings.SelectColor");
        private readonly Border _fillPreview = new Border { BorderThickness = new Thickness(1), BorderBrush = Brushes.Black };
        private readonly TextBox _barChartBarHeight = Number(3);
        private readonly TextBox _sunburstDepth = Number(2);
        private readonly TextBox _sunburstMaxItems = Number(5);
        private readonly CheckBox _showPartitionPanel = Check("Settings.ShowPartitionPanel");

        private readonly CheckBox _storageHistoryDetails = Check("Settings.StorageHistoryDetails");
        private readonly TextBlock _detailsDatabaseSize = Label();
        private readonly TextBlock _detailsReusableSpace = Label();
        private readonly CheckBox _autoCompact = Check("Settings.StorageHistoryDetailsAutoCompact");
        private readonly CheckBox _autoPurge = Check("Settings.StorageHistoryDetailsAutoPurge");
        private readonly TextBlock _maximumAgeLabel = Text("Settings.StorageHistoryDetailsAutoPurgeMaximumAgeDays");
        private readonly TextBox _maximumAge = Number(5);
        private readonly TextBlock _maximumSnapshotsLabel = Text("Settings.StorageHistoryDetailsAutoPurgeMaximumSnapshotsPerDrive");
        private readonly TextBox _maximumSnapshots = Number(5);

        private readonly CheckBox _exportPath = Check("Settings.ExportPath");
        private readonly CheckBox _exportSizeGb = Check("Settings.ExportSizeGb");
        private readonly CheckBox _exportSizeMb = Check("Settings.ExportSizeMb");
        private readonly NumericUpDown _exportMaxDepth = new NumericUpDown { Minimum = 0, Maximum = 100, ClipValueToMinMax = true, Increment = 1, FormatString = "0", ParsingNumberStyle = NumberStyles.None, Width = 80, Height = 36, Classes = { "ant" } };

        private readonly ComboBox _logLevel = Select();
        private readonly CheckBox _autoSaveLog = Check("Settings.AutoSaveLog");
        private readonly TextBlock _maximumLogSizeLabel = Text("Settings.MaximumLogFileSizeMb");
        private readonly TextBox _maximumLogSize = Number(5);
        private readonly TextBlock _maximumLogSizeUnit = Label("(MB)");

        private Color _fillColorValue;

        public SettingsWindow(AppSettings settings)
            : this(settings, OperatingSystem.IsWindows())
        {
        }

        // windowsOptions: show the Windows-only rows (tests compare the
        // Windows layout on every OS).
        internal SettingsWindow(AppSettings settings, bool windowsOptions)
        {
            _settings = settings;
            _windowsOptions = windowsOptions;
            _settings.ScanHistoryDatabasePath = ScanHistoryService.NormalizeDatabasePath(_settings.ScanHistoryDatabasePath);
            ScanHistoryService.ConfigureDatabasePath(_settings.ScanHistoryDatabasePath);

            Title = LocalizationService.GetText("Settings.Title");
            Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://c2flux.App/Assets/c2flux.png")));
            Width = 520;
            Height = 454;
            CanResize = false;
            CanMinimize = false;
            CanMaximize = false;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            _pageHost = new Border
            {
                BorderThickness = new Thickness(1),
                // BorderStyle.FixedSingle on the dark theme, measured.
                BorderBrush = new SolidColorBrush(Color.FromRgb(100, 100, 100)),
            };

            Canvas canvas = new Canvas();
            AddPage(canvas, "Settings.General", 18, 80, CreateGeneralPage());
            AddPage(canvas, "Settings.LayoutTab", 102, 80, CreateLayoutPage());
            AddPage(canvas, "Settings.Statistics", 186, 100, CreateStatisticsPage());
            AddPage(canvas, "Settings.Export", 290, 80, CreateExportPage());
            AddPage(canvas, "Settings.Logging", 374, 100, CreateLoggingPage());
            Place(canvas, _pageHost, 18, 54, 484, 338);

            Button ok = Button("Common.OK");
            Button cancel = Button("Common.Cancel");
            ok.IsDefault = true;
            cancel.IsCancel = true;
            ok.Click += async (_, _) => await OkAsync();
            cancel.Click += (_, _) => Close();
            Place(canvas, ok, 312, 406, 90, 32);
            Place(canvas, cancel, 412, 406, 90, 32);
            Content = canvas;

            LoadSettings();
            ShowPage(0);
        }

        // True once OK wrote the settings.
        public bool Saved { get; private set; }

        internal void ShowPage(int index)
        {
            for (int pageIndex = 0; pageIndex < _pages.Count; pageIndex++)
            {
                _pages[pageIndex].Tab.IsEnabled = pageIndex != index;
            }

            if (index == 2)
            {
                UpdateDetailsDatabaseInfo();
            }

            _pageHost.Child = _pages[index].Page;
        }

        private void AddPage(Canvas canvas, string textKey, double x, double width, Control page)
        {
            Button tab = Button(textKey);
            int index = _pages.Count;
            tab.Click += (_, _) => ShowPage(index);
            Place(canvas, tab, x, 16, width, 32);
            _pages.Add((tab, new ScrollViewer { Content = page, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled }));
        }

        // ----- pages ------------------------------------------------------

        private Control CreateGeneralPage()
        {
            bool windows = _windowsOptions;
            _bufferSize.ItemsSource = BufferSizes.Select(size => size.Text).ToList();
            _clearRedundancyCache.Click += (_, _) =>
            {
                RedundancyHashCacheService.Clear();
                UpdateRedundancyCacheInfo();
            };

            _language.Width = 216;
            _bufferSize.Width = 108;
            _clearRedundancyCache.MinWidth = 220;
            _clearRedundancyCache.HorizontalAlignment = HorizontalAlignment.Left;
            List<Control[]> rows = new List<Control[]>
            {
                new Control[] { Text("Settings.Language"), _language },
                new Control[] { _showFilesInTree },
            };

            if (windows)
            {
                rows.Add(new Control[] { Text("Settings.NtQueryDirectoryBufferSize"), _bufferSize });
            }

            rows.Add(new Control[] { _skipReparsePoints });

            if (windows)
            {
                rows.Add(new Control[] { _startElevated });
                rows.Add(new Control[] { _showElevationPrompt });
                rows.Add(new Control[] { _shellContextMenu });
                rows.Add(new Control[] { _shellSearchContextMenu });
            }

            rows.Add(new Control[] { _autoCheckForUpdates });
            rows.Add(new Control[] { _redundancyCacheSize });
            rows.Add(new Control[] { _clearRedundancyCache });
            return Rows(rows.ToArray());
        }

        private Control CreateLayoutPage()
        {
            _fillColor.Click += async (_, _) => await PickFillColorAsync();
            _fillColor.MinWidth = 140;
            _fillPreview.Width = 42;
            _fillPreview.Height = 28;
            _barChartBarHeight.Width = 80;
            _sunburstDepth.Width = 80;
            _sunburstMaxItems.Width = 80;

            return Rows(
                // One fill color per theme; the page shows the current one.
                new Control[] { Text(IsDark ? "Settings.PartitionFillDark" : "Settings.PartitionFillLight"), new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { _fillColor, _fillPreview } } },
                new Control[] { Text("Settings.BarChartBarHeight"), _barChartBarHeight, Label(LocalizationService.Format("Settings.BarChartBarHeightDefault", 14)) },
                new Control[] { Text("Settings.SunburstDepth"), _sunburstDepth, Text("Settings.SunburstDepthHint") },
                new Control[] { Text("Settings.SunburstMaxItems"), _sunburstMaxItems },
                new Control[] { _showPartitionPanel });
        }

        // The deprecated scan history settings (database path, scans per
        // path) are not shown: in WinForms they stayed visible on top of
        // these controls after their checkbox was removed.
        private Control CreateStatisticsPage()
        {
            _storageHistoryDetails.IsCheckedChanged += (_, _) =>
            {
                if (_storageHistoryDetails.IsChecked == true)
                {
                    _showFilesInTree.IsChecked = true;
                }

                _showFilesInTree.IsEnabled = _storageHistoryDetails.IsChecked != true;
                UpdateAutoPurgeControls();
            };
            _autoPurge.IsCheckedChanged += (_, _) => UpdateAutoPurgeControls();

            Button help = RoundButton("?");
            help.Classes.Add("primary");
            help.Focusable = false;
            help.Width = 24;
            help.Height = 22;
            help.VerticalAlignment = VerticalAlignment.Center;
            ToolTip.SetTip(help, LocalizationService.GetText("Settings.StorageHistoryDetailsHelp"));
            ToolTip.SetShowDelay(help, 0);
            _maximumAge.Width = 60;
            _maximumSnapshots.Width = 60;

            return Rows(
                new Control[] { new StackPanel { Orientation = Orientation.Horizontal, Children = { _storageHistoryDetails, help } } },
                new Control[] { _detailsDatabaseSize },
                new Control[] { _detailsReusableSpace },
                new Control[] { _autoCompact },
                new Control[] { _autoPurge },
                new Control[] { _maximumAgeLabel, _maximumAge },
                new Control[] { _maximumSnapshotsLabel, _maximumSnapshots });
        }

        private Control CreateExportPage()
        {
            // Digits only: a typed minus sign would just be clipped to 0 later.
            _exportMaxDepth.AddHandler(TextInputEvent, (_, e) => e.Handled = e.Text == null || !e.Text.All(char.IsAsciiDigit), Avalonia.Interactivity.RoutingStrategies.Tunnel);

            return Rows(
                new Control[] { _exportPath },
                new Control[] { _exportSizeGb },
                new Control[] { _exportSizeMb },
                new Control[] { Text("Settings.ExportMaxDepth"), _exportMaxDepth, Text("Settings.ExportMaxDepthHint") });
        }

        private Control CreateLoggingPage()
        {
            _logLevel.ItemsSource = new[] { AppLogLevel.Normal, AppLogLevel.Verbose };
            _autoSaveLog.IsCheckedChanged += (_, _) => UpdateLoggingControls();
            _logLevel.Width = 150;
            _maximumLogSize.Width = 56;

            return Rows(
                new Control[] { Text("Settings.LogLevel"), _logLevel },
                new Control[] { _autoSaveLog },
                new Control[] { _maximumLogSizeLabel, _maximumLogSize, _maximumLogSizeUnit });
        }

        // ----- state ------------------------------------------------------

        private bool IsDark => (Application.Current?.ActualThemeVariant ?? ThemeVariant.Dark) != ThemeVariant.Light;

        private void LoadSettings()
        {
            _showFilesInTree.IsChecked = _settings.ShowFilesInTree;
            int bufferIndex = Array.FindIndex(BufferSizes, size => size.Bytes == _settings.NtQueryDirectoryBufferSize);
            _bufferSize.SelectedIndex = bufferIndex >= 0 ? bufferIndex : BufferSizes.Length - 1;
            _skipReparsePoints.IsChecked = _settings.SkipReparsePoints;
            _showPartitionPanel.IsChecked = _settings.ShowPartitionPanel;
            _startElevated.IsChecked = _settings.StartElevatedOnStartup;
            _showElevationPrompt.IsChecked = _settings.ShowElevationPromptOnStartup;
            _shellContextMenu.IsChecked = _settings.ShellContextMenuEnabled;
            _shellSearchContextMenu.IsChecked = _settings.ShellSearchContextMenuEnabled;
            _autoCheckForUpdates.IsChecked = _settings.AutoCheckForUpdates;
            _exportPath.IsChecked = _settings.ExportPath;
            _exportSizeGb.IsChecked = _settings.ExportSizeGb;
            _exportSizeMb.IsChecked = _settings.ExportSizeMb;
            _exportMaxDepth.Value = _settings.ExportMaxDepth is int depth ? Math.Clamp(depth, 0, 100) : null;
            _barChartBarHeight.Text = _settings.BarChartBarHeight.ToString();
            _sunburstDepth.Text = _settings.SunburstDepth.ToString();
            _sunburstMaxItems.Text = _settings.SunburstMaxItems.ToString();
            _storageHistoryDetails.IsChecked = _settings.StorageHistoryDetailsEnabled;
            _autoCompact.IsChecked = _settings.StorageHistoryDetailsAutoCompactEnabled;
            _autoPurge.IsChecked = _settings.StorageHistoryDetailsAutoPurgeEnabled;
            _maximumAge.Text = _settings.StorageHistoryDetailsAutoPurgeMaximumAgeDays.ToString();
            _maximumSnapshots.Text = _settings.StorageHistoryDetailsAutoPurgeMaximumSnapshotsPerDrive.ToString();
            _logLevel.SelectedItem = _settings.LogLevel == AppLogLevel.Verbose ? AppLogLevel.Verbose : AppLogLevel.Normal;
            _autoSaveLog.IsChecked = _settings.AutoSaveLog;
            _maximumLogSize.Text = _settings.MaximumLogFileSizeMb.ToString();
            _fillColorValue = Color.FromUInt32(unchecked((uint)(IsDark ? _settings.PartitionFillColorDarkArgb : _settings.PartitionFillColorLightArgb)));
            _fillPreview.Background = new SolidColorBrush(_fillColorValue);

            ReloadLanguageItems(_settings.LanguageCode);
            UpdateAutoPurgeControls();
            UpdateDetailsDatabaseInfo();
            UpdateLoggingControls();
            UpdateRedundancyCacheInfo();
        }

        private void UpdateAutoPurgeControls()
        {
            bool details = _storageHistoryDetails.IsChecked == true;
            bool autoPurge = details && _autoPurge.IsChecked == true;
            _detailsDatabaseSize.IsEnabled = details;
            _detailsReusableSpace.IsEnabled = details;
            _autoCompact.IsEnabled = details;
            _autoPurge.IsEnabled = details;
            _maximumAgeLabel.IsEnabled = autoPurge;
            _maximumAge.IsEnabled = autoPurge;
            _maximumSnapshotsLabel.IsEnabled = autoPurge;
            _maximumSnapshots.IsEnabled = autoPurge;
        }

        private void UpdateDetailsDatabaseInfo()
        {
            string unavailable = LocalizationService.GetText("Settings.DatabaseSizeUnavailable");
            bool known = StorageHistoryDetailsService.TryGetDatabaseStorageInfo(out long databaseSize, out long reusableSpace);
            _detailsDatabaseSize.Text = LocalizationService.Format("Settings.StorageHistoryDetailsDatabaseSize", known ? SizeFormatter.Format(databaseSize) : unavailable);
            _detailsReusableSpace.Text = LocalizationService.Format("Settings.StorageHistoryDetailsReusableSpace", known ? SizeFormatter.Format(reusableSpace) : unavailable);
        }

        private void UpdateLoggingControls()
        {
            bool autoSave = _autoSaveLog.IsChecked == true;
            _maximumLogSizeLabel.IsEnabled = autoSave;
            _maximumLogSize.IsEnabled = autoSave;
            _maximumLogSizeUnit.IsEnabled = autoSave;
        }

        private void UpdateRedundancyCacheInfo()
        {
            long size = RedundancyHashCacheService.GetCacheSizeBytes();
            _redundancyCacheSize.Text = LocalizationService.Format("Settings.RedundancyCacheSize", SizeFormatter.Format(size));
            _clearRedundancyCache.IsEnabled = size > 0;
        }

        private async Task PickFillColorAsync()
        {
            Color? color = await ColorDialog.ShowAsync(this, _fillColorValue);

            if (color.HasValue)
            {
                _fillColorValue = color.Value;
                _fillPreview.Background = new SolidColorBrush(_fillColorValue);
            }
        }

        // ----- languages --------------------------------------------------

        private sealed record LanguageItem(string Text, string Code)
        {
            public override string ToString() => Text;
        }

        private void ReloadLanguageItems(string selectedCode)
        {
            string normalized = LocalizationService.NormalizeLanguageCode(selectedCode);
            List<LanguageItem> items = LocalizationService.GetAvailableLanguageCodes()
                .Select(code => new LanguageItem(LocalizationService.GetLanguageDisplayName(code), code))
                .OrderBy(item => item.Text, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            _language.ItemsSource = items;
            _language.SelectedIndex = items.FindIndex(item => item.Code == normalized);
        }

        // ----- saving -----------------------------------------------------

        private async Task OkAsync()
        {
            if (await TrySaveAsync())
            {
                Saved = true;
                Close();
            }
        }

        // A number in range, or a warning that opens the field's page.
        private async Task<int?> ReadNumberAsync(TextBox box, int minimum, int maximum, string messageKey, int page)
        {
            if (int.TryParse(box.Text?.Trim(), out int value) && value >= minimum && value <= maximum)
            {
                return value;
            }

            await AppDialogs.ShowWarningOkAsync(this, LocalizationService.GetText(messageKey), Title);
            ShowPage(page);
            box.Focus();
            box.SelectAll();
            return null;
        }

        internal async Task<bool> TrySaveAsync()
        {
            int? maximumLogSize = await ReadNumberAsync(_maximumLogSize, 1, int.MaxValue, "Settings.MaximumLogFileSizeMbInvalid", 4);

            if (maximumLogSize == null)
            {
                return false;
            }

            int maximumAge = _settings.StorageHistoryDetailsAutoPurgeMaximumAgeDays;
            int maximumSnapshots = _settings.StorageHistoryDetailsAutoPurgeMaximumSnapshotsPerDrive;

            if (_autoPurge.IsChecked == true)
            {
                int? age = await ReadNumberAsync(_maximumAge, 1, int.MaxValue, "Settings.StorageHistoryDetailsAutoPurgeMaximumAgeDaysInvalid", 2);

                if (age == null)
                {
                    return false;
                }

                int? snapshots = await ReadNumberAsync(_maximumSnapshots, 1, int.MaxValue, "Settings.StorageHistoryDetailsAutoPurgeMaximumSnapshotsPerDriveInvalid", 2);

                if (snapshots == null)
                {
                    return false;
                }

                maximumAge = age.Value;
                maximumSnapshots = snapshots.Value;
            }

            int? barHeight = await ReadNumberAsync(_barChartBarHeight, 5, 30, "Settings.BarChartBarHeightInvalid", 1);
            int? sunburstDepth = barHeight == null ? null : await ReadNumberAsync(_sunburstDepth, 0, 50, "Settings.SunburstDepthInvalid", 1);
            int? sunburstMaxItems = sunburstDepth == null ? null : await ReadNumberAsync(_sunburstMaxItems, 100, 10000, "Settings.SunburstMaxItemsInvalid", 1);

            if (sunburstMaxItems == null)
            {
                return false;
            }

            _settings.ShowFilesInTree = _showFilesInTree.IsChecked == true;
            _settings.NtQueryDirectoryBufferSize = BufferSizes[Math.Max(0, _bufferSize.SelectedIndex)].Bytes;
            _settings.SkipReparsePoints = _skipReparsePoints.IsChecked == true;
            _settings.ShowPartitionPanel = _showPartitionPanel.IsChecked == true;
            _settings.StartElevatedOnStartup = _startElevated.IsChecked == true;
            _settings.ShowElevationPromptOnStartup = _showElevationPrompt.IsChecked == true;
            _settings.ShellContextMenuEnabled = _shellContextMenu.IsChecked == true;
            _settings.ShellSearchContextMenuEnabled = _shellSearchContextMenu.IsChecked == true;
            _settings.AutoCheckForUpdates = _autoCheckForUpdates.IsChecked == true;
            _settings.ExportPath = _exportPath.IsChecked == true;
            _settings.ExportSizeGb = _exportSizeGb.IsChecked == true;
            _settings.ExportSizeMb = _exportSizeMb.IsChecked == true;
            _settings.ExportMaxDepth = (int?)_exportMaxDepth.Value;

            if (IsDark)
            {
                _settings.PartitionFillColorDarkArgb = unchecked((int)_fillColorValue.ToUInt32());
                _settings.PartitionFillBrightnessDarkPercent = 100;
            }
            else
            {
                _settings.PartitionFillColorLightArgb = unchecked((int)_fillColorValue.ToUInt32());
                _settings.PartitionFillBrightnessLightPercent = 100;
            }

            _settings.BarChartBarHeight = barHeight.Value;
            _settings.SunburstDepth = sunburstDepth.Value;
            _settings.SunburstMaxItems = sunburstMaxItems.Value;
            _settings.StorageHistoryDetailsEnabled = _storageHistoryDetails.IsChecked == true;
            _settings.StorageHistoryDetailsAutoCompactEnabled = _autoCompact.IsChecked == true;
            _settings.StorageHistoryDetailsAutoPurgeEnabled = _autoPurge.IsChecked == true;
            _settings.StorageHistoryDetailsAutoPurgeMaximumAgeDays = maximumAge;
            _settings.StorageHistoryDetailsAutoPurgeMaximumSnapshotsPerDrive = maximumSnapshots;
            _settings.LogLevel = _logLevel.SelectedItem is AppLogLevel level ? level : AppLogLevel.Normal;
            _settings.AutoSaveLog = _autoSaveLog.IsChecked == true;
            _settings.MaximumLogFileSizeMb = maximumLogSize.Value;
            AppAlertLog.Configure(_settings.LogLevel, _settings.AutoSaveLog, _settings.MaximumLogFileSizeMb);

            if (_language.SelectedItem is LanguageItem language)
            {
                _settings.LanguageCode = LocalizationService.NormalizeLanguageCode(language.Code);
            }

            if (OperatingSystem.IsWindows())
            {
                try
                {
                    ShellContextMenuService.Apply(_settings.ShellContextMenuEnabled, _settings.ShellSearchContextMenuEnabled);
                }
                catch
                {
                    await AppDialogs.ShowWarningOkAsync(this, LocalizationService.GetText("Settings.ShellContextMenuFailed"), Title);
                    return false;
                }
            }

            return true;
        }

        // Ctrl+Shift+Alt+D: the debug window with the status symbols.
        protected override async void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            if (e.Key == Key.D && e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Alt))
            {
                e.Handled = true;
                await new DebugClassWindow().ShowDialog(this);
            }
        }

        // ----- controls ---------------------------------------------------

        // Label, field and hint columns as wide as their widest text. A lone
        // control has the whole row, and wraps if that is not enough.
        private static Grid Rows(params Control[][] rows)
        {
            Grid grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"), RowSpacing = 8, ColumnSpacing = 8, Margin = new Thickness(24, 22, 16, 16) };

            for (int row = 0; row < rows.Length; row++)
            {
                grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

                for (int column = 0; column < rows[row].Length; column++)
                {
                    Control control = rows[row][column];
                    Grid.SetRow(control, row);
                    Grid.SetColumn(control, column);
                    Grid.SetColumnSpan(control, column == rows[row].Length - 1 ? 3 - column : 1);
                    control.MinHeight = control is TextBlock ? 0 : 28;
                    control.VerticalAlignment = VerticalAlignment.Center;
                    control.HorizontalAlignment = column == 0 && rows[row].Length == 1 ? control.HorizontalAlignment : HorizontalAlignment.Left;
                    // In line with the box of a checkbox.
                    control.Margin = control is TextBlock && column == 0 ? new Thickness(5, 0, 0, 0) : default;
                    grid.Children.Add(control);
                }
            }

            return grid;
        }

        private static CheckBox Check(string textKey) => new CheckBox { Content = LocalizationService.GetText(textKey), Classes = { "ant" } };

        private static ComboBox Select() => new ComboBox { Classes = { "ant", "field" } };

        private static TextBlock Text(string textKey) => Label(LocalizationService.GetText(textKey));

        private static TextBlock Label(string text = null) => new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Classes = { "ant", "antd" } };

        // AntdUI.Input with TextAlign = Right; no limit for 0.
        private static TextBox Number(int maxLength) => new TextBox { MaxLength = maxLength, TextAlignment = TextAlignment.Right, Classes = { "ant" } };

        private static Button Button(string textKey)
        {
            return new Button
            {
                Content = LocalizationService.GetText(textKey),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                Classes = { "ant", "dialog" },
            };
        }

        private static Button RoundButton(string text)
        {
            return new Button
            {
                Content = text,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                Classes = { "ant", "dialog", "round" },
            };
        }

        private static void Place(Canvas canvas, Control control, double x, double y, double width, double height)
        {
            Canvas.SetLeft(control, x);
            Canvas.SetTop(control, y);
            control.Width = width;
            control.Height = height;
            canvas.Children.Add(control);
        }
    }
}
