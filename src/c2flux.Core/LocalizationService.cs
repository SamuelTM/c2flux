using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace c2flux
{
    public static class LocalizationService
    {
        public const string EnglishLanguageCode = "en";
        public const string PortugueseLanguageCode = "pt";

        private static readonly object SyncRoot = new object();
        private static Dictionary<string, string> _texts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static bool _isInitializing;

        public static string CurrentLanguageCode { get; private set; } = EnglishLanguageCode;

        public static string StartupWarningMessage { get; private set; }

        // Raised after Load swaps the texts, so UIs can refresh at runtime.
        public static event Action LanguageChanged;

        public static void Initialize(string languageCode)
        {
            StartupWarningMessage = null;
            _isInitializing = true;

            try
            {
                Load(languageCode);
            }
            finally
            {
                _isInitializing = false;
            }
        }

        public static void Load(string languageCode)
        {
            string normalizedLanguageCode = NormalizeLanguageCode(languageCode);
            Dictionary<string, string> fallbackTexts = CreateEnglishTexts();
            Dictionary<string, string> loadedTexts =
                LoadLanguageFile(
                    normalizedLanguageCode,
                    out bool usedFallback);

            foreach (KeyValuePair<string, string> fallbackText in fallbackTexts)
            {
                if (!loadedTexts.ContainsKey(fallbackText.Key))
                {
                    loadedTexts[fallbackText.Key] = fallbackText.Value;
                }
            }

            lock (SyncRoot)
            {
                CurrentLanguageCode = usedFallback
                    ? EnglishLanguageCode
                    : normalizedLanguageCode;
                _texts = loadedTexts;
            }

            LanguageChanged?.Invoke();
        }

        public static string GetText(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return string.Empty;

            lock (SyncRoot)
            {
                if (_texts.TryGetValue(key, out string value))
                {
                    return value ?? string.Empty;
                }
            }

            Dictionary<string, string> englishTexts = CreateEnglishTexts();

            if (englishTexts.TryGetValue(key, out string fallbackValue))
            {
                return fallbackValue ?? string.Empty;
            }

            return key;
        }

        public static string Format(string key, params object[] args)
        {
            string formatText = GetText(key);

            try
            {
                return string.Format(formatText, args);
            }
            catch (FormatException)
            {
                Dictionary<string, string> englishTexts =
                    CreateEnglishTexts();

                if (englishTexts.TryGetValue(
                        key,
                        out string englishFormatText))
                {
                    try
                    {
                        return string.Format(
                            englishFormatText,
                            args);
                    }
                    catch (FormatException)
                    {
                        return englishFormatText;
                    }
                }

                return formatText;
            }
        }

        // Portuguese is the one other language; any other code (a language of
        // an older version included) is English.
        public static string NormalizeLanguageCode(string languageCode)
        {
            string code = languageCode?.Trim().ToLowerInvariant() ?? string.Empty;

            return code == PortugueseLanguageCode || code.StartsWith("pt-", StringComparison.Ordinal) || code.StartsWith("pt_", StringComparison.Ordinal)
                ? PortugueseLanguageCode
                : EnglishLanguageCode;
        }

        public static string[] GetAvailableLanguageCodes()
        {
            return new[] { EnglishLanguageCode, PortugueseLanguageCode };
        }

        public static string GetLanguageDisplayName(string languageCode)
        {
            string normalizedLanguageCode = NormalizeLanguageCode(languageCode);

            if (normalizedLanguageCode == EnglishLanguageCode)
            {
                return GetText("Settings.LanguageEnglish");
            }

            Dictionary<string, string> languageTexts = LoadLanguageFile(normalizedLanguageCode);

            if (languageTexts.TryGetValue("Language.Name", out string languageName) &&
                !string.IsNullOrWhiteSpace(languageName))
            {
                return languageName.Trim();
            }

            return normalizedLanguageCode.ToUpperInvariant();
        }

        private static Dictionary<string, string> LoadLanguageFile(
            string languageCode)
        {
            return LoadLanguageFile(
                languageCode,
                out _);
        }

        private static Dictionary<string, string> LoadLanguageFile(
            string languageCode,
            out bool usedFallback)
        {
            usedFallback = false;

            string normalizedLanguageCode =
                NormalizeLanguageCode(languageCode);

            if (normalizedLanguageCode == EnglishLanguageCode)
            {
                return CreateEnglishTexts();
            }

            // Shipped with the app.
            string languageFilePath = Path.Combine(
                AppPaths.ResourcesDirectory,
                "Languages",
                "lang_" + normalizedLanguageCode + ".json");

            try
            {
                string json = File.ReadAllText(languageFilePath);
                Dictionary<string, string> loadedTexts =
                    JsonSerializer.Deserialize<Dictionary<string, string>>(
                        json);

                if (loadedTexts == null)
                {
                    throw new JsonException(
                        "The language file did not contain a JSON object.");
                }

                return new Dictionary<string, string>(
                    loadedTexts,
                    StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception exception)
                when (exception is IOException ||
                      exception is UnauthorizedAccessException ||
                      exception is JsonException ||
                      exception is NotSupportedException)
            {
                usedFallback = true;

                AppAlertLog.AddError(
                    "Localization",
                    "The language file could not be loaded.",
                    "Path: " + languageFilePath +
                    Environment.NewLine +
                    exception);

                AddStartupWarning(
                    "The selected language file could not be loaded. English will be used instead.");
            }

            return CreateEnglishTexts();
        }

        private static void AddStartupWarning(string message)
        {
            if (!_isInitializing ||
                string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(StartupWarningMessage))
            {
                StartupWarningMessage = message;
                return;
            }

            if (StartupWarningMessage.IndexOf(
                    message,
                    StringComparison.Ordinal) >= 0)
            {
                return;
            }

            StartupWarningMessage +=
                Environment.NewLine +
                Environment.NewLine +
                message;
        }

        private static Dictionary<string, string> CreateEnglishTexts()
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Search.Title"] = "Search",
                ["Search.Source"] = "Search source",
                ["Search.Source.CurrentScan"] = "Current scan",
                ["Search.Source.SavedScan"] = "Saved SQLite scan",
                ["Search.SavedScan"] = "Saved scan",
                ["Search.Text"] = "Search text",
                ["Search.MatchMode"] = "Match mode",
                ["Search.MatchMode.Contains"] = "Contains",
                ["Search.MatchMode.StartsWith"] = "Starts with",
                ["Search.MatchMode.ExactName"] = "Exact name",
                ["Search.MatchMode.FileExtension"] = "File extension",
                ["Search.Scope"] = "Search scope",
                ["Search.Scope.FilesAndFolders"] = "Files and folders",
                ["Search.Scope.FilesOnly"] = "Files only",
                ["Search.Scope.FoldersOnly"] = "Folders only",
                ["Search.Filters"] = "Filters",
                ["Search.MinimumSize"] = "Minimum size (MB)",
                ["Search.MaximumSize"] = "Maximum size (MB)",
                ["Search.ModifiedAfter"] = "Modified after",
                ["Search.ModifiedBefore"] = "Modified before",
                ["Search.FileTypes"] = "File types",
                ["Search.PathContains"] = "Path contains",
                ["Search.ResetFilters"] = "Reset filters",
                ["Search.Start"] = "Search",
                ["Search.Cancel"] = "Cancel",
                ["Calendar.Cancel"] = "Cancel",
                ["Calendar.OK"] = "OK",
                ["Calendar.Now"] = "Now",
                ["Calendar.Today"] = "Today",
                ["Search.NoData"] = "No current scan is available. Run a scan first.",
                ["Search.NoSavedScan"] = "Select a saved scan.",
                ["Search.NoSavedScansAvailable"] = "No saved SQLite scans are available.",
                ["Search.LoadingSavedScan"] = "Loading saved scan...",
                ["Search.LoadSavedScanFailed"] = "The saved scan could not be loaded.",
                ["Search.EnterCriteria"] = "Enter search text or enable at least one filter.",
                ["Search.Searching"] = "Searching...",
                ["Search.Completed"] = "{0:N0} results in {1:0.00} seconds",
                ["Search.Canceled"] = "Search canceled — {0:N0} partial results in {1:0.00} seconds",
                ["Search.Drive"] = "Drive",
                ["Search.FullPath"] = "Full path",
                ["Search.Modified"] = "Modified",
                ["Search.OpenParentFolder"] = "Open parent folder",
                ["Search.CopyFullPath"] = "Copy full path",
                ["Search.CopyName"] = "Copy name",
                ["Search.ItemMissing"] = "The item no longer exists at this location.",
                ["Common.OK"] = "OK",
                ["Common.Cancel"] = "Cancel",
                ["Common.Retry"] = "Retry",
                ["Common.Yes"] = "Yes",
                ["Common.No"] = "No",
                ["Common.Close"] = "Close",
                ["Common.Ready"] = "Ready",
                ["Common.Unknown"] = "Unknown",
                ["Common.Name"] = "Name",
                ["Common.Size"] = "Size",
                ["Common.Free"] = "Free",
                ["Common.FreePercent"] = "% Free",
                ["Common.Bytes"] = "Bytes",
                ["Common.Percent"] = "Share",
                ["Chart.TableUsage"] = "Usage",
                ["Common.Path"] = "Path",
                ["Common.Folder"] = "Folder",
                ["Common.Folders"] = "Folders",
                ["Common.Files"] = "Files",
                ["Toolbar.FilesRequiresTreeFiles"] = "Enable files in the tree first.",
                ["Common.Information"] = "Information",
                ["Common.Warning"] = "Warning",
                ["Common.Error"] = "Error",
                ["Common.General"] = "General",
                ["Menu.File"] = "File",
                ["Menu.NewScan"] = "New scan...",
                ["Menu.View"] = "View",
                ["Menu.Tools"] = "Tools",
                ["Menu.CompareScans"] = "Compare scans",
                ["Toolbar.CompareScansDisabled"] = "To enable this feature: Settings → Statistics → enable “Save scan history”.",
                ["Menu.ExportCsv"] = "Export CSV",
                ["Menu.SaveScanResult"] = "Save scan...",
                ["Menu.LoadScanResult"] = "Load scan...",
                ["Menu.Analysis"] = "Analysis",
                ["Menu.SpaceHistory"] = "Scan History",
                ["Menu.Settings"] = "Settings",
                ["Menu.Exit"] = "Exit",
                ["Menu.Help"] = "Help",
                ["Menu.OnlineHelp"] = "Online Help",
                ["Menu.About"] = "About",
                ["Toolbar.Drive"] = "Drive:",
                ["Toolbar.Open"] = "Open",
                ["Toolbar.ScanStart"] = "Start scan",
                ["Toolbar.ScanCancel"] = "Cancel scan",
                ["Toolbar.ScanHistorySaving"] = "Saving scan history",
                ["Toolbar.SelectFolderAndScan"] = "Select folder and scan",
                ["Toolbar.Table"] = "▦ Table",
                ["Toolbar.PieChart"] = "◔ Pie chart",
                ["Toolbar.BarChart"] = "▥ Bar chart",
                ["Toolbar.Sunburst"] = "Sunburst",
                ["Toolbar.Treemap"] = "Treemap",
                ["Toolbar.Export"] = "Export",
                ["Toolbar.ExportCsv"] = "Export CSV",
                ["Toolbar.PauseResume"] = "Pause/resume scan",
                ["Toolbar.CustomizeButtons"] = "Toolbar buttons",
                ["Toolbar.ShowAllButtons"] = "Show all buttons",
                ["Toolbar.ScanButton"] = "Start/cancel scan",
                ["Toolbar.PauseButton"] = "Pause/resume",
                ["Toolbar.OpenFolderButton"] = "Open folder",
                ["Toolbar.TableButton"] = "Table",
                ["Toolbar.PieChartButton"] = "Pie chart",
                ["Toolbar.BarChartButton"] = "Bar chart",
                ["Toolbar.SunburstButton"] = "Sunburst",
                ["Toolbar.TreemapButton"] = "Treemap",
                ["Toolbar.ExportButton"] = "Export",
                ["Toolbar.AnalysisButton"] = "Analysis",
                ["Toolbar.StorageHistoryButton"] = "Scan History",
                ["Toolbar.CompareScansButton"] = "Compare scans",
                ["Toolbar.SearchButton"] = "Search",
                ["Context.OpenInExplorer"] = "Open in Explorer",
                ["Context.Export"] = "Export",
                ["Context.CopyToClipboard"] = "Copy to clipboard",
                ["Context.CopyPath"] = "Copy path",
                ["Context.RemoveFromTreePane"] = "Remove \"{0}\" from tree pane",
                ["Context.MoveToTrash"] = "Move to Trash",
                ["Context.CopyTreeText"] = "Copy tree as text ({0})",
                ["Context.CopyTreeCsv"] = "Copy tree as CSV ({0})",
                ["Context.DepthUnlimited"] = "all levels",
                ["Context.DepthLevels"] = "depth {0}",
                ["Export.Progress"] = "Exporting \"{0}\"...",
                ["Trash.Confirm"] = "Move \"{0}\" to the Trash?",
                ["Trash.Failed"] = "\"{0}\" could not be moved to the Trash.",
                ["Dialog.SelectFolder"] = "Select folder to scan",
                ["Dialog.FileName"] = "File name",
                ["Dialog.FileAlreadyExists"] = "The file \"{0}\" already exists. Do you want to replace it?",
                ["Message.NoPathSelected"] = "No path selected.",
                ["Message.PathNotFoundPrefix"] = "Path not found: ",
                ["Message.SettingsSaveFailedPrefix"] = "Settings could not be saved: ",
                ["Message.SettingsSaveFailed"] = "The settings could not be saved.",
                ["Status.FreeSpace"] = "Free space {0} {1} (of {2}), cluster size: {3}",
                ["Status.FreeSpaceWithFileCount"] = "Free space {0} {1} (of {2}), files: {3:N0}, cluster size: {4}",
                ["Status.ScanCacheSave"] = "{0} | {1} | Folders: {2} | Files: {3}",
                ["Status.CacheVerification"] = "Cache loaded - verifying changes: {0} | {1} | Folders: {2} | Files: {3}",
                ["Status.FastScan"] = "Fast scan: {0} | {1} | Folders: {2} | Files: {3}",
                ["Status.MftFastScanRunning"] = "NTFS MFT fast scan is running...",
                ["Status.MftUnavailableNtQuery"] = "MFT fast scan unavailable - NT API fast scan is running...",
                ["Status.NtQueryUnavailableNormal"] = "NT API fast scan unavailable - normal scan is running...",
                ["Status.NtQueryRunning"] = "NT API fast scan is running...",
                ["Status.ScanRunning"] = "Scan is running...",
                ["Status.ScanCanceled"] = "Scan canceled",
                ["Status.TitleCacheVerification"] = "Cache loaded / verifying changes",
                ["Status.ScanCompletedTitle"] = "Scan: 100% / completed",
                ["Status.ScanHistorySaving"] = "Scan completed, saving scan history: {0}%",
                ["Status.ScanHistorySavingTitle"] = "Saving scan history: {0}%",
                ["Status.ExportCopied"] = "Export copied to clipboard: ",
                ["Status.ExportSaved"] = "Export saved: ",
                ["Status.CacheSave"] = "Scan completed, saving cache...",
                ["Alert.Scan"] = "Scan",
                ["Alert.ToolTipInformation"] = "Show information",
                ["Alert.ToolTipWarning"] = "Show warnings",
                ["Alert.ToolTipError"] = "Show errors",
                ["Alert.MftUnavailable"] = "MFT fast scan unavailable: {0}",
                ["Alert.NtQueryUnavailable"] = "NT API fast scan unavailable: {0}",
                ["Alert.NormalScanUnavailable"] = "Normal scan unavailable: {0}",
                ["Alert.ManagedScanUnavailable"] = "Scan unavailable: {0}",
                ["Alert.NativeScanUnavailable"] = "Fast scan unavailable: {0}",
                ["Alert.ExpectedSystemDirectorySingle"] = "1 system folder was skipped as expected.",
                ["Alert.ExpectedSystemDirectoryMultiple"] = "{0} system folders were skipped as expected.",
                ["Alert.SkippedDirectorySingle"] = "1 folder could not be read.",
                ["Alert.SkippedDirectoryMultiple"] = "{0} folders could not be read.",
                ["Alert.UnknownSkippedDirectories"] = "{0} additional folders could not be read. Details were not captured.",
                ["Alert.Reason"] = "Reason: {0}",
                ["Alert.UnknownReason"] = "Unknown",
                ["Alert.Win32Error"] = "Win32 error {0}: {1}",
                ["Alert.NtStatusOpen"] = "Folder could not be opened. NTSTATUS: {0}",
                ["Alert.NtStatusRead"] = "Folder could not be read. NTSTATUS: {0}",
                ["Alert.NtQueryRootOpenFailed"] = "NT API fast scan could not open the root path: {0}",
                ["Alert.NtQueryRootReadFailed"] = "NT API fast scan could not read the root path: {0}",
                ["Alert.InvalidNtfsDrive"] = "No valid NTFS drive.",
                ["Alert.ScanHistorySaveFailed"] = "Scan history could not be saved: {0}",
                ["Status.MftFastScanCompleted"] = "MFT fast scan completed",
                ["Settings.Title"] = "Settings",
                ["Settings.General"] = "General",
                ["Settings.Export"] = "Export",
                ["Settings.Colors"] = "Colors",
                ["Settings.LayoutTab"] = "UI",
                ["Settings.Statistics"] = "History",
                ["Settings.Logging"] = "Logging",
                ["Settings.LogLevel"] = "Log level:",
                ["Settings.AutoSaveLog"] = "Automatically save log",
                ["Settings.MaximumLogFileSizeMb"] = "Max. log size:",
                ["Settings.MaximumLogFileSizeMbInvalid"] = "The maximum log size must be at least 1 MB.",
                ["Settings.SaveScanHistory"] = "Save scan history (deprecated)",
                ["Settings.SaveScanHistoryHelp"] = "Saves scan data so changes in storage usage (growth or shrinkage) can be tracked. Only the top 10 changes are stored.",
                ["Settings.StorageHistoryDetails"] = "Save detailed scan history",
                ["Settings.StorageHistoryDetailsHelp"] = "Enable this to save a small snapshot after each scan so that details can be displayed under \"Scan History\". General -> Show files in tree is automatically enabled and cannot be disabled while detailed scan history is enabled.",
                ["Settings.StorageHistoryDetailsDatabaseSize"] = "Database size: {0}",
                ["Settings.StorageHistoryDetailsReusableSpace"] = "Reusable database space: {0}",
                ["Settings.StorageHistoryDetailsAutoCompact"] = "Auto-compact DB",
                ["Settings.StorageHistoryDetailsAutoPurge"] = "Auto-purge",
                ["Settings.StorageHistoryDetailsAutoPurgeMaximumAgeDays"] = "Delete details older than (days):",
                ["Settings.StorageHistoryDetailsAutoPurgeMaximumAgeDaysInvalid"] = "The maximum age must be at least 1 day.",
                ["Settings.StorageHistoryDetailsAutoPurgeMaximumSnapshotsPerDrive"] = "Maximum snapshots per drive:",
                ["Settings.StorageHistoryDetailsAutoPurgeMaximumSnapshotsPerDriveInvalid"] = "The maximum number of snapshots per drive must be at least 1.",
                ["Settings.ScanHistoryDatabasePath"] = "Database path:",
                ["Settings.MoveDatabase"] = "Browse...",
                ["Settings.DatabaseSize"] = "Database size: {0}",
                ["Settings.DatabaseSizeUnavailable"] = "—",
                ["Settings.ScanHistoryMaximumScansPerPath"] = "Maximum scans per path:",
                ["Settings.ScanHistoryMaximumScansPerPathInvalid"] = "The maximum number of saved scans per path must be at least 1.",
                ["DatabaseBrowse.Title"] = "Select database",
                ["DatabaseBrowse.CurrentPath"] = "Current database path:",
                ["DatabaseBrowse.Hint"] = "Choose exactly what should happen to the database. Existing files are never overwritten.",
                ["DatabaseBrowse.MoveCurrent"] = "Move DB to new location",
                ["DatabaseBrowse.UseExisting"] = "Use existing DB",
                ["DatabaseBrowse.CreateNew"] = "Create new DB",
                ["DatabaseBrowse.MoveSelectTitle"] = "Select a new location for the current database",
                ["DatabaseBrowse.UseExistingSelectTitle"] = "Select an existing database",
                ["DatabaseBrowse.CreateNewSelectTitle"] = "Select a path for the new database",
                ["DatabaseBrowse.Filter"] = "SQLite database (*.db)|*.db|All files (*.*)|*.*",
                ["DatabaseBrowse.TargetExists"] = "A file already exists at the selected target. It will not be overwritten.",
                ["DatabaseBrowse.SourceMissing"] = "The selected database no longer exists.",
                ["DatabaseBrowse.MoveConfirm"] = "Move the current database to the selected location?",
                ["DatabaseBrowse.SelectionRequired"] = "Select the database path again using “Browse...”.",
                ["DatabaseBrowse.ApplyFailed"] = "The database selection could not be applied.",
                ["ScanHistory.Title"] = "Scan history",
                ["ScanHistory.DatabaseMaintenanceTitle"] = "Scan history database",
                ["ScanHistory.DatabaseMaintenanceMessage"] = "The scan history database is being optimized.\nPlease wait and do not close the application.",
                ["ScanHistory.BaselineScan"] = "Baseline scan:",
                ["ScanHistory.CompareScan"] = "Compare scan:",
                ["ScanHistory.Compare"] = "Compare",
                ["ScanHistory.CompareProgressTitle"] = "Scan history - Comparing: {0}%",
                ["ScanHistory.Refresh"] = "Refresh",
                ["ScanHistory.ScanCount"] = "{0} saved scan(s)",
                ["ScanHistory.SelectDifferentScans"] = "Please select two different scans.",
                ["ScanHistory.Scans"] = "Scans",
                ["ScanHistory.Summary"] = "Summary",
                ["ScanHistory.Overview"] = "Growth overview",
                ["ScanHistory.OverviewBack"] = "Back",
                ["ScanHistory.OverviewTotalGrowth"] = "Total growth: {0}",
                ["ScanHistory.OverviewNewFiles"] = "New files: {0}",
                ["ScanHistory.OverviewChangedFiles"] = "Changed files: {0}",
                ["ScanHistory.OverviewDeletedFiles"] = "Deleted files: {0}",
                ["ScanHistory.OverviewLargestFolder"] = "Largest folder growth: {0}",
                ["ScanHistory.OverviewLargestFile"] = "Largest new file: {0}",
                ["ScanHistory.OverviewLargestGrowth"] = "Largest growth",
                ["ScanHistory.OverviewMostNewFiles"] = "Most new files",
                ["ScanHistory.OverviewLargestNewFiles"] = "Largest new files",
                ["ScanHistory.OverviewValue"] = "Value",
                ["ScanHistory.OverviewNoGrowth"] = "No positive growth on this level.",
                ["ScanHistory.OverviewDriveComparison"] = "Drive – before / after",
                ["ScanHistory.OverviewFolders"] = "Folders with the largest changes",
                ["ScanHistory.OverviewNewFilesView"] = "Largest new files",
                ["ScanHistory.OverviewChangedFilesView"] = "Largest changed files",
                ["ScanHistory.OverviewBefore"] = "Before",
                ["ScanHistory.OverviewAfter"] = "After",
                ["ScanHistory.OverviewNew"] = "New",
                ["ScanHistory.OverviewFileCountShort"] = "{0} new file(s)",
                ["ScanHistory.FolderGrowth"] = "Folder growth",
                ["ScanHistory.NewFiles"] = "New files",
                ["ScanHistory.ChangedFiles"] = "Changed files",
                ["ScanHistory.DeletedFiles"] = "Deleted files",
                ["ScanHistory.Date"] = "Date",
                ["ScanHistory.RootPath"] = "Root path",
                ["ScanHistory.TotalSize"] = "Total size",
                ["ScanHistory.Metric"] = "Metric",
                ["ScanHistory.Value"] = "Value",
                ["ScanHistory.Path"] = "Path",
                ["ScanHistory.BaselineSize"] = "Baseline size",
                ["ScanHistory.CompareSize"] = "Compare size",
                ["ScanHistory.Delta"] = "Delta",
                ["ScanHistory.BaselineFiles"] = "Baseline files",
                ["ScanHistory.CompareFiles"] = "Compare files",
                ["ScanHistory.InvalidChronologicalOrder"] = "The baseline scan must be older than the comparison scan.",
                ["ScanHistory.LastWriteUtc"] = "Last write",
                ["Settings.BarChartBarHeight"] = "Bar chart height:",
                ["Settings.BarChartBarHeightDefault"] = "(Default: {0})",
                ["Settings.UiDesigner"] = "UI designer",
                ["Settings.UiDesignerTitle"] = "Design UI page",
                ["Settings.UiDesignerControl"] = "Control",
                ["Settings.UiDesignerLeft"] = "X",
                ["Settings.UiDesignerTop"] = "Y",
                ["Settings.UiDesignerWidth"] = "Width",
                ["Settings.UiDesignerHeight"] = "Height",
                ["Settings.UiDesignerReset"] = "Default",
                ["Settings.BarChartBarHeightInvalid"] = "The bar height must be between 5 and 30 pixels.",
                ["Settings.SunburstDepth"] = "Sunburst depth:",
                ["Settings.SunburstDepthHint"] = "(0 = unlimited, max. 50)",
                ["Settings.SunburstDepthInvalid"] = "The sunburst depth must be between 0 and 50.",
                ["Settings.SunburstMaxItems"] = "Max. items:",
                ["Settings.SunburstMaxItemsInvalid"] = "The maximum item count must be between 100 and 10000.",
                ["Settings.PartitionFillLight"] = "Fill indicator:",
                ["Settings.PartitionFillDark"] = "Fill indicator:",
                ["Settings.SelectColor"] = "Select color",
                ["Settings.Brightness"] = "Brightness:",
                ["Settings.Language"] = "Language:",
                ["Settings.LanguageEnglish"] = "English",
                ["Settings.ShowFilesInTree"] = "Show files in tree",
                ["Settings.NtQueryDirectoryBufferSize"] = "NT query buffer:",
                ["Settings.SkipReparsePoints"] = "Skip reparse points / junctions",
                ["Settings.ShowPartitionPanel"] = "Show partition panel",
                ["Settings.StartElevated"] = "Start with elevated privileges",
                ["Settings.ShowElevationPrompt"] = "Show admin notice at startup",
                ["Settings.ShellContextMenu"] = "Explorer context menu: c² flux: Scan drive",
                ["Settings.ShellSearchContextMenu"] = "Explorer context menu: c² flux: Search",
                ["Settings.AutoCheckForUpdates"] = "Automatically check for updates",
                ["Settings.RedundancyCacheSize"] = "Redundancy cache size: {0}",
                ["Settings.ClearRedundancyCache"] = "Clear redundancy cache",
                ["Settings.Layout"] = "Theme:",
                ["Settings.LayoutWindowsDefault"] = "Windows default",
                ["Settings.LayoutWindowsLight"] = "Windows light mode",
                ["Settings.LayoutWindowsDark"] = "Windows dark mode",
                ["Settings.ExportPath"] = "Export path",
                ["Settings.ExportSizeGb"] = "Export size (GB)",
                ["Settings.ExportSizeMb"] = "Export size (MB)",
                ["Settings.ExportMaxDepth"] = "Maximum levels/depth:",
                ["Settings.ExportMaxDepthInvalid"] = "The maximum levels/depth must be empty or a number from 0 upward.",
                ["Settings.ShellContextMenuFailed"] = "The Explorer context menu entry could not be updated.",
                ["AlertHistory.Title"] = "Short log",
                ["AlertHistory.Type"] = "Type",
                ["AlertHistory.Category"] = "Category",
                ["AlertHistory.Message"] = "Message",
                ["AlertHistory.Details"] = "Details:",
                ["AlertHistory.CreatedAt"] = "Date and time",
                ["AlertHistory.Confirmed"] = "Confirmed",
                ["AlertHistory.Confirm"] = "Confirm",
                ["AlertHistory.Delete"] = "Delete",
                ["AlertHistory.ConfirmAll"] = "Confirm all",
                ["AlertHistory.DeleteAll"] = "Delete all",
                ["About.Title"] = "About {0}",
                ["About.VersionPrefix"] = "Version: ",
                ["About.UpdateChecking"] = "Checking for update...",
                ["About.UpdateCheckDisabled"] = "Automatic update check disabled",
                ["About.GitHubUnavailable"] = "GitHub unreachable",
                ["About.NoNewVersion"] = "No new version available",
                ["About.UpdateAvailable"] = "Update available: {0}",
                ["About.UpdateAvailableMessage"] = "A new version of {0} is available: {1}",
                ["About.UpdateChangelog"] = "Changelog",
                ["About.UpdateDownload"] = "Download",
                ["About.UpdateLater"] = "Later",
                ["About.FreeText"] = "{0} can be used free of charge.",
                ["About.SupportText"] = "If this tool helps you, you can support development here:",
                                ["Elevation.Message"] = "Would you like to run {0} with elevated privileges to\nincrease scan speed and accuracy?",
                ["Elevation.Important"] = "Important: MFT scanning is much faster but requires\nadministrator rights.",
                ["Elevation.DoNotShowAgain"] = "Do not show this message again",
                ["MacOS.FullDiskAccessMessage"] = "macOS does not let {0} read every folder (Mail, Safari, Messages, ...). Their sizes will be missing from the scan.\n\nGrant \"Full Disk Access\" in System Settings, Privacy & Security, then restart {0}.",
                ["MacOS.FullDiskAccessOpen"] = "Open Settings",
                ["MacOS.FullDiskAccessContinue"] = "Continue",
                ["Chart.NoData"] = "No data available.",
                ["Chart.Other"] = "Other",
                ["Chart.TooltipDates"] = "Created: {0}{1}Modified: {2}{1}Last access: {3}",
                ["Chart.PieTooltip"] = "{0}{1}Created: {2}{1}Modified: {3}{1}Last access: {4}",
                ["Chart.ItemLabel"] = "{0} - {1} ({2:0.0} %)",
                ["Chart.Directory"] = "Directory",
                ["Chart.FilePrefix"] = "File:",
                ["Status.ScanPaused"] = "Scan paused",
                ["Status.ClusterSize"] = "Cluster-Size",
                ["Status.Scanning"] = "Scanning...",
                ["Advanced.Title"] = "Analysis",
                ["Advanced.FileTypes"] = "Extensions",
                ["Advanced.LargestFiles"] = "Largest files",
                ["Advanced.Redundancies"] = "Redundancies",
                ["Advanced.Redundancy.Progress.SizeGrouping"] = "File sizes",
                ["Advanced.Redundancy.Progress.LiveRead"] = "Reading live",
                ["Advanced.Redundancy.Progress.CacheRead"] = "Reading cache",
                ["Advanced.Redundancy.Progress.FileIdentity"] = "File IDs",
                ["Advanced.Redundancy.Progress.CacheSave"] = "Saving cache",
                ["Advanced.Redundancy.Progress.Completed"] = "Completed",
                ["Advanced.Count"] = "Count",
                ["Advanced.FileType"] = "File type",
                ["Advanced.Usage"] = "Usage",
                ["Advanced.SizeGb"] = "Size (GB)",
                ["Advanced.SizeMb"] = "Size (MB)",
                ["Advanced.Files"] = "Files",
                ["Advanced.Bytes"] = "Bytes",
                ["Advanced.Modified"] = "Modified",
                ["Advanced.NoExtension"] = "(no extension)",
                ["Advanced.FileCategories"] = "File types",
                ["Advanced.FileType.Images"] = "Images",
                ["Advanced.FileType.Video"] = "Video",
                ["Advanced.FileType.Audio"] = "Audio",
                ["Advanced.FileType.Documents"] = "Documents",
                ["Advanced.FileType.Archives"] = "Archives",
                ["Advanced.FileType.Applications"] = "Applications",
                ["Advanced.FileType.SystemFiles"] = "System files",
                ["Advanced.FileType.Development"] = "Development",
                ["Advanced.FileType.Databases"] = "Databases",
                ["Advanced.FileType.DiskImages"] = "Disk images & virtual disks",
                ["Advanced.FileType.Backups"] = "Backups",
                ["Advanced.FileType.LogFiles"] = "Log files",
                ["Advanced.FileType.TemporaryFiles"] = "Temporary files",
                ["Advanced.FileType.GameFiles"] = "Game files",
                ["Advanced.FileType.OtherFiles"] = "Other files",
                ["Csv.FileFilter"] = "CSV files (*.csv)|*.csv",
                ["Csv.Path"] = "Path",
                ["Csv.Level"] = "Level",
                ["Csv.SizeGb"] = "Size (GB)",
                ["Csv.SizeMb"] = "Size (MB)",
                ["Csv.Root"] = "Root",
                ["Drive.LocalDisk"] = "Local Disk",
                ["Drive.Display"] = "{0} ({1})",
                ["StorageHistory.Title"] = "Scan History",
                ["StorageHistory.Path"] = "Scan path:",
                ["StorageHistory.Drive"] = "Drive:",
                ["StorageHistory.Display"] = "Display:",
                ["StorageHistory.Intensity"] = "Intensity:",
                ["StorageHistory.Used"] = "Used space",
                ["StorageHistory.Free"] = "Free space",
                ["StorageHistory.Date"] = "Date",
                ["StorageHistory.Size"] = "Size",
                ["StorageHistory.Change"] = "Change",
                ["StorageHistory.Increased"] = "Increased since last measurement",
                ["StorageHistory.Decreased"] = "Decreased since last measurement",
                ["StorageHistory.Unchanged"] = "Unchanged since last measurement",
                ["StorageHistory.NoData"] = "No history data available.",
                ["StorageHistory.Graph"] = "Storage usage development",
                ["StorageHistory.Range.Last7Days"] = "Last 7 days",
                ["StorageHistory.Range.Last14Days"] = "Last 14 days",
                ["StorageHistory.Range.Last30Days"] = "Last 30 days",
                ["StorageHistory.Range.Last90Days"] = "Last 90 days",
                ["StorageHistory.Range.Last365Days"] = "Last 365 days",
                ["StorageHistory.Range.All"] = "All time",
                ["StorageHistory.Range.Custom"] = "Custom range",
                ["StorageHistory.Calendar"] = "Calendar",
                ["StorageHistory.From"] = "From",
                ["StorageHistory.To"] = "To",
                ["StorageHistory.Delete"] = "Delete history",
                ["StorageHistory.DeleteConfirm"] = "Delete the history for this scan path?",
                ["StorageHistory.DeleteRecord"] = "Delete data point",
                ["StorageHistory.DeleteRecordConfirm"] = "Delete the selected data point?",
                ["StorageHistory.Details.Menu"] = "Details",
                ["StorageHistory.Details.Title"] = "Scan History Details",
                ["StorageHistory.Details.Date"] = "Date",
                ["StorageHistory.Details.FilePath"] = "File / Path",
                ["StorageHistory.Details.ChangeType"] = "Added / Removed",
                ["StorageHistory.Details.Added"] = "Added",
                ["StorageHistory.Details.Removed"] = "Removed",
                ["StorageHistory.Details.Change"] = "Change",
                ["Status.ScanTitlePrefix"] = "Scan: "
            };
        }
    }
}
