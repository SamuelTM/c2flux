using System.Linq;
using Avalonia.Input;
using Avalonia.VisualTree;
using System.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Xunit;

namespace c2flux.AppTests
{
    // Renders the Avalonia charts like c2flux-shots --charts renders the
    // WinForms ones (chart-*.png), for side-by-side comparison (phase 5.2).
    // Files go to $C2FLUX_CHART_OUT, or chart-captures/ next to the test
    // binaries.
    public class ChartCaptures
    {
        private static readonly Color Background = Color.FromRgb(32, 32, 32);

        // English, like the reference captures.
        public ChartCaptures()
        {
            LocalizationService.Load("en");
        }

        // The client area of the 1280x800 reference window (main-empty.png
        // from x = 8, y = 31: WinForms draws borders and a title bar).
        [AvaloniaFact]
        public void MainEmpty()
        {
            MainWindow window = new MainWindow(new AppSettings()) { Width = 1264, Height = 761 };
            window.Show();

            try
            {
                Dispatcher.UIThread.RunJobs();
                System.Threading.Thread.Sleep(500);
                Dispatcher.UIThread.RunJobs();
                using WriteableBitmap frame = window.CaptureRenderedFrame();
                frame.Save(OutputPath("main-empty"));
            }
            finally
            {
                window.Close();
            }
        }

        // The phase 5.4 state references (Screenshots RunStatesAsync): the
        // pointer over a control, pressed, and smaller windows.
        [AvaloniaFact]
        public void States()
        {
            CaptureState("state-about-ok-hover", new AboutWindow(new AppSettings { AutoCheckForUpdates = false }), "OK", press: false);
            CaptureState("state-about-ok-pressed", new AboutWindow(new AppSettings { AutoCheckForUpdates = false }), "OK", press: true);
            CaptureState("state-settings-checkbox-hover", new SettingsWindow(new AppSettings { StorageHistoryDetailsEnabled = true }, windowsOptions: true), LocalizationService.GetText("Settings.SkipReparsePoints"), press: false);
            CaptureState("state-main-toolbar-hover", new MainWindow(new AppSettings()) { Width = 1264, Height = 761 }, "TableButton", press: false);

            CaptureState("state-main-view-hover", new MainWindow(new AppSettings()) { Width = 1264, Height = 761 }, "PieButton", press: false);
            CaptureState("state-settings-tab-hover", new SettingsWindow(new AppSettings { StorageHistoryDetailsEnabled = true }, windowsOptions: true), LocalizationService.GetText("Settings.Export"), press: false);
            CaptureState("state-database-move-hover", new DatabaseMoveWindow(ScanHistoryService.DefaultDatabasePath), LocalizationService.GetText("DatabaseBrowse.UseExisting"), press: false);
            CaptureState("state-update-download-hover", new UpdateAvailableWindow(SampleUpdate()), LocalizationService.GetText("About.UpdateDownload"), press: false);

            // 1000x650, 1600x900 and the minimum size, with the Windows frame.
            CaptureWindow("main-empty-small", new MainWindow(new AppSettings()) { Width = 984, Height = 611 });
            CaptureWindow("main-empty-large", new MainWindow(new AppSettings()) { Width = 1584, Height = 861 });
            AppAlertLog.DeleteAll();
            AppAlertLog.AddInformation("Scan", "Scan of T:\\ completed.");
            AppAlertLog.AddWarning("Scan", "3 folders could not be read.", "T:\\tree\\unreadable\\locked-dir\nAccess is denied.");
            AppAlertLog.AddError("Export", "The CSV file could not be written.", "The file is in use by another process.");
            AlertHistoryWindow alerts = new AlertHistoryWindow();
            alerts.Width = alerts.MinWidth;
            alerts.Height = alerts.MinHeight;
            CaptureWindow("alert-history-entries-min", alerts);
            CaptureState("state-alert-history-close-hover", new AlertHistoryWindow(), LocalizationService.GetText("Common.Close"), press: false);
            AppAlertLog.DeleteAll();
        }

        // target: a control name, or the text of a button or checkbox.
        private static void CaptureState(string name, Window window, string target, bool press)
        {
            window.Show();

            try
            {
                Dispatcher.UIThread.RunJobs();
                Control control = window.GetVisualDescendants().OfType<Control>().First(item =>
                    item.Name == target || (item as ContentControl)?.Content as string == target);
                Point center = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window) ?? default;
                window.MouseMove(center);

                if (press)
                {
                    window.MouseDown(center, MouseButton.Left);
                }

                Dispatcher.UIThread.RunJobs();
                using WriteableBitmap frame = window.CaptureRenderedFrame();
                frame.Save(OutputPath(name));

                if (press)
                {
                    window.MouseUp(new Point(0, 0), MouseButton.Left);
                }
            }
            finally
            {
                window.Close();
            }
        }

