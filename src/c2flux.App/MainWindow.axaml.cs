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

            _table.SetShowFiles(settings.ShowFilesInTree);
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
            _liveTreeTimer.Tick += (_, _) => FlushLiveTree();

            _export = new ExportActions(settings, this, SetStatusText);
            ExportButton.Click += async (_, _) => await _export.ExportAsync(_currentRootEntry);
            Tree.EntryPressed += OnTreeEntryPressed;
            BuildMenu();

            // Set from code only: a binding here would overwrite the summary
            // whenever the language changes.
            SetStatusText(LocalizationService.GetText("Common.Ready"));
            BuildAlertCounters();
            AppAlertLog.Changed += OnAlertLogChanged;

            ApplyWindowSettings();
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
                    Item("Menu.Settings", () => { }, enabled: false),
                    new NativeMenuItemSeparator(),
                    Item("Menu.Exit", Close)),
                Submenu(
                    "Menu.View",
                    Item("Toolbar.Table", () => SetViewMode(ViewMode.Table, save: true)),
                    Item("Toolbar.PieChart", () => SetViewMode(ViewMode.PieChart, save: true)),
                    Item("Toolbar.BarChart", () => SetViewMode(ViewMode.BarChart, save: true)),
                    Item("Toolbar.Sunburst", () => SetViewMode(ViewMode.Sunburst, save: true)),
                    Item("Toolbar.Treemap", () => SetViewMode(ViewMode.Treemap, save: true))),
                Submenu(
                    "Menu.Tools",
                    Item("Search.Title", () => { }, enabled: false)),
                Submenu(
                    "Menu.Help",
                    Item("Menu.OnlineHelp", () => FileManager.Open(AppConstants.HelpUrl))),
            };

            NativeMenu.SetMenu(this, main);
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

        private async Task StartScanAsync(string rootPath)
        {
            string normalizedRootPath = NormalizeScanPath(rootPath);
            ViewMode viewMode = _settings.SelectedViewMode;

            if (_sessions.TryGetValue(normalizedRootPath, out ScanSession existing))
            {
                viewMode = existing.ViewMode;

                if (existing.IsRunning)
                {
                    existing.Cancellation.Cancel();
                }
            }

            ScanSession session = new ScanSession(normalizedRootPath) { ViewMode = viewMode };
            _sessions[normalizedRootPath] = session;
            FileSystemEntry initialRoot = new FileSystemEntry { Name = normalizedRootPath, FullPath = normalizedRootPath, IsDirectory = true };
            session.RootEntry = initialRoot;
            _currentRootEntry = initialRoot;

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

            SetViewMode(session.ViewMode, save: false);
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

                SetViewMode(session.ViewMode, save: false);
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

            foreach (var (viewMode, (button, view)) in _views)
            {
                button.IsChecked = viewMode == mode;
                view.IsVisible = viewMode == mode;
            }

            ScanSession session = _sessions.Values.FirstOrDefault(item => item.RootEntry != null && ReferenceEquals(item.RootEntry, _currentRootEntry));

            if (session != null)
            {
                session.ViewMode = mode;
            }

            if (save)
            {
                _settings.Save();
            }
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
            _settings.SplitContainerLeftDistance = (int)LeftPane.RowDefinitions[2].ActualHeight;
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
            public HashSet<string> SkippedDirectoryDetailSet { get; } = new HashSet<string>();
            public List<string> SkippedDirectoryDetails { get; } = new List<string>();
        }
    }
}
