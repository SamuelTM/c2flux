using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
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
    }
}