        // Client areas of about.png and update-available.png (from 8, 31).
        [AvaloniaFact]
        public void About()
        {
            CaptureWindow("about", new AboutWindow(new AppSettings { AutoCheckForUpdates = false }));
        }

        [AvaloniaFact]
        public void UpdateAvailable()
        {
            CaptureWindow("update-available", new UpdateAvailableWindow(SampleUpdate()));
        }

        private static GitHubUpdateResult SampleUpdate()
        {
            return new GitHubUpdateResult
            {
                CanConnectToGitHub = true,
                UpdateAvailable = true,
                LatestVersion = "9.9.9",
                DownloadUrl = "https://example.invalid/c2flux.zip",
                ReleaseNotes = "## Changelog v9.9.9\n\n- Sample release notes used for UI screenshots.\n- Second line.\n- Third line.",
            };
        }

        // As in the reference: the default database exists, so moving it is
        // possible.
        [AvaloniaFact]
        public void DatabaseMove()
        {
            string path = ScanHistoryService.DefaultDatabasePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path));

            // Another test may hold it open (Windows locks it): only create it.
            if (!File.Exists(path))
            {
                File.WriteAllBytes(path, Array.Empty<byte>());
            }
            CaptureWindow("database-move", new DatabaseMoveWindow(path));
        }

        [AvaloniaFact]
        public void DebugClass()
        {
            CaptureWindow("debug-class", new DebugClassWindow());
        }

        // The five pages of settings.png, settings-layout.png and the others.
        [AvaloniaFact]
        public void Settings()
        {
            string[] names = { "settings", "settings-layout", "settings-statistics", "settings-export", "settings-logging" };
            // As in the reference: detailed history on, so the tree shows files.
            SettingsWindow window = new SettingsWindow(new AppSettings { StorageHistoryDetailsEnabled = true }, windowsOptions: true);
            window.Show();

            try
            {
                for (int index = 0; index < names.Length; index++)
                {
                    window.ShowPage(index);
                    Dispatcher.UIThread.RunJobs();
                    using WriteableBitmap frame = window.CaptureRenderedFrame();
                    frame.Save(OutputPath(names[index]));
                }
            }
            finally
            {
                window.Close();
            }
        }

        // search.png empty; search-results.png after searching "file-00" in
        // the reference tree: T:\tree\wide\file-000000.dat and on, with the
        // sizes generate_test_tree.py gives them (seed 20260101).
        [AvaloniaFact]
        public async Task Search()
        {
            int[] sizes = { 5920, 3485, 1781, 7356, 7258, 2420, 4863, 6758, 2811, 6759, 2990, 3638 };
            FileSystemEntry root = new FileSystemEntry { Name = "T:\\", FullPath = "T:\\", IsDirectory = true };

            for (int index = 0; index < 1000; index++)
            {
                root.AllFiles.Add(new FileSystemEntry
                {
                    Name = $"file-{index:000000}.dat",
                    FullPath = $"T:\\tree\\wide\\file-{index:000000}.dat",
                    SizeBytes = sizes[index % sizes.Length],
                    LastWriteTimeUtc = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(12 + index),
                });
            }

            AppSettings settings = new AppSettings();
            SearchWindow window = new SearchWindow(settings, () => root, _ => Task.FromResult<FileSystemEntry>(null));
            window.Show();

            try
            {
                Dispatcher.UIThread.RunJobs();
                using (WriteableBitmap frame = window.CaptureRenderedFrame())
                {
                    frame.Save(OutputPath("search"));
                }

                window.SearchText = "file-00";
                await window.SearchAsync();
                Dispatcher.UIThread.RunJobs();
                using (WriteableBitmap results = window.CaptureRenderedFrame())
                {
                    results.Save(OutputPath("search-results"));
                }

                // The minimum size with the frame (550x560), then the
                // filters button under the pointer.
                window.Width = window.MinWidth;
                window.Height = window.MinHeight;
                Dispatcher.UIThread.RunJobs();
                using (WriteableBitmap small = window.CaptureRenderedFrame())
                {
                    small.Save(OutputPath("search-results-min"));
                }

                Control filters = window.FiltersButton;
                window.MouseMove(filters.TranslatePoint(new Point(filters.Bounds.Width / 2, filters.Bounds.Height / 2), window) ?? default);
                Dispatcher.UIThread.RunJobs();
                using WriteableBitmap hover = window.CaptureRenderedFrame();
                hover.Save(OutputPath("search-results-min-filters-hover"));
            }
            finally
            {
                window.Close();
            }
        }

        // The right pane of main-storage-history.png (892x632 from 379, 126):
        // one data point of the temp folder's volume.
        [AvaloniaFact]
        public void StorageHistoryView()
        {
            string path = Path.GetTempPath();
            StorageHistoryService.DeleteRecords(path);
            StorageHistoryService.AddRecord(path, 4L << 30);
            c2flux.StorageHistoryView view = new c2flux.StorageHistoryView(new AppSettings());
            view.RefreshHistory();
            Capture("storage-history-view", view, 892, 632);
        }

        // The right pane of main-analysis.png (892x632), with the chart
        // fixture: the extensions and largest files tabs.
        [AvaloniaFact]
        public void Analysis()
        {
            AnalysisView view = new AnalysisView(LoadFixture());
            Window window = new Window { Width = 892, Height = 632, Content = view };
            window.Show();

            try
            {
                Dispatcher.UIThread.RunJobs();
                using (WriteableBitmap frame = window.CaptureRenderedFrame())
                {
                    frame.Save(OutputPath("analysis"));
                }

                view.ShowPageForTest(2);
                Dispatcher.UIThread.RunJobs();
                using WriteableBitmap largest = window.CaptureRenderedFrame();
                largest.Save(OutputPath("analysis-largest-files"));
            }
            finally
            {
                window.Close();
            }
        }

        // The Full Disk Access notice: long text and labels must fit.
        [AvaloniaFact]
        public void FullDiskAccessNotice()
        {
            Window owner = new Window { Width = 400, Height = 300 };
            owner.Show();

            try
            {
                Task<bool> choice = AppDialogs.ShowWarningChoiceAsync(
                    owner,
                    LocalizationService.Format("MacOS.FullDiskAccessMessage", AppConstants.ApplicationName),
                    LocalizationService.GetText("MacOS.FullDiskAccessOpen"),
                    LocalizationService.GetText("MacOS.FullDiskAccessContinue"));
                Dispatcher.UIThread.RunJobs();
                Window dialog = owner.OwnedWindows.Single();
                using (WriteableBitmap frame = dialog.CaptureRenderedFrame())
                {
                    frame.Save(OutputPath("dialog-full-disk-access"));
                }

                foreach (TextBlock text in dialog.GetVisualDescendants().OfType<TextBlock>())
                {
                    Assert.True(text.Bounds.Width >= text.DesiredSize.Width - 1, "text cut: " + text.Text);
                }

                dialog.Close();
                Dispatcher.UIThread.RunJobs();
                Assert.False(choice.Result);
            }
            finally
            {
                owner.Close();
            }
        }

        private static void CaptureWindow(string name, Window window)
        {
            window.Show();

            try
            {
                Dispatcher.UIThread.RunJobs();
                using WriteableBitmap frame = window.CaptureRenderedFrame();
                frame.Save(OutputPath(name));
            }
            finally
            {
                window.Close();
            }
        }

        // Client area of alert-history-entries.png (from x = 8, y = 31).
        [AvaloniaFact]
        public void AlertHistory()
        {
            AppAlertLog.DeleteAll();
            AppAlertLog.AddInformation("Scan", "Scan of T:\\ completed.");
            AppAlertLog.AddWarning("Scan", "3 folders could not be read.", "T:\\tree\\unreadable\\locked-dir\nAccess is denied.");
            AppAlertLog.AddError("Export", "The CSV file could not be written.", "The file is in use by another process.");
            AlertHistoryWindow window = new AlertHistoryWindow();
            window.Show();

            try
            {
                Dispatcher.UIThread.RunJobs();
                using WriteableBitmap frame = window.CaptureRenderedFrame();
                frame.Save(OutputPath("alert-history-entries"));
            }
            finally
            {
                window.Close();
                AppAlertLog.DeleteAll();
            }
        }

        [AvaloniaFact]
        public void Symbols()
        {
            StatusSymbolKind[] kinds = Enum.GetValues<StatusSymbolKind>();
            int[] sizes = { 14, 48 };
            const int cell = 56;

            string path = Render("chart-symbols", cell * (kinds.Length + 2), cell * sizes.Length, context =>
            {
                for (int row = 0; row < sizes.Length; row++)
                {
                    double offset = (cell - sizes[row]) / 2.0;

                    for (int column = 0; column < kinds.Length + 2; column++)
                    {
                        Rect box = new Rect(column * cell + offset, row * cell + offset, sizes[row], sizes[row]);

                        if (column < kinds.Length)
                        {
                            StatusSymbolRenderer.DrawSymbol(context, box, kinds[column]);
                        }
                        else
                        {
                            StatusSymbolRenderer.DrawTreeExpandGlyph(context, box, column == kinds.Length + 1);
                        }
                    }
                }
            });

            Assert.True(new FileInfo(path).Length > 0);
        }

        // Size of the chart area in the 1280x800 main window, as in c2flux-shots.
        private const int ChartWidth = 890;
        private const int ChartHeight = 630;

        [AvaloniaFact]
        public void Pie()
        {
            PieChart chart = new PieChart();
            chart.SetEntry(LoadFixture());
            Capture("chart-pie", chart, ChartWidth, ChartHeight);
        }

        [AvaloniaFact]
        public void Bar()
        {
            BarChart chart = new BarChart();
            chart.SetEntry(LoadFixture());
            Capture("chart-bar", chart, ChartWidth, ChartHeight);
        }

        [AvaloniaFact]
        public void Sunburst()
        {
            Sunburst chart = new Sunburst();
            chart.SetEntry(LoadFixture());
            Capture("chart-sunburst", chart, ChartWidth, ChartHeight);
        }

        [AvaloniaFact]
        public void Tree()
        {
            EntryTree tree = new EntryTree();
            tree.SetRootEntry(LoadFixture());
            // Size of the tree in the 1280x800 main window.
            Capture("chart-tree", tree, 360, 450);
        }

        [AvaloniaFact]
        public void Treemap()
        {
            FileSystemEntry root = LoadFixture();
            TreemapView view = new TreemapView();
            view.SetRootEntry(root);
            view.SetEntry(root);
            Capture("chart-treemap", view, ChartWidth, ChartHeight);
        }

        [AvaloniaFact]
        public void Table()
        {
            EntryTable table = new EntryTable();
            table.SetEntry(LoadFixture());
            Capture("chart-table", table, ChartWidth, ChartHeight);
        }

        [AvaloniaFact]
        public void StorageHistory()
        {
            StorageHistoryChart chart = new StorageHistoryChart();
            chart.SetGradientIntensity(55);
            chart.SetRecords(ReadFixture<List<StorageHistoryRecord>>("chart-storage-history.json"), StorageHistoryDisplayMode.FreeSpace);
            Capture("chart-storage-history", chart, 660, 520);
        }

        [AvaloniaFact]
        public void GrowthOverview()
        {
            GrowthOverview overview = new GrowthOverview();
            overview.BindResult(ReadFixture<ScanHistoryComparisonResult>("chart-scan-comparison.json"));
            Capture("chart-growth-overview", overview, 1080, 520);
        }

        // Read-only list properties (ScanHistoryComparisonResult.NewFiles, ...)
        // are filled in place.
        internal static T ReadFixture<T>(string name)
        {
            return JsonSerializer.Deserialize<T>(
                File.ReadAllText(FixturePath(name)),
                new JsonSerializerOptions { PreferredObjectCreationHandling = JsonObjectCreationHandling.Populate });
        }

        internal static FileSystemEntry LoadFixture()
        {
            return ScanResultFileService.Load(FixturePath("chart-tree.json"));
        }

        internal static string FixturePath(string name)
        {
            return Path.Combine(AppContext.BaseDirectory, "charts", name);
        }

        // The control alone in a borderless window of the given size.
        private static void Capture(string name, Control control, int width, int height)
        {
            Window window = new Window { Width = width, Height = height, Content = control };
            window.Show();

            try
            {
                Dispatcher.UIThread.RunJobs();
                using WriteableBitmap frame = window.CaptureRenderedFrame();
                frame.Save(OutputPath(name));
            }
            finally
            {
                window.Close();
            }
        }

        private static string OutputPath(string name)
        {
            string directory = Environment.GetEnvironmentVariable("C2FLUX_CHART_OUT") ??
                Path.Combine(AppContext.BaseDirectory, "chart-captures");
            Directory.CreateDirectory(directory);
            return Path.Combine(directory, name + ".png");
        }

        private static string Render(string name, int width, int height, Action<DrawingContext> draw)
        {
            using RenderTargetBitmap bitmap = new RenderTargetBitmap(new PixelSize(width, height));

            using (DrawingContext context = bitmap.CreateDrawingContext())
            {
                context.FillRectangle(new SolidColorBrush(Background), new Rect(0, 0, width, height));
                draw(context);
            }

            string path = OutputPath(name);
            bitmap.Save(path);
            return path;
        }
    }
}
