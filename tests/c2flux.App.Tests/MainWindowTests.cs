using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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
    }
}
