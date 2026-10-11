using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;

namespace c2flux
{
    // Port of the WinForms SettingsForm: General, UI, History, Export and
    // Logging pages behind tab buttons, laid out at the WinForms coordinates.
    // OK validates and writes the settings; the caller saves and applies
    // them when Saved is true. Windows-only options (NT query buffer,
    // elevation, Explorer context menu) are left out elsewhere and the rows
    // below them move up.
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
        private bool _isLoadingLanguageItems;

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
        private readonly Button _addLanguage = RoundButton("+");
        private readonly Button _deleteLanguage = RoundButton("−");

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
        private readonly TextBox _exportMaxDepth = Number(0);

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
            _pages.Add((tab, page));
        }

        // ----- pages ------------------------------------------------------

        private Control CreateGeneralPage()
        {
            bool windows = _windowsOptions;
            _bufferSize.ItemsSource = BufferSizes.Select(size => size.Text).ToList();
            _language.SelectionChanged += (_, _) => OnLanguageChanged();
            _addLanguage.Click += async (_, _) => await AddLanguageAsync();
            _deleteLanguage.Click += async (_, _) => await DeleteLanguageAsync();
            ToolTip.SetTip(_addLanguage, LocalizationService.GetText("Settings.AddLanguage"));
            ToolTip.SetTip(_deleteLanguage, LocalizationService.GetText("Settings.DeleteLanguage"));
            _clearRedundancyCache.Click += (_, _) =>
            {
                RedundancyHashCacheService.Clear();
                UpdateRedundancyCacheInfo();
            };

            // Rows at their WinForms tops; a row left out moves the next ones
            // up by its own pitch.
            (double Top, bool Shown, (Control Control, double X, double Y, double Width, double Height)[] Controls)[] rows =
            {
                (24, true, new[] { Item(Text("Settings.Language"), 34, 26, 70, 32), Item(_language, 104, 24, 216, 32), Item(_addLanguage, 320, 24, 32, 32), Item(_deleteLanguage, 346, 24, 32, 32) }),
                (64, true, new[] { Item(_showFilesInTree, 24, 64, 420, 24) }),
                (100, windows, new[] { Item(Text("Settings.NtQueryDirectoryBufferSize"), 34, 102, 125, 28), Item(_bufferSize, 160, 100, 108, 32) }),
                (144, true, new[] { Item(_skipReparsePoints, 24, 144, 420, 24) }),
                (180, windows, new[] { Item(_startElevated, 24, 180, 420, 24) }),
                (216, windows, new[] { Item(_showElevationPrompt, 24, 216, 420, 24) }),
                (252, windows, new[] { Item(_shellContextMenu, 24, 252, 420, 24) }),
                (288, windows, new[] { Item(_shellSearchContextMenu, 24, 288, 420, 24) }),
                (324, true, new[] { Item(_autoCheckForUpdates, 24, 324, 420, 24) }),
                (360, true, new[] { Item(Box(_redundancyCacheSize), 34, 360, 390, 28), Item(_clearRedundancyCache, 34, 392, 220, 32) }),
            };

            Canvas page = new Canvas { Width = 460, Height = 480, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
            double shift = 0;

            for (int index = 0; index < rows.Length; index++)
            {
                if (!rows[index].Shown)
                {
                    shift += index + 1 < rows.Length ? rows[index + 1].Top - rows[index].Top : 0;
                    continue;
                }

                foreach (var item in rows[index].Controls)
                {
                    Place(page, item.Control, item.X, item.Y - shift, item.Width, item.Height);
                }
            }

            page.Height -= shift;
            return new ScrollViewer { Content = page, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        }

        private Control CreateLayoutPage()
        {
            _fillColor.Click += async (_, _) => await PickFillColorAsync();
            Canvas page = new Canvas();
            // One fill color per theme; the page shows the current one.
            Place(page, Text(IsDark ? "Settings.PartitionFillDark" : "Settings.PartitionFillLight"), 34, 24, 120, 28);
            Place(page, _fillColor, 150, 24, 140, 28);
            Place(page, _fillPreview, 300, 24, 42, 28);
            Place(page, Text("Settings.BarChartBarHeight"), 34, 62, 120, 28);
            Place(page, _barChartBarHeight, 150, 60, 56, 34);
            Place(page, Label(LocalizationService.Format("Settings.BarChartBarHeightDefault", 14)), 210, 62, 160, 28);
            Place(page, Text("Settings.SunburstDepth"), 34, 98, 120, 28);
            Place(page, _sunburstDepth, 150, 96, 56, 34);
            Place(page, Text("Settings.SunburstDepthHint"), 210, 98, 220, 28);
            Place(page, Text("Settings.SunburstMaxItems"), 34, 134, 120, 28);
            Place(page, _sunburstMaxItems, 150, 132, 80, 34);
            Place(page, _showPartitionPanel, 24, 172, 420, 24);
            return page;
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

            // The checkbox is as wide as its text; the help button follows it.
            double detailsWidth = 28 + Math.Ceiling(new FormattedText(
                LocalizationService.GetText("Settings.StorageHistoryDetails"),
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface(FontFamily.Default),
                12,
                null).WidthIncludingTrailingWhitespace);
            Button help = RoundButton("?");
            help.Classes.Add("primary");
            help.Focusable = false;
            ToolTip.SetTip(help, CreateDetailsHelp());
            ToolTip.SetShowDelay(help, 0);

            Canvas page = new Canvas();
            Place(page, _storageHistoryDetails, 24, 24, detailsWidth, 24);
            Place(page, help, 24 + detailsWidth, 25, 24, 22);
            Place(page, Box(_detailsDatabaseSize), 32, 60, 400, 24);
            Place(page, Box(_detailsReusableSpace), 32, 88, 400, 24);
            Place(page, _autoCompact, 24, 124, 250, 24);
            Place(page, _autoPurge, 24, 160, 250, 24);
            Place(page, Box(_maximumAgeLabel), 32, 196, 220, 32);
            Place(page, _maximumAge, 261, 196, 60, 32);
            Place(page, Box(_maximumSnapshotsLabel), 32, 232, 220, 32);
            Place(page, _maximumSnapshots, 261, 232, 60, 32);
            return page;
        }

        // The help tooltip: the text and a preview of the details view.
        private static Control CreateDetailsHelp()
        {
            Bitmap preview = new Bitmap(AssetLoader.Open(new Uri("avares://c2flux.App/Assets/scan-history-details.png")));
            double width = Math.Min(500, preview.Size.Width);

            return new StackPanel
            {
                Spacing = 8,
                Width = width,
                Children =
                {
                    new TextBlock { Text = LocalizationService.GetText("Settings.StorageHistoryDetailsHelp"), TextWrapping = TextWrapping.Wrap },
                    new Image { Source = preview, Width = width, Height = Math.Round(preview.Size.Height * width / preview.Size.Width) },
                },
            };
        }

        private Control CreateExportPage()
        {
            _exportMaxDepth.MaxLength = 0;
            Canvas page = new Canvas();
            Place(page, _exportPath, 24, 24, 420, 24);
            Place(page, _exportSizeGb, 24, 60, 420, 24);
            Place(page, _exportSizeMb, 24, 96, 420, 24);
            Place(page, Text("Settings.ExportMaxDepth"), 34, 146, 160, 28);
            Place(page, _exportMaxDepth, 194, 144, 56, 34);
            return page;
        }

        private Control CreateLoggingPage()
        {
            _logLevel.ItemsSource = new[] { AppLogLevel.Normal, AppLogLevel.Verbose };
            _autoSaveLog.IsCheckedChanged += (_, _) => UpdateLoggingControls();
            Canvas page = new Canvas();
            Place(page, Text("Settings.LogLevel"), 34, 24, 75, 28);
            Place(page, _logLevel, 120, 22, 150, 32);
            Place(page, _autoSaveLog, 26, 64, 420, 24);
            Place(page, Box(_maximumLogSizeLabel), 34, 96, 96, 28);
            Place(page, _maximumLogSize, 126, 94, 56, 34);
            Place(page, Box(_maximumLogSizeUnit), 192, 96, 50, 28);
            return page;
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
            _exportMaxDepth.Text = _settings.ExportMaxDepth?.ToString() ?? string.Empty;
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
            _isLoadingLanguageItems = true;

            try
            {
                List<LanguageItem> items = LocalizationService.GetAvailableLanguageCodes()
                    .Select(code => new LanguageItem(LocalizationService.GetLanguageDisplayName(code), code))
                    .OrderBy(item => item.Text, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
                _language.ItemsSource = items;
                int index = items.FindIndex(item => string.Equals(item.Code, normalized, StringComparison.OrdinalIgnoreCase));
                _language.SelectedIndex = index >= 0 ? index : items.Count > 0 ? 0 : -1;
            }
            finally
            {
                _isLoadingLanguageItems = false;
            }

            UpdateDeleteLanguage();
        }

        private void UpdateDeleteLanguage()
        {
            _deleteLanguage.IsEnabled = _language.SelectedItem is LanguageItem item && !LocalizationService.IsBuiltInLanguage(item.Code);
        }

        private async void OnLanguageChanged()
        {
            UpdateDeleteLanguage();

            if (_isLoadingLanguageItems || _language.SelectedItem is not LanguageItem item || LocalizationService.CanLoadLanguage(item.Code))
            {
                return;
            }

            await AppDialogs.ShowWarningOkAsync(this, "The selected language file could not be loaded. English will be used instead.", LocalizationService.GetText("Common.Warning"));
            ReloadLanguageItems(LocalizationService.EnglishLanguageCode);
        }

        private async Task AddLanguageAsync()
        {
            if (!await AppDialogs.ShowWarningYesNoAsync(this, LocalizationService.GetText("Settings.AddLanguageWarning"), LocalizationService.GetText("Common.Warning")))
            {
                return;
            }

            string directory = LocalizationService.GetSettingsDirectoryPath();
            Directory.CreateDirectory(directory);
            string file = await FileDialogs.OpenAsync(this, LocalizationService.GetText("Settings.AddLanguage"), LocalizationService.GetText("Settings.LanguageFileFilter"), directory);

            if (file == null)
            {
                return;
            }

            string code = GetLanguageCodeFromFileName(Path.GetFileName(file));

            if (code == null || !IsValidLanguageFile(file))
            {
                await AppDialogs.ShowWarningOkAsync(this, LocalizationService.GetText("Settings.InvalidLanguageFile"), LocalizationService.GetText("Common.Warning"));
                return;
            }

            try
            {
                string source = Path.GetFullPath(file);
                string target = Path.GetFullPath(LocalizationService.GetLanguageFilePath(code));

                if (!string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
                {
                    File.Copy(source, target, true);
                }

                ReloadLanguageItems(code);
            }
            catch (Exception exception)
            {
                await AppDialogs.ShowErrorOkAsync(this, LocalizationService.GetText("Settings.LanguageImportFailed") + Environment.NewLine + Environment.NewLine + exception.Message);
            }
        }

        private async Task DeleteLanguageAsync()
        {
            if (_language.SelectedItem is not LanguageItem item || LocalizationService.IsBuiltInLanguage(item.Code) ||
                !await AppDialogs.ShowWarningYesNoAsync(this, LocalizationService.Format("Settings.DeleteLanguageConfirm", item.Text), LocalizationService.GetText("Common.Warning")))
            {
                return;
            }

            try
            {
                string path = LocalizationService.GetLanguageFilePath(item.Code);

                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                ReloadLanguageItems(LocalizationService.EnglishLanguageCode);
            }
            catch
            {
                await AppDialogs.ShowErrorOkAsync(this, LocalizationService.GetText("Settings.LanguageDeleteFailed"));
            }
        }

        // "lang_<code>.json" with a code that is already normalized.
        internal static string GetLanguageCodeFromFileName(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName) ||
                !fileName.StartsWith("lang_", StringComparison.OrdinalIgnoreCase) ||
                !fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
                fileName.Length <= 10)
            {
                return null;
            }

            string code = fileName.Substring(5, fileName.Length - 10);
            string normalized = LocalizationService.NormalizeLanguageCode(code);
            return string.Equals(normalized, code, StringComparison.OrdinalIgnoreCase) ? normalized : null;
        }

        private static bool IsValidLanguageFile(string path)
        {
            try
            {
                Dictionary<string, string> texts = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path));
                return texts != null && texts.Count > 0;
            }
            catch
            {
                return false;
            }
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

        private async Task<bool> TrySaveAsync()
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

            int? exportMaxDepth = null;

            if (!string.IsNullOrWhiteSpace(_exportMaxDepth.Text))
            {
                exportMaxDepth = await ReadNumberAsync(_exportMaxDepth, 0, int.MaxValue, "Settings.ExportMaxDepthInvalid", 3);

                if (exportMaxDepth == null)
                {
                    return false;
                }
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
            _settings.ExportMaxDepth = exportMaxDepth;

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

        private static (Control, double, double, double, double) Item(Control control, double x, double y, double width, double height) => (control, x, y, width, height);

        private static CheckBox Check(string textKey) => new CheckBox { Content = LocalizationService.GetText(textKey), Classes = { "ant" } };

        private static ComboBox Select() => new ComboBox { Classes = { "ant", "field" } };

        private static TextBlock Text(string textKey) => Label(LocalizationService.GetText(textKey));

        private static TextBlock Label(string text = null) => new TextBlock { Text = text, Classes = { "ant", "antd" } };

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

        // A label box (AntdUI.Label, MiddleLeft): text centered vertically.
        private static Border Box(TextBlock text)
        {
            text.VerticalAlignment = VerticalAlignment.Center;
            return new Border { Child = text };
        }

        private static void Place(Canvas canvas, Control control, double x, double y, double width, double height)
        {
            if (control is TextBlock text)
            {
                control = Box(text);
            }

            Canvas.SetLeft(control, x);
            Canvas.SetTop(control, y);
            control.Width = width;
            control.Height = height;
            canvas.Children.Add(control);
        }
    }

    // WinForms ColorDialog (FullOpen): a color view with OK and Cancel.
    public static class ColorDialog
    {
        public static async Task<Color?> ShowAsync(Window owner, Color color)
        {
            ColorView view = new ColorView { Color = color, IsAlphaVisible = false, IsAlphaEnabled = false };
            Color? result = null;
            Window dialog = new Window
            {
                Title = owner.Title,
                Width = 560,
                Height = 420,
                CanResize = false,
                CanMinimize = false,
                CanMaximize = false,
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
            };
            Button ok = new Button { Content = LocalizationService.GetText("Common.OK"), Width = 90, Height = 32, IsDefault = true, HorizontalContentAlignment = HorizontalAlignment.Center, Classes = { "ant", "dialog", "primary" } };
            Button cancel = new Button { Content = LocalizationService.GetText("Common.Cancel"), Width = 90, Height = 32, IsCancel = true, HorizontalContentAlignment = HorizontalAlignment.Center, Classes = { "ant", "dialog" } };
            ok.Click += (_, _) => { result = view.Color; dialog.Close(); };
            cancel.Click += (_, _) => dialog.Close();
            StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 10, Margin = new Thickness(0, 10, 0, 0), Children = { ok, cancel } };
            DockPanel.SetDock(buttons, Dock.Bottom);
            dialog.Content = new DockPanel { Margin = new Thickness(18, 16), Children = { buttons, view } };
            await dialog.ShowDialog(owner);
            return result;
        }
    }
}
