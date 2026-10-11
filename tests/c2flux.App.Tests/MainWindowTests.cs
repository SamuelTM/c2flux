using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Xunit;

namespace c2flux.AppTests
{
    public class MainWindowTests
    {
        [AvaloniaFact]
        public async Task Scanning_a_folder_fills_the_tree_and_the_status()
        {
            string folder = Path.Combine(Path.GetTempPath(), "c2flux-scan-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(folder, "sub"));
            File.WriteAllBytes(Path.Combine(folder, "a.bin"), new byte[3000]);
            File.WriteAllBytes(Path.Combine(folder, "sub", "b.bin"), new byte[5000]);
            MainWindow window = new MainWindow(new AppSettings { SaveScanHistory = false, StorageHistoryDetailsEnabled = false });
            window.Show();

            try
            {
                LocalizationService.Load("en");
                await window.ScanPathAsync(folder);
                Dispatcher.UIThread.RunJobs();

                FileSystemEntry root = window.EntryTree.GetRootEntry(new FileSystemEntry { FullPath = Path.Combine(folder, "a.bin") });
                Assert.NotNull(root);
                Assert.Equal(8000, root.SizeBytes);
                Assert.Equal(new[] { "sub", "a.bin" }, root.Children.Select(child => child.Name));
                Assert.Contains("Files: 2", window.StatusLine);
            }
            finally
            {
                window.Close();
                Directory.Delete(folder, recursive: true);
            }
        }

        // Scan, then navigate into a folder, search the scan and export it:
        // the main flows of the phase 5.4 checklist.
        [AvaloniaFact]
        public async Task Navigating_searching_and_exporting_a_scan()
        {
            string folder = Path.Combine(Path.GetTempPath(), "c2flux-flow-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(folder, "sub"));
            File.WriteAllBytes(Path.Combine(folder, "a.bin"), new byte[3000]);
            File.WriteAllBytes(Path.Combine(folder, "sub", "b.bin"), new byte[5000]);
            MainWindow window = new MainWindow(new AppSettings { SaveScanHistory = false, StorageHistoryDetailsEnabled = false });
            window.Show();

            try
            {
                LocalizationService.Load("en");
                await window.ScanPathAsync(folder);
                Dispatcher.UIThread.RunJobs();
                FileSystemEntry root = window.CurrentRootEntry;
                FileSystemEntry sub = root.Children.Single(child => child.Name == "sub");

                Assert.True(window.EntryTree.SelectEntry(sub));
                Dispatcher.UIThread.RunJobs();
                Assert.Contains("sub", window.StatusLine);
                Assert.Contains("Files: 1", window.StatusLine);

                SearchWindow search = new SearchWindow(new AppSettings(), () => root, _ => Task.FromResult<FileSystemEntry>(null)) { SearchText = "b.bin" };
                search.Show();
                await search.SearchAsync();
                Assert.Equal(new[] { "b.bin" }, search.Results.Select(result => result.Name));
                search.Close();

                // While a copy runs, export is off and a second request is ignored.
                Button export = window.FindControl<Button>("ExportButton");
                Assert.True(export.IsEnabled);
                Task copy = window.Export.CopyCsvAsync(root);
                Assert.False(export.IsEnabled);
                Assert.True(window.Export.CopyTreeTextAsync(root).IsCompleted);
                await copy;
                Assert.True(export.IsEnabled);
                string csv = await Avalonia.Input.Platform.ClipboardExtensions.TryGetTextAsync(window.Clipboard);
                Assert.Contains("sub", csv);
                Assert.DoesNotContain("└─", csv);
            }
            finally
            {
                window.Close();
                Directory.Delete(folder, recursive: true);
            }
        }

        [Fact]
        public void Show_files_copies_keep_folders_and_add_files_only_when_asked()
        {
            string root = Path.Combine(Path.GetTempPath(), "fixture");
            FileSystemEntry file = new FileSystemEntry { Name = "a.bin", FullPath = Path.Combine(root, "sub", "a.bin"), SizeBytes = 3 };
            FileSystemEntry sub = new FileSystemEntry { Name = "sub", FullPath = Path.Combine(root, "sub"), IsDirectory = true, SizeBytes = 3, Children = { file } };
            FileSystemEntry top = new FileSystemEntry { Name = "fixture", FullPath = root + Path.DirectorySeparatorChar, IsDirectory = true, SizeBytes = 3, Children = { sub } };
            top.AllFiles.Add(file);

            FileSystemEntry withoutFiles = MainWindow.CopyWithShowFiles(top, showFiles: false);
            FileSystemEntry withFiles = MainWindow.CopyWithShowFiles(withoutFiles, showFiles: true);

            Assert.Empty(withoutFiles.Children.Single().Children);
            Assert.Same(file, withFiles.Children.Single().Children.Single());
            Assert.Equal(3, withFiles.SizeBytes);
        }

        [AvaloniaFact]
        public void Toolbar_hides_empty_groups_and_restores_the_saved_order()
        {
            AppSettings settings = new AppSettings
            {
                ToolbarButtonVisibilitySettingsVersion = 1,
                ToolbarExportCsvButtonVisible = false,
                HasToolStripLayout = true,
                ToolStripLayoutVersion = 14,
                ToolStripMainLeft = 3,
                ToolStripViewModeLeft = 2,
                ToolStripExportLeft = 1,
                ToolStripFeaturesLeft = 0,
            };
            settings.ToolbarScanButtonVisible = settings.ToolbarPauseButtonVisible = settings.ToolbarOpenFolderButtonVisible = true;
            settings.ToolbarTableButtonVisible = settings.ToolbarPieChartButtonVisible = settings.ToolbarBarChartButtonVisible = true;
            settings.ToolbarSunburstButtonVisible = settings.ToolbarTreemapButtonVisible = settings.ToolbarAnalysisButtonVisible = true;
            settings.ToolbarStorageHistoryButtonVisible = settings.ToolbarSearchButtonVisible = true;
            MainWindow window = new MainWindow(settings);

            Avalonia.Controls.WrapPanel toolbar = window.FindControl<Avalonia.Controls.WrapPanel>("Toolbar");
            Assert.Equal(new[] { "GroupFeatures", "GroupExport", "GroupViews", "GroupMain" }, toolbar.Children.Select(child => child.Name));
            Assert.False(window.FindControl<Avalonia.Controls.StackPanel>("GroupExport").IsVisible);
            Assert.True(window.FindControl<Avalonia.Controls.StackPanel>("GroupViews").IsVisible);
        }
    
        // An item moved to the trash leaves the scanned tree: the tree, the
        // sizes, the status count and the treemap show its folder without it.
        [AvaloniaFact]
        public async Task A_trashed_item_leaves_the_tree_and_the_views()
        {
            string folder = Path.Combine(Path.GetTempPath(), "c2flux-trash-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(folder, "sub"));
            File.WriteAllBytes(Path.Combine(folder, "a.bin"), new byte[3000]);
            File.WriteAllBytes(Path.Combine(folder, "sub", "b.bin"), new byte[5000]);
            MainWindow window = new MainWindow(new AppSettings { SaveScanHistory = false, StorageHistoryDetailsEnabled = false });
            window.Show();

            try
            {
                LocalizationService.Load("en");
                await window.ScanPathAsync(folder);
                Dispatcher.UIThread.RunJobs();
                FileSystemEntry root = window.EntryTree.GetRootEntry(new FileSystemEntry { FullPath = Path.Combine(folder, "a.bin") });
                FileSystemEntry sub = root.Children.Single(child => child.Name == "sub");
                window.EntryTree.SelectEntry(sub);
                Treemap.TreemapNode before = Treemap.TreemapNode.For(root, root);
                Assert.Equal(2, before.Children.Count);

                Directory.Delete(Path.Combine(folder, "sub"), recursive: true);
                window.RemoveFromScan(sub, root);
                Dispatcher.UIThread.RunJobs();

                Assert.Equal(new[] { "a.bin" }, root.Children.Select(child => child.Name));
                Assert.Equal(3000, root.SizeBytes);
                Assert.Equal(1, EntryIndex.FileCount(root, root));
                Assert.Contains("Files: 1", window.StatusLine);
                Assert.Equal(new[] { "a.bin" }, Treemap.TreemapNode.For(root, root).Children.Select(child => child.DisplayName));
            }
            finally
            {
                window.Close();
                Directory.Delete(folder, recursive: true);
            }
        }

        // The real trash of each system, on CI only: locally it would fill
        // the developer's trash.
        [AvaloniaFact]
        public void The_system_trash_takes_a_file()
        {
            if (Environment.GetEnvironmentVariable("CI") != "true")
            {
                return;
            }

            // Under the home folder: gio finds no trash on a tmpfs /tmp.
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "c2flux-trash-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string file = Path.Combine(folder, "trash me.txt");
            File.WriteAllText(file, "c2flux");

            try
            {
                FileManager.MoveToTrash(file);
                Assert.False(File.Exists(file));
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        }

        // Long work shows a dialog with its progress; closing it (Cancel)
        // cancels the work. Quick work shows none.
        [AvaloniaFact]
        public void Long_work_shows_progress_and_can_be_canceled()
        {
            Window owner = new Window();
            owner.Show();

            try
            {
                Task<(bool Completed, int Result)> quick = AppDialogs.RunWithProgressAsync(owner, "quick", (report, token) => 7);
                while (!quick.IsCompleted)
                {
                    Dispatcher.UIThread.RunJobs();
                }

                Assert.Equal((true, 7), quick.Result);
                Assert.Empty(owner.OwnedWindows);

                Task<(bool Completed, int Result)> slow = AppDialogs.RunWithProgressAsync<int>(owner, "slow", (report, token) =>
                {
                    report(0.5);

                    while (true)
                    {
                        token.ThrowIfCancellationRequested();
                        System.Threading.Thread.Sleep(5);
                    }
                });
                DateTime deadline = DateTime.UtcNow.AddSeconds(5);

                while (owner.OwnedWindows.Count == 0 && DateTime.UtcNow < deadline)
                {
                    Dispatcher.UIThread.RunJobs();
                    System.Threading.Thread.Sleep(10);
                }

                Window dialog = Assert.Single(owner.OwnedWindows);
                bool Shows50() => dialog.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == "50 %");

                while (!Shows50() && DateTime.UtcNow < deadline)
                {
                    Dispatcher.UIThread.RunJobs();
                    System.Threading.Thread.Sleep(10);
                }

                Assert.True(Shows50());

                dialog.Close();

                while (!slow.IsCompleted && DateTime.UtcNow < deadline)
                {
                    Dispatcher.UIThread.RunJobs();
                    System.Threading.Thread.Sleep(10);
                }

                Assert.False(slow.Result.Completed);

                // finish runs once, with the result, after work past the dialog's delay.
                int finished = 0;
                Task<(bool Completed, int Result)> withFinish = AppDialogs.RunWithProgressAsync<int>(owner, "finish", (report, token) =>
                {
                    System.Threading.Thread.Sleep(600);
                    return 3;
                }, finish: result =>
                {
                    finished += result;
                    return Task.CompletedTask;
                });
                deadline = DateTime.UtcNow.AddSeconds(5);

                while (!withFinish.IsCompleted && DateTime.UtcNow < deadline)
                {
                    Dispatcher.UIThread.RunJobs();
                    System.Threading.Thread.Sleep(10);
                }

                Assert.Equal((true, 3), withFinish.Result);
                Assert.Equal(3, finished);
                Assert.Empty(owner.OwnedWindows);
            }
            finally
            {
                owner.Close();
            }
        }

        // Dragging a group by its grip: a ghost follows the pointer and a
        // bar marks the gap; the drop puts the group there.
        [AvaloniaFact]
        public void Dragging_a_toolbar_group_shows_a_ghost_and_the_drop_gap()
        {
            MainWindow window = new MainWindow(new AppSettings()) { Width = 1264, Height = 761 };
            window.Show();

            try
            {
                Dispatcher.UIThread.RunJobs();
                WrapPanel toolbar = window.FindControl<WrapPanel>("Toolbar");
                Canvas overlay = window.FindControl<Canvas>("ToolbarOverlay");
                StackPanel views = window.FindControl<StackPanel>("GroupViews");
                StackPanel features = window.FindControl<StackPanel>("GroupFeatures");
                Control grip = features.Children[0];
                Avalonia.Point start = grip.TranslatePoint(new Avalonia.Point(3, grip.Bounds.Height / 2), window).Value;
                Avalonia.Point target = views.TranslatePoint(new Avalonia.Point(10, views.Bounds.Height / 2), window).Value;

                window.MouseDown(start, Avalonia.Input.MouseButton.Left);
                window.MouseMove(new Avalonia.Point(start.X + 2, start.Y));
                Dispatcher.UIThread.RunJobs();
                Assert.Empty(overlay.Children);

                window.MouseMove(target, Avalonia.Input.RawInputModifiers.LeftMouseButton);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(3, overlay.Children.Count);
                Control marker = overlay.Children[^1];
                Assert.True(marker.IsVisible);
                Assert.InRange(Canvas.GetLeft(marker) + 1.5, views.Bounds.Left - 9, views.Bounds.Left - 7);

                if (Environment.GetEnvironmentVariable("C2FLUX_CHART_OUT") is string output)
                {
                    Directory.CreateDirectory(output);
                    using Avalonia.Media.Imaging.WriteableBitmap frame = Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(window);
                    frame.Save(Path.Combine(output, "toolbar-drag.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
                }

                window.MouseUp(target, Avalonia.Input.MouseButton.Left);
                Dispatcher.UIThread.RunJobs();
                Assert.Empty(overlay.Children);
                Assert.Equal(toolbar.Children.IndexOf(views) - 1, toolbar.Children.IndexOf(features));
            }
            finally
            {
                window.Close();
            }
        }
}
}
