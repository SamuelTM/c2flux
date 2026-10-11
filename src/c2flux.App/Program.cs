using System;
using System.IO;
using System.Linq;
using Avalonia;

namespace c2flux
{
    public static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length == 1 && args[0] == "--self-test")
            {
                return SelfTest();
            }

            FileIdentities.Reader =
                OperatingSystem.IsWindows() ? WindowsFileIdentity.TryRead
                : OperatingSystem.IsMacOS() ? MacFileIdentity.TryRead
                : LinuxFileIdentity.TryRead;

            RegisterPlatformServices();

            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }

        // For the release builds (trimmed, per RID): JSON settings, scan
        // files and history, a language file, SQLite and the volume list,
        // in a temporary data folder. No window. Exit code 0 when all work.
        private static int SelfTest()
        {
            string home = Path.Combine(Path.GetTempPath(), "c2flux-self-test-" + Guid.NewGuid().ToString("N"));
            Environment.SetEnvironmentVariable(AppPaths.HomeVariable, home);

            try
            {
                new AppSettings { LanguageCode = "pt" }.Save();
                Check(AppSettings.Load().LanguageCode == "pt", "settings");

                FileSystemEntry root = new FileSystemEntry { Name = "root", FullPath = home, IsDirectory = true, SizeBytes = 3 };
                root.Children.Add(new FileSystemEntry { Name = "a.bin", FullPath = Path.Combine(home, "a.bin"), SizeBytes = 3 });
                string scanFile = Path.Combine(home, "scan.json");
                ScanResultFileService.Save(scanFile, root);
                Check(ScanResultFileService.Load(scanFile).Children.Single().Name == "a.bin", "scan file");

                Check(StorageHistoryService.AddRecord(home, 3).HasValue && StorageHistoryService.GetRecords(home).Count == 1, "storage history");

                LocalizationService.Load("pt");
                Check(LocalizationService.GetText("Common.Cancel") == "Cancelar", "language file");

                ScanHistoryService.List();
                StorageHistoryDetailsService.TryGetDatabaseStorageInfo(out _, out _);
                Check(Volumes.List().Count > 0, "volumes");

                Console.WriteLine("self-test: ok");
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("self-test: failed: " + exception);
                return 1;
            }
            finally
            {
                try
                {
                    Directory.Delete(home, recursive: true);
                }
                catch
                {
                }
            }
        }

        private static void Check(bool condition, string what)
        {
            if (!condition)
            {
                throw new InvalidOperationException(what + " did not round-trip.");
            }
        }

        // The native pieces Core and the UI ask for (tests use them too).
        public static void RegisterPlatformServices()
        {
            // Updates download this build's package (packaging/).
            GitHubUpdateService.PackageSuffix = GitHubUpdateService.GetPackageSuffix(
                OperatingSystem.IsWindows() ? AppPaths.Platform.Windows : OperatingSystem.IsMacOS() ? AppPaths.Platform.MacOS : AppPaths.Platform.Linux,
                System.Runtime.InteropServices.RuntimeInformation.OSArchitecture);

            if (OperatingSystem.IsWindows())
            {
                Volumes.ClusterSizeReader = WindowsClusterSize.Read;
                FileIcons.Reader = WindowsFileIcons.Read;
            }
            else if (OperatingSystem.IsMacOS())
            {
                FileIcons.Reader = MacFileIcons.Read;
            }
        }

        // Also used by the XAML previewer.
        public static AppBuilder BuildAvaloniaApp()
        {
            return AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .UseAppFonts()
                .LogToTrace();
        }
    }
}
