using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace c2flux
{
    // Port of the WinForms MainForm and its controllers (status, tree, layout,
    // drive list, partitions). One scan session per scanned path: switching
    // drives shows that drive's session, running or finished.
    public partial class MainWindow : Window
    {
        private readonly AppSettings _settings;
        private GridLength _partitionPanelHeight = new GridLength(180);
        private SearchWindow _searchWindow;
        private StorageHistoryView _storageHistory;
        private readonly ScannerPipeline _scannerPipeline;
        private readonly IStorageHistorySnapshotSource _storageHistorySnapshotSource;
        private readonly Dictionary<string, ScanSession> _sessions = new Dictionary<string, ScanSession>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ScanProgress> _pendingLiveTree = new Dictionary<string, ScanProgress>(StringComparer.OrdinalIgnoreCase);
        private readonly DispatcherTimer _liveTreeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        private readonly ObservableCollection<DriveItem> _drives = new ObservableCollection<DriveItem>();
        private readonly EntryTable _table = new EntryTable();
        private readonly PieChart _pie = new PieChart();
        private readonly BarChart _bar = new BarChart();
        private readonly Sunburst _sunburst = new Sunburst();
        private readonly TreemapView _treemap = new TreemapView();
        private readonly PartitionList _partitions;
        private readonly Dictionary<ViewMode, (ToggleButton Button, Control View)> _views;
        private readonly ExportActions _export;
        private NativeMenuItem _menuSaveScan;
        private NativeMenuItem _menuExport;
        private FileSystemEntry _currentRootEntry;
        private FileSystemEntry _selectedEntry;
        private ViewMode _viewMode;
        private bool _suppressDriveSelection;

        public MainWindow()
            : this(AppSettings.Load())
        {
        }

        public MainWindow(AppSettings settings)
        {
            _settings = settings;
            InitializeComponent();

            _scannerPipeline =
                OperatingSystem.IsWindows() ? WindowsScanners.CreatePipeline(settings)
                : OperatingSystem.IsMacOS() ? MacScanners.CreatePipeline(settings)
                : LinuxScanners.CreatePipeline(settings);
            _storageHistorySnapshotSource = OperatingSystem.IsWindows()
                ? new WindowsStorageHistorySnapshotSource(settings)
                : new ScanResultStorageHistorySnapshotSource();

            _partitions = new PartitionList(settings);
            PartitionHost.Content = _partitions;
            _partitions.SelectedVolumeChanged += UpdateStatusForDrive;

            _views = new Dictionary<ViewMode, (ToggleButton, Control)>
            {
                [ViewMode.Table] = (TableButton, _table),
                [ViewMode.PieChart] = (PieButton, _pie),
                [ViewMode.BarChart] = (BarButton, _bar),
                [ViewMode.Sunburst] = (SunburstButton, _sunburst),
                [ViewMode.Treemap] = (TreemapButton, _treemap),
            };

            foreach (var (mode, (button, view)) in _views)
            {
                ViewHost.Children.Add(view);
                button.Click += (_, _) => SetViewMode(mode, save: true);
            }

            _storageHistory = new StorageHistoryView(settings) { IsVisible = false };
            ViewHost.Children.Add(_storageHistory);
            AnalysisButton.Click += (_, _) =>
            {
                if (AnalysisButton.IsChecked == true)
                {
                    ShowAnalysis(CurrentSession);
                    AnalysisButton.IsChecked = CurrentSession?.AnalysisView?.IsVisible == true;
                }
                else
                {
                    SetViewMode(_viewMode, save: false);
                }
            };
            StorageHistoryButton.Click += (_, _) =>
            {
                if (StorageHistoryButton.IsChecked == true)
                {
                    ShowStorageHistory();
                }
                else
                {
                    SetViewMode(_viewMode, save: false);
                }
            };

            _table.SetShowFiles(settings.ShowFilesInTree);
            _bar.BarHeight = settings.BarChartBarHeight;
            _sunburst.SetDisplayOptions(settings.SunburstDepth, settings.SunburstMaxItems);
            _treemap.EntryActivated += entry => Tree.SelectEntry(entry);
            Tree.SelectedEntryChanged += OnSelectedEntryChanged;

            DriveSelect.ItemsSource = _drives;
            // Volumes show their icon, added folders the folder icon.
            DriveSelect.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<DriveItem>((drive, _) => drive == null ? null : new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    new Image { Width = 16, Height = 16, Source = Volumes.Find(drive.RootPath) != null ? FileIconCache.Volume(drive.RootPath) : FileIconCache.Folder },
                    new TextBlock { Text = drive.DisplayName, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center },
                },
            });
            DriveSelect.SelectionChanged += OnDriveSelectionChanged;
            ScanButton.Click += OnScanClick;
            PauseButton.Click += OnPauseClick;
            OpenFolderButton.Click += OnOpenFolderClick;
            SearchButton.Click += (_, _) => OpenSearch(null);
            _liveTreeTimer.Tick += (_, _) => FlushLiveTree();

            _export = new ExportActions(settings, this, SetStatusText);
            ExportButton.Click += async (_, _) => await _export.ExportAsync(_currentRootEntry);
            Tree.EntryPressed += OnTreeEntryPressed;
            BuildMenu();

            // Set from code only: a binding here would overwrite the summary
            // whenever the language changes.
            SetStatusText(LocalizationService.GetText("Common.Ready"));
            ConfigureToolbar();
            BuildAlertCounters();
            AppAlertLog.Changed += OnAlertLogChanged;

            ApplyWindowSettings();
            ApplyPartitionPanelVisibility();
            SetViewMode(settings.SelectedViewMode, save: false);
            SetScanningState(false);
            _ = LoadDrivesAsync();
            _ = _partitions.LoadAsync();
        }

        // File, View, Tools and Help. NativeMenuBar shows it inside the window
        // on Windows and Linux; on macOS it goes to the system menu bar.
        private void BuildMenu()
        {
            NativeMenuItem Item(string key, Action click, bool enabled = true)
            {
                NativeMenuItem item = new NativeMenuItem { IsEnabled = enabled };
                item.Bind(NativeMenuItem.HeaderProperty, new TExtension(key).ProvideValue(null) as Avalonia.Data.BindingBase);
                item.Click += (_, _) => click();
                return item;
            }

            NativeMenuItem Submenu(string key, params NativeMenuItemBase[] items)
            {
                NativeMenu menu = new NativeMenu();

                foreach (NativeMenuItemBase item in items)
                {
                    menu.Items.Add(item);
                }

                NativeMenuItem header = new NativeMenuItem { Menu = menu };
                header.Bind(NativeMenuItem.HeaderProperty, new TExtension(key).ProvideValue(null) as Avalonia.Data.BindingBase);
                return header;
            }

            NativeMenu main = new NativeMenu
            {
                Submenu(
                    "Menu.File",
                    Item("Menu.NewScan", () => OnScanClick(this, EventArgs.Empty)),
                    _menuSaveScan = Item("Menu.SaveScanResult", async () => await SaveScanResultAsync()),
                    Item("Menu.LoadScanResult", async () => await LoadScanResultAsync()),
                    new NativeMenuItemSeparator(),
                    _menuExport = Item("Menu.ExportCsv", async () => await _export.ExportAsync(_currentRootEntry)),
                    new NativeMenuItemSeparator(),
                    // shortcut: disabled until SettingsForm is ported (5.3).
                    Item("Menu.Settings", async () => await ShowSettingsAsync()),
                    new NativeMenuItemSeparator(),
                    Item("Menu.Exit", Close)),
                Submenu(
                    "Menu.View",
                    Item("Toolbar.Table", () => SetViewMode(ViewMode.Table, save: true)),
                    Item("Toolbar.PieChart", () => SetViewMode(ViewMode.PieChart, save: true)),
                    Item("Toolbar.BarChart", () => SetViewMode(ViewMode.BarChart, save: true)),
                    Item("Toolbar.Sunburst", () => SetViewMode(ViewMode.Sunburst, save: true)),
                    Item("Toolbar.Treemap", () => SetViewMode(ViewMode.Treemap, save: true)),
                    new NativeMenuItemSeparator(),
                    Item("Menu.Analysis", () => ShowAnalysis(CurrentSession)),
                    Item("Menu.SpaceHistory", ShowStorageHistory)),
                Submenu(
                    "Menu.Tools",
                    Item("Search.Title", () => OpenSearch(null))),
                Submenu(
                    "Menu.Help",
                    Item("Menu.OnlineHelp", () => FileManager.Open(AppConstants.HelpUrl)),
                    new NativeMenuItemSeparator(),
                    Item("Menu.About", async () => await ShowAboutAsync())),
            };

            NativeMenu.SetMenu(this, main);
        }

        internal Task ShowAboutAsync() => new AboutWindow(_settings).ShowDialog(this);

        // The search window, one at a time; initialDrivePath preselects a
        // drive (Explorer "c² flux: Search").
        internal void OpenSearch(string initialDrivePath)
        {
            if (_searchWindow != null)
            {
                _searchWindow.WindowState = _searchWindow.WindowState == WindowState.Minimized ? WindowState.Normal : _searchWindow.WindowState;
                _searchWindow.Activate();
                return;
            }

            _searchWindow = new SearchWindow(_settings, () => _currentRootEntry, ScanForSearchAsync, initialDrivePath);
            _searchWindow.Closed += (_, _) => _searchWindow = null;
            _searchWindow.Show(this);
        }

        // Scans a drive for the search; its result, unless the scan was
        // canceled.
        private async Task<FileSystemEntry> ScanForSearchAsync(string rootPath)
        {
            if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
            {
                return null;
            }

            await ScanPathAsync(rootPath);
            return _sessions.TryGetValue(NormalizeScanPath(rootPath), out ScanSession session) && !session.IsRunning && !session.WasCanceled
                ? session.RootEntry
                : null;
        }

        // Settings: on OK, saves them and applies what changed (files in the
        // tree, the partition panel, chart options, language).
        internal async Task ShowSettingsAsync()
        {
            bool previousShowFiles = _settings.ShowFilesInTree;
            string previousLanguage = _settings.LanguageCode;
            SettingsWindow window = new SettingsWindow(_settings);
            await window.ShowDialog(this);

            if (!window.Saved)
            {
                return;
            }

            if (previousShowFiles != _settings.ShowFilesInTree)
            {
                bool showFiles = _settings.ShowFilesInTree;

                foreach (ScanSession session in _sessions.Values.Where(session => session.RootEntry != null))
                {
                    FileSystemEntry previous = session.RootEntry;
                    session.RootEntry = await Task.Run(() => CopyWithShowFiles(previous, showFiles));
                    Tree.UpdateRootEntry(session.RootEntry);

                    if (ReferenceEquals(_currentRootEntry, previous))
                    {
                        _currentRootEntry = session.RootEntry;
                    }
                }

                _table.SetShowFiles(showFiles);
            }

            _settings.Save();

            if (!string.Equals(previousLanguage, _settings.LanguageCode, StringComparison.OrdinalIgnoreCase))
            {
                LocalizationService.Load(_settings.LanguageCode);
            }

            await LoadDrivesAsync();
            _bar.BarHeight = _settings.BarChartBarHeight;
            ApplyPartitionPanelVisibility();
            _partitions.InvalidateVisual();

            if (_currentRootEntry != null)
            {
                RenderScanResult(_currentRootEntry);
            }
        }

        // A copy of a scanned tree with only its folders, plus (showFiles)
        // every file under its parent folder: how WinForms applies "Show
        // files in tree" to results already scanned.
        internal static FileSystemEntry CopyWithShowFiles(FileSystemEntry root, bool showFiles)
        {
            Dictionary<string, FileSystemEntry> directories = new Dictionary<string, FileSystemEntry>(
                OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            string Key(string path) => Path.TrimEndingDirectorySeparator(EntryPaths.ToNativeSeparators(path));

            FileSystemEntry Copy(FileSystemEntry source)
            {
                FileSystemEntry copy = new FileSystemEntry
                {
                    Name = source.Name,
                    FullPath = source.FullPath,
                    SizeBytes = source.SizeBytes,
                    IsDirectory = source.IsDirectory,
                    LastWriteTimeUtc = source.LastWriteTimeUtc,
                };

                lock (source.AllFiles)
                {
                    copy.AllFiles = new List<FileSystemEntry>(source.AllFiles);
                }

                directories[Key(source.FullPath)] = copy;
                List<FileSystemEntry> children;

                lock (source.Children)
                {
                    children = source.Children.FindAll(child => child != null && child.IsDirectory);
                }

                foreach (FileSystemEntry child in children)
                {
                    copy.Children.Add(Copy(child));
                }

                return copy;
            }

            FileSystemEntry copiedRoot = Copy(root);

            if (showFiles)
            {
                foreach (FileSystemEntry file in copiedRoot.AllFiles)
                {
                    string parent = file == null || file.IsDirectory ? null : Path.GetDirectoryName(file.FullPath);

                    if (!string.IsNullOrWhiteSpace(parent) && directories.TryGetValue(Key(parent), out FileSystemEntry parentEntry))
                    {
                        parentEntry.Children.Add(file);
                    }
                }
            }

            return copiedRoot;
        }

        // ----- drives -----------------------------------------------------

        private async Task LoadDrivesAsync()
        {
            IReadOnlyList<VolumeInfo> volumes = await Task.Run(Volumes.List);

            _suppressDriveSelection = true;

            try
            {
                foreach (VolumeInfo volume in volumes)
                {
                    string label = string.IsNullOrWhiteSpace(volume.Label) ? LocalizationService.GetText("Drive.LocalDisk") : volume.Label;
                    _drives.Add(new DriveItem(volume.RootPath, volume.RootPath + "  " + label));
                }

                DriveSelect.SelectedIndex = _drives.Count > 0 ? 0 : -1;
            }
            finally
            {
                _suppressDriveSelection = false;
            }

            if (_drives.Count > 0)
            {
                UpdateStatusForDrive(_drives[0].RootPath);
            }
        }

        private string SelectedScanPath => (DriveSelect.SelectedItem as DriveItem)?.RootPath ?? string.Empty;

        // Selects path in the drive list, adding it for a folder.
        private void AddOrSelectPath(string path)
        {
            string fullPath = Path.GetFullPath(path);
            DriveItem item = _drives.FirstOrDefault(drive => PathsEqual(drive.RootPath, fullPath));

            if (item == null)
            {
                item = new DriveItem(fullPath, fullPath);
                _drives.Add(item);
            }

            _suppressDriveSelection = true;

            try
            {
                DriveSelect.SelectedItem = item;
            }
            finally
            {
                _suppressDriveSelection = false;
            }

            UpdateStatusForDrive(fullPath);
        }

        // Picking a drive shows its session, or scans it the first time.
        private async void OnDriveSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressDriveSelection || DriveSelect.SelectedItem is not DriveItem drive)
            {
                return;
            }

            if (!Directory.Exists(drive.RootPath))
            {
                await AppDialogs.ShowWarningOkAsync(this, LocalizationService.GetText("Message.PathNotFoundPrefix") + drive.RootPath);
                return;
            }

            if (_sessions.ContainsKey(NormalizeScanPath(drive.RootPath)))
            {
                ShowScanSession(drive.RootPath);
                return;
            }

            await StartScanAsync(drive.RootPath);
        }

        private async void OnOpenFolderClick(object sender, RoutedEventArgs e)
        {
            IReadOnlyList<IStorageFolder> folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = LocalizationService.GetText("Dialog.SelectFolder"),
                AllowMultiple = false,
            });
            string path = folders.Count == 0 ? null : folders[0].TryGetLocalPath();

            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            AddOrSelectPath(path);
            await StartScanAsync(path);
        }

        // ----- scanning ---------------------------------------------------

        private async void OnScanClick(object sender, EventArgs e)
        {
            string rootPath = SelectedScanPath;

            if (string.IsNullOrWhiteSpace(rootPath))
            {
                await AppDialogs.ShowWarningOkAsync(this, LocalizationService.GetText("Message.NoPathSelected"));
                return;
            }

            // The scan button cancels a running scan.
            if (_sessions.TryGetValue(NormalizeScanPath(rootPath), out ScanSession running) && running.IsRunning)
            {
                running.Cancellation.Cancel();
                return;
            }

            if (!Directory.Exists(rootPath))
            {
                await AppDialogs.ShowWarningOkAsync(this, LocalizationService.GetText("Message.PathNotFoundPrefix") + rootPath);
                return;
            }

            AddOrSelectPath(rootPath);
            await StartScanAsync(rootPath);
        }

        private void OnPauseClick(object sender, RoutedEventArgs e)
        {
            if (!_sessions.TryGetValue(NormalizeScanPath(SelectedScanPath), out ScanSession session) || !session.IsRunning)
            {
                return;
            }

            if (session.Pause.IsPaused)
            {
                session.Pause.Resume();
                PauseIcon.Kind = ToolbarIconKind.Pause;
                SetStatusText(LocalizationService.GetText("Status.NtQueryRunning"));
            }
            else
            {
                session.Pause.Pause();
                PauseIcon.Kind = ToolbarIconKind.Scan;
                SetStatusText(LocalizationService.GetText("Status.ScanPaused"));
            }
        }

        // For tests: select path and scan it as the toolbar would.
        internal Task ScanPathAsync(string path)
        {
            AddOrSelectPath(path);
            return StartScanAsync(path);
        }

        internal EntryTree EntryTree => Tree;

        internal string StatusLine => StatusText.Text;

        private bool _fullDiskAccessChecked;

        // macOS hides folders like ~/Library/Mail from apps without Full Disk
        // Access; a scan that covers the home folder would silently miss them.
        // Asked once per run, for scans that include the home folder.
        private async Task CheckFullDiskAccessAsync(string rootPath)
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            if (!OperatingSystem.IsMacOS() || _fullDiskAccessChecked || !EntryTreeCanvas.IsSameOrDescendantPath(home, rootPath) || HasFullDiskAccess(home))
            {
                return;
            }

            _fullDiskAccessChecked = true;
            AppAlertLog.AddWarning(LocalizationService.GetText("Alert.Scan"), LocalizationService.Format("MacOS.FullDiskAccessMessage", AppConstants.ApplicationName));

            bool openSettings = await AppDialogs.ShowWarningYesNoAsync(
                this,
                LocalizationService.Format("MacOS.FullDiskAccessMessage", AppConstants.ApplicationName),
                yesText: LocalizationService.GetText("MacOS.FullDiskAccessOpen"),
                noText: LocalizationService.GetText("MacOS.FullDiskAccessContinue"));

            if (openSettings)
            {
                FileManager.Open("x-apple.systempreferences:com.apple.preference.security?Privacy_AllFiles");
            }
        }

        // ~/Library/Safari is protected by TCC: listing it fails without Full
        // Disk Access. Without that folder there is nothing to tell.
        internal static bool HasFullDiskAccess(string home)
        {
            string probe = Path.Combine(home, "Library", "Safari");

            try
            {
                if (Directory.Exists(probe))
                {
                    using IEnumerator<string> entries = Directory.EnumerateFileSystemEntries(probe).GetEnumerator();
                    entries.MoveNext();
                }

                return true;
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException || exception is IOException)
            {
                return false;
            }
        }

        private async Task StartScanAsync(string rootPath)
        {
            await CheckFullDiskAccessAsync(rootPath);
            string normalizedRootPath = NormalizeScanPath(rootPath);
            ViewMode viewMode = _settings.SelectedViewMode;

            if (_sessions.TryGetValue(normalizedRootPath, out ScanSession existing))
            {
                viewMode = existing.ViewMode;

                if (existing.IsRunning)
                {
                    existing.Cancellation.Cancel();
                }

                if (existing.AnalysisView != null)
                {
                    existing.AnalysisView.Cancel();
                    ViewHost.Children.Remove(existing.AnalysisView);
                }
            }

            ScanSession session = new ScanSession(normalizedRootPath) { ViewMode = viewMode };
            _sessions[normalizedRootPath] = session;
            FileSystemEntry initialRoot = new FileSystemEntry { Name = normalizedRootPath, FullPath = normalizedRootPath, IsDirectory = true };
            session.RootEntry = initialRoot;
            _currentRootEntry = initialRoot;

            if (_storageHistory.IsVisible)
            {
                SetViewMode(_settings.SelectedViewMode, save: false);
            }

            // Until the scan reports real progress, the bar creeps from 0.5 %
            // to 3 % so the user sees it started.
            bool progressStarted = false;
            float pendingValue = 0.005F;
            DispatcherTimer pendingTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            pendingTimer.Tick += (_, _) =>
            {
                if (!progressStarted && IsCurrentSession(session) && IsSelectedScanPath(session.RootPath))
                {
                    pendingValue = Math.Min(0.03F, pendingValue + 0.002F);
                    SetScanProgressPending(pendingValue, session.Stopwatch.Elapsed);
                }
            };

            if (IsSelectedScanPath(session.RootPath))
            {
                SetScanProgressPending(pendingValue, session.Stopwatch.Elapsed);
                pendingTimer.Start();
            }

            session.TargetBytes = await Task.Run(() => GetUsedSpaceBytes(rootPath));
            _pendingLiveTree.Remove(normalizedRootPath);

            if (IsSelectedScanPath(session.RootPath))
            {
                SetViewMode(session.ViewMode, save: false);
            }

            RenderScanResult(initialRoot);
            SetScanningState(true);

            Progress<ScanProgress> progress = new Progress<ScanProgress>(scanProgress =>
            {
                if (!IsCurrentSession(session))
                {
                    return;
                }

                session.LatestProgress = scanProgress;
                session.SkippedDirectories = Math.Max(session.SkippedDirectories, scanProgress.SkippedDirectories);

                foreach (string detail in scanProgress.SkippedDirectoryDetails ?? new List<string>())
                {
                    if (session.SkippedDirectoryDetailSet.Add(detail))
                    {
                        session.SkippedDirectoryDetails.Add(detail);
                    }
                }

                if (IsSelectedScanPath(session.RootPath))
                {
                    double realPercent = session.TargetBytes <= 0 ? 0 : scanProgress.ScannedBytes * 100D / session.TargetBytes;

                    if (!progressStarted && realPercent >= pendingValue * 100)
                    {
                        progressStarted = true;
                        pendingTimer.Stop();
                    }

                    if (progressStarted)
                    {
                        UpdateSelectedScanStatus(session, scanProgress);
                    }
                }

                QueueLiveTreeUpdate(scanProgress);
            });

            try
            {
                FileSystemEntry rootEntry = await _scannerPipeline.ScanAsync(
                    rootPath,
                    progress,
                    session.Cancellation.Token,
                    session.Pause.Token,
                    statusKey =>
                    {
                        if (IsCurrentSession(session) && IsSelectedScanPath(session.RootPath))
                        {
                            SetStatusText(LocalizationService.GetText(statusKey));
                        }
                    });

                if (!IsCurrentSession(session))
                {
                    return;
                }

                ApplyVolumeSizeToRootEntry(rootPath, rootEntry);
                session.RootEntry = rootEntry;
                session.LatestProgress = null;
                FlushLiveTree();
                Tree.UpdateRootEntry(rootEntry);
                Task partitionReload = _partitions.LoadAsync();

                if (IsSelectedScanPath(session.RootPath))
                {
                    _currentRootEntry = rootEntry;
                    BindViews(rootEntry);
                    SetSelectedEntrySummary(rootEntry, rootEntry.AllFiles.Count);
                    SetScanProgress(100, session.Stopwatch.Elapsed, visible: true);
                    ScanAlerts.ReportSkippedDirectories(session.SkippedDirectories, session.SkippedDirectoryDetails);
                }

                await RecordStorageHistoryAsync(rootEntry, session);
                await SaveScanHistoryIfEnabledAsync(rootEntry, session);
                session.Stopwatch.Stop();
                await partitionReload;

                if (IsCurrentSession(session) && IsSelectedScanPath(session.RootPath))
                {
                    UpdateStatusForDrive(rootEntry.FullPath, rootEntry.AllFiles.Count);
                    SetScanProgress(100, session.Stopwatch.Elapsed, visible: true);
                }
            }
            catch (OperationCanceledException)
            {
                session.WasCanceled = true;

                if (IsCurrentSession(session) && IsSelectedScanPath(session.RootPath))
                {
                    session.Stopwatch.Stop();
                    SetStatusText(LocalizationService.GetText("Status.ScanCanceled"));
                    SetScanProgress(null, session.Stopwatch.Elapsed, visible: false);
                }
            }
            catch (Exception exception)
            {
                session.Stopwatch.Stop();
                AppAlertLog.AddError(LocalizationService.GetText("Alert.Scan"), exception.Message, "Path: " + rootPath + Environment.NewLine + exception);

                if (IsCurrentSession(session) && IsSelectedScanPath(session.RootPath))
                {
                    SetStatusText(LocalizationService.GetText("Common.Error") + ": " + exception.Message);
                    SetScanProgress(null, session.Stopwatch.Elapsed, visible: false);
                }
            }
            finally
            {
                pendingTimer.Stop();
                session.IsRunning = false;
                session.Pause.Dispose();
                session.Cancellation.Dispose();

                if (IsCurrentSession(session) && IsSelectedScanPath(session.RootPath))
                {
                    SetScanningState(false);
                }
            }
        }

        // The storage history record of the scanned path, with the file list
        // as its details when enabled (orange progress while saving).
        private async Task RecordStorageHistoryAsync(FileSystemEntry rootEntry, ScanSession session)
        {
            FileSystemEntry snapshot = rootEntry;
            bool showProgress = false;
            int lastPercent = -1;
            Progress<double> overall = new Progress<double>(percent =>
            {
                int shown = (int)Math.Round(Math.Clamp(percent, 0, 100));

                if (!showProgress || shown <= lastPercent || !IsCurrentSession(session) || !IsSelectedScanPath(session.RootPath))
                {
                    return;
                }

                lastPercent = shown;
                SetStorageHistoryDetailsProgress(shown / 100F, session.Stopwatch.Elapsed);
                Title = "Saving History details to SQLite-DB " + shown + "%";
            });
            IProgress<double> report = overall;

            try
            {
                if (_settings.StorageHistoryDetailsEnabled)
                {
                    showProgress = true;
                    report.Report(0);

                    try
                    {
                        FileSystemEntry captured = await _storageHistorySnapshotSource.CaptureAsync(
                            rootEntry.FullPath,
                            session.Cancellation.Token,
                            new Progress<double>(percent => report.Report(Math.Clamp(percent, 0, 100) * 0.25)));
                        snapshot = captured ?? snapshot;
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        try
                        {
                            int start = Math.Max(0, lastPercent);
                            long target = Math.Max(1, rootEntry.SizeBytes);
                            FileSystemEntry fallback = await _storageHistorySnapshotSource.CaptureFallbackAsync(
                                rootEntry.FullPath,
                                new Progress<ScanProgress>(scan => report.Report(start + (25 - start) * Math.Clamp((double)scan.ScannedBytes / target, 0, 1))),
                                session.Cancellation.Token,
                                session.Pause.Token);
                            report.Report(25);
                            snapshot = fallback ?? snapshot;
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception fallbackException)
                        {
                            AppAlertLog.AddError(
                                "StorageHistory",
                                "Storage History details snapshot could not be captured.",
                                "Path: " + rootEntry.FullPath + Environment.NewLine + "Snapshot error:" + Environment.NewLine + exception +
                                Environment.NewLine + Environment.NewLine + "Fallback snapshot error:" + Environment.NewLine + fallbackException);
                        }
                    }
                }

                DateTime? recordedAtUtc = StorageHistoryService.AddRecord(rootEntry.FullPath, rootEntry.SizeBytes);
                StorageHistoryRecorded(recordedAtUtc);

                if (recordedAtUtc.HasValue && _settings.StorageHistoryDetailsEnabled && snapshot != null)
                {
                    report.Report(25);
                    Progress<double> save = new Progress<double>(percent => report.Report(25 + Math.Clamp(percent, 0, 100) * 0.75));
                    await Task.Run(() => StorageHistoryDetailsService.AddSnapshot(
                        rootEntry.FullPath,
                        recordedAtUtc.Value,
                        snapshot,
                        save,
                        _settings.StorageHistoryDetailsAutoCompactEnabled,
                        _settings.StorageHistoryDetailsAutoPurgeEnabled,
                        _settings.StorageHistoryDetailsAutoPurgeMaximumAgeDays,
                        _settings.StorageHistoryDetailsAutoPurgeMaximumSnapshotsPerDrive));
                }
            }
            finally
            {
                showProgress = false;

                if (lastPercent >= 0 && IsCurrentSession(session) && IsSelectedScanPath(session.RootPath))
                {
                    Title = AppConstants.FullApplicationName;
                }
            }
        }

        private void StorageHistoryRecorded(DateTime? recordedAtUtc)
        {
            if (recordedAtUtc.HasValue && _storageHistory.IsVisible)
            {
                _storageHistory.RefreshHistory();
            }
        }

        private async Task SaveScanHistoryIfEnabledAsync(FileSystemEntry rootEntry, ScanSession session)
        {
            if (!_settings.SaveScanHistory)
            {
                return;
            }

            int percent = 0;
            DispatcherTimer elapsedTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            elapsedTimer.Tick += (_, _) =>
            {
                if (IsCurrentSession(session) && IsSelectedScanPath(session.RootPath))
                {
                    SetScanHistorySaveProgress(percent, session.Stopwatch.Elapsed);
                }
            };
            Progress<int> progress = new Progress<int>(value =>
            {
                percent = Math.Clamp(value, 0, 100);

                if (IsCurrentSession(session) && IsSelectedScanPath(session.RootPath))
                {
                    SetScanHistorySaveProgress(percent, session.Stopwatch.Elapsed);
                }
            });

            try
            {
                if (IsCurrentSession(session) && IsSelectedScanPath(session.RootPath))
                {
                    SetScanHistorySavingState();
                    SetScanHistorySaveProgress(0, session.Stopwatch.Elapsed);
                    elapsedTimer.Start();
                }

                await Task.Run(() => ScanHistoryService.Save(rootEntry, progress));
            }
            catch (Exception exception)
            {
                AppAlertLog.AddWarning(LocalizationService.GetText("Alert.Scan"), LocalizationService.Format("Alert.ScanHistorySaveFailed", exception.Message));
            }
            finally
            {
                elapsedTimer.Stop();
            }
        }

        private bool IsCurrentSession(ScanSession session)
        {
            return _sessions.TryGetValue(session.RootPath, out ScanSession current) && ReferenceEquals(current, session);
        }

        private bool IsSelectedScanPath(string rootPath) => PathsEqual(NormalizeScanPath(SelectedScanPath), rootPath);

        private void ShowScanSession(string rootPath)
        {
            _liveTreeTimer.Stop();
            _pendingLiveTree.Clear();

            if (!_sessions.TryGetValue(NormalizeScanPath(rootPath), out ScanSession session))
            {
                _currentRootEntry = null;
                Tree.ClearEntries();
                BindViews(null);
                SetScanningState(false);
                UpdateStatusForDrive(rootPath);
                return;
            }

            _currentRootEntry = session.RootEntry;

            if (session.RootEntry != null)
            {
                RenderScanResult(session.RootEntry);
            }
            else if (session.LatestProgress?.LiveRootEntry != null)
            {
                Tree.SetRootEntry(session.LatestProgress.LiveRootEntry);
                BindViews(session.LatestProgress.LiveRootEntry);
            }
            else
            {
                Tree.ClearEntries();
                BindViews(null);
            }

            SetScanningState(session.IsRunning);

            if (session.IsRunning && session.LatestProgress != null)
            {
                UpdateSelectedScanStatus(session, session.LatestProgress);
                QueueLiveTreeUpdate(session.LatestProgress);
            }
            else
            {
                UpdateStatusForDrive(rootPath);
            }

            RestoreSessionView(session);
        }

        private void UpdateSelectedScanStatus(ScanSession session, ScanProgress scanProgress)
        {
            FileSystemEntry statusEntry = scanProgress.LiveRootEntry ?? _selectedEntry ?? session.RootEntry;

            if (statusEntry != null)
            {
                SetSelectedEntrySummary(statusEntry, scanProgress.ScannedFiles);
            }

            if (session.TargetBytes <= 0)
            {
                SetScanProgressPending(0.03F, session.Stopwatch.Elapsed);
                return;
            }

            SetScanProgress(scanProgress.ScannedBytes * 100D / session.TargetBytes, session.Stopwatch.Elapsed, visible: true);
        }

        // The tree follows a running scan once a second.
        private void QueueLiveTreeUpdate(ScanProgress scanProgress)
        {
            string rootPath = scanProgress?.LiveRootEntry?.FullPath;

            if (string.IsNullOrWhiteSpace(rootPath))
            {
                return;
            }

            _pendingLiveTree[rootPath] = scanProgress;

            if (!_liveTreeTimer.IsEnabled)
            {
                _liveTreeTimer.Start();
            }
        }

        private void FlushLiveTree()
        {
            List<ScanProgress> pending = _pendingLiveTree.Values.ToList();
            _pendingLiveTree.Clear();

            foreach (ScanProgress scanProgress in pending)
            {
                Tree.UpdateRootEntry(scanProgress.LiveRootEntry);
            }

            if (_pendingLiveTree.Count == 0)
            {
                _liveTreeTimer.Stop();
            }
        }

        private void RenderScanResult(FileSystemEntry rootEntry)
        {
            TreeSortService.Sort(rootEntry, _settings.TreeSortMode);
            Tree.SetRootEntry(rootEntry);
            _treemap.SetRootEntry(rootEntry);
            BindViews(rootEntry);
        }

        private void BindViews(FileSystemEntry entry)
        {
            _table.SetEntry(entry);
            _pie.SetEntry(entry);
            _bar.SetEntry(entry);
            _sunburst.SetDisplayOptions(_settings.SunburstDepth, _settings.SunburstMaxItems);
            _sunburst.SetEntry(entry);
            _treemap.SetEntry(entry);
        }

        // A click in the tree shows that entry; an entry of another scanned
        // drive brings back that drive's session.
        private void OnSelectedEntryChanged(FileSystemEntry entry)
        {
            FileSystemEntry selectedRoot = entry == null ? null : Tree.GetRootEntry(entry);

            if (selectedRoot != null &&
                _sessions.TryGetValue(NormalizeScanPath(selectedRoot.FullPath), out ScanSession session) &&
                session.RootEntry != null &&
                !ReferenceEquals(_currentRootEntry, session.RootEntry))
            {
                _currentRootEntry = session.RootEntry;
                _treemap.SetRootEntry(session.RootEntry);
                SetScanningState(session.IsRunning);

                if (session.IsRunning && session.LatestProgress != null)
                {
                    UpdateSelectedScanStatus(session, session.LatestProgress);
                }
                else
                {
                    UpdateStatusForDrive(session.RootPath);
                }

                RestoreSessionView(session);
            }

            _selectedEntry = entry;
            BindViews(entry);
            SetSelectedEntrySummary(entry, GetFileCount(entry));
        }

        private int GetFileCount(FileSystemEntry entry)
        {
            if (entry == null || _currentRootEntry == null)
            {
                return 0;
            }

            if (!entry.IsDirectory)
            {
                return 1;
            }

            string prefix = NormalizeScanPath(entry.FullPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

            lock (_currentRootEntry.AllFiles)
            {
                return _currentRootEntry.AllFiles.Count(file =>
                    file != null && !file.IsDirectory && file.FullPath != null &&
                    file.FullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
            }
        }

        // ----- toolbar groups and button visibility ------------------------

        private (Control Button, string TextKey, Func<bool> Get, Action<bool> Set)[] ToolbarButtons => new (Control, string, Func<bool>, Action<bool>)[]
        {
            (ScanButton, "Toolbar.ScanButton", () => _settings.ToolbarScanButtonVisible, value => _settings.ToolbarScanButtonVisible = value),
            (PauseButton, "Toolbar.PauseButton", () => _settings.ToolbarPauseButtonVisible, value => _settings.ToolbarPauseButtonVisible = value),
            (OpenFolderButton, "Toolbar.OpenFolderButton", () => _settings.ToolbarOpenFolderButtonVisible, value => _settings.ToolbarOpenFolderButtonVisible = value),
            (TableButton, "Toolbar.TableButton", () => _settings.ToolbarTableButtonVisible, value => _settings.ToolbarTableButtonVisible = value),
            (PieButton, "Toolbar.PieChartButton", () => _settings.ToolbarPieChartButtonVisible, value => _settings.ToolbarPieChartButtonVisible = value),
            (BarButton, "Toolbar.BarChartButton", () => _settings.ToolbarBarChartButtonVisible, value => _settings.ToolbarBarChartButtonVisible = value),
            (SunburstButton, "Toolbar.SunburstButton", () => _settings.ToolbarSunburstButtonVisible, value => _settings.ToolbarSunburstButtonVisible = value),
            (TreemapButton, "Toolbar.TreemapButton", () => _settings.ToolbarTreemapButtonVisible, value => _settings.ToolbarTreemapButtonVisible = value),
            (ExportButton, "Toolbar.ExportButton", () => _settings.ToolbarExportCsvButtonVisible, value => _settings.ToolbarExportCsvButtonVisible = value),
            (AnalysisButton, "Toolbar.AnalysisButton", () => _settings.ToolbarAnalysisButtonVisible, value => _settings.ToolbarAnalysisButtonVisible = value),
            (StorageHistoryButton, "Toolbar.StorageHistoryButton", () => _settings.ToolbarStorageHistoryButtonVisible, value => _settings.ToolbarStorageHistoryButtonVisible = value),
            (SearchButton, "Toolbar.SearchButton", () => _settings.ToolbarSearchButtonVisible, value => _settings.ToolbarSearchButtonVisible = value),
        };

        private StackPanel[] ToolbarGroups => new[] { GroupMain, GroupViews, GroupExport, GroupFeatures };

        private void ConfigureToolbar()
        {
            _settings.EnsureToolbarButtonVisibilitySettings();
            ApplyToolbarOrder();
            ApplyToolbarButtonVisibility();
            Toolbar.ContextRequested += (_, e) =>
            {
                BuildToolbarMenu().Open(Toolbar);
                e.Handled = true;
            };

            foreach (StackPanel group in ToolbarGroups)
            {
                Control grip = group.Children[0];
                grip.Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.SizeAll);
                grip.PointerPressed += (_, e) => e.Pointer.Capture(grip);
                grip.PointerReleased += (_, e) =>
                {
                    e.Pointer.Capture(null);
                    MoveToolbarGroup(group, e.GetPosition(Toolbar));
                };
            }
        }

        // Port of RebuildToolbarContextMenu: a check per button, "show all".
        private ContextMenu BuildToolbarMenu()
        {
            List<object> items = new List<object>
            {
                new MenuItem { Header = LocalizationService.GetText("Toolbar.CustomizeButtons"), IsEnabled = false },
                new Separator(),
            };
            int[] separatorsAfter = { 2, 7, 8 };

            for (int index = 0; index < ToolbarButtons.Length; index++)
            {
                var (_, textKey, get, set) = ToolbarButtons[index];
                MenuItem item = new MenuItem { Header = LocalizationService.GetText(textKey), ToggleType = MenuItemToggleType.CheckBox, IsChecked = get() };
                item.Click += (_, _) =>
                {
                    set(!get());
                    SaveToolbarButtonVisibility();
                };
                items.Add(item);

                if (Array.IndexOf(separatorsAfter, index) >= 0)
                {
                    items.Add(new Separator());
                }
            }

            items.Add(new Separator());
            MenuItem showAll = new MenuItem { Header = LocalizationService.GetText("Toolbar.ShowAllButtons") };
            showAll.Click += (_, _) =>
            {
                foreach (var (_, _, _, set) in ToolbarButtons)
                {
                    set(true);
                }

                _settings.ToolbarScanHistoryButtonVisible = true;
                SaveToolbarButtonVisibility();
            };
            items.Add(showAll);
            return new ContextMenu { ItemsSource = items };
        }

        private void SaveToolbarButtonVisibility()
        {
            _settings.ToolbarButtonVisibilitySettingsVersion = 1;
            ApplyToolbarButtonVisibility();
            _settings.Save();
        }

        // A group without visible buttons disappears, except the first one,
        // which keeps the drive list.
        private void ApplyToolbarButtonVisibility()
        {
            foreach (var (button, _, get, _) in ToolbarButtons)
            {
                button.IsVisible = get();
            }

            foreach (StackPanel group in ToolbarGroups)
            {
                group.IsVisible = group == GroupMain || group.Children.Skip(1).Any(child => child.IsVisible);
            }
        }

        // Group order is saved as each group's position (ToolStrip*Left), as
        // WinForms did with layout version 14.
        private void ApplyToolbarOrder()
        {
            if (!_settings.HasToolStripLayout || _settings.ToolStripLayoutVersion != 14)
            {
                return;
            }

            int[] order = { _settings.ToolStripMainLeft, _settings.ToolStripViewModeLeft, _settings.ToolStripExportLeft, _settings.ToolStripFeaturesLeft };
            StackPanel[] groups = ToolbarGroups.Select((group, index) => (group, order[index])).OrderBy(item => item.Item2).Select(item => item.group).ToArray();
            Toolbar.Children.Clear();
            Toolbar.Children.AddRange(groups);
        }

        // Drop a group (dragged by its grip) before the group under the
        // pointer, or at the end.
        private void MoveToolbarGroup(StackPanel group, Point point)
        {
            List<Control> others = Toolbar.Children.Where(child => child != group).ToList();
            int target = others.FindIndex(child => point.Y < child.Bounds.Bottom && (point.Y < child.Bounds.Top || point.X < child.Bounds.Center.X));
            target = target < 0 ? others.Count : target;

            Toolbar.Children.Remove(group);
            Toolbar.Children.Insert(Math.Min(target, Toolbar.Children.Count), group);
            _settings.HasToolStripLayout = true;
            _settings.ToolStripLayoutVersion = 14;
            _settings.ToolStripMainLeft = Toolbar.Children.IndexOf(GroupMain);
            _settings.ToolStripViewModeLeft = Toolbar.Children.IndexOf(GroupViews);
            _settings.ToolStripExportLeft = Toolbar.Children.IndexOf(GroupExport);
            _settings.ToolStripFeaturesLeft = Toolbar.Children.IndexOf(GroupFeatures);
            _settings.Save();
        }

        // ----- tree context menu, save and load ----------------------------

        // Right click on a folder: the app's own menu (WinForms showed the
        // Explorer menu with these commands added; that menu is Windows-only
        // and was not ported). Files get no menu, as in WinForms.
        private void OnTreeEntryPressed(FileSystemEntry entry, Avalonia.Input.PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(this).Properties.IsRightButtonPressed || entry == null || !entry.IsDirectory || string.IsNullOrWhiteSpace(entry.FullPath))
            {
                return;
            }

            List<object> items = new List<object>();
            FileSystemEntry root = Tree.GetRootEntry(entry);

            MenuItem Command(string header, Action action)
            {
                MenuItem item = new MenuItem { Header = header };
                item.Click += (_, _) => action();
                return item;
            }

            if (root != null)
            {
                string rootName = NormalizeScanPath(root.FullPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                items.Add(Command(LocalizationService.Format("Context.RemoveFromTreePane", rootName.Length == 0 ? root.FullPath : rootName), () => RemoveFromTreePane(entry)));
            }

            items.Add(Command(LocalizationService.GetText("Context.Export"), async () => await _export.ExportAsync(entry)));
            items.Add(Command("Copy: Selected item", async () => await _export.CopyNameAsync(entry)));
            items.Add(Command(_export.TreeCopyMenuText("Text"), async () => await _export.CopyTreeTextAsync(entry)));
            items.Add(Command(_export.TreeCopyMenuText(".CSV"), async () => await _export.CopyCsvAsync(entry)));
            items.Add(new Separator());
            items.Add(Command(LocalizationService.GetText("Context.OpenInExplorer"), () => FileManager.Open(entry.FullPath)));

            new ContextMenu { ItemsSource = items }.Open(Tree);
        }

        // Hides a scanned root from the tree; scanning it again shows it.
        private void RemoveFromTreePane(FileSystemEntry entry)
        {
            FileSystemEntry root = Tree.GetRootEntry(entry);

            if (root != null && Tree.RemoveRootEntry(root))
            {
                _pendingLiveTree.Remove(root.FullPath);
            }
        }

        private async Task SaveScanResultAsync()
        {
            if (_currentRootEntry == null)
            {
                return;
            }

            string fileName = await FileDialogs.SaveAsync(
                this,
                LocalizationService.GetText("Menu.SaveScanResult"),
                "WTF Scan (*.wtfscan)|*.wtfscan|JSON (*.json)|*.json",
                "scan-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".wtfscan");

            if (fileName == null)
            {
                return;
            }

            try
            {
                ScanResultFileService.Save(fileName, _currentRootEntry);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is NotSupportedException)
            {
                AppAlertLog.AddError(LocalizationService.GetText("Menu.SaveScanResult"), exception.Message, "Path: " + fileName + Environment.NewLine + exception);
                SetStatusText(LocalizationService.GetText("Common.Error") + ": " + exception.Message);
            }
        }

        private async Task LoadScanResultAsync()
        {
            string fileName = await FileDialogs.OpenAsync(
                this,
                LocalizationService.GetText("Menu.LoadScanResult"),
                "WTF Scan (*.wtfscan;*.json)|*.wtfscan;*.json");

            if (fileName == null)
            {
                return;
            }

            try
            {
                FileSystemEntry loaded = ScanResultFileService.Load(fileName);

                if (loaded != null)
                {
                    _currentRootEntry = loaded;
                    RenderScanResult(loaded);
                    SetScanningState(false);
                }
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is System.Text.Json.JsonException || exception is NotSupportedException)
            {
                AppAlertLog.AddError(LocalizationService.GetText("Menu.LoadScanResult"), exception.Message, "Path: " + fileName + Environment.NewLine + exception);
                SetStatusText(LocalizationService.GetText("Common.Error") + ": " + exception.Message);
            }
        }

        // ----- view mode and toolbar state --------------------------------

        private void SetViewMode(ViewMode mode, bool save)
        {
            if (!_views.ContainsKey(mode))
            {
                mode = ViewMode.Table;
            }

            _viewMode = mode;
            _settings.SelectedViewMode = mode;
            _storageHistory.IsVisible = false;
            StorageHistoryButton.IsChecked = false;
            HideAnalysisViews();

            foreach (var (viewMode, (button, view)) in _views)
            {
                button.IsChecked = viewMode == mode;
                view.IsVisible = viewMode == mode;
            }

            ScanSession session = CurrentSession;

            if (session != null)
            {
                session.ViewMode = mode;
                session.AnalysisVisible = false;
            }

            if (save)
            {
                _settings.Save();
            }
        }

        // The storage history in place of the chart views (WinForms
        // ShowStorageHistoryView); a view button or a new scan brings them back.
        private ScanSession CurrentSession => _sessions.Values.FirstOrDefault(item => item.RootEntry != null && ReferenceEquals(item.RootEntry, _currentRootEntry));

        // A session shows its analysis again when it had it open.
        private void RestoreSessionView(ScanSession session)
        {
            if (session.AnalysisVisible && !session.IsRunning && session.RootEntry != null)
            {
                ShowAnalysis(session);
            }
            else
            {
                SetViewMode(session.ViewMode, save: false);
            }
        }

        // The analysis of the current scan, created the first time
        // (WinForms ShowAnalysisView).
        private void ShowAnalysis(ScanSession session)
        {
            if (session?.RootEntry == null || session.IsRunning)
            {
                return;
            }

            if (session.AnalysisView == null)
            {
                session.AnalysisView = new AnalysisView(session.RootEntry);
                ViewHost.Children.Add(session.AnalysisView);
            }

            foreach (var (_, (button, view)) in _views)
            {
                button.IsChecked = false;
                view.IsVisible = false;
            }

            _storageHistory.IsVisible = false;
            StorageHistoryButton.IsChecked = false;
            HideAnalysisViews();
            session.AnalysisView.IsVisible = true;
            session.AnalysisVisible = true;
            AnalysisButton.IsChecked = true;
        }

        private void HideAnalysisViews()
        {
            foreach (ScanSession session in _sessions.Values.Where(session => session.AnalysisView != null))
            {
                session.AnalysisView.IsVisible = false;
            }

            AnalysisButton.IsChecked = false;
        }

        private void ShowStorageHistory()
        {
            _storageHistory.RefreshHistory();
            HideAnalysisViews();

            if (CurrentSession != null)
            {
                CurrentSession.AnalysisVisible = false;
            }

            foreach (var (_, (button, view)) in _views)
            {
                button.IsChecked = false;
                view.IsVisible = false;
            }

            _storageHistory.IsVisible = true;
            StorageHistoryButton.IsChecked = true;
        }

        private void SetScanningState(bool scanning)
        {
            ScanIcon.Kind = scanning ? ToolbarIconKind.Stop : ToolbarIconKind.Scan;
            ToolTip.SetTip(ScanButton, LocalizationService.GetText(scanning ? "Toolbar.ScanCancel" : "Toolbar.ScanStart"));
            ToolTip.SetTip(PauseButton, LocalizationService.GetText("Toolbar.PauseResume"));
            ToolTip.SetTip(OpenFolderButton, LocalizationService.GetText("Toolbar.SelectFolderAndScan"));
            ScanButton.IsEnabled = true;
            DriveSelect.IsEnabled = !scanning;
            OpenFolderButton.IsEnabled = !scanning;
            PauseButton.IsEnabled = scanning;

            if (!scanning)
            {
                PauseIcon.Kind = ToolbarIconKind.Pause;
            }

            bool hasResult = !scanning && _currentRootEntry != null;
            ExportButton.IsEnabled = hasResult;
            AnalysisButton.IsEnabled = hasResult;

            if (_menuExport != null)
            {
                _menuExport.IsEnabled = hasResult;
                _menuSaveScan.IsEnabled = hasResult;
            }
        }

        private void SetScanHistorySavingState()
        {
            ToolTip.SetTip(ScanButton, LocalizationService.GetText("Toolbar.ScanHistorySaving"));
            ScanButton.IsEnabled = false;
            DriveSelect.IsEnabled = false;
            OpenFolderButton.IsEnabled = false;
            PauseButton.IsEnabled = false;
            ExportButton.IsEnabled = false;
            AnalysisButton.IsEnabled = false;
        }

        // ----- status bar -------------------------------------------------

        private void SetStatusText(string text) => StatusText.Text = text;

        private void SetSelectedEntrySummary(FileSystemEntry entry, int fileCount)
        {
            if (entry == null)
            {
                SetStatusText(LocalizationService.GetText("Common.Ready"));
                return;
            }

            string name = !string.IsNullOrWhiteSpace(entry.FullPath) ? entry.FullPath : entry.Name ?? string.Empty;
            SetStatusText(string.Format(
                "{0} | Size: {1} | Files: {2:N0} | Cluster-Size: {3:N0}",
                name,
                SizeFormatter.Format(entry.SizeBytes),
                Math.Max(0, fileCount),
                Volumes.GetClusterSize(entry.FullPath)));
        }

        private void UpdateStatusForDrive(string rootPath) => UpdateStatusForDrive(rootPath, null);

        // The volume holding rootPath: size, free space and cluster size.
        private void UpdateStatusForDrive(string rootPath, int? fileCount)
        {
            VolumeInfo volume = Volumes.List().Where(item => EntryTreeCanvas.IsSameOrDescendantPath(rootPath, item.RootPath))
                .OrderByDescending(item => item.RootPath.Length)
                .FirstOrDefault();

            SetScanProgress(null, null, visible: false);

            if (volume == null)
            {
                SetStatusText(LocalizationService.GetText("Common.Ready"));
                return;
            }

            long clusterSize = Volumes.GetClusterSize(volume.RootPath);
            SetStatusText(fileCount.HasValue
                ? string.Format(
                    "{0} | Size: {1} | Free: {2} | Files: {3:N0} | Cluster-Size: {4:N0}",
                    volume.RootPath, SizeFormatter.Format(volume.TotalBytes), SizeFormatter.Format(volume.FreeBytes), fileCount.Value, clusterSize)
                : string.Format(
                    "{0} | Size: {1} | Free: {2} | Cluster-Size: {3:N0}",
                    volume.RootPath, SizeFormatter.Format(volume.TotalBytes), SizeFormatter.Format(volume.FreeBytes), clusterSize));
        }

        private void SetScanProgress(double? percent, TimeSpan? elapsed, bool visible)
        {
            double value = Math.Clamp(percent ?? 0, 0, 100);
            ScanProgress.Fill = null;
            ScanProgress.Text = string.Format("{0:0.0} % | {1:0.0} s", value, elapsed.GetValueOrDefault().TotalSeconds);
            ScanProgress.Value = value / 100;
            ScanProgress.IsVisible = visible;
            SetWindowTitle(visible && percent.HasValue ? value : null);
        }

        private void SetScanProgressPending(float value, TimeSpan elapsed)
        {
            ScanProgress.Fill = null;
            ScanProgress.Text = string.Format("{0:0.0} s", elapsed.TotalSeconds);
            ScanProgress.Value = value;
            ScanProgress.IsVisible = true;
            SetWindowTitle(null);
        }

        private void SetStorageHistoryDetailsProgress(float value, TimeSpan elapsed)
        {
            ScanProgress.Fill = Brushes.Orange;
            ScanProgress.Text = string.Format("{0:0.0} s", elapsed.TotalSeconds);
            ScanProgress.Value = value;
            ScanProgress.IsVisible = true;
            SetStatusText(LocalizationService.GetText("Settings.StorageHistoryDetails"));
        }

        private void SetScanHistorySaveProgress(int percent, TimeSpan elapsed)
        {
            SetStatusText(LocalizationService.Format("Status.ScanHistorySaving", percent));
            SetScanProgress(percent, elapsed, visible: true);
            Title = AppConstants.FullApplicationName + " - " + LocalizationService.Format("Status.ScanHistorySavingTitle", percent);
        }

        private void SetWindowTitle(double? scanPercent)
        {
            string title = AppConstants.FullApplicationName;

            if (scanPercent.HasValue)
            {
                title += scanPercent.Value >= 100
                    ? " - " + LocalizationService.GetText("Status.ScanCompletedTitle")
                    : " - " + LocalizationService.GetText("Status.ScanTitlePrefix") + scanPercent.Value.ToString("0.0") + "%";
            }

            Title = title;
        }

        // Information, warning and error counters of the unconfirmed alerts.
        private void BuildAlertCounters()
        {
            foreach (var (kind, toolTipKey) in new[]
                     {
                         (StatusSymbolKind.Information, "Alert.ToolTipInformation"),
                         (StatusSymbolKind.Warning, "Alert.ToolTipWarning"),
                         (StatusSymbolKind.Error, "Alert.ToolTipError"),
                     })
            {
                StackPanel counter = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 3 };
                counter.Children.Add(new StatusSymbolIcon { Kind = kind, Width = 11, Height = 11, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
                counter.Children.Add(new TextBlock { VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Tag = kind });
                ToolTip.SetTip(counter, LocalizationService.GetText(toolTipKey));
                AlertCounters.Children.Add(counter);
            }

            UpdateAlertCounters();
            AlertCounters.PointerPressed += async (_, e) =>
            {
                e.Handled = true;
                await new AlertHistoryWindow().ShowDialog(this);
            };
        }

        private void OnAlertLogChanged(object sender, EventArgs e) => Dispatcher.UIThread.Post(UpdateAlertCounters);

        private void UpdateAlertCounters()
        {
            foreach (TextBlock count in AlertCounters.Children.OfType<StackPanel>().Select(panel => panel.Children.OfType<TextBlock>().First()))
            {
                StatusSymbolKind kind = (StatusSymbolKind)count.Tag;
                AppAlertSeverity severity = kind == StatusSymbolKind.Warning ? AppAlertSeverity.Warning
                    : kind == StatusSymbolKind.Error ? AppAlertSeverity.Error
                    : AppAlertSeverity.Information;
                count.Text = AppAlertLog.GetUnconfirmedCount(severity).ToString();
            }
        }

        // ----- window settings --------------------------------------------

        private void ApplyWindowSettings()
        {
            if (_settings.HasMainWindowBounds && _settings.MainWindowWidth >= MinWidth && _settings.MainWindowHeight >= MinHeight)
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Position = new PixelPoint(_settings.MainWindowLeft, _settings.MainWindowTop);
                Width = _settings.MainWindowWidth;
                Height = _settings.MainWindowHeight;

                if (_settings.MainWindowMaximized)
                {
                    WindowState = WindowState.Maximized;
                }
            }

            if (_settings.HasSplitterLayout)
            {
                if (_settings.SplitContainerMainDistance >= 220)
                {
                    Body.ColumnDefinitions[0].Width = new GridLength(_settings.SplitContainerMainDistance);
                }

                if (_settings.SplitContainerLeftDistance >= 90)
                {
                    LeftPane.RowDefinitions[2].Height = new GridLength(_settings.SplitContainerLeftDistance);
                }
            }
        }

        // Settings, UI tab: the partition panel below the tree.
        private void ApplyPartitionPanelVisibility()
        {
            bool show = _settings.ShowPartitionPanel;

            if (!show && LeftPane.RowDefinitions[2].Height.Value > 0)
            {
                _partitionPanelHeight = LeftPane.RowDefinitions[2].Height;
            }

            LeftPane.RowDefinitions[1].Height = new GridLength(show ? 6 : 0);
            LeftPane.RowDefinitions[2].Height = show ? _partitionPanelHeight : new GridLength(0);
            PartitionSplitter.IsVisible = show;
            PartitionPane.IsVisible = show;
        }

        protected override void OnClosing(WindowClosingEventArgs e)
        {
            base.OnClosing(e);

            foreach (ScanSession session in _sessions.Values.Where(session => session.IsRunning))
            {
                session.Cancellation.Cancel();
            }

            AppAlertLog.Changed -= OnAlertLogChanged;
            _settings.HasMainWindowBounds = true;
            _settings.MainWindowMaximized = WindowState == WindowState.Maximized;

            if (WindowState == WindowState.Normal)
            {
                _settings.MainWindowLeft = Position.X;
                _settings.MainWindowTop = Position.Y;
                _settings.MainWindowWidth = (int)Width;
                _settings.MainWindowHeight = (int)Height;
            }

            _settings.HasSplitterLayout = true;
            _settings.PartitionPanelLayoutVersion = 2;
            _settings.SplitContainerMainDistance = (int)Body.ColumnDefinitions[0].ActualWidth;
            // A hidden partition panel keeps its last height.
            if (_settings.ShowPartitionPanel)
            {
                _settings.SplitContainerLeftDistance = (int)LeftPane.RowDefinitions[2].ActualHeight;
            }
            _settings.SelectedViewMode = _viewMode;
            _settings.Save();
        }

        // ----- paths ------------------------------------------------------

        // Volume roots keep their separator ("C:\", "/"); folders lose it.
        internal static string NormalizeScanPath(string rootPath)
        {
            if (string.IsNullOrWhiteSpace(rootPath))
            {
                return string.Empty;
            }

            try
            {
                string fullPath = Path.GetFullPath(rootPath);
                VolumeInfo volume = Volumes.Find(fullPath);
                return volume != null ? volume.RootPath : fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch (Exception exception) when (exception is ArgumentException || exception is IOException || exception is NotSupportedException)
            {
                return rootPath.Trim();
            }
        }

        private static bool PathsEqual(string left, string right) =>
            string.Equals(NormalizeScanPath(left), NormalizeScanPath(right), StringComparison.OrdinalIgnoreCase);

        // Used bytes of a scanned volume root, the target of the progress bar;
        // 0 for a folder (the bar then only shows the time).
        private static long GetUsedSpaceBytes(string rootPath)
        {
            VolumeInfo volume = Volumes.Find(rootPath);
            return volume == null ? 0 : Math.Max(0, volume.TotalBytes - volume.FreeBytes);
        }

        // A scanned volume shows the volume's size at its root.
        private static void ApplyVolumeSizeToRootEntry(string rootPath, FileSystemEntry rootEntry)
        {
            VolumeInfo volume = Volumes.Find(rootPath);

            if (volume != null)
            {
                rootEntry.SizeBytes = volume.TotalBytes;
            }
        }

        private sealed class DriveItem
        {
            public DriveItem(string rootPath, string displayName)
            {
                RootPath = rootPath;
                DisplayName = displayName;
            }

            public string RootPath { get; }
            public string DisplayName { get; }

            public override string ToString() => DisplayName;
        }

        private sealed class ScanSession
        {
            public ScanSession(string rootPath)
            {
                RootPath = rootPath;
            }

            public string RootPath { get; }
            public CancellationTokenSource Cancellation { get; } = new CancellationTokenSource();
            public PauseTokenSource Pause { get; } = new PauseTokenSource();
            public Stopwatch Stopwatch { get; } = Stopwatch.StartNew();
            public FileSystemEntry RootEntry { get; set; }
            public ScanProgress LatestProgress { get; set; }
            public long TargetBytes { get; set; }
            public bool IsRunning { get; set; } = true;
            public bool WasCanceled { get; set; }
            public int SkippedDirectories { get; set; }
            public ViewMode ViewMode { get; set; } = ViewMode.Table;
            public bool AnalysisVisible { get; set; }
            public AnalysisView AnalysisView { get; set; }
            public HashSet<string> SkippedDirectoryDetailSet { get; } = new HashSet<string>();
            public List<string> SkippedDirectoryDetails { get; } = new List<string>();
        }
    }
}
